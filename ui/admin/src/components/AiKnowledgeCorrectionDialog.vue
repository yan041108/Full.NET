<script setup lang="ts">
import { computed, onUnmounted, reactive, ref, shallowRef, watch } from 'vue';
import { ElAlert, ElButton, ElInput, ElInputNumber } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import type { AiKnowledgeCorrectionPrepareRequest, AiKnowledgeCorrectionPreparationResponse, AiKnowledgeCorrectionWorkspace, AiKnowledgeSourceLocation, AiKnowledgeVersionResponse, AiKnowledgeCorrectionHeadRequest } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeCorrectionText } from '../i18n/ai-knowledge-corrections';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { useKnowledgeCorrectionEditor } from './useKnowledgeCorrectionEditor';

const props = defineProps<{ open: boolean; knowledgeBaseId: string; documentId: string; versionId: string; documentVersion: number; version: AiKnowledgeVersionResponse }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; saved: [documentVersion: number] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const tr = (key: Parameters<typeof knowledgeCorrectionText>[1]) => knowledgeCorrectionText(locale.value, key);
const canRead = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read',
  'ai.knowledge_documents.versions_read', 'ai.knowledge_corrections.read'].every(code => session.can(code)));
const canPrepare = computed(() => canRead.value && session.can('ai.knowledge_corrections.prepare'));
const workspace = shallowRef<AiKnowledgeCorrectionWorkspace>(); const preparation = shallowRef<AiKnowledgeCorrectionPreparationResponse>();
const sourceId = ref<string>(); const loading = ref(false); const busy = ref(false);
const revisionId = ref<string>(); const currentHead = shallowRef<AiKnowledgeCorrectionHeadRequest>();
const currentView = computed(() => !sourceId.value && !revisionId.value);
const failed = ref(false); const unknown = ref(false); const paused = ref(false);
const sourceOpen = ref(false); const location = shallowRef<AiKnowledgeSourceLocation>();
const pending = computed(() => preparation.value && ['queued', 'running'].includes(preparation.value.state));
let epoch = 0; let viewEpoch = 0; let polls = 0; let timer: ReturnType<typeof setTimeout> | undefined;
let controller: AbortController | undefined; let pageController: AbortController | undefined;
let pendingRequest: AiKnowledgeCorrectionPrepareRequest | undefined;
const parameters = () => ({ knowledgeBaseId: props.knowledgeBaseId, documentId: props.documentId, versionId: props.versionId });
const editor = reactive(useKnowledgeCorrectionEditor({
  active: computed(() => props.open && canRead.value), workspace, currentHead, currentView, binding: parameters,
  saved: async receipt => { emit('saved', receipt.documentVersion); await loadPage(currentView.value ? workspace.value?.blockIndex ?? 0 : 0, undefined, undefined, true); },
  unavailable: () => { discardBody(); currentHead.value = undefined; failed.value = true; }
}));
function rememberHead(value: AiKnowledgeCorrectionWorkspace) { currentHead.value = { documentVersion: value.documentVersion, sourceId: value.sourceId,
  revisionId: value.revisionId, headVersion: value.headVersion, sourceHash: value.sourceHash, payloadHash: value.payloadHash }; }
const current = (value: number) => value === epoch && props.open && canRead.value;
const missing = (error: unknown) => isFullNetProblemDetails(error) && error.status === 404;
function discardBody() { workspace.value = undefined; sourceOpen.value = false; location.value = undefined; editor.wipe(); }
function clear() {
  ++epoch; ++viewEpoch; controller?.abort(); pageController?.abort(); clearTimeout(timer); timer = undefined;
  workspace.value = undefined; preparation.value = undefined; sourceId.value = undefined; sourceOpen.value = false; location.value = undefined;
  revisionId.value = undefined; currentHead.value = undefined;
  loading.value = false; busy.value = false; failed.value = false; unknown.value = false; paused.value = false; polls = 0;
}
function stop() { clear(); pendingRequest = undefined; editor.wipe(); }
function schedule(value: number) {
  clearTimeout(timer);
  if (!current(value) || !pending.value) return;
  if (++polls > 120) { paused.value = true; return; }
  timer = setTimeout(() => { void poll(value); }, 1500);
}
async function poll(value: number) {
  if (!current(value) || !preparation.value) return;
  try {
    const result = await api.getKnowledgeCorrectionPreparation({ ...parameters(), preparationId: preparation.value.id }, controller?.signal);
    if (!current(value)) return;
    preparation.value = result;
    if (result.state === 'completed' && result.sourceId) { pendingRequest = undefined; if (!editor.hasDraft && !editor.saving) await loadPage(0, result.sourceId); }
    else schedule(value);
  } catch {
    if (current(value)) { discardBody(); failed.value = true; paused.value = true; }
  }
}
async function refresh() {
  const hadDraft = editor.hasDraft;
  clear(); const value = epoch;
  if (!current(value)) return;
  controller = new AbortController(); loading.value = true;
  const results = await Promise.allSettled([
    api.getLatestKnowledgeCorrectionPreparation(parameters(), controller.signal),
    api.getKnowledgeCorrectionWorkspace({ ...parameters(), block: 0 }, controller.signal)
  ]);
  if (!current(value)) return;
  if (results.some(result => result.status === 'rejected' && !missing(result.reason))) {
    editor.wipe(); failed.value = true; loading.value = false; return;
  }
  const task = results[0], page = results[1];
  if (hadDraft && page?.status === 'rejected') { editor.wipe(); failed.value = true; loading.value = false; return; }
  if (task?.status === 'fulfilled') preparation.value = task.value;
  if (page?.status === 'fulfilled') { workspace.value = page.value; rememberHead(page.value); }
  loading.value = false; schedule(value);
}
async function loadPage(block: number, selected?: string, selectedRevision?: string, refreshHead = false) {
  if (!props.open || !canRead.value || loading.value) return;
  pageController?.abort(); const value = epoch; const view = ++viewEpoch; const previous = workspace.value;
  pageController = new AbortController(); workspace.value = undefined; loading.value = true; failed.value = false; sourceOpen.value = false; location.value = undefined;
  try {
    const result = await api.getKnowledgeCorrectionWorkspace({ ...parameters(), block, ...(selected ? { sourceId: selected } : {}), ...(selectedRevision ? { revisionId: selectedRevision } : {}) }, pageController.signal);
    if (!current(value) || view !== viewEpoch) return;
    // 普通翻页冻结整份修订；明确保存成功后的刷新允许读取新头，并保留正在维护的块页码。
    if (!refreshHead && block > 0 && previous && (previous.sourceId !== result.sourceId || previous.revisionId !== result.revisionId || previous.headVersion !== result.headVersion
      || previous.payloadHash !== result.payloadHash || previous.sourceHash !== result.sourceHash || previous.documentVersion !== result.documentVersion))
      throw new Error('client.knowledge_correction_changed');
    workspace.value = result; sourceId.value = selected; revisionId.value = selectedRevision;
    if (!selected && !selectedRevision) rememberHead(result);
  } catch { if (current(value) && view === viewEpoch) { editor.wipe(); failed.value = true; } }
  finally { if (current(value) && view === viewEpoch) loading.value = false; }
}
async function prepare() {
  if (!props.open || !canPrepare.value || busy.value || loading.value || pending.value || editor.hasDraft || editor.saving) return;
  const value = epoch;
  pendingRequest ??= { requestKey: crypto.randomUUID(), documentVersion: workspace.value?.documentVersion ?? props.documentVersion };
  busy.value = true; failed.value = false; unknown.value = false;
  controller?.abort(); controller = new AbortController();
  try {
    const result = await api.prepareKnowledgeCorrection({ ...parameters(), body: pendingRequest }, controller.signal);
    if (!current(value)) return;
    preparation.value = result; pendingRequest = undefined; polls = 0; paused.value = false;
    if (result.state === 'completed' && result.sourceId) await loadPage(0, result.sourceId); else schedule(value);
  } catch (error) {
    if (current(value)) {
      discardBody();
      unknown.value = !isFullNetProblemDetails(error) || error.status >= 500;
      failed.value = !unknown.value;
    }
  }
  finally { if (current(value)) busy.value = false; }
}
function showSource(value: AiKnowledgeSourceLocation | null) {
  if (!value || !canRead.value || !workspace.value || !props.open) return;
  location.value = value; sourceOpen.value = true;
}
function position(value: AiKnowledgeSourceLocation): string {
  const parts: string[] = [];
  if (value.pageNumber !== null) parts.push('p. ' + value.pageNumber);
  if (value.paragraphNumber !== null) parts.push('¶ ' + value.paragraphNumber);
  if (value.tableNumber !== null) parts.push('▦ ' + [value.tableNumber, value.rowNumber, value.cellNumber].join(' / '));
  if (value.grid) parts.push([value.grid.sheetName, value.grid.row, value.grid.column].join(' / '));
  if (value.slide) parts.push([value.slide.slideNumber, value.slide.shapeNumber, value.slide.rowNumber, value.slide.columnNumber].filter(v => v !== null).join(' / '));
  if (value.htmlPath) parts.push(value.htmlPath);
  if (value.image) {
    const image = value.image;
    if (image.parent) parts.push(position(image.parent));
    parts.push([image.frameNumber, image.x, image.y, image.width, image.height].join(' / '), tr('confidence') + ' ' + Math.round(image.confidence * 100) + '%');
  }
  return parts.length ? parts.join(' · ') : '#' + value.blockSequence;
}
watch(() => [props.open, props.knowledgeBaseId, props.documentId, props.versionId], () => { stop(); if (props.open) void refresh(); }, { immediate: true });
watch(() => [session.currentUser, session.switching], () => { stop(); emit('update:open', false); }, { deep: true });
onUnmounted(stop);
</script>

<template>
  <ArtFormDialog :open="open" :title="tr('title')" width="1100px" :show-confirm="false" :cancel-label="tr('close')" class="knowledge-correction-dialog" @update:open="emit('update:open', $event)">
    <section class="knowledge-correction-workspace" data-testid="knowledge-correction-dialog" :aria-label="tr('title')">
      <el-alert :title="tr('hint')" type="info" :closable="false" />
      <p>{{ tr(editor.canEdit || editor.canRestore ? 'editHint' : 'readonly') }}</p>
      <div v-if="canRead" class="correction-toolbar">
        <el-button :disabled="busy || loading || editor.saving || editor.unconfirmed" data-testid="correction-refresh" @click="refresh">{{ tr('refresh') }}</el-button>
        <el-button v-if="canPrepare" :disabled="busy || loading || !!pending || editor.hasDraft || editor.saving" :loading="busy" data-testid="correction-prepare" @click="prepare">{{ tr(workspace ? 'reparse' : 'prepare') }}</el-button>
        <el-button :disabled="loading || busy || editor.hasDraft || editor.saving" data-testid="correction-current" @click="loadPage(0)">{{ tr('current') }}</el-button>
        <el-button v-if="preparation?.sourceId" :disabled="loading || busy || editor.hasDraft || editor.saving" data-testid="correction-prepared" @click="loadPage(0, preparation.sourceId)">{{ tr('prepared') }}</el-button>
        <el-button :disabled="editor.historyLoading || editor.saving" data-testid="correction-history" @click="editor.readHistory()">{{ tr('history') }}</el-button>
      </div>
      <p v-if="preparation && canRead" role="status" data-testid="correction-state">{{ tr(preparation.state as 'queued' | 'running' | 'completed' | 'failed' | 'cancelled') }}</p>
      <el-alert v-if="failed || !canRead" :title="tr('unavailable')" type="error" :closable="false" data-testid="correction-error" />
      <el-alert v-if="unknown" :title="tr('unknown')" type="warning" :closable="false" data-testid="correction-unknown" />
      <p v-if="paused">{{ tr('bounded') }}</p>
      <el-alert v-if="editor.receipt" :title="tr('savedDraft') + ' ' + editor.receipt.headVersion" type="success" :closable="false" data-testid="correction-saved" />
      <el-alert v-if="editor.conflict" :title="tr('conflict')" type="warning" :closable="false" data-testid="correction-conflict" />
      <el-alert v-if="editor.unconfirmed" :title="tr('writeUnknown')" type="warning" :closable="false" />
      <el-alert v-if="editor.invalid" :title="tr('invalidEdit')" type="error" :closable="false" />
      <el-button v-if="editor.unconfirmed && (editor.canEdit || editor.canRestore)" :disabled="editor.saving" data-testid="correction-retry-write" @click="editor.retry">{{ tr('retryWrite') }}</el-button>
      <section v-if="editor.historyOpen && canRead" class="correction-history" :aria-label="tr('history')">
        <p v-if="!editor.history.length">{{ tr('noHistory') }}</p>
        <div v-for="row in editor.history" :key="row.id" class="correction-toolbar">
          <span>{{ row.headVersion }} · {{ row.reason }}</span>
          <el-button :disabled="loading || editor.hasDraft || editor.saving" data-testid="correction-history-preview" @click="loadPage(0, undefined, row.id)">{{ tr('previewRevision') }} {{ row.headVersion }}</el-button>
        </div>
        <el-button v-if="editor.historyNext !== null" :disabled="editor.historyLoading" data-testid="correction-history-more" @click="editor.readHistory(true)">{{ tr('moreHistory') }}</el-button>
      </section>
      <p v-if="loading" role="status">{{ tr('loading') }}</p>
      <template v-else-if="workspace && canRead">
        <p v-if="workspace.isSourceView" data-testid="correction-source-view">{{ tr('sourceView') }}</p>
        <p v-if="revisionId" data-testid="correction-history-view">{{ tr('historyView') }} {{ currentHead?.headVersion ?? '—' }}</p>
        <p>{{ tr('documentVersion') }} {{ workspace.documentVersion }} · {{ tr('head') }} {{ workspace.headVersion }}</p>
        <label v-if="editor.canEdit || editor.canRestore" class="correction-reason">{{ tr('reason') }}
          <el-input v-model="editor.reason" :disabled="editor.saving || editor.unconfirmed" type="textarea" maxlength="1000" :aria-label="tr('reason')" data-testid="correction-reason" />
        </label>
        <el-button v-if="editor.canRestore && !currentView" :disabled="!currentHead || !editor.reasonReady || editor.hasDraft || editor.saving" data-testid="correction-restore" @click="editor.restore">{{ tr('restoreSelected') }}</el-button>
        <nav class="correction-toolbar" :aria-label="tr('block')">
          <el-button :disabled="workspace.blockIndex === 0 || busy || editor.hasDraft || editor.saving" data-testid="correction-previous" @click="loadPage(workspace.blockIndex - 1, sourceId, revisionId)">{{ tr('previous') }}</el-button>
          <span data-testid="correction-page">{{ workspace.blockIndex + 1 }} / {{ workspace.totalBlocks }}</span>
          <el-button :disabled="workspace.blockIndex + 1 >= workspace.totalBlocks || busy || editor.hasDraft || editor.saving" data-testid="correction-next" @click="loadPage(workspace.blockIndex + 1, sourceId, revisionId)">{{ tr('next') }}</el-button>
        </nav>
        <p v-if="workspace.block.excluded">{{ tr('excluded') }}</p>
        <article v-for="part in workspace.block.parts" :key="part.id" class="correction-part" data-testid="correction-part">
          <h3>{{ tr(part.contentKind as 'original' | 'corrected' | 'supplement') }}</h3>
          <p v-if="part.sourceLocation">{{ tr('position') }}: {{ position(part.sourceLocation) }} · {{ part.sourceOffset }} + {{ part.sourceLength }}</p>
          <p v-else>{{ tr('noPosition') }}</p>
          <p v-if="part.contentKind === 'original' && part.sourceLocation?.image && part.sourceLocation.image.confidence < 0.8">{{ tr('unconfirmed') }}</p>
          <div class="correction-comparison">
            <section v-if="part.originalText !== null" :aria-label="tr('originalContext')"><h4>{{ tr('originalContext') }}</h4><pre data-testid="correction-original">{{ part.originalText || tr('emptyCell') }}</pre></section>
            <section :aria-label="tr('currentText')"><h4>{{ tr('currentText') }}</h4><pre data-testid="correction-text">{{ part.text || tr('emptyCell') }}</pre></section>
          </div>
          <el-button v-if="part.sourceLocation" data-testid="correction-source" @click="showSource(part.sourceLocation)">{{ tr('preview') }}</el-button>
          <div v-if="editor.writable" class="correction-toolbar">
            <el-button :disabled="editor.hasDraft" data-testid="correction-edit-part" @click="editor.begin('replace', part.id)">{{ tr('editPart') }}</el-button>
            <el-button v-if="Array.from(part.text).length > 1" :disabled="editor.hasDraft" data-testid="correction-split-part" @click="editor.begin('split', part.id)">{{ tr('splitPart') }}</el-button>
            <el-button v-if="part.anchorId" :disabled="editor.hasDraft || !editor.reasonReady" data-testid="correction-confirm-part" @click="editor.blockAction('confirm', part.id)">{{ tr('confirmOcr') }}</el-button>
            <el-button v-if="editor.draft.kind === 'replace' && editor.draft.partId !== part.id" data-testid="correction-retarget" @click="editor.retarget(part.id)">{{ tr('retarget') }}</el-button>
          </div>
        </article>
        <section v-if="editor.hasDraft" class="correction-draft" :aria-label="tr('localDraft')" data-testid="correction-local-draft">
          <h3>{{ tr('localDraft') }}</h3>
          <label v-if="editor.draft.kind === 'split'">{{ tr('splitOffset') }} <el-input-number v-model="editor.draft.offset" :min="1" :max="Array.from(editor.draft.text).length - 1" :disabled="editor.saving || editor.unconfirmed" :aria-label="tr('splitOffset')" data-testid="correction-split-offset" /></label>
          <el-input v-else-if="editor.draft.kind" v-model="editor.draft.text" type="textarea" :rows="5" maxlength="32768" :disabled="editor.saving || editor.unconfirmed" :aria-label="tr('localDraft')" data-testid="correction-draft-text" />
          <p v-if="!editor.targetExists">{{ tr('targetChanged') }}</p>
          <div class="correction-toolbar">
            <el-button v-if="editor.canEdit && editor.draft.kind" :disabled="!editor.writable || !editor.targetExists || !editor.reasonReady" data-testid="correction-save-draft" @click="editor.submitDraft">{{ tr('saveDraft') }}</el-button>
            <el-button :disabled="editor.saving || editor.unconfirmed" data-testid="correction-discard-draft" @click="editor.discard">{{ tr('discardDraft') }}</el-button>
          </div>
        </section>
        <div v-if="editor.writable" class="correction-toolbar">
          <el-button :disabled="editor.hasDraft" data-testid="correction-append" @click="editor.begin('append')">{{ tr('append') }}</el-button>
          <el-button :disabled="editor.hasDraft || !editor.reasonReady || workspace.blockIndex + 1 >= workspace.totalBlocks" data-testid="correction-merge" @click="editor.blockAction('merge')">{{ tr('merge') }}</el-button>
          <el-button :disabled="editor.hasDraft || !editor.reasonReady" data-testid="correction-exclude" @click="editor.blockAction('exclude')">{{ tr(workspace.block.excluded ? 'includeBlock' : 'excludeBlock') }}</el-button>
        </div>
      </template>
      <p v-else-if="!loading && !failed && canRead">{{ tr('empty') }}</p>
    </section>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-model:open="sourceOpen" :knowledge-base-id="knowledgeBaseId" :document-id="documentId" :version-id="versionId" :version="version" :location="location" />
</template>

<style scoped>
.knowledge-correction-workspace { min-width: 0; color: var(--el-text-color-primary); }
.correction-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-block: 14px; }
.correction-toolbar :deep(.el-button + .el-button) { margin-left: 0; }
.correction-history, .correction-draft, .correction-reason { display: block; min-width: 0; margin-block: 14px; overflow-wrap: anywhere; }
.correction-part { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 12px; margin-block: 12px; min-width: 0; overflow-wrap: anywhere; }
.correction-comparison { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.correction-comparison section { min-width: 0; }
.correction-comparison pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 45vh; overflow-y: auto; font: inherit; background: var(--el-fill-color-light); padding: 10px; }
@media (max-width: 700px) { .correction-comparison { grid-template-columns: minmax(0, 1fr); } }
</style>
