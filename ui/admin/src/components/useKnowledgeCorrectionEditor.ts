import { computed, onUnmounted, reactive, ref, shallowRef, watch, type Ref } from 'vue';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import type { AiKnowledgeCorrectionWorkspace, AiKnowledgeCorrectionHeadRequest, AiKnowledgeCorrectionEditRequest,
  AiKnowledgeCorrectionRestoreRequest, AiKnowledgeCorrectionEditOperation, AiKnowledgeCorrectionRevisionResponse } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';

type Pending = { kind: 'edit'; body: AiKnowledgeCorrectionEditRequest } | { kind: 'restore'; body: AiKnowledgeCorrectionRestoreRequest };
/** 草稿与来源正文分离；明确冲突保留草稿，未知回执只重试原请求，撤权或换作用域立即销毁。 */
export function useKnowledgeCorrectionEditor(options: {
  active: Ref<boolean>; workspace: Ref<AiKnowledgeCorrectionWorkspace | undefined>; currentHead: Ref<AiKnowledgeCorrectionHeadRequest | undefined>;
  currentView: Ref<boolean>; binding: () => { knowledgeBaseId: string; documentId: string; versionId: string };
  saved: (receipt: AiKnowledgeCorrectionRevisionResponse) => Promise<void>; unavailable: () => void;
}) {
  const session = useSessionStore();
  const canEdit = computed(() => options.active.value && session.can('ai.knowledge_corrections.edit'));
  const canRestore = computed(() => options.active.value && session.can('ai.knowledge_corrections.restore'));
  const draft = reactive({ kind: '' as '' | 'replace' | 'split' | 'append', partId: null as string | null, text: '', offset: 1 });
  const reason = ref(''); const saving = ref(false); const conflict = ref(false); const unconfirmed = ref(false); const invalid = ref(false);
  const pending = shallowRef<Pending>(); const receipt = shallowRef<AiKnowledgeCorrectionRevisionResponse>();
  const history = shallowRef<readonly AiKnowledgeCorrectionRevisionResponse[]>([]); const historyNext = ref<number | null>(null);
  const historyOpen = ref(false); const historyLoading = ref(false);
  const hasDraft = computed(() => draft.kind !== '' || pending.value !== undefined);
  const writable = computed(() => canEdit.value && options.currentView.value && !!options.workspace.value && !saving.value && !pending.value);
  const reasonReady = computed(() => reason.value.trim().length > 0 && reason.value.length <= 1000);
  const targetExists = computed(() => draft.kind === 'append' || options.workspace.value?.block.parts.some(p => p.id === draft.partId && (draft.kind !== 'split' || p.text === draft.text)) === true);
  let epoch = 0; let controller: AbortController | undefined; let historyController: AbortController | undefined;
  function discard() { draft.kind = ''; draft.partId = null; draft.text = ''; draft.offset = 1; pending.value = undefined; conflict.value = false; unconfirmed.value = false; invalid.value = false; }
  function wipe() { ++epoch; controller?.abort(); historyController?.abort(); discard(); reason.value = ''; receipt.value = undefined; history.value = []; historyNext.value = null; historyOpen.value = false; saving.value = false; historyLoading.value = false; }
  function begin(kind: 'replace' | 'split' | 'append', partId: string | null = null) {
    if (!writable.value || hasDraft.value) return;
    const part = options.workspace.value?.block.parts.find(p => p.id === partId);
    if (kind !== 'append' && !part) return;
    draft.kind = kind; draft.partId = partId; draft.text = kind === 'append' ? '' : part!.text; draft.offset = 1; invalid.value = false;
  }
  function retarget(partId: string) { if (writable.value && draft.kind === 'replace' && options.workspace.value?.block.parts.some(p => p.id === partId)) draft.partId = partId; }
  async function submit(operation: AiKnowledgeCorrectionEditOperation) {
    if (!writable.value || !reasonReady.value || !options.currentHead.value) return;
    const body: AiKnowledgeCorrectionEditRequest = { requestKey: crypto.randomUUID(), expected: { ...options.currentHead.value }, reason: reason.value, operation };
    await dispatch({ kind: 'edit', body });
  }
  async function submitDraft() {
    const workspace = options.workspace.value;
    if (!workspace || !draft.kind || !targetExists.value) return;
    if (draft.kind === 'split' && (!Number.isSafeInteger(draft.offset) || draft.offset < 1 || draft.offset >= Array.from(draft.text).length)) { invalid.value = true; return; }
    await submit({ kind: draft.kind, blockId: workspace.block.id, partId: draft.partId,
      text: draft.kind === 'split' ? null : draft.text,
      offset: draft.kind === 'split' ? Array.from(draft.text).slice(0, draft.offset).join('').length : null, excluded: null });
  }
  async function blockAction(kind: 'merge' | 'exclude' | 'confirm', partId: string | null = null) {
    const workspace = options.workspace.value;
    if (!workspace || hasDraft.value) return;
    await submit({ kind, blockId: workspace.block.id, partId, text: null, offset: null, excluded: kind === 'exclude' ? !workspace.block.excluded : null });
  }
  async function restore() {
    const selected = options.workspace.value;
    if (!canRestore.value || options.currentView.value || !selected || !options.currentHead.value || !reasonReady.value || hasDraft.value || saving.value) return;
    await dispatch({ kind: 'restore', body: { requestKey: crypto.randomUUID(), expected: { ...options.currentHead.value },
      reason: reason.value, targetSourceId: selected.sourceId, targetRevisionId: selected.revisionId } });
  }
  async function retry() { if (pending.value && !saving.value) await dispatch(pending.value); }
  async function dispatch(action: Pending) {
    if (!options.active.value || saving.value || (action.kind === 'edit' ? !canEdit.value : !canRestore.value)) return;
    const value = epoch; pending.value = action; saving.value = true; invalid.value = false; conflict.value = false; unconfirmed.value = false;
    controller = new AbortController();
    try {
      const result = action.kind === 'edit' ? await api.editKnowledgeCorrection({ ...options.binding(), body: action.body }, controller.signal)
        : await api.restoreKnowledgeCorrection({ ...options.binding(), body: action.body }, controller.signal);
      if (value !== epoch || !options.active.value) return;
      discard(); reason.value = ''; receipt.value = result; history.value = []; historyNext.value = null;
      await options.saved(result);
      if (historyOpen.value && value === epoch) await readHistory();
    } catch (error) {
      if (value !== epoch || !options.active.value) return;
      if (isFullNetProblemDetails(error) && [401, 403, 404].includes(error.status)) { wipe(); options.unavailable(); return; }
      if (isFullNetProblemDetails(error) && error.status < 500) {
        pending.value = undefined; conflict.value = error.status === 409; invalid.value = !conflict.value;
      } else unconfirmed.value = true;
    } finally { if (value === epoch) saving.value = false; }
  }
  async function readHistory(more = false) {
    if (!options.active.value || historyLoading.value || more && historyNext.value === null) return;
    const value = epoch; historyController?.abort(); historyController = new AbortController(); historyLoading.value = true; historyOpen.value = true;
    try {
      const result = await api.listKnowledgeCorrectionRevisions({ ...options.binding(), ...(more ? { beforeHeadVersion: historyNext.value! } : {}) }, historyController.signal);
      if (value !== epoch || !options.active.value) return;
      const combined = more ? [...history.value, ...result.items] : result.items;
      if (new Set(combined.map(r => r.id)).size !== combined.length) throw new Error('client.invalid_knowledge_correction');
      history.value = combined; historyNext.value = result.nextHeadVersion;
    } catch { if (value === epoch && options.active.value) { history.value = []; historyNext.value = null; options.unavailable(); } }
    finally { if (value === epoch) historyLoading.value = false; }
  }
  watch(() => [options.active.value, ...Object.values(options.binding())], wipe);
  watch(() => [session.currentUser, session.switching], wipe, { deep: true });
  onUnmounted(wipe);
  return { canEdit, canRestore, writable, reasonReady, draft, reason, saving, conflict, unconfirmed, invalid, hasDraft, targetExists,
    receipt, history, historyNext, historyOpen, historyLoading, begin, retarget, submitDraft, blockAction, restore, retry, discard, wipe, readHistory };
}
