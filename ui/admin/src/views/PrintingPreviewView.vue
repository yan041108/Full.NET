<script setup lang="ts">
import { computed, inject, nextTick, onActivated, onDeactivated, onBeforeUnmount, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import type { InputInstance } from 'element-plus';
import DOMPurify from 'dompurify';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect, ElSwitch, ElPopconfirm
} from 'element-plus';
import { Plus, Printer } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, PrintingTemplateSummary, PrintingTemplate, PrintingTemplateVersionSummary } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useArtDialogLandmarks } from '../framework/art-design/composables/useArtDialogLandmarks';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { validHostPrintingTemplate, validHostPrintingVersion } from '../guards/printing-host-guards';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import PrintingTemplateFields from '../components/PrintingTemplateFields.vue';
import PrintingTenantGrantsDialog from '../components/printing/PrintingTenantGrantsDialog.vue';
import PrintingFormSchemaSelector from '../components/printing/PrintingFormSchemaSelector.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createPrintingTemplate,
  listPrintingTemplates,
  publishPrintingTemplate, updatePrintingTemplate, getPrintingTemplate, getPrintingTemplateVersion, listPrintingTemplateVersions
} from '../api/printing-templates';

defineOptions({ name: 'PrintingPreviewView' });

const DEFAULT_LAYOUT_HTML = `<div class="print-card">
  <h1>{{tenantName}}</h1>
  <p>编码：{{tenantCode}}</p>
  <p>域名：{{tenantDomain}}</p>
  <p>打印人：{{printedByDisplayName}}</p>
  <p>打印时间：{{printedAtUtc}}</p>
</div>`;

const session = useSessionStore();
const route = inject(routeLocationKey, undefined);
const { t } = useAdminI18n();
const templates = ref<PrintingTemplateSummary[]>([]);
const selectedTemplateId = ref('');
const page = ref(1), total = ref(0), searchValue = ref('');
const history = ref<PrintingTemplateVersionSummary[]>([]), historyPage = ref(1), historyTotal = ref(0), selectedPublished = ref<number>();
const preview = ref<{ html: string; versionNumber: number }>();
const frame = ref<HTMLIFrameElement>();
const active = ref(true), writeUnconfirmed = ref(false), publishOpen = ref(false);
const editingId = ref(''), editingVersion = ref(0), publishTarget = ref<PrintingTemplate>(), changeNote = ref('');
let generation = 0, detailVersion = 0, historyVersion = 0, historyRequest: AbortController | undefined, listRequest: AbortController | undefined, requests = new AbortController(),
  detailRequest: AbortController | undefined, previewRequest: AbortController | undefined;
const available = () => active.value && !session.switching && (!route || route.name === 'printing-preview')
  && session.currentUser?.scope === 'host' && session.currentUser.actorScope === 'host' && !session.currentUser.tenantId;
const can = (action: string) => available() && session.can('printing.templates.read') && session.can(`printing.templates.${action}`);
// 布局属于可编辑的不可信内容；必须在最终插入 DOM 前净化，覆盖数据库中已经保存的旧模板。
const safePreviewHtml = computed(() => DOMPurify.sanitize(preview.value?.html ?? '', {
  USE_PROFILES: { html: true },
  FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta', 'form', 'input', 'button', 'textarea', 'select', 'option'],
  FORBID_ATTR: ['id', 'name'],
  ALLOW_DATA_ATTR: false
}));
const loading = ref(false), historyLoading = ref(false), historyProblem = ref<FullNetProblemDetails>();
const catalogReady = ref(false), catalogProblem = ref<FullNetProblemDetails>();
const revisionFloors = new Map<string, { draft: number; published: number }>();
const observedVersionIds = new Map<string, Map<number, string>>();
let printIntent: { scope: number; version: number; id: string; published: number } | undefined;
let lastListQuery = '', lastHistoryQuery = '';
const previewing = ref(false);
const creating = ref(false);
const publishing = ref(false);
const busy = computed(() => creating.value || publishing.value);
const problem = ref<FullNetProblemDetails>();
const editorDialog = ref<InstanceType<typeof ElDialog>>(), publicationDialog = ref<InstanceType<typeof ElDialog>>();
const editorLandmarks = useArtDialogLandmarks(() => editorDialog.value?.dialogContentRef?.$el);
const publicationLandmarks = useArtDialogLandmarks(() => publicationDialog.value?.dialogContentRef?.$el);
const createDialogVisible = ref(false), createBlocked = ref(false);
type WriteReceipt = { kind: 'create' | 'update' | 'publish'; outcome: 'processing' | 'succeeded' | 'unconfirmed' | 'failed'; templateKey: string; templateId?: string; versionNumber?: number; draftVersion?: number };
const writeReceipt = ref<WriteReceipt>();
let editorEpoch = 0, publishEpoch = 0, recoverySequence = 0;
let unconfirmedDraft: { form: { templateKey: string; name: string; layoutHtml: string; isEnabled: boolean }; schema: string } | undefined;
let activeWrite: { controller: AbortController; kind: WriteReceipt['kind']; receipt: WriteReceipt; draft?: NonNullable<typeof unconfirmedDraft> } | undefined;
const recoveryPrompt = ref<{ token: number; kind: 'create' | 'publish'; confirm: () => void; close: (open: boolean) => void }>();
const publicationBlocks = ref(new Map<string, number>());
let lastUnknownKind: WriteReceipt['kind'] | undefined, grantSequence = 0;
const grantWindow = ref<{ token: number; template: Pick<PrintingTemplate, 'id' | 'name' | 'latestPublishedVersionNumber'>; close: () => void }>();
const layoutInput = ref<InputInstance>(), editorSchemaKey = ref('printing.tenant_profile_card');
const createForm = ref({
  templateKey: 'tenant-profile-card',
  name: '租户档案卡片',
  layoutHtml: DEFAULT_LAYOUT_HTML,
  isEnabled: true
});
const canSaveDraft = computed(() => !busy.value && !writeUnconfirmed.value && (!!editingId.value || !createBlocked.value) && can(editingId.value ? 'update' : 'create')
  && !!createForm.value.name.trim() && createForm.value.name.trim().length <= 128
  && !!createForm.value.layoutHtml.trim() && createForm.value.layoutHtml.length <= 65536
  && (!!editingId.value || (!!createForm.value.templateKey.trim() && createForm.value.templateKey.trim().length <= 128)));

const selectedTemplate = computed(() =>
  templates.value.find(item => item.id.toLowerCase() === selectedTemplateId.value.toLowerCase()));
const publicationBlocked = computed(() => {
  const blocked = publicationBlocks.value.get(selectedTemplateId.value.toLowerCase());
  return blocked !== undefined && (selectedTemplate.value?.version ?? 0) <= blocked;
});

const printableTemplates = computed(() =>
  templates.value.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0));

const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
function currentDraft(draft: PrintingTemplate, id: string): boolean {
  const floor = revisionFloors.get(id.toLowerCase()), summary = selectedTemplate.value;
  return validHostPrintingTemplate(draft) && sameId(draft.id, id) && !!summary && sameId(summary.id, id)
    && summary.templateKey === draft.templateKey && summary.formSchemaKey === draft.formSchemaKey
    && (!floor || draft.version >= floor.draft && draft.latestPublishedVersionNumber >= floor.published);
}
const editorSignature = () => JSON.stringify([editingId.value, editingVersion.value, editorSchemaKey.value, createForm.value]);
function markUnconfirmed(operation: NonNullable<typeof activeWrite>) {
  lastUnknownKind = operation.kind;
  writeUnconfirmed.value = true;
  writeReceipt.value = { ...operation.receipt, outcome: 'unconfirmed' };
  if (operation.kind === 'publish' && operation.receipt.templateId && operation.receipt.draftVersion) publicationBlocks.value.set(operation.receipt.templateId.toLowerCase(), operation.receipt.draftVersion);
  if (operation.kind === 'create') { createBlocked.value = true; unconfirmedDraft = operation.draft; }
}
/** 取消只结束本窗口接入；已发出的写入仍可能提交，必须留下待核对事实。 */
function cancelOwnedWrite(kind: 'editor' | 'publish') {
  const operation = activeWrite;
  if (!operation || (operation.kind === 'publish') !== (kind === 'publish')) return;
  markUnconfirmed(operation); activeWrite = undefined; operation.controller.abort(); creating.value = false; publishing.value = false;
}
watch(createDialogVisible, open => {
  editorEpoch++; recoveryPrompt.value = undefined;
  if (!open) { detailVersion++; detailRequest?.abort(); cancelOwnedWrite('editor'); }
}, { flush: 'sync' });
watch(editorSignature, () => { editorEpoch++; recoveryPrompt.value = undefined; cancelOwnedWrite('editor'); }, { flush: 'sync' });
watch(() => [publishOpen.value, publishTarget.value, changeNote.value], () => {
  publishEpoch++; cancelOwnedWrite('publish');
}, { flush: 'sync' });
const editorControls = computed(() => {
  const owner = editorEpoch, signature = editorSignature(), open = createDialogVisible.value;
  const owned = () => owner === editorEpoch && open && createDialogVisible.value && signature === editorSignature() && can(editingId.value ? 'update' : 'create');
  const input = <K extends keyof typeof createForm.value>(key: K, value: typeof createForm.value[K]) => {
    if (owned() && !busy.value) createForm.value = { ...createForm.value, [key]: value };
  };
  return { update: (value: boolean) => { if (!value && owned()) createDialogVisible.value = false; },
    close: () => { if (owned()) createDialogVisible.value = false; }, save: () => { if (owned()) void submitCreate(); },
    key: (value: string) => input('templateKey', value), name: (value: string) => input('name', value), layout: (value: string) => input('layoutHtml', value), enabled: (value: string | number | boolean) => { if (typeof value === 'boolean') input('isEnabled', value); },
    insert: (key: string) => { if (owned()) void insertLayoutField(key); },
    schema: (value: string) => { if (owned() && !busy.value && !editingId.value && !createBlocked.value && !writeUnconfirmed.value) editorSchemaKey.value = value; },
    schemaLayout: (value: {formSchemaKey:string;layoutHtml:string}) => { if (owned() && !editingId.value && !createBlocked.value && !writeUnconfirmed.value && value.formSchemaKey===editorSchemaKey.value && value.layoutHtml.length<=65536) input('layoutHtml',value.layoutHtml); },
    reload: () => { if (owned() && editingId.value === selectedTemplateId.value && !busy.value) void editSelected(); },
    recover: () => { if (owned()) acknowledgeRecovery(); }
  };
});
const publishControls = computed(() => {
  const owner = publishEpoch, target = publishTarget.value, open = publishOpen.value, note = changeNote.value;
  const owned = () => owner === publishEpoch && open && publishOpen.value && target === publishTarget.value && note === changeNote.value && can('publish');
  return { update: (value: boolean) => { if (!value && owned()) publishOpen.value = false; }, close: () => { if (owned()) publishOpen.value = false; },
    note: (value: string) => { if (owned() && !busy.value) changeNote.value = value; }, confirm: () => { if (owned()) void publishSelected(); } };
});
function acknowledgeRecovery() {
  if (!can('create') || !createDialogVisible.value || !createBlocked.value || busy.value) return;
  const owner = editorEpoch, token = ++recoverySequence;
  const owned = () => recoveryPrompt.value?.token === token && owner === editorEpoch && createDialogVisible.value && createBlocked.value && can('create');
  recoveryPrompt.value = { token, kind: 'create', confirm: () => { if (owned()) { createBlocked.value = false; writeUnconfirmed.value = false; recoveryPrompt.value = undefined; } }, close: open => { if (!open && owned()) recoveryPrompt.value = undefined; } };
}
function acknowledgePublicationRecovery() {
  if (!can('publish') || !catalogReady.value || !publicationBlocked.value || busy.value || !selectedTemplate.value) return;
  const scope = generation, catalog = listVersion, id = selectedTemplateId.value, revision = selectedTemplate.value.version, token = ++recoverySequence;
  const owned = () => current(scope) && can('publish') && !busy.value && catalog === listVersion && id === selectedTemplateId.value && revision === selectedTemplate.value?.version
    && publicationBlocked.value && recoveryPrompt.value?.token === token;
  recoveryPrompt.value = { token, kind: 'publish', confirm: () => { if (owned()) { publicationBlocks.value.delete(id.toLowerCase()); writeUnconfirmed.value = false; recoveryPrompt.value = undefined; } },
    close: open => { if (!open && owned()) recoveryPrompt.value = undefined; } };
}
function openGrants() {
  const target = selectedTemplate.value;
  if (!can('grant_tenants') || !catalogReady.value || busy.value || !target || target.latestPublishedVersionNumber < 1) return;
  const token = ++grantSequence;
  grantWindow.value = { token, template: { id: target.id, name: target.name, latestPublishedVersionNumber: target.latestPublishedVersionNumber },
    close: () => { if (grantWindow.value?.token === token) grantWindow.value = undefined; } };
}
const receiptMessage = computed(() => writeReceipt.value?.outcome === 'succeeded'
  ? t(writeReceipt.value.kind === 'publish' ? 'printingTemplates.published' : 'printingTemplates.saved')
  : t(writeReceipt.value?.outcome === 'unconfirmed' ? 'printingTemplates.writeUnconfirmed' : writeReceipt.value?.outcome === 'failed' ? 'printingTemplates.writeFailed' : 'printingTemplates.writeProcessing'));

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function current(value: number) { return value === generation && can('read'); }
function uncertain(error: unknown) { return !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
let previewVersion = 0, listVersion = 0;
async function searchTemplates(keyword: string): Promise<void> {
  if (!can('read') || busy.value) return;
  searchValue.value = keyword; page.value = 1;
  await load();
}

/** 分页、搜索与刷新共用一个请求代次，后发查询立即使旧目录和旧行操作失效。 */
async function load(): Promise<void> {
  if (!can('read') || busy.value) return;
  const scope = generation, version = ++listVersion, controller = new AbortController(); listRequest?.abort(); listRequest = controller;
  const query = { keyword: searchValue.value.trim() || undefined, page: page.value };
  const key = JSON.stringify(query);
  if (key !== lastListQuery) { templates.value = []; total.value = 0; selectedTemplateId.value = ''; }
  lastListQuery = key;
  loading.value = true; catalogReady.value = false; catalogProblem.value = undefined; problem.value = undefined; printIntent = undefined; grantWindow.value = undefined;
  if (recoveryPrompt.value?.kind === 'publish') recoveryPrompt.value = undefined;
  try {
    const result = await listPrintingTemplates(query.keyword, controller.signal, query.page, 20);
    if (!current(scope) || version !== listVersion || controller.signal.aborted) return;
    if (result.page !== query.page || result.pageSize !== 20) throw Error('client.invalid_printing_template_list');
    if (new Set(result.items.map(row => row.id.toLowerCase())).size !== result.items.length || result.items.some(row => {
      const floor = revisionFloors.get(row.id.toLowerCase()); return !Number.isSafeInteger(row.version) || row.version < 1 || !Number.isSafeInteger(row.latestPublishedVersionNumber) || row.latestPublishedVersionNumber < 0
        || floor && (row.version < floor.draft || row.latestPublishedVersionNumber < floor.published);
    })) throw Error('client.stale_printing_template_catalog');
    for (const row of result.items) {
      revisionFloors.set(row.id.toLowerCase(), { draft: row.version, published: row.latestPublishedVersionNumber });
      const blocked = publicationBlocks.value.get(row.id.toLowerCase()); if (blocked !== undefined && row.version > blocked) publicationBlocks.value.delete(row.id.toLowerCase());
    }
    catalogReady.value = true;
    const previousId = selectedTemplateId.value;
    templates.value = result.items; total.value = result.total;
    const selected = templates.value.find(item => sameId(item.id, selectedTemplateId.value));
    selectedTemplateId.value = selected?.id ?? printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    if (selectedTemplateId.value && previousId === selectedTemplateId.value) void loadHistory();
    previewVersion++; previewRequest?.abort(); previewing.value = false; preview.value = undefined;
    if (!createDialogVisible.value) writeUnconfirmed.value = publicationBlocked.value;
  } catch (error: unknown) {
    if (current(scope) && version === listVersion && !controller.signal.aborted) { catalogProblem.value = toProblem(error, 'printingPreview.loadFailed'); problem.value = catalogProblem.value; }
  } finally {
    if (scope === generation && version === listVersion) loading.value = false;
  }
}

async function runPreview(): Promise<void> {
  if (!can('preview') || !catalogReady.value || !selectedTemplate.value || !selectedPublished.value || !Number.isSafeInteger(selectedPublished.value) || selectedPublished.value < 1 || previewing.value || busy.value) {
    return;
  }

  const scope = generation, version = ++previewVersion, id = selectedTemplateId.value, published = selectedPublished.value, controller = new AbortController();
  previewRequest?.abort(); previewRequest = controller;
  previewing.value = true; preview.value = undefined;
  problem.value = undefined;
  try {
    const body = await getPrintingTemplateVersion(id, published, controller.signal);
    if (current(scope) && can('preview') && version === previewVersion && !controller.signal.aborted && !!selectedTemplate.value && id === selectedTemplateId.value && selectedPublished.value === published) {
      const known = observedVersionIds.get(id.toLowerCase()) ?? new Map<number, string>();
      if (!validHostPrintingVersion(body) || !sameId(body.templateId, id) || body.versionNumber !== published || known.has(published) && !sameId(known.get(published)!, body.id)) throw Error('client.invalid_printing_preview_version');
      known.set(published, body.id); observedVersionIds.set(id.toLowerCase(), known);
      preview.value = { html: body.layoutHtml, versionNumber: body.versionNumber };
    }
  } catch (error: unknown) {
    if (current(scope) && version === previewVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.previewFailed');
  } finally {
    if (scope === generation && version === previewVersion) previewing.value = false;
  }
}

async function printPreview(): Promise<void> {
  if (!can('preview') || !catalogReady.value || !selectedTemplate.value || !selectedPublished.value || preview.value?.versionNumber !== selectedPublished.value || previewing.value || busy.value) return;
  const intent = { scope: generation, version: previewVersion + 1, id: selectedTemplateId.value, published: selectedPublished.value };
  printIntent = intent; await runPreview();
  if (printIntent === intent && (!preview.value || previewVersion !== intent.version)) printIntent = undefined;
}
/** 等新 iframe 正文加载后再打印，重新授权失败或旧 frame 的 load 事件均不能输出。 */
function onPreviewFrameLoaded(event: Event): void {
  const intent = printIntent;
  if (!intent || event.currentTarget !== frame.value || !current(intent.scope) || !can('preview') || !catalogReady.value || previewVersion !== intent.version
    || selectedTemplateId.value !== intent.id || selectedPublished.value !== intent.published || preview.value?.versionNumber !== intent.published) return;
  printIntent = undefined;
  try { frame.value?.contentWindow?.print(); } catch (error) { problem.value = toProblem(error, 'printingTemplates.printFailed'); }
}
/** 历史列表只读取二十条摘要，正文等用户选定具体版本后再读取。 */
async function loadHistory() {
  if (!can('read') || !catalogReady.value || !selectedTemplateId.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++historyVersion, controller = new AbortController(); historyRequest?.abort(); historyRequest = controller;
  const requestedPage = historyPage.value, key = id + ':' + requestedPage;
  if (key !== lastHistoryQuery) history.value = [];
  lastHistoryQuery = key; historyLoading.value = true; historyProblem.value = undefined;
  try {
    const result = await listPrintingTemplateVersions(id, requestedPage, controller.signal);
    if (current(scope) && version === historyVersion && selectedTemplateId.value === id && !controller.signal.aborted) {
      const known = observedVersionIds.get(id.toLowerCase()) ?? new Map<number, string>();
      if (result.total < (revisionFloors.get(id.toLowerCase())?.published ?? 0) || result.items.some(row => known.has(row.versionNumber) && !sameId(known.get(row.versionNumber)!, row.id))) throw Error('client.stale_printing_version_history');
      for (const row of result.items) known.set(row.versionNumber, row.id);
      observedVersionIds.set(id.toLowerCase(), known); history.value = result.items; historyTotal.value = result.total;
    }
  } catch (error) { if (current(scope) && version === historyVersion && !controller.signal.aborted) historyProblem.value = toProblem(error, 'printingPreview.loadFailed'); }
  finally { if (scope === generation && version === historyVersion) historyLoading.value = false; }
}

function openCreate() {
  if (!can('create') || busy.value || writeUnconfirmed.value && !createBlocked.value) return;
  if (loading.value) { listVersion++; listRequest?.abort(); loading.value = false; }
  detailVersion++; detailRequest?.abort();
  const recoveryDraft = createBlocked.value ? unconfirmedDraft : undefined;
  editingId.value = ''; editingVersion.value = 0; editorSchemaKey.value = recoveryDraft?.schema ?? 'printing.tenant_profile_card';
  createForm.value = recoveryDraft ? { ...recoveryDraft.form } : { templateKey: '', name: '', layoutHtml: DEFAULT_LAYOUT_HTML, isEnabled: true }; createDialogVisible.value = true;
}
/** 仅修改当前草稿文本，按选区替换；字段目录不能触发保存、发布或数据绑定。 */
async function insertLayoutField(fieldKey: string) {
  if (!createDialogVisible.value || !can(editingId.value ? 'update' : 'create') || !session.can('printing.form_schemas.read') || busy.value || writeUnconfirmed.value) return;
  const input = layoutInput.value?.textarea, body = createForm.value.layoutHtml, token = `{{${fieldKey}}}`;
  const start = Math.min(input?.selectionStart ?? body.length, body.length), end = Math.max(start, Math.min(input?.selectionEnd ?? start, body.length));
  if (body.length - (end - start) + token.length > 65536) { ElMessage.warning(t('printingTemplates.layoutFull')); return; }
  const scope = generation; createForm.value.layoutHtml = body.slice(0, start) + token + body.slice(end);
  const owner = editorEpoch; await nextTick();
  if (current(scope) && owner === editorEpoch && createDialogVisible.value && input) { input.focus(); input.setSelectionRange(start + token.length, start + token.length); }
}
async function editSelected() {
  if (!can('update') || !catalogReady.value || !selectedTemplateId.value || busy.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++detailVersion, controller = new AbortController(); detailRequest?.abort(); detailRequest = controller;
  try {
    const draft = await getPrintingTemplate(id, controller.signal); if (!current(scope) || selectedTemplateId.value !== id || version !== detailVersion || controller.signal.aborted) return;
    if (!currentDraft(draft, id)) throw Error('client.stale_printing_draft');
    editingId.value = id; editingVersion.value = draft.version; editorSchemaKey.value = draft.formSchemaKey; createForm.value = { templateKey: draft.templateKey, name: draft.name, layoutHtml: draft.layoutHtml, isEnabled: draft.isEnabled };
    createDialogVisible.value = true; writeUnconfirmed.value = false; problem.value = undefined;
  } catch (error) { if (current(scope) && version === detailVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}
async function confirmPublish() {
  if (!can('publish') || !catalogReady.value || !selectedTemplateId.value || busy.value || writeUnconfirmed.value || publicationBlocked.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++detailVersion, controller = new AbortController(); detailRequest?.abort(); detailRequest = controller;
  try { const target = await getPrintingTemplate(id, controller.signal); if (current(scope) && selectedTemplateId.value === id && version === detailVersion && !controller.signal.aborted) {
    if (!currentDraft(target, id)) throw Error('client.stale_printing_draft');
    publishTarget.value = target; changeNote.value = ''; publishOpen.value = true; } }
  catch (error) { if (current(scope) && version === detailVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}
async function publishSelected() {
  const target = publishTarget.value ? { ...publishTarget.value } : undefined;
  if (!publishOpen.value || !target || !sameId(target.id, selectedTemplateId.value) || !can('publish') || busy.value || writeUnconfirmed.value || publicationBlocked.value) return;
  const scope = generation, owner = publishEpoch, note = changeNote.value.trim() || null;
  const operation = { controller: new AbortController(), kind: 'publish' as const, receipt: { kind: 'publish' as const, outcome: 'processing' as const, templateKey: target.templateKey, templateId: target.id, draftVersion: target.version } };
  activeWrite = operation; writeReceipt.value = { ...operation.receipt }; publishing.value = true; beginWrite();
  const owned = () => activeWrite === operation && current(scope) && can('publish') && publishOpen.value && owner === publishEpoch;
  try {
    const result = await publishPrintingTemplate(target.id, { version: target.version, changeNote: note }, operation.controller.signal);
    if (!owned()) return;
    if (!validHostPrintingVersion(result) || !sameId(result.templateId, target.id) || result.versionNumber !== target.latestPublishedVersionNumber + 1
      || !sameId(result.publishedByUserId, session.currentUser!.id) || result.layoutHtml !== target.layoutHtml || result.changeNote !== note) throw Error('client.invalid_printing_publication');
    // 先固定已确认事实，再关闭自己的窗口；后续目录故障不能把成功降为未知。
    writeReceipt.value = { ...operation.receipt, outcome: 'succeeded', versionNumber: result.versionNumber };
    publicationBlocks.value.delete(target.id.toLowerCase());
    revisionFloors.set(target.id.toLowerCase(), { draft: target.version + 1, published: result.versionNumber });
    const known = observedVersionIds.get(target.id.toLowerCase()) ?? new Map<number, string>(); known.set(result.versionNumber, result.id); observedVersionIds.set(target.id.toLowerCase(), known);
    templates.value = templates.value.map(row => sameId(row.id, target.id) ? { ...row, version: target.version + 1, latestPublishedVersionNumber: result.versionNumber } : row);
    selectedPublished.value = result.versionNumber; history.value = []; historyPage.value = 1; historyTotal.value = 0;
    activeWrite = undefined; publishing.value = false; publishOpen.value = false; ElMessage.success(t('printingTemplates.published')); await load();
  } catch (error) {
    if (owned()) { if (uncertain(error)) markUnconfirmed(operation); else writeReceipt.value = { ...operation.receipt, outcome: 'failed' };
      writeUnconfirmed.value = uncertain(error); problem.value = toProblem(error, 'printingPreview.createFailed'); activeWrite = undefined; publishing.value = false; publishOpen.value = false; }
  } finally { if (activeWrite === operation) { activeWrite = undefined; publishing.value = false; } }
}

async function submitCreate(): Promise<void> {
  if (!createDialogVisible.value || !canSaveDraft.value) return;
  const scope = generation, owner = editorEpoch, id = editingId.value, revision = editingVersion.value, schema = editorSchemaKey.value, form = { ...createForm.value };
  const kind = id ? 'update' as const : 'create' as const;
  const operation = { controller: new AbortController(), kind, receipt: { kind, outcome: 'processing' as const, templateKey: form.templateKey, templateId: id || undefined }, draft: { form, schema } };
  const owned = () => activeWrite === operation && current(scope) && can(id ? 'update' : 'create') && owner === editorEpoch && createDialogVisible.value;
  activeWrite = operation; writeReceipt.value = { ...operation.receipt }; creating.value = true; beginWrite(); problem.value = undefined;
  try {
    const body = { name: form.name.trim(), layoutHtml: form.layoutHtml, isEnabled: form.isEnabled };
    const created = id ? await updatePrintingTemplate(id, { ...body, version: revision }, operation.controller.signal)
      : await createPrintingTemplate({ templateKey: form.templateKey.trim(), formSchemaKey: schema, ...body }, operation.controller.signal);
    if (!owned()) return;
    if (!validHostPrintingTemplate(created) || created.name !== body.name || created.isEnabled !== body.isEnabled || created.templateKey !== form.templateKey.trim()
      || created.formSchemaKey !== schema || (id ? !sameId(created.id, id) || created.version !== revision + 1 : created.version !== 1 || created.latestPublishedVersionNumber !== 0 || revisionFloors.has(created.id.toLowerCase()))) throw Error('client.invalid_printing_saved_draft');
    writeReceipt.value = { ...operation.receipt, outcome: 'succeeded', templateId: created.id };
    revisionFloors.set(created.id.toLowerCase(), { draft: created.version, published: created.latestPublishedVersionNumber });
    activeWrite = undefined; creating.value = false; unconfirmedDraft = undefined; createBlocked.value = false;
    createDialogVisible.value = false; const postOwner = editorEpoch; ElMessage.success(t('printingTemplates.saved'));
    await load();
    if (!current(scope) || postOwner !== editorEpoch || createDialogVisible.value) return;
    if (catalogReady.value && templates.value.some(item => sameId(item.id, created.id))) selectedTemplateId.value = created.id;
    preview.value = undefined;
  } catch (error) {
    if (owned()) { const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status === 408 || error.status >= 500;
      if (unknown) markUnconfirmed(operation); else { writeReceipt.value = { ...operation.receipt, outcome: 'failed' }; writeUnconfirmed.value = !!id && error.status === 409; }
      problem.value = toProblem(error, 'printingPreview.createFailed'); }
  } finally { if (activeWrite === operation) { activeWrite = undefined; creating.value = false; } }
}
/** 写入开始时作废更早的目录/版本读取，未知结果不能被写前快照解除锁定。 */
function beginWrite() {
  printIntent = undefined;
  listVersion++; historyVersion++; previewVersion++;
  listRequest?.abort(); historyRequest?.abort(); previewRequest?.abort(); loading.value = false; previewing.value = false; preview.value = undefined;
}

function reset() {
  publicationBlocks.value.clear(); lastUnknownKind = undefined; grantWindow.value = undefined;
  printIntent = undefined; revisionFloors.clear(); observedVersionIds.clear(); catalogReady.value = false; catalogProblem.value = undefined;
  activeWrite?.controller.abort(); activeWrite = undefined; writeReceipt.value = undefined; unconfirmedDraft = undefined; createBlocked.value = false; recoveryPrompt.value = undefined;
  generation++; previewVersion++; listVersion++; detailVersion++; historyVersion++; requests.abort(); requests = new AbortController(); detailRequest?.abort(); previewRequest?.abort(); historyRequest?.abort(); listRequest?.abort();
  templates.value = []; selectedTemplateId.value = ''; preview.value = undefined; problem.value = undefined; createDialogVisible.value = false; publishOpen.value = false;
  publishTarget.value = undefined; writeUnconfirmed.value = false; loading.value = false; previewing.value = false; creating.value = false; publishing.value = false;
  editingId.value = ''; editingVersion.value = 0; createForm.value = { templateKey: '', name: '', layoutHtml: DEFAULT_LAYOUT_HTML, isEnabled: true };
  editorSchemaKey.value = 'printing.tenant_profile_card';
  lastListQuery = ''; lastHistoryQuery = ''; historyLoading.value = false; historyProblem.value = undefined;
  page.value = 1; total.value = 0; searchValue.value = ''; history.value = []; historyPage.value = 1; historyTotal.value = 0; selectedPublished.value = undefined;
}
watch(() => [session.state, route?.name, session.currentUser?.scope, session.currentUser?.actorScope, session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching, active.value], () => { reset(); if (can('read')) void load(); }, { immediate: true, flush: 'sync' });
watch(selectedTemplateId, () => { grantWindow.value = undefined; if (recoveryPrompt.value?.kind === 'publish') recoveryPrompt.value = undefined;
  if (lastUnknownKind === 'publish' && !createDialogVisible.value) writeUnconfirmed.value = publicationBlocked.value;
  previewVersion++; detailVersion++; previewRequest?.abort(); detailRequest?.abort(); preview.value = undefined; previewing.value = false; publishOpen.value = false; }, { flush: 'sync' });
watch(() => [selectedTemplateId.value, selectedTemplate.value?.latestPublishedVersionNumber], () => {
  historyVersion++; historyRequest?.abort(); historyLoading.value = false; historyProblem.value = undefined; lastHistoryQuery = '';
  historyPage.value = 1; history.value = []; historyTotal.value = 0; selectedPublished.value = selectedTemplate.value?.latestPublishedVersionNumber || undefined;
  if (can('read') && selectedTemplateId.value && selectedTemplate.value) void loadHistory();
}, { flush: 'sync' });
watch(selectedPublished, () => { previewVersion++; previewRequest?.abort(); preview.value = undefined; previewing.value = false; }, { flush: 'sync' });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; reset(); });
/** 每次渲染冻结目录与窗口归属，旧按钮不能借用刷新后的选择或新弹窗。 */
function catalogAction(action: () => unknown, row = false) {
  const scope = generation, catalog = listVersion, id = selectedTemplateId.value, revision = selectedTemplate.value?.version, published = selectedPublished.value;
  return () => {
    if (!current(scope) || catalog !== listVersion
      || row && (id !== selectedTemplateId.value || revision !== selectedTemplate.value?.version || published !== selectedPublished.value)) return;
    if ((action === openCreate || action === editSelected || action === confirmPublish || action === openGrants) && (createDialogVisible.value || publishOpen.value)) return;
    void action();
  };
}
function catalogSelection() {
  const scope = generation;
  const key = (row: PrintingTemplateSummary) => JSON.stringify([row.id.toLowerCase(), row.version, row.latestPublishedVersionNumber, row.templateKey, row.formSchemaKey, row.isEnabled]);
  const choices = new Map(templates.value.map(row => [row.id.toLowerCase(), key(row)]));
  // 展开选择器会先触发同条件远程读取；允许选择仍在当前目录且修订未变的可见候选。
  return (id: string) => { const row = templates.value.find(item => sameId(item.id, id));
    if (current(scope) && !busy.value && !createDialogVisible.value && row && choices.get(id.toLowerCase()) === key(row)) selectedTemplateId.value = row.id; };
}
function catalogSearch() {
  const scope = generation, catalog = listVersion;
  return (keyword: string) => { if (current(scope) && catalog === listVersion) void searchTemplates(keyword); };
}
function catalogPage(delta: number) {
  return catalogAction(() => { const next = page.value + delta; if (!loading.value && next >= 1 && next <= Math.max(1, Math.ceil(total.value / 20))) { page.value = next; void load(); } });
}
function historyAction(action: () => unknown) {
  const scope = generation, catalog = listVersion, history = historyVersion, id = selectedTemplateId.value, requestedPage = historyPage.value;
  return () => { if (current(scope) && catalog === listVersion && history === historyVersion && id === selectedTemplateId.value && requestedPage === historyPage.value) void action(); };
}
function historySelection() {
  const scope = generation, catalog = listVersion, historyOwner = historyVersion, id = selectedTemplateId.value;
  return (number: number) => { if (current(scope) && catalog === listVersion && historyOwner === historyVersion && id === selectedTemplateId.value && !busy.value
    && history.value.some(row => row.versionNumber === number)) selectedPublished.value = number; };
}
function historyPageAction(delta: number) {
  return historyAction(() => { const next = historyPage.value + delta; if (!historyLoading.value && next >= 1 && next <= Math.max(1, Math.ceil(historyTotal.value / 20))) { historyPage.value = next; void loadHistory(); } });
}
</script>

<template>
  <div class="printing-preview-view">
    <ElAlert v-if="!can('read')" type="info" :closable="false" :title="t('printingTemplates.hostOnly')" />
    <ElCard v-if="can('read')" shadow="never" class="art-table-card no-print">
      <ArtTableHeader :loading="loading" v-on="{ refresh: catalogAction(load) }">
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              data-testid="printing-preview-create"
              :disabled="busy || writeUnconfirmed && !createBlocked"
              v-on="{ click: catalogAction(openCreate) }"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
        </template>
      </ArtTableHeader>
      <div v-if="writeReceipt" role="status" data-testid="printing-write-receipt"><p>{{ writeReceipt.templateKey }} <code v-if="writeReceipt.templateId">{{ writeReceipt.templateId }}</code></p><p>{{ receiptMessage }} <span v-if="writeReceipt.versionNumber">{{ writeReceipt.versionNumber }}</span></p></div>
      <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="t('printingTemplates.unconfirmed')" data-testid="printing-template-unconfirmed" />
      <ElButton :loading="loading" :disabled="busy" data-testid="printing-template-refresh" v-on="{ click: catalogAction(load) }">{{ t('common.refresh') }}</ElButton>
      <div class="template-actions"><ElButton :disabled="busy || loading || page <= 1" v-on="{ click: catalogPage(-1) }">{{ t('printingTemplates.previous') }}</ElButton><span>{{ page }}</span><ElButton :disabled="busy || loading || page * 20 >= total" v-on="{ click: catalogPage(1) }">{{ t('printingTemplates.next') }}</ElButton></div>

      <ElAlert
        v-if="problem"
        type="error"
        :title="problem.title"
        show-icon
        class="mb-4"
      />

      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect
            :model-value="selectedTemplateId" v-on="{ 'update:modelValue': catalogSelection() }"
            filterable remote :remote-method="catalogSearch()"
            data-testid="printing-preview-template"
            :disabled="busy || createDialogVisible"
            class="w-full"
          >
            <ElOption
              v-for="template in templates"
              :key="template.id"
              :label="template.name"
              :value="template.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <ElButton v-if="can('preview')"
            type="primary"
            :icon="Printer"
            :loading="previewing"
            :disabled="!selectedPublished || !catalogReady || busy"
            data-testid="printing-preview-run"
            v-on="{ click: catalogAction(runPreview, true) }"
          >
            {{ t('printingTemplates.previewPublished') }}
          </ElButton>
          <ElButton
            v-if="preview && can('preview')"
            :disabled="!preview || !catalogReady || previewing || busy"
            data-testid="printing-preview-print"
            v-on="{ click: catalogAction(printPreview, true) }"
          >
            {{ t('printingPreview.print') }}
          </ElButton>
        </ElFormItem>
      </ElForm>
      <p v-if="selectedTemplate" data-testid="printing-template-versions">{{ t('printingTemplates.draftVersion') }} {{ selectedTemplate.version }} · {{ t('printingTemplates.publishedVersion') }} {{ selectedTemplate.latestPublishedVersionNumber }}</p>
      <p>{{ t('printingTemplates.versionHelp') }}</p>
      <ElForm v-if="selectedTemplate" label-position="top"><ElFormItem :label="t('printingTemplates.history')">
        <ElSelect :model-value="selectedPublished" v-on="{ 'update:modelValue': historySelection() }" :disabled="busy || historyLoading" data-testid="printing-template-version" class="w-full">
          <ElOption v-for="item in history" :key="item.id" :value="item.versionNumber" :label="`${item.versionNumber} · ${item.changeNote ?? ''}`" />
        </ElSelect>
      </ElFormItem></ElForm>
      <ElAlert v-if="historyProblem" type="error" :closable="false" :title="historyProblem.title" data-testid="printing-template-history-problem" />
      <ElButton v-if="historyProblem" :disabled="busy || historyLoading" data-testid="printing-template-history-retry" v-on="{ click: historyAction(loadHistory) }">{{ t('common.refresh') }}</ElButton>
      <div v-if="selectedTemplate" class="template-actions"><ElButton :disabled="busy || historyLoading || historyPage <= 1" v-on="{ click: historyPageAction(-1) }">{{ t('printingTemplates.previous') }}</ElButton><span>{{ historyPage }}</span><ElButton :disabled="busy || historyLoading || historyPage * 20 >= historyTotal" v-on="{ click: historyPageAction(1) }">{{ t('printingTemplates.next') }}</ElButton></div>
      <div class="template-actions">
        <ElButton v-if="can('update') && selectedTemplate" :disabled="!catalogReady || busy || writeUnconfirmed" data-testid="printing-template-edit" v-on="{ click: catalogAction(editSelected, true) }">{{ t('printingTemplates.edit') }}</ElButton>
        <ElButton v-if="can('publish') && selectedTemplate" type="primary" :disabled="!catalogReady || busy || writeUnconfirmed || publicationBlocked || createDialogVisible" data-testid="printing-template-publish" v-on="{ click: catalogAction(confirmPublish, true) }">{{ t('printingTemplates.publish') }}</ElButton>
        <ElButton v-if="can('publish') && publicationBlocked" :disabled="!catalogReady || busy" data-testid="printing-publish-review" v-on="{ click: catalogAction(acknowledgePublicationRecovery, true) }">{{ t('printingTemplates.reviewPublication') }}</ElButton>
        <ElButton v-if="can('grant_tenants') && selectedTemplate?.latestPublishedVersionNumber" :disabled="!catalogReady || busy" data-testid="printing-tenant-grants-open" v-on="{ click: catalogAction(openGrants, true) }">{{ t('printingTemplates.tenantGrants') }}</ElButton>
      </div>
    </ElCard>

    <section v-if="preview && can('preview')" class="printing-preview-surface print-only-surface">
      <p>{{ t('printingTemplates.previewVersion') }} {{ preview.versionNumber }}</p>
      <ElAlert type="info" :closable="false" :title="t('printingTemplates.sampleHelp')" />
      <iframe ref="frame" @load="onPreviewFrameLoaded" class="printing-preview-html" :title="t('printingTemplates.layoutTitle')" :srcdoc="safePreviewHtml" sandbox="allow-same-origin allow-modals" referrerpolicy="no-referrer" />
    </section>

    <ElDialog
      ref="editorDialog" @open="editorLandmarks" @opened="editorLandmarks"
      :model-value="createDialogVisible" v-on="{ 'update:modelValue': editorControls.update }"
      :title="t(editingId ? 'printingTemplates.edit' : 'printingTemplates.create')"
      width="min(760px, calc(100vw - 32px))"
      :close-on-click-modal="false" :close-on-press-escape="true" :show-close="true"
      class="no-print"
    >
      <p v-if="createBlocked && !editingId" id="printing-create-blocked-help" role="status">{{ t('printingPreview.createUnconfirmedReceipt') }}</p>
      <ElButton v-if="createBlocked && !editingId" data-testid="printing-create-review" :disabled="busy" v-on="{ click: editorControls.recover }">{{ t('printingPreview.recoveryTitle') }}</ElButton>
      <ElAlert v-if="creating" type="info" :closable="false" :title="t('printingTemplates.closeInFlight')" />
      <ElForm label-position="top">
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="t('printingTemplates.unconfirmed')" />
        <ElFormItem :label="t('printingPreview.fieldTemplateKey')" required>
          <ElInput :model-value="createForm.templateKey" v-on="{ 'update:modelValue': editorControls.key }" :disabled="!!editingId || busy" maxlength="128" data-testid="printing-template-key" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldName')" required>
          <ElInput :model-value="createForm.name" v-on="{ 'update:modelValue': editorControls.name }" :disabled="busy" maxlength="128" data-testid="printing-template-name" />
        </ElFormItem>
        <ElFormItem :label="t('printingTemplates.schema')">
          <PrintingFormSchemaSelector v-if="createDialogVisible" :model-value="editorSchemaKey" :layout-html="createForm.layoutHtml" :immutable="!!editingId" :disabled="busy||writeUnconfirmed||createBlocked" v-on="{'update:modelValue':editorControls.schema,replace:editorControls.schemaLayout}" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldLayoutHtml')" required>
          <ElInput ref="layoutInput" :model-value="createForm.layoutHtml" v-on="{ 'update:modelValue': editorControls.layout }" :disabled="busy" type="textarea" :rows="10" :maxlength="65536" show-word-limit data-testid="printing-template-layout" />
        </ElFormItem>
        <ElFormItem v-if="createDialogVisible && session.can('printing.form_schemas.read')" :label="t('printingTemplates.fields')">
          <PrintingTemplateFields :form-schema-key="editorSchemaKey" :disabled="busy || writeUnconfirmed" v-on="{ insert: editorControls.insert }" />
        </ElFormItem>
        <ElFormItem :label="t('printingTemplates.enabled')"><ElSwitch :model-value="createForm.isEnabled" v-on="{ 'update:modelValue': editorControls.enabled }" :disabled="busy" :aria-label="t('printingTemplates.enabled')" /></ElFormItem>
        <p>{{ t('printingTemplates.enabledHelp') }}</p>
      </ElForm>
      <template #footer>
        <ElButton data-testid="printing-preview-create-back" v-on="{ click: editorControls.close }">
          {{ t('printingTemplates.close') }}
        </ElButton>
        <ElPopconfirm v-if="editingId" :title="t('printingTemplates.discardConfirm')" width="280" v-on="{ confirm: editorControls.reload }">
          <template #reference><ElButton :disabled="busy" data-testid="printing-template-reload-draft">{{ t('printingTemplates.reloadDraft') }}</ElButton></template>
        </ElPopconfirm>
        <ElButton v-if="can(editingId ? 'update' : 'create')" type="primary" :loading="creating" :disabled="!canSaveDraft" :aria-describedby="createBlocked ? 'printing-create-blocked-help' : undefined" data-testid="printing-preview-submit" v-on="{ click: editorControls.save }">
          {{ t('printingTemplates.save') }}
        </ElButton>
      </template>
    </ElDialog>
    <ElDialog ref="publicationDialog" @open="publicationLandmarks" @opened="publicationLandmarks" :model-value="publishOpen" v-on="{ 'update:modelValue': publishControls.update }" :title="t('printingTemplates.publish')" width="min(480px, calc(100vw - 32px))" :close-on-click-modal="false" :close-on-press-escape="true" :show-close="true">
      <p>{{ publishTarget?.name }}</p><p>{{ t('printingTemplates.publishHelp') }}</p><ElAlert v-if="publishing" type="info" :closable="false" :title="t('printingTemplates.closeInFlight')" />
      <ElForm label-position="top"><ElFormItem :label="t('printingTemplates.changeNote')"><ElInput :model-value="changeNote" v-on="{ 'update:modelValue': publishControls.note }" :disabled="busy" maxlength="512" data-testid="printing-template-change-note" /></ElFormItem></ElForm>
      <template #footer><ElButton data-testid="printing-publish-close" v-on="{ click: publishControls.close }">{{ t('printingTemplates.close') }}</ElButton><ElButton type="primary" :loading="publishing" :disabled="busy || writeUnconfirmed || !can('publish')" data-testid="printing-template-confirm-publish" v-on="{ click: publishControls.confirm }">{{ t('printingTemplates.publish') }}</ElButton></template>
    </ElDialog>
    <ArtFormDialog v-if="recoveryPrompt" :key="recoveryPrompt.token" :open="true" width="min(520px, calc(100vw - 32px))" :title="t('printingPreview.recoveryTitle')"
      :confirm-label="t('common.confirm')" :cancel-label="t('common.cancel')" :confirm-test-id="recoveryPrompt.kind === 'create' ? 'printing-create-review-confirm' : 'printing-publish-review-confirm'"
      v-on="{ confirm: recoveryPrompt.confirm, 'update:open': recoveryPrompt.close }"><p>{{ t('printingPreview.recoveryHint') }}</p></ArtFormDialog>
    <PrintingTenantGrantsDialog v-if="grantWindow" :key="grantWindow.token" :template="grantWindow.template" v-on="{ close: grantWindow.close }" />
  </div>
</template>

<style scoped>
.printing-preview-surface {
  margin-top: 16px;
  padding: 24px;
  background: #fff;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.template-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.printing-preview-html { display: block; width: 100%; min-height: 480px; border: 1px solid var(--el-border-color); margin-top: 12px; }

.printing-preview-html :deep(.print-card) {
  max-width: 720px;
  margin: 0 auto;
  font-family: Arial, sans-serif;
}

@media print {
  .no-print {
    display: none !important;
  }

  .printing-preview-view {
    padding: 0;
  }

  .printing-preview-surface {
    margin: 0;
    padding: 0;
    border: none;
  }
}
</style>
