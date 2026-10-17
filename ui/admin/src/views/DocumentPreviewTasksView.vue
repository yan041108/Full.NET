<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
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
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useTaskStatusRefresh } from '../composables/useTaskStatusRefresh';
import { useUnavailableResultFiles } from '../composables/useUnavailableResultFiles';
import {
  cancelDocumentPreviewTask,
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
const { t } = useAdminI18n();
const items = ref<HostDocumentPreviewTaskResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const cancelConfirmation = ref<HostDocumentPreviewTaskResponse>();
let cancelSequence = 0;
const cancelHandlers = ref<{ confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const problem = ref<FullNetProblemDetails>();
const detailProblem = ref<FullNetProblemDetails>();
const detailTargetId = ref(''), detailOwner = ref(0);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const filterDocumentItemId = ref('');
const appliedDocumentItemId = ref('');
const editorOpen = ref(false);
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
let lastQuery: { page: number; pageSize: number; documentItemId: string | undefined } | undefined;
let mutation: { request: NonNullable<ReturnType<typeof scope.begin>>; receipt: Receipt; task?: HostDocumentPreviewTaskResponse; dispatched: boolean } | undefined;
interface ListControls { token: number; input: (value: string) => void; apply: () => void; submit: (event: Event) => void; key: (event: KeyboardEvent) => void; refresh: () => void; create: () => void; page: (value: number) => void; size: (value: number) => void; change: (nextPage: number, nextSize: number) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  const token = ++listSequence, owned = () => listControls.value.token === token && canRead();
  const apply = () => { if (owned()) void applyFilter(); };
  return { token, input: value => { if (owned()) filterDocumentItemId.value = value; }, apply, submit: event => { event.preventDefault(); apply(); }, key: event => { if (event.key === 'Enter') { event.preventDefault(); apply(); } },
    refresh: () => { if (owned()) void refreshResults(); }, create: () => { if (owned()) openCreate(); }, page: value => { if (owned() && Number.isSafeInteger(value) && value > 0) page.value = value; }, size: value => { if (owned() && [10, 20, 50].includes(value)) pageSize.value = value; },
    change: (nextPage, nextSize) => {
      if (!owned() || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10, 20, 50].includes(nextSize)) return;
      page.value = lastQuery?.pageSize !== nextSize ? 1 : nextPage; pageSize.value = nextSize;
      if (lastQuery?.page !== page.value || lastQuery.pageSize !== nextSize) void load();
    } };
}
const tableRows = computed(() => {
  const token = listControls.value.token;
  return items.value.map(row => { const task = { ...row }, owned = () => listControls.value.token === token && canRead() && !!currentTask(task);
    return { ...task, detail: () => { if (owned()) void openDetail(task.id); }, pdf: () => { if (owned()) void openPdf(task); }, cancel: () => { if (owned()) requestCancellation(task); } }; });
});
const scope = useAuthorizedViewScope(session, () => {
  linkBlocked = true; linkedTaskId = undefined; detailVisible.value = false; clearDetail();
  cancelConfirmation.value = undefined;
  unavailableFiles.reset();
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
const host = () => pageActive.value && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = () => host() && (!route || route.name === 'document-preview-tasks') && session.can('document.host_preview_tasks.read');
const canCreate = () => canRead() && session.can('document.host_preview_tasks.create');
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
watch(editorOpen, open => {
  editorSequence++; recoveryConfirmation.value = undefined;
  if (!open) {
    cancelAction(createRequest); createRequest = undefined;
    editorForm.documentItemId = ''; editorForm.versionId = '';
  }
}, { flush: 'sync' });
watch(() => JSON.stringify([editorForm.documentItemId, editorForm.versionId]), () => { cancelAction(createRequest); createRequest = undefined; editorSequence++; recoveryConfirmation.value = undefined; }, { flush: 'sync' });
const editorControls = computed(() => {
  const token = editorSequence, open = editorOpen.value, document = editorForm.documentItemId, version = editorForm.versionId;
  const owned = () => token === editorSequence && open && editorOpen.value && document === editorForm.documentItemId && version === editorForm.versionId && canCreate();
  return { document: (value: string) => { if (owned() && !changing.value) editorForm.documentItemId = value; }, version: (value: string) => { if (owned() && !changing.value) editorForm.versionId = value; },
    submit: () => { if (owned()) void submitCreate(); }, update: (value: boolean) => { if (!value && owned()) editorOpen.value = false; }, recovery: () => { if (owned()) acknowledgeCreate(); } };
});

function clearDetail(): void {
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
  clearDetail(); detailTargetId.value = id; editorOpen.value = false; await refreshDetail(id, true);
}
async function refreshDetail(id: string, open = false): Promise<void> {
  if (!canRead() || !validId(id) || !sameId(id, detailTargetId.value) || !open && !detailVisible.value) return;
  detailRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); if (!request) return;
  detailRequest = request; if (open) detailVisible.value = true; detailLoading.value = true; detailProblem.value = undefined;
  try {
    const task = await getDocumentPreviewTask(id, request.signal);
    if (!request.current()) return;
    if (task.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_document_preview_task_identity');
    if (!Number.isSafeInteger(task.version) || task.version < 1 || [cancellationBlocks.value.get(id.toLowerCase()), selectedTask.value].some(baseline => baseline && (!sameTaskSource(task, baseline) || task.version < baseline.version))) throw new Error('client.invalid_document_preview_task_identity');
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
    pdf: () => { if (owned() && task) void openPdf(task); }, cancel: () => { if (owned() && task) requestCancellation(task); } };
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

const isRunning = (task?: HostDocumentPreviewTaskResponse) => task?.statusKey === 'pending' || task?.statusKey === 'processing';
useTaskStatusRefresh(() => canRead() && !problem.value
  && (items.value.some(isRunning) || detailVisible.value && isRunning(selectedTask.value)), async current => {
  if (loading.value || detailLoading.value || changing.value || editorOpen.value || cancelDialogVisible.value) return;
  await load();
  const task = selectedTask.value;
  if (current() && !loading.value && !problem.value && !detailProblem.value && detailVisible.value && task && isRunning(task) && !detailLoading.value && !changing.value) await refreshDetail(task.id);
});

async function load(recoverPage = true): Promise<boolean> {
  if (!canRead() || !Number.isSafeInteger(page.value) || page.value < 1 || ![10, 20, 50].includes(pageSize.value)) return false;
  listRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); listRequest = request;
  if (!request) return false;
  const query = { page: page.value, pageSize: pageSize.value, documentItemId: appliedDocumentItemId.value || undefined };
  if (!lastQuery || lastQuery.page !== query.page || lastQuery.pageSize !== query.pageSize || !sameId(lastQuery.documentItemId, query.documentItemId)) {
    cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined; items.value = []; total.value = 0; listControls.value = makeListControls();
  }
  lastQuery = query;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listDocumentPreviewTasks(query.page, query.pageSize, query.documentItemId, request.signal);
    if (!request.current()) return false;
    if (result.page !== query.page || result.pageSize !== query.pageSize || query.documentItemId && result.items.some(row => !sameId(row.documentItemId, query.documentItemId))) throw new Error('client.invalid_document_preview_task_page');
    const lastPage = Math.max(1, Math.ceil(result.total / query.pageSize));
    if (recoverPage && !result.items.length && query.page > lastPage) { page.value = lastPage; return await load(false); }
    items.value = result.items.map(row => { const existing = items.value.find(item => sameId(item.id, row.id)); return existing && existing.version > row.version ? existing : row; });
    total.value = result.total;
    return request.current();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error);
    return false;
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
  }
}

async function applyFilter(): Promise<void> {
  if (!canRead()) return;
  const documentItemId = filterDocumentItemId.value.trim();
  if (documentItemId && !validId(documentItemId)) { problem.value = { status: 400, code: 'client.invalid_document_preview_filter', title: t('documentPreviewTasks.invalidIdentity') }; return; }
  // 输入是草稿，分页、轮询与刷新只能复用已明确应用的查询。
  cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined;
  appliedDocumentItemId.value = documentItemId; page.value = 1;
  await load();
}

function openCreate() {
  if (!canCreate() || changing.value || cancelDialogVisible.value) return;
  detailVisible.value = false;
  editorForm.documentItemId = appliedDocumentItemId.value;
  editorForm.versionId = '';
  editorOpen.value = true;
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
  receipt.value = { sequence: ++receiptSequence, kind: 'create', ...target, outcome: 'processing' }; mutation = { request, receipt: { ...receipt.value }, dispatched: true };
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  try {
    const created = await createDocumentPreviewTask(target, request.signal);
    if (!request.current()) return;
    if (!validId(created.id) || !Number.isSafeInteger(created.version) || created.version < 1 || !sameId(created.documentItemId, target.documentItemId) || !sameId(created.versionId, target.versionId)
      || !sameId(created.requestedByUserId, session.currentUser?.id)) throw new Error('client.invalid_document_preview_task_identity');
    mutation!.dispatched = false; listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    receipt.value = { ...receipt.value!, taskId: created.id, outcome: 'succeeded' };
    if ((!appliedDocumentItemId.value || sameId(appliedDocumentItemId.value, created.documentItemId)) && page.value === 1) {
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

const isCancellable = (task: HostDocumentPreviewTaskResponse) => canRead() && task.statusKey === 'pending'
  && task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
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
    const result = await cancelDocumentPreviewTask(taskId, request.signal);
    if (!request.current()) return;
    if (!sameTaskSource(result, current) || result.statusKey !== 'cancelled' || !Number.isSafeInteger(result.version) || result.version < current.version) throw new Error('client.invalid_document_preview_task_cancellation');
    mutation!.dispatched = false; listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    receipt.value = { ...receipt.value!, outcome: 'succeeded' };
    items.value = items.value.map(item => sameId(item.id, taskId) && item.version <= result.version ? result : item);
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

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <div v-if="receipt" role="status" data-testid="document-preview-receipt">
      <p>{{ t('documentPreviewTasks.receiptTarget', {document:receipt.documentItemId,version:receipt.versionId ?? t('documentPreviewTasks.currentVersion')}) }} · {{ t(receipt.kind === 'create' ? 'documentPreviewTasks.addTask' : 'common.cancel') }}</p>
      <p>{{ t(receipt.outcome === 'succeeded' ? 'documentPreviewTasks.resultSucceeded' : receipt.outcome === 'failed' ? 'documentPreviewTasks.resultFailed' : receipt.outcome === 'processing' ? 'documentPreviewTasks.resultProcessing' : 'documentPreviewTasks.resultUnconfirmed') }} <code v-if="receipt.taskId" translate="no">{{ receipt.taskId }}</code> <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code></p>
      <p v-if="receipt.kind === 'cancel' && receipt.outcome === 'unconfirmed'">{{ t('documentPreviewTasks.cancelRecoveryHint') }}</p>
    </div>

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPreviewTasks.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" v-on="{ click: listControls.refresh }">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>
        <el-form class="document-module-inline-query" :inline="true" v-on="{ submit: listControls.submit }">
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
             <el-button type="primary" :icon="Search" v-on="{ click: listControls.apply }">{{ t('documentPermissions.query') }}</el-button>
          </el-form-item>
        </el-form>
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
                data-testid="document-preview-task-create"
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
            <el-table-column :label="t('users.columnActions')" width="180" fixed="right" align="center">
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
                    <span v-if="unavailableFiles.isUnavailable(row.id)" data-testid="document-result-unavailable" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
                  </PermissionGate>
                  <ElButton v-if="isCancellable(row as HostDocumentPreviewTaskResponse)" link type="warning"
                    data-testid="document-preview-task-cancel" :data-task-id="row.id" :disabled="changing || editorOpen"
                    v-on="{ click: row.cancel }">{{ t('common.cancel') }}</ElButton>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPreviewTasks.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          :current-page="page"
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

    <ElDrawer :model-value="detailVisible" v-on="{ 'update:modelValue': detailControls.update }" :title="t('documentPreviewTasks.detailTitle')" size="560px">
      <div v-loading="detailLoading" data-testid="document-preview-detail">
        <ElAlert v-if="detailProblem" type="error" :closable="false" data-testid="document-preview-detail-problem" :title="detailProblem.title" :description="detailProblem.code" />
        <ElButton data-testid="document-preview-detail-refresh" :disabled="changing || detailLoading" v-on="{ click: detailControls.refresh }">{{ t('documentPreviewTasks.refreshDetail') }}</ElButton>
        <ElDescriptions v-if="selectedTask" :column="1" border>
          <ElDescriptionsItem :label="t('documentPreviewTasks.documentTitle')">{{ selectedTask.documentTitle }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.documentItemId')">{{ selectedTask.documentItemId }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.status')">{{ statusLabel(selectedTask.statusKey) }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.provider')">{{ selectedTask.providerKey }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('documentPreviewTasks.createdAtUtc')">{{ selectedTask.createdAtUtc }}</ElDescriptionsItem>
          <ElDescriptionsItem v-if="selectedTask.errorCode" :label="t('documentPreviewTasks.failureReason')">{{ documentPreviewFailureLabel(selectedTask.errorCode, t) }}</ElDescriptionsItem>
        </ElDescriptions>
        <template v-if="selectedTask">
          <ElButton v-if="selectedTask.statusKey === 'succeeded'" data-testid="document-preview-detail-open-pdf"
            :disabled="detailLoading || changing || editorOpen || cancelDialogVisible || unavailableFiles.isUnavailable(selectedTask.id)"
             v-on="{ click: detailControls.pdf }">{{ t('documentPreviewTasks.openPdf') }}</ElButton>
          <span v-if="unavailableFiles.isUnavailable(selectedTask.id)" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
          <ElButton v-if="isCancellable(selectedTask)" data-testid="document-preview-detail-cancel"
             :disabled="detailLoading || changing || editorOpen" v-on="{ click: detailControls.cancel }">{{ t('common.cancel') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
    <ArtFormDialog
      :open="editorOpen"
      v-on="{ 'update:open': editorControls.update, confirm: editorControls.submit }"
      :title="t('documentPreviewTasks.createDialogTitle')"
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
          <el-input :model-value="editorForm.documentItemId" :disabled="changing" autocomplete="off" v-on="{ 'update:modelValue': editorControls.document }" />
        </el-form-item>
        <el-form-item :label="t('documentPreviewTasks.versionIdOptional')">
          <el-input :model-value="editorForm.versionId" :disabled="changing" autocomplete="off" v-on="{ 'update:modelValue': editorControls.version }" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
    <ArtFormDialog v-if="recoveryConfirmation && canCreate()" :key="recoveryConfirmation.token" :open="true" :title="t('documentPreviewTasks.reviewCreation')"
      :confirm-label="t('documentPreviewTasks.allowCreation')" :cancel-label="t('common.cancel')" confirm-test-id="document-preview-recovery-confirm"
      v-on="{ confirm: recoveryConfirmation.confirm, 'update:open': recoveryConfirmation.update }">
      <p>{{ t('documentPreviewTasks.recoveryConfirm') }}</p>
    </ArtFormDialog>
    <ElDialog v-if="cancelConfirmation && cancelHandlers" :key="cancelSequence" :model-value="true"
      :title="t('common.cancel')" width="460px" :close-on-click-modal="false"
      v-on="{ 'update:modelValue': cancelHandlers.update }">
      <p>{{ t('documentPreviewTasks.cancelConfirm') }}</p>
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
</style>
