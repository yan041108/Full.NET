<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import 'element-plus/theme-chalk/el-alert.css';
import { documentPreviewFailureLabel } from './documentPreviewFailureLabel';
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElDrawer,
  ElDescriptions,
  ElDescriptionsItem,
  ElForm,
  ElFormItem,
  ElInput,
  ElSelect,
  ElOption,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus, Refresh, Search } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { translateDocumentPreviewLookup } from '@fullnet/admin-i18n';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useBoundedPolling } from '../composables/useBoundedPolling';
import { useUnavailableResultFiles } from '../composables/useUnavailableResultFiles';
import {
  cancelDocumentPreviewTask, getDocumentPreviewCapabilities, retryDocumentPreviewTask,
  createDocumentPreviewTask,
  getDocumentPreviewTask,
  listDocumentPreviewTasks,
  openDocumentPreviewTaskContent
} from '../api/document-preview-tasks';

defineOptions({ name: 'DocumentPreviewTasksView' });

const session = useSessionStore();
const route = inject(routeLocationKey, undefined);
const pageActive = ref(false); let linkBlocked = false; let linkedTaskId: string | undefined;
onMounted(() => { pageActive.value = true; }); onActivated(() => { pageActive.value = true; });
onDeactivated(() => { pageActive.value = false; }); onBeforeUnmount(() => { pageActive.value = false; });
const { t, locale } = useAdminI18n();
const lookupText = (key: Parameters<typeof translateDocumentPreviewLookup>[1]) => translateDocumentPreviewLookup(locale.value, key);
const taskLookup = ref(''), lookupProblem = ref<string>();
const lookupSequence = ref(0);
const items = ref<HostDocumentPreviewTaskResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const cancelConfirmation = ref<HostDocumentPreviewTaskResponse>();
let cancelSequence = 0;
const cancelHandlers = ref<{ confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const problem = ref<FullNetProblemDetails>();
const capabilityEnabled = ref(false);
const detailProblem = ref<FullNetProblemDetails>();
const detailTargetId = ref(''), detailOwner = ref(0);
let detailBaseline: HostDocumentPreviewTaskResponse | undefined;
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const filterDocumentItemId = ref('');
const appliedDocumentItemId = ref('');
const statusDraft = ref('all'), appliedStatus = ref<string>();
const statusKeys = ['preparing', 'pending', 'processing', 'succeeded', 'failed', 'cancelled'] as const;
let invalidQuery = false;
const confirmedCancellations = new Map<string, HostDocumentPreviewTaskResponse>();
const editorOpen = ref(false);
const regenerationOrigin = ref<HostDocumentPreviewTaskResponse>();
const detailVisible = ref(false); const detailLoading = ref(false);
const selectedTask = ref<HostDocumentPreviewTaskResponse>();
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  documentItemId: '',
  versionId: ''
});
type Receipt = { sequence: number; kind: 'create' | 'cancel'; documentItemId: string; versionId: string | null; taskId?: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode?: string };
const receipt = ref<Receipt>(); let receiptSequence = 0;
const creationBlocks = ref(new Map<string, Receipt>()), cancellationBlocks = ref(new Map<string, HostDocumentPreviewTaskResponse>());
const createKey = (document: string, version?: string | null) => document.toLowerCase() + ':' + (version?.toLowerCase() || '');
const validId = (id: string) => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000';
const createBlocked = computed(() => creationBlocks.value.has(createKey(editorForm.documentItemId.trim(), editorForm.versionId.trim())));
let editorSequence = 0, listSequence = 0, recoverySequence = 0;
const recoveryConfirmation = ref<{ token: number; confirm: () => void; update: (open: boolean) => void }>();
let lastQuery: { page: number; pageSize: number; documentItemId: string | undefined; statusKey?: string } | undefined;
let mutation: { request: NonNullable<ReturnType<typeof scope.begin>>; receipt: Receipt; task?: HostDocumentPreviewTaskResponse; dispatched: boolean } | undefined;
interface ListControls { token: number; input: (value: string) => void; apply: () => void; submit: (event: Event) => void; key: (event: KeyboardEvent) => void; refresh: () => void; create: () => void; page: (value: number) => void; size: (value: number) => void; change: (nextPage: number, nextSize: number) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  const token = ++listSequence, owned = () => listControls.value.token === token && canRead();
  const canPage = () => owned() && !loading.value && !invalidQuery && !(problem.value && items.value.length === 0);
  const apply = () => { if (owned()) void applyFilter(); };
  return { token, input: value => { if (owned()) filterDocumentItemId.value = value; }, apply, submit: event => { event.preventDefault(); apply(); }, key: event => { if (event.key === 'Enter') { event.preventDefault(); apply(); } },
    refresh: () => { if (owned()) void refreshResults(); }, create: () => { if (owned()) openCreate(); }, page: value => { if (canPage() && Number.isSafeInteger(value) && value > 0) page.value = value; }, size: value => { if (canPage() && [10, 20, 50].includes(value)) pageSize.value = value; },
    change: (nextPage, nextSize) => {
      if (!canPage() || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10, 20, 50].includes(nextSize)) return;
      page.value = lastQuery?.pageSize !== nextSize ? 1 : nextPage; pageSize.value = nextSize;
      if (lastQuery?.page !== page.value || lastQuery.pageSize !== nextSize) void load();
    } };
}
const tableRows = computed(() => {
  const token = listControls.value.token;
  return items.value.map(row => { const task = { ...row }, owned = () => listControls.value.token === token && canRead() && !!currentTask(task);
    return { ...task, detail: () => { if (owned()) void openDetail(task.id); }, pdf: () => { if (owned()) void openPdf(task); }, cancel: () => { if (owned()) requestCancellation(task); }, regenerate: () => { if (owned()) openRegenerate(task); }, retry: () => { if (owned()) void retryTask(task); }, reviewRetry: () => { if (owned()) reviewRetry(task); } }; });
});
const scope = useAuthorizedViewScope(session, () => {
  lookupSequence.value++; taskLookup.value = ''; lookupProblem.value = undefined;
  statusDraft.value = 'all'; appliedStatus.value = undefined; invalidQuery = false; confirmedCancellations.clear();
  linkBlocked = true; linkedTaskId = undefined; detailVisible.value = false; clearDetail();
  cancelConfirmation.value = undefined;
  unavailableFiles.reset(); capabilityEnabled.value = false; pollingFailed.value = false;
  items.value = []; total.value = 0; page.value = 1; problem.value = undefined;
  editorOpen.value = false; filterDocumentItemId.value = ''; appliedDocumentItemId.value = ''; editorForm.documentItemId = ''; editorForm.versionId = '';
  loading.value = false; changing.value = false;
  receipt.value = undefined; creationBlocks.value = new Map(); cancellationBlocks.value = new Map(); recoveryConfirmation.value = undefined; mutation = undefined;
  lastQuery = undefined; listControls.value = makeListControls();
}, async () => { openLinkedTask(); await load(); });
let listRequest: ReturnType<typeof scope.begin>;
let createRequest: ReturnType<typeof scope.begin>;
let detailRequest: ReturnType<typeof scope.begin>;
let actionRequest: ReturnType<typeof scope.begin>;
let detailActionRequest: ReturnType<typeof scope.begin>;
let pdfRequest: ReturnType<typeof scope.begin>;
let pdfTarget: HostDocumentPreviewTaskResponse | undefined;
const host = () => pageActive.value && !session.switching && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = () => host() && (!route || route.name === 'document-preview-tasks') && session.can('document.host_preview_tasks.read');
const canCreate = () => canRead() && capabilityEnabled.value && session.can('document.host_preview_tasks.create');
const begin = (permission: string) => canRead() ? scope.begin(permission) : undefined;
function beginAction(permission: string) {
  if (changing.value) return;
  const request = begin(permission); if (!request) return;
  actionRequest = request; changing.value = true; problem.value = undefined; return request;
}
function cancelAction(request: typeof actionRequest): void {
  if (request?.current() && mutation?.request === request && mutation.dispatched) markUnconfirmed(mutation);
  if (mutation?.request === request) mutation = undefined;
  request?.cancel(); if (request && actionRequest === request) { actionRequest = undefined; changing.value = false; }
}
function finishAction(request: NonNullable<typeof actionRequest>): void {
  if (request.current() && actionRequest === request) { actionRequest = undefined; changing.value = false; }
  request.finish();
  if (mutation?.request === request) mutation = undefined;
}
// 关闭同步撤销本弹窗创建的接入资格；已到服务端的写入仍由服务端完成。
watch(() => session.switching, switching => { scope.invalidate(); if (!switching) { openLinkedTask(); void load(); } }, { flush: 'sync' });
watch(editorOpen, open => {
  editorSequence++; recoveryConfirmation.value = undefined;
  if (!open) {
    regenerationOrigin.value = undefined;
    cancelAction(createRequest); createRequest = undefined;
    editorForm.documentItemId = ''; editorForm.versionId = '';
  }
}, { flush: 'sync' });
watch(() => JSON.stringify([editorForm.documentItemId, editorForm.versionId]), () => { cancelAction(createRequest); createRequest = undefined; editorSequence++; recoveryConfirmation.value = undefined; }, { flush: 'sync' });
const editorControls = computed(() => {
  const token = editorSequence, open = editorOpen.value, document = editorForm.documentItemId, version = editorForm.versionId;
  const owned = () => token === editorSequence && open && editorOpen.value && document === editorForm.documentItemId && version === editorForm.versionId && canCreate();
  return { document: (value: string) => { if (owned() && !changing.value && !regenerationOrigin.value) editorForm.documentItemId = value; }, version: (value: string) => { if (owned() && !changing.value && !regenerationOrigin.value) editorForm.versionId = value; },
    submit: () => { if (owned()) void submitCreate(); }, update: (value: boolean) => { if (!value && owned()) editorOpen.value = false; }, recovery: () => { if (owned()) acknowledgeCreate(); } };
});

function clearDetail(): void {
  detailBaseline = undefined;
  detailOwner.value++; detailTargetId.value = ''; detailProblem.value = undefined;
  detailRequest?.cancel(); detailRequest = undefined; cancelAction(detailActionRequest); detailActionRequest = undefined;
  selectedTask.value = undefined; detailLoading.value = false; cancelConfirmation.value = undefined;
}
watch(detailVisible, open => {
  if (!open) { clearDetail(); linkBlocked = true; linkedTaskId = undefined; }
}, { flush: 'sync' });
function openLinkedTask(): void {
  const id = route?.query.taskId;
  if (linkBlocked || !canRead() || !isGuid(id) || id === '00000000-0000-0000-0000-000000000000'
    || linkedTaskId === id.toLowerCase()) return;
  linkedTaskId = id.toLowerCase(); void openDetail(id);
}
// 链接只定位任务；作用域恢复不重放旧链接，停用期间显式变化的链接在恢复后才读取。
watch(() => JSON.stringify([route?.name, route?.query.taskId]), () => {
  const wasBlocked = linkBlocked;
  cancelAction(actionRequest);
  clearDetail(); detailVisible.value = false; linkedTaskId = undefined; editorOpen.value = false;
  lookupSequence.value++; taskLookup.value = ''; lookupProblem.value = undefined;
  linkBlocked = route?.name !== 'document-preview-tasks';
  if (linkBlocked) {
    listRequest?.cancel(); items.value = []; total.value = 0; loading.value = false;
    lastQuery = undefined; listControls.value = makeListControls();
  } else {
    openLinkedTask(); if (wasBlocked && pageActive.value) void load();
  }
}, { flush: 'sync' });
async function openDetail(id: string): Promise<void> {
  if (!canRead() || !isGuid(id) || id === '00000000-0000-0000-0000-000000000000') return;
  const row = items.value.find(task => sameId(task.id, id));
  clearDetail(); detailBaseline = row ? { ...row } : undefined; detailTargetId.value = id; editorOpen.value = false; await refreshDetail(id, true);
}
const lookupControls = computed(() => {
  const sequence = lookupSequence.value, draft = taskLookup.value;
  const owned = () => sequence === lookupSequence.value && canRead();
  return { input: (value: string) => { if (owned()) { taskLookup.value = value; lookupProblem.value = undefined; } },
    submit: () => {
      if (!owned() || changing.value || editorOpen.value || cancelDialogVisible.value) return;
      const id = draft.trim().toLowerCase();
      if (!validId(id)) { lookupProblem.value = lookupText('invalid'); return; }
      lookupProblem.value = undefined; void openDetail(id);
    } };
});
async function refreshDetail(id: string, open = false): Promise<void> {
  if (!canRead() || !validId(id) || !sameId(id, detailTargetId.value) || !open && !detailVisible.value) return;
  detailRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); if (!request) return;
  detailRequest = request; if (open) detailVisible.value = true; detailLoading.value = true; detailProblem.value = undefined;
  try {
    const task = await getDocumentPreviewTask(id, request.signal);
    if (!request.current()) return;
    if (task.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_document_preview_task_identity');
    if (!Number.isSafeInteger(task.version) || task.version < 1 || [detailBaseline, cancellationBlocks.value.get(id.toLowerCase()), selectedTask.value].some(baseline => baseline && (!sameTaskSource(task, baseline) || task.version < baseline.version))) throw new Error('client.invalid_document_preview_task_identity');
    detailBaseline = { ...task };
    selectedTask.value = task;
    const next = new Map(cancellationBlocks.value); next.delete(id.toLowerCase()); cancellationBlocks.value = next;
  } catch (error) {
    if (request.current()) detailProblem.value = toProblem(error);
  } finally { if (request.current()) detailLoading.value = false; request.finish(); }
}
const sameId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
function sameTaskSource(left: HostDocumentPreviewTaskResponse, right: HostDocumentPreviewTaskResponse): boolean {
  return sameId(left.id, right.id) && sameId(left.documentItemId, right.documentItemId) && sameId(left.versionId, right.versionId)
    && sameId(left.sourceFileId, right.sourceFileId) && sameId(left.requestedByUserId, right.requestedByUserId);
}
const detailControls = computed(() => {
  const owner = detailOwner.value, target = detailTargetId.value, task = selectedTask.value ? { ...selectedTask.value } : undefined;
  const owned = () => owner === detailOwner.value && canRead() && detailVisible.value && (!task || !!currentTask(task));
  return { update: (open: boolean) => { if (!open && owner === detailOwner.value && canRead()) detailVisible.value = false; },
    refresh: () => { if (owned() && !changing.value && !detailLoading.value) { unavailableFiles.reset(); void refreshDetail(target); } },
    regenerate: () => { if (owned() && task) openRegenerate(task); }, retry: () => { if (owned() && task) void retryTask(task); }, reviewRetry: () => { if (owned() && task) reviewRetry(task); }, pdf: () => { if (owned() && task) void openPdf(task); }, cancel: () => { if (owned() && task) requestCancellation(task); } };
});
function findTask(id: string): HostDocumentPreviewTaskResponse | undefined {
  return detailVisible.value && sameId(selectedTask.value?.id, id) ? selectedTask.value : items.value.find(item => sameId(item.id, id));
}
function currentTask(row: HostDocumentPreviewTaskResponse): HostDocumentPreviewTaskResponse | undefined {
  const current = findTask(row.id);
  return current && current.version === row.version && current.statusKey === row.statusKey
    && sameId(current.documentItemId, row.documentItemId) && sameId(current.versionId, row.versionId)
    && sameId(current.sourceFileId, row.sourceFileId) && sameId(current.outputFileId, row.outputFileId)
    && sameId(current.requestedByUserId, row.requestedByUserId) ? current : undefined;
}
// PDF 由适配器执行最终打开；目录/详情变更必须先同步取消信号，阻止迟到 Blob 打开。
watch([items, selectedTask, detailVisible], () => {
  if (pdfTarget && !currentTask(pdfTarget)) {
    cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined;
  }
}, { flush: 'sync', deep: true });

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  syncTableLayout
} = useArtPagedTableInCard(loading);

function statusTagType(statusKey: string): 'success' | 'warning' | 'danger' | 'info' {
  if (statusKey === 'succeeded') {
    return 'success';
  }
  if (statusKey === 'failed') {
    return 'danger';
  }
  if (statusKey === 'processing') {
    return 'warning';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  if (statusKey === 'cancelled') return t('importExportTasks.status.cancelled');
  const key = `documentPreviewTasks.status.${statusKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}

const pollingFailed = ref(false);
const isRunning = (task?: HostDocumentPreviewTaskResponse) => !!task && ['preparing', 'pending', 'processing'].includes(task.statusKey);
const progress = useBoundedPolling({
  enabled: () => canRead() && !loading.value && !changing.value && !editorOpen.value && !cancelDialogVisible.value
    && !problem.value && !detailProblem.value && !creationBlocks.value.size && !cancellationBlocks.value.size,
  pending: () => items.value.some(isRunning) || detailVisible.value && isRunning(selectedTask.value),
  key: () => JSON.stringify([session.currentUser?.sessionId, session.currentUser?.id, page.value, pageSize.value, appliedDocumentItemId.value, appliedStatus.value]),
  intervalMs: 5000,
  read: async signal => {
    const query = { page: page.value, pageSize: pageSize.value, documentItemId: appliedDocumentItemId.value || undefined, statusKey: appliedStatus.value };
    return { query, result: await listDocumentPreviewTasks(query, signal) };
  },
  accept: ({ query, result }) => {
    if (result.page !== query.page || result.pageSize !== query.pageSize || query.documentItemId && result.items.some(row => !sameId(row.documentItemId, query.documentItemId))) throw new Error('client.invalid_document_preview_task_page');
    items.value = rowsForQuery(result.items, query.statusKey);
    total.value = result.total;
    if (detailVisible.value && !detailLoading.value && isRunning(selectedTask.value)) void refreshDetail(detailTargetId.value);
  },
  failed: error => { pollingFailed.value = true; problem.value = toProblem(error); }
});
const progressPaused = computed(() => progress.paused.value || pollingFailed.value);

async function load(recoverPage = true): Promise<boolean> {
  if (!canRead() || invalidQuery || !Number.isSafeInteger(page.value) || page.value < 1 || ![10, 20, 50].includes(pageSize.value)) return false;
  pollingFailed.value = false;
  listRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); listRequest = request;
  if (!request) return false;
  const query = { page: page.value, pageSize: pageSize.value, documentItemId: appliedDocumentItemId.value || undefined, statusKey: appliedStatus.value };
  const changedQuery = !lastQuery || lastQuery.page !== query.page || lastQuery.pageSize !== query.pageSize || !sameId(lastQuery.documentItemId, query.documentItemId) || lastQuery.statusKey !== query.statusKey;
  if (changedQuery) {
    cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined; items.value = []; listControls.value = makeListControls();
  }
  lastQuery = query;
  loading.value = true;
  problem.value = undefined;
  try {
    const [capability, result] = await Promise.all([getDocumentPreviewCapabilities(request.signal).catch(() => ({ enabled: false })), listDocumentPreviewTasks(query, request.signal)]);
    if (!request.current()) return false;
    if (result.page !== query.page || result.pageSize !== query.pageSize || query.documentItemId && result.items.some(row => !sameId(row.documentItemId, query.documentItemId))) throw new Error('client.invalid_document_preview_task_page');
    capabilityEnabled.value = capability.enabled === true;
    const lastPage = Math.max(1, Math.ceil(result.total / query.pageSize));
    if (recoverPage && !result.items.length && query.page > lastPage) { page.value = lastPage; return await load(false); }
    items.value = rowsForQuery(result.items, query.statusKey);
    total.value = result.total;
    return request.current();
  } catch (error) {
    if (request.current()) { problem.value = toProblem(error); if (changedQuery) total.value = 0; }
    return false;
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
  }
}

async function applyFilter(): Promise<void> {
  if (!canRead() || changing.value && !pdfTarget || editorOpen.value || cancelDialogVisible.value) return;
  const documentItemId = filterDocumentItemId.value.trim();
  const statusKey = statusDraft.value === 'all' ? undefined : statusDraft.value;
  if (documentItemId && !validId(documentItemId) || statusKey && !statusKeys.includes(statusKey as typeof statusKeys[number])) {
    invalidQuery = true; listRequest?.cancel(); loading.value = false; items.value = []; total.value = 0; page.value = 1; listControls.value = makeListControls();
    problem.value = { status: 400, code: 'client.invalid_document_preview_filter', title: documentItemId && !validId(documentItemId) ? t('documentPreviewTasks.invalidIdentity') : lookupText('invalidStatus') }; return;
  }
  // 输入是草稿，分页、轮询与刷新只能复用已明确应用的查询。
  cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined;
  invalidQuery = false; appliedDocumentItemId.value = documentItemId; appliedStatus.value = statusKey; page.value = 1;
  await load();
}
function rowsForQuery(rows: HostDocumentPreviewTaskResponse[], statusKey?: string): HostDocumentPreviewTaskResponse[] {
  // 已确认取消是当前会话内的权威下界；陈旧源页不能把任务恢复为待执行。
  const current = rows.map(row => {
    const confirmed = confirmedCancellations.get(row.id.toLowerCase());
    const existing = items.value.find(item => sameId(item.id, row.id));
    return confirmed && sameTaskSource(confirmed, row) && confirmed.version >= row.version ? confirmed : existing && existing.version > row.version ? existing : row;
  });
  if (statusKey && current.some(row => row.statusKey !== statusKey)) throw new Error('client.invalid_document_preview_task_page');
  return current;
}

function openCreate() {
  if (!canCreate() || changing.value || cancelDialogVisible.value) return;
  detailVisible.value = false;
  regenerationOrigin.value = undefined;
  editorForm.documentItemId = appliedDocumentItemId.value;
  editorForm.versionId = '';
  editorOpen.value = true;
}

function canRegenerate(row: HostDocumentPreviewTaskResponse): boolean {
  return canCreate() && row.statusKey === 'succeeded' && !!row.versionId && validId(row.versionId)
    && unavailableFiles.isUnavailable(row.id) && !!currentTask(row);
}
function openRegenerate(row: HostDocumentPreviewTaskResponse): void {
  if (!canRegenerate(row) || changing.value || detailLoading.value || editorOpen.value || cancelDialogVisible.value) return;
  // 缺失结果只允许重新处理明确的原版本，不把当前版本指针替代历史输入。
  const original = { ...row }; openCreate(); if (!editorOpen.value) return;
  editorForm.documentItemId = original.documentItemId; editorForm.versionId = original.versionId!;
  regenerationOrigin.value = original;
}

const retryEligible = (task: HostDocumentPreviewTaskResponse) => canRead() && capabilityEnabled.value
  && session.can('document.host_preview_tasks.retry')
  && ['failed', 'cancelled'].includes(task.statusKey);
const retryBlocked = (task: HostDocumentPreviewTaskResponse) => creationBlocks.value.has(createKey(task.documentItemId, task.versionId));
async function retryTask(row: HostDocumentPreviewTaskResponse): Promise<void> {
  const current = currentTask(row); if (!current || !retryEligible(current) || retryBlocked(current) || changing.value || detailLoading.value || editorOpen.value || cancelDialogVisible.value) return;
  const target = { ...current }, request = beginAction('document.host_preview_tasks.retry'); if (!request) return;
  // 重试会新建任务；未知结果沿用同来源创建门槛，不能靠切换入口重复提交。
  receipt.value = { sequence: ++receiptSequence, kind: 'create', documentItemId: target.documentItemId, versionId: target.versionId, outcome: 'processing' };
  mutation = { request, receipt: { ...receipt.value }, task: target, dispatched: true };
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  if (detailVisible.value && sameId(selectedTask.value?.id, target.id)) detailActionRequest = request;
  try {
    const result = await retryDocumentPreviewTask(target.id, request.signal); if (!request.current()) return;
    if (!validId(result.id) || sameId(result.id, target.id) || !sameId(result.documentItemId, target.documentItemId)
      || target.versionId && !sameId(result.versionId, target.versionId) || !sameId(result.requestedByUserId, session.currentUser?.id)) throw Error('client.invalid_document_preview_task_retry');
    mutation!.dispatched = false; receipt.value = { ...receipt.value!, taskId: result.id, versionId: result.versionId, outcome: 'succeeded' };
    finishAction(request); detailActionRequest = undefined;
    clearDetail(); detailTargetId.value = result.id; selectedTask.value = result; detailVisible.value = true;
    ElMessage.success(t('documentPreviewTasks.createSuccess')); await Promise.all([load(), refreshDetail(result.id)]);
  } catch (error) { if (request.current()) mutationFailed(error, 'documentPreviewTasks.operationFailed'); }
  finally { finishAction(request); }
}
function reviewRetry(row: HostDocumentPreviewTaskResponse): void {
  const task = currentTask(row), key = task && createKey(task.documentItemId, task.versionId), block = key && creationBlocks.value.get(key);
  if (!task || !key || !block || !retryEligible(task) || changing.value || editorOpen.value || loading.value || detailLoading.value) return;
  const token = ++recoverySequence;
  const owned = () => recoveryConfirmation.value?.token === token && canRead() && retryEligible(task) && !!currentTask(task) && creationBlocks.value.get(key)?.sequence === block.sequence;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { creationBlocks.value.delete(key); recoveryConfirmation.value = undefined; } }, update: open => { if (!open && owned()) recoveryConfirmation.value = undefined; } };
}

async function submitCreate() {
  if (changing.value || createBlocked.value || !editorOpen.value || !canCreate()) return;
  const documentItemId = editorForm.documentItemId.trim();
  const versionId = editorForm.versionId.trim();
  if (!validId(documentItemId) || versionId && !validId(versionId)) {
    problem.value = { status: 400, code: 'client.invalid_create_document_preview_task_request', title: t('documentPreviewTasks.invalidIdentity') };
    return;
  }
  const request = beginAction('document.host_preview_tasks.create'); if (!request) return; createRequest = request;
  const target = { documentItemId, versionId: versionId || null };
  const original = regenerationOrigin.value;
  if (original && (!sameId(original.documentItemId, target.documentItemId) || !sameId(original.versionId, target.versionId))) { finishAction(request); return; }
  receipt.value = { sequence: ++receiptSequence, kind: 'create', ...target, outcome: 'processing' }; mutation = { request, receipt: { ...receipt.value }, dispatched: true };
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  try {
    const created = await createDocumentPreviewTask(target, request.signal);
    if (!request.current()) return;
    if (!validId(created.id) || original && sameId(created.id, original.id) || !Number.isSafeInteger(created.version) || created.version < 1 || !sameId(created.documentItemId, target.documentItemId) || !sameId(created.versionId, target.versionId)
      || !sameId(created.requestedByUserId, session.currentUser?.id)) throw new Error('client.invalid_document_preview_task_identity');
    mutation!.dispatched = false; listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    receipt.value = { ...receipt.value!, taskId: created.id, outcome: 'succeeded' };
    if ((!appliedDocumentItemId.value || sameId(appliedDocumentItemId.value, created.documentItemId)) && (!appliedStatus.value || appliedStatus.value === created.statusKey) && page.value === 1) {
      const existed = items.value.some(row => sameId(row.id, created.id));
      items.value = [created, ...items.value.filter(row => !sameId(row.id, created.id))].slice(0, pageSize.value); if (!existed) total.value++;
    }
    // 先保留确认事实并解除创建请求归属，再关闭窗口；关闭不能取消本次成功处理。
    createRequest = undefined; finishAction(request);
    editorOpen.value = false;
    ElMessage.success(t('documentPreviewTasks.createSuccess'));
    clearDetail(); detailTargetId.value = created.id; selectedTask.value = created; detailVisible.value = true;
    await Promise.all([load(), refreshDetail(created.id)]);
  } catch (error) {
    if (request.current()) mutationFailed(error, 'documentPreviewTasks.operationFailed');
  } finally {
    finishAction(request);
  }
}

function markUnconfirmed(pending: NonNullable<typeof mutation>): void {
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  const result = { ...pending.receipt, outcome: 'unconfirmed' as const };
  receipt.value = result;
  if (result.kind === 'create') creationBlocks.value = new Map(creationBlocks.value).set(createKey(result.documentItemId, result.versionId), result);
  else if (pending.task) cancellationBlocks.value = new Map(cancellationBlocks.value).set(pending.task.id.toLowerCase(), { ...pending.task });
}
function mutationFailed(error: unknown, fallback: 'documentPreviewTasks.operationFailed' | 'documentPreviewTasks.cancelFailed'): void {
  const failure = toProblem(error, fallback), unknown = !isFullNetProblemDetails(error) || failure.status < 400 || failure.status >= 500 || failure.status === 408;
  if (mutation) {
    mutation.receipt = { ...mutation.receipt, errorCode: failure.code };
    if (unknown) markUnconfirmed(mutation); else receipt.value = { ...mutation.receipt, outcome: 'failed' };
  }
  problem.value = failure;
}
function acknowledgeCreate(): void {
  if (!canCreate() || !editorOpen.value || changing.value || !createBlocked.value) return;
  const key = createKey(editorForm.documentItemId.trim(), editorForm.versionId.trim()), sequence = creationBlocks.value.get(key)!.sequence, owner = editorSequence, token = ++recoverySequence;
  const owned = () => recoveryConfirmation.value?.token === token && editorSequence === owner && editorOpen.value && canCreate() && creationBlocks.value.get(key)?.sequence === sequence;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { const next = new Map(creationBlocks.value); next.delete(key); creationBlocks.value = next; recoveryConfirmation.value = undefined; } }, update: open => { if (!open && owned()) recoveryConfirmation.value = undefined; } };
}

async function openPdf(row: HostDocumentPreviewTaskResponse) {
  const current = currentTask(row);
  const task = current ? { ...current } : undefined;
  if (task?.statusKey !== 'succeeded' || detailLoading.value || changing.value || editorOpen.value || cancelDialogVisible.value || !canRead() || unavailableFiles.isUnavailable(task.id)) {
    return;
  }
  const request = beginAction('document.host_preview_tasks.read'); if (!request) return;
  pdfRequest = request; pdfTarget = task;
  if (detailVisible.value && sameId(selectedTask.value?.id, task.id)) detailActionRequest = request;
  const fileRequest = unavailableFiles.begin(task.id);
  try {
    await openDocumentPreviewTaskContent(task.id, request.signal);
    if (request.current() && fileRequest.current() && currentTask(task)) fileRequest.markReadable();
  } catch (error) {
    if (request.current() && fileRequest.current() && currentTask(task)) problem.value = fileRequest.markUnavailable(error)
      ? { title: t('dataOutput.resultUnavailable'), status: 404, code: 'files.file.not_found' }
      : toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    if (pdfRequest === request) { pdfRequest = undefined; pdfTarget = undefined; }
    finishAction(request);
  }
}

async function refreshResults(): Promise<void> {
  if (changing.value) return;
  unavailableFiles.reset();
  await Promise.all([load(), detailVisible.value && detailTargetId.value && !detailLoading.value ? refreshDetail(detailTargetId.value) : Promise.resolve()]);
}

const isCancellable = (task: HostDocumentPreviewTaskResponse) => canRead() && ['preparing', 'pending', 'processing'].includes(task.statusKey)
  && sameId(task.requestedByUserId, session.currentUser?.id)
  && session.can('document.host_preview_tasks.cancel') && !cancellationBlocks.value.has(task.id.toLowerCase());

function requestCancellation(task: HostDocumentPreviewTaskResponse) {
  const current = currentTask(task);
  if (!current || !isCancellable(current) || detailLoading.value || changing.value || editorOpen.value || cancelDialogVisible.value) return;
  const token = ++cancelSequence; cancelConfirmation.value = { ...current };
  // 捕获本次令牌，旧弹窗的确认/关闭事件不能接入下一次确认。
  const dismiss = () => { if (token === cancelSequence) cancelConfirmation.value = undefined; };
  cancelHandlers.value = { confirm: () => { void confirmCancellation(token); }, dismiss, update: open => { if (!open) dismiss(); } };
}

async function confirmCancellation(token = cancelSequence) {
  if (token !== cancelSequence) return;
  const confirmed = cancelConfirmation.value;
  cancelConfirmation.value = undefined;
  const row = confirmed && findTask(confirmed.id);
  const current = row ? { ...row } : undefined;
  if (!confirmed || !current || !isCancellable(current) || detailLoading.value || changing.value || editorOpen.value) return;
  if (!currentTask(confirmed)) { ElMessage.warning(t('documentPreviewTasks.cancelStateChanged')); return; }
  const request = beginAction('document.host_preview_tasks.cancel'); if (!request) return;
  const taskId = current.id;
  receipt.value = { sequence: ++receiptSequence, kind: 'cancel', documentItemId: current.documentItemId, versionId: current.versionId, taskId, outcome: 'processing' };
  mutation = { request, receipt: { ...receipt.value }, task: current, dispatched: true };
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  if (detailVisible.value && sameId(selectedTask.value?.id, taskId)) detailActionRequest = request;
  try {
    const result = await cancelDocumentPreviewTask(current, request.signal);
    if (!request.current()) return;
    if (!sameTaskSource(result, current) || result.statusKey !== 'cancelled' || !Number.isSafeInteger(result.version) || result.version < current.version) throw new Error('client.invalid_document_preview_task_cancellation');
    mutation!.dispatched = false; listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    receipt.value = { ...receipt.value!, outcome: 'succeeded' };
    confirmedCancellations.set(taskId.toLowerCase(), { ...result });
    const wasListed = items.value.some(item => sameId(item.id, taskId));
    items.value = items.value.flatMap(item => sameId(item.id, taskId) && item.version <= result.version ? appliedStatus.value && appliedStatus.value !== result.statusKey ? [] : [result] : [item]);
    if (wasListed && appliedStatus.value && appliedStatus.value !== result.statusKey) total.value = Math.max(0, total.value - 1);
    if (detailVisible.value && sameId(selectedTask.value?.id, taskId)) selectedTask.value = result;
    ElMessage.success(t('documentPreviewTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (request.current()) mutationFailed(error, 'documentPreviewTasks.cancelFailed');
  } finally {
    finishAction(request);
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentPreviewTasks.loadFailed' | 'documentPreviewTasks.operationFailed' | 'documentPreviewTasks.cancelFailed' = 'documentPreviewTasks.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

</script>

<template>
  <section v-if="canRead()" class="document-preview-tasks-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPreviewTasks.title') }}</h1>
    <ElAlert v-if="canRead() && !capabilityEnabled" type="info" :closable="false" :title="t('documentPreviewTasks.conversionDisabled')" data-testid="document-preview-disabled" />
    <ElAlert v-if="receipt?.outcome === 'unconfirmed'" type="warning" :closable="false" :title="t('documentPreviewTasks.writeUnconfirmed')" data-testid="document-preview-unconfirmed" />
    <ElAlert v-if="progressPaused" type="info" :closable="false" :title="t('documentPreviewTasks.refreshPaused')" />

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />
    <p v-if="problem && !invalidQuery && items.length === 0" data-testid="document-preview-requested-page">{{ lookupText('requestedPage') }} {{ page }}</p>

    <div v-if="receipt" role="status" data-testid="document-preview-receipt">
      <p>{{ t('documentPreviewTasks.receiptTarget', {document:receipt.documentItemId,version:receipt.versionId ?? t('documentPreviewTasks.currentVersion')}) }} · {{ t(receipt.kind === 'create' ? 'documentPreviewTasks.addTask' : 'common.cancel') }}</p>
      <p>{{ t(receipt.outcome === 'succeeded' ? 'documentPreviewTasks.resultSucceeded' : receipt.outcome === 'failed' ? 'documentPreviewTasks.resultFailed' : receipt.outcome === 'processing' ? 'documentPreviewTasks.resultProcessing' : 'documentPreviewTasks.resultUnconfirmed') }} <code v-if="receipt.taskId" translate="no">{{ receipt.taskId }}</code> <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code></p>
      <p v-if="receipt.kind === 'cancel' && receipt.outcome === 'unconfirmed'">{{ t('documentPreviewTasks.cancelRecoveryHint') }}</p>
    </div>

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPreviewTasks.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" :disabled="changing" data-testid="document-preview-refresh" v-on="{ click: listControls.refresh }">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>
        <el-form class="document-module-inline-query" :inline="true" v-on="{ submit: listControls.submit }">
          <el-form-item :label="t('documentPreviewTasks.status')">
            <ElSelect v-model="statusDraft" class="document-preview-status-filter" data-testid="document-preview-status-filter" :disabled="changing && !pdfTarget || editorOpen || cancelDialogVisible">
              <ElOption value="all" :label="lookupText('allStatuses')" />
              <ElOption v-for="key in statusKeys" :key="key" :value="key" :label="statusLabel(key)" />
            </ElSelect>
          </el-form-item>
          <el-form-item :label="t('documentPreviewTasks.documentItemId')">
            <el-input
              :model-value="filterDocumentItemId"
              class="document-preview-tasks-view__filter"
              :placeholder="t('documentPreviewTasks.filterDocumentItemId')"
              clearable
              data-testid="document-preview-task-filter"
              v-on="{ 'update:modelValue': listControls.input, keydown: listControls.key, clear: listControls.apply }"
            />
          </el-form-item>
          <el-form-item>
             <el-button type="primary" :icon="Search" data-testid="document-preview-filter-submit" :disabled="changing && !pdfTarget || editorOpen || cancelDialogVisible" v-on="{ click: listControls.apply }">{{ t('documentPermissions.query') }}</el-button>
          </el-form-item>
        </el-form>
        <p data-testid="document-preview-applied-status">{{ lookupText('submittedStatus') }} {{ appliedStatus ? statusLabel(appliedStatus) : lookupText('allStatuses') }}</p>
        <form class="document-preview-lookup" @submit.prevent="lookupControls.submit">
          <label for="document-preview-exact-id">{{ lookupText('taskId') }}</label>
          <div class="document-preview-lookup__controls">
            <ElInput id="document-preview-exact-id" :model-value="taskLookup" data-testid="document-preview-exact-id" autocomplete="off"
              :disabled="changing || editorOpen || cancelDialogVisible" v-on="{ 'update:modelValue': lookupControls.input }" />
            <ElButton native-type="submit" data-testid="document-preview-read-id" :disabled="changing || editorOpen || cancelDialogVisible">{{ lookupText('read') }}</ElButton>
          </div>
          <p>{{ lookupText('hint') }}</p>
          <ElAlert v-if="lookupProblem" type="error" :closable="false" :title="lookupProblem" data-testid="document-preview-lookup-problem" />
        </form>
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen"
          v-on="{ refresh: listControls.refresh }"
        >
          <template #left>
            <PermissionGate code="document.host_preview_tasks.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-preview-task-create" v-if="canCreate()" :disabled="changing"
                v-on="{ click: listControls.create }"
              >
                {{ t('documentPreviewTasks.addTask') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>
      <div ref="tableMainRef" class="art-crud-table-main">
        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            v-loading="loading"
            :data="tableRows"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
            <el-table-column :label="t('documentPreviewTasks.documentTitle')" prop="documentTitle" min-width="180" />
            <el-table-column :label="t('documentPreviewTasks.documentItemId')" prop="documentItemId" min-width="280" />
            <el-table-column :label="t('documentPreviewTasks.status')" width="140">
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.statusKey)" size="small">
                  {{ statusLabel(row.statusKey) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('documentPreviewTasks.provider')" prop="providerKey" width="160" />
            <el-table-column :label="t('documentPreviewTasks.failureReason')" min-width="240">
              <template #default="{ row }">{{ documentPreviewFailureLabel(row.errorCode, t) }}</template>
            </el-table-column>
            <el-table-column :label="t('documentPreviewTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" width="224" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <ArtTableActionButton type="view" test-id="document-preview-task-detail"
                    :title="t('documentPreviewTasks.details')" v-on="{ click: row.detail }" />
                  <PermissionGate v-if="row.statusKey === 'succeeded'" code="document.host_preview_tasks.read">
                    <ArtTableActionButton
                      type="view"
                      test-id="document-preview-task-open-pdf"
                      :title="t('documentPreviewTasks.openPdf')"
                      :disabled="changing || editorOpen || cancelDialogVisible || unavailableFiles.isUnavailable(row.id)"
                      v-on="{ click: row.pdf }"
                    />
                  </PermissionGate>
                  <ElButton v-if="retryEligible(row as HostDocumentPreviewTaskResponse)" data-testid="document-preview-task-retry" :disabled="changing || editorOpen || cancelDialogVisible || retryBlocked(row as HostDocumentPreviewTaskResponse)" v-on="{ click: row.retry }">{{ t('documentPreviewTasks.retryTask') }}</ElButton>
                  <ElButton v-if="retryEligible(row as HostDocumentPreviewTaskResponse) && retryBlocked(row as HostDocumentPreviewTaskResponse)" data-testid="document-preview-task-review-retry" :disabled="changing || loading" v-on="{ click: row.reviewRetry }">{{ t('documentPreviewTasks.reviewCreation') }}</ElButton>
                  <ElButton v-if="isCancellable(row as HostDocumentPreviewTaskResponse)" link type="warning"
                    data-testid="document-preview-task-cancel" :data-task-id="row.id" :disabled="changing || editorOpen"
                    v-on="{ click: row.cancel }">{{ t('common.cancel') }}</ElButton>
                </ArtTableActionGroup>
                <p v-if="unavailableFiles.isUnavailable(row.id)" class="document-preview-tasks-view__result-status" data-testid="document-result-unavailable" role="status">{{ t('dataOutput.resultUnavailable') }}</p>
                <ElButton v-if="canRegenerate(row as HostDocumentPreviewTaskResponse)" data-testid="document-preview-task-regenerate" :disabled="changing || editorOpen || cancelDialogVisible" v-on="{ click: row.regenerate }">{{ t('documentPreviewTasks.regenerate') }}</ElButton>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPreviewTasks.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          :current-page="page"
          :disabled="loading || !!problem && items.length === 0"
          :page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          v-on="{ 'update:currentPage': listControls.page, 'update:pageSize': listControls.size, change: listControls.change }"
        />
      </div>
    </el-card>

    <ElDrawer :model-value="detailVisible" v-on="{ 'update:modelValue': detailControls.update }" :title="t('documentPreviewTasks.detailTitle')" size="min(560px, 100vw)">
      <p>{{ lookupText('taskId') }} <code class="document-preview-identity" data-testid="document-preview-detail-id">{{ detailTargetId }}</code></p>
      <p v-if="detailProblem && selectedTask" role="status" data-testid="document-preview-stale-snapshot">{{ lookupText('stale') }}</p>
      <div v-loading="detailLoading" data-testid="document-preview-detail">
        <ElAlert v-if="detailProblem" type="error" :closable="false" data-testid="document-preview-detail-problem" :title="detailProblem.title" :description="detailProblem.code" />
        <ElButton data-testid="document-preview-detail-refresh" :disabled="changing || detailLoading" v-on="{ click: detailControls.refresh }">{{ t('documentPreviewTasks.refreshDetail') }}</ElButton>
        <ElDescriptions v-if="selectedTask" :column="1" border>
          <ElDescriptionsItem :label="lookupText('versionId')"><code class="document-preview-identity" data-testid="document-preview-version-id">{{ selectedTask.versionId ?? lookupText('currentFile') }}</code></ElDescriptionsItem>
          <ElDescriptionsItem :label="lookupText('sourceFile')"><code class="document-preview-identity" data-testid="document-preview-source-file">{{ selectedTask.sourceFileId }}</code></ElDescriptionsItem>
          <ElDescriptionsItem :label="lookupText('outputFile')"><code class="document-preview-identity" data-testid="document-preview-output-file">{{ selectedTask.outputFileId ?? lookupText('absent') }}</code></ElDescriptionsItem>
          <ElDescriptionsItem :label="lookupText('requester')"><code class="document-preview-identity" data-testid="document-preview-requester">{{ selectedTask.requestedByUserId ?? lookupText('absent') }}</code></ElDescriptionsItem>
          <ElDescriptionsItem :label="lookupText('revision')">{{ selectedTask.version }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.documentTitle')">{{ selectedTask.documentTitle }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.documentItemId')">{{ selectedTask.documentItemId }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.status')">{{ statusLabel(selectedTask.statusKey) }}</ElDescriptionsItem>
          <ElDescriptionsItem v-if="selectedTask.statusKey === 'succeeded' && canRead()" :label="t('dataOutput.resultAvailability')">
            <span role="status" data-testid="document-preview-result-availability">{{ t(unavailableFiles.availabilityMessageKey(selectedTask.id)) }}</span>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.provider')">{{ selectedTask.providerKey }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.createdAtUtc')">{{ selectedTask.createdAtUtc }}</ElDescriptionsItem>
          <ElDescriptionsItem v-if="selectedTask.errorCode" :label="t('documentPreviewTasks.failureReason')">{{ documentPreviewFailureLabel(selectedTask.errorCode, t) }}</ElDescriptionsItem>
        </ElDescriptions>
        <template v-if="selectedTask">
          <ElButton v-if="selectedTask.statusKey === 'succeeded'" data-testid="document-preview-detail-open-pdf"
            :disabled="detailLoading || changing || editorOpen || cancelDialogVisible || unavailableFiles.isUnavailable(selectedTask.id)"
             v-on="{ click: detailControls.pdf }">{{ t('documentPreviewTasks.openPdf') }}</ElButton>
          <ElButton v-if="canRegenerate(selectedTask)" data-testid="document-preview-detail-regenerate" :disabled="changing || detailLoading || editorOpen || cancelDialogVisible" v-on="{ click: detailControls.regenerate }">{{ t('documentPreviewTasks.regenerate') }}</ElButton>
          <ElButton v-if="retryEligible(selectedTask)" data-testid="document-preview-detail-retry" :disabled="changing || detailLoading || editorOpen || cancelDialogVisible || retryBlocked(selectedTask)" v-on="{ click: detailControls.retry }">{{ t('documentPreviewTasks.retryTask') }}</ElButton>
          <ElButton v-if="retryEligible(selectedTask) && retryBlocked(selectedTask)" data-testid="document-preview-detail-review-retry" :disabled="changing || detailLoading || loading" v-on="{ click: detailControls.reviewRetry }">{{ t('documentPreviewTasks.reviewCreation') }}</ElButton>
          <ElButton v-if="isCancellable(selectedTask)" data-testid="document-preview-detail-cancel"
             :disabled="detailLoading || changing || editorOpen" v-on="{ click: detailControls.cancel }">{{ t('common.cancel') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
    <ArtFormDialog
      :open="editorOpen"
      width="min(520px, 94vw)"
      v-on="{ 'update:open': editorControls.update, confirm: editorControls.submit }"
      :title="t(regenerationOrigin ? 'documentPreviewTasks.regenerate' : 'documentPreviewTasks.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-preview-task-editor-submit"
      :show-confirm="canCreate() && !createBlocked"
    >
      <ElAlert v-if="createBlocked" type="warning" :closable="false" :title="t('documentPreviewTasks.recoveryHint')" />
      <ElButton v-if="createBlocked" data-testid="document-preview-recovery" :disabled="changing" v-on="{ click: editorControls.recovery }">{{ t('documentPreviewTasks.reviewCreation') }}</ElButton>
      <el-form ref="editorFormRef" data-testid="document-preview-task-editor-form" :model="editorForm" label-width="140px">
        <el-form-item :label="t('documentPreviewTasks.documentItemId')" required>
          <el-input :model-value="editorForm.documentItemId" :disabled="changing || !!regenerationOrigin" autocomplete="off" v-on="{ 'update:modelValue': editorControls.document }" />
        </el-form-item>
        <el-form-item :label="t('documentPreviewTasks.versionIdOptional')">
          <el-input :model-value="editorForm.versionId" :disabled="changing || !!regenerationOrigin" autocomplete="off" v-on="{ 'update:modelValue': editorControls.version }" />
        </el-form-item>
      </el-form>
      <p v-if="regenerationOrigin" data-testid="document-preview-regeneration-hint">{{ t('documentPreviewTasks.regenerateHint') }}</p>
    </ArtFormDialog>
    <ArtFormDialog v-if="recoveryConfirmation && canRead()" :key="recoveryConfirmation.token" :open="true" :title="t('documentPreviewTasks.reviewCreation')"
      width="min(520px, 94vw)"
      :confirm-label="t('documentPreviewTasks.allowCreation')" :cancel-label="t('common.cancel')" confirm-test-id="document-preview-recovery-confirm"
      v-on="{ confirm: recoveryConfirmation.confirm, 'update:open': recoveryConfirmation.update }">
      <p>{{ t('documentPreviewTasks.recoveryConfirm') }}</p>
    </ArtFormDialog>
    <ElDialog v-if="cancelConfirmation && cancelHandlers" :key="cancelSequence" :model-value="true"
      :title="t('documentPreviewTasks.cancelConfirmTitle')" width="min(480px, 100vw)" :close-on-click-modal="false"
      v-on="{ 'update:modelValue': cancelHandlers.update }">
      <p>{{ t('documentPreviewTasks.cancelConfirmDescription') }}</p>
      <template #footer>
        <ElButton data-testid="document-preview-cancel-dismiss" v-on="{ click: cancelHandlers.dismiss }">{{ t('common.cancel') }}</ElButton>
        <ElButton type="warning" data-testid="document-preview-cancel-confirm" v-on="{ click: cancelHandlers.confirm }">{{ t('common.confirm') }}</ElButton>
      </template>
    </ElDialog>
  </section>
</template>

<style scoped>
.document-preview-tasks-view {
  flex: 1;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-preview-tasks-view__filter {
  width: 280px;
  margin-right: 12px;
}
.document-preview-tasks-view__result-status {
  margin: 6px 0;
  line-height: 1.5;
}
.document-preview-lookup { margin-block: 12px; }
.document-preview-lookup__controls { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 8px; }
.document-preview-lookup__controls :deep(.el-input) { flex: 1 1 240px; max-width: 520px; }
.document-preview-identity { overflow-wrap: anywhere; }
.document-preview-status-filter { width: 200px; }
@media (max-width: 640px) {
  .document-module-inline-query :deep(.el-form-item) { display: flex; flex-wrap: wrap; max-width: 100%; margin-right: 0; }
  .document-module-inline-query :deep(.el-form-item__content) { min-width: 0; max-width: 100%; }
  .document-preview-tasks-view__filter { max-width: 100%; margin-right: 0; }
}
</style>
