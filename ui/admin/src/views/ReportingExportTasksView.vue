<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
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
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type {
  FullNetProblemDetails,
  ReportingPublishedDefinition,
  ReportingExportTask,
  ReportingExportTaskDetail,
  ReportingExportTaskParameterValue
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../auth/session';
import { isTenantSessionContext } from '../auth/tenant-session-context';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useBoundedPolling } from '../composables/useBoundedPolling';
import { useUnavailableResultFiles } from '../composables/useUnavailableResultFiles';
import { listReportingPublishedDefinitions } from '../api/reporting-publications';
import { translateReportingExportRecovery } from '@fullnet/admin-i18n';
import {
  createReportingExportTask,
  cancelReportingExportTask,
  downloadReportingExportTask,
  getReportingExportTask,
  listReportingExportTasks
} from '../api/reporting-export-tasks';

defineOptions({ name: 'ReportingExportTasksView' });

const session = useSessionStore();
const route = inject(routeLocationKey, undefined);
const pageActive = ref(false); let linkBlocked = false; let linkedTaskId: string | undefined;
const narrowViewport = ref(false); let viewportQuery: MediaQueryList | undefined;
function updateViewport() { narrowViewport.value = viewportQuery?.matches ?? false; }
onMounted(() => { if (typeof window.matchMedia !== 'function') return; viewportQuery = window.matchMedia('(max-width: 640px)');
  updateViewport(); viewportQuery.addEventListener?.('change', updateViewport); });
onBeforeUnmount(() => viewportQuery?.removeEventListener?.('change', updateViewport));
onMounted(() => { pageActive.value = true; }); onActivated(() => { pageActive.value = true; });
onDeactivated(() => { pageActive.value = false; }); onBeforeUnmount(() => { pageActive.value = false; });
const { t, locale } = useAdminI18n();
const items = ref<ReportingExportTask[]>([]);
const definitions = ref<ReportingPublishedDefinition[]>([]);
const loading = ref(false);
const creating = ref(false);
const downloading = ref(false);
const cancelling = ref(false);
const cancelConfirmation = ref<ReportingExportTask>();
let cancelSequence = 0;
const cancelHandlers = ref<{ confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const problem = ref<FullNetProblemDetails>();
const definitionsProblem = ref<FullNetProblemDetails>(), detailProblem = ref<FullNetProblemDetails>(), definitionsLoading = ref(false);
const detailTargetId = ref(''), detailOwner = ref(0), createOwner = ref(0);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const createDialogVisible = ref(false);
const detailVisible = ref(false); const detailLoading = ref(false); const selectedTask = ref<ReportingExportTaskDetail>();
const selectedDefinitionVersionKey = ref('');
// Map 隔离参数机器键与对象成员，并让特殊键的改值也参与响应式意图失效。
const parameterValues = reactive(new Map<string, string>());

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

// 租户最小发布目录与任务页面共用可信会话边界，子操作仍要求本页读取权限。
const canRead = () => pageActive.value && !session.switching && (!route || route.name === 'reporting-export-tasks')
  && isTenantSessionContext(session.currentUser) && session.can('reporting.export_tasks.read');
const canCreate = () => canRead() && session.can('reporting.export_tasks.create') && session.can('reporting.published_definitions.read');
const canDownload = () => canRead() && session.can('reporting.export_tasks.download');
const begin = (permission: string) => canRead() ? scope.begin(permission) : undefined;

// 同一定义的每个不可变发布版本都是独立授权资源，选择身份同时包含定义和版本。
const versionKey = (definition: ReportingPublishedDefinition) => definition.id.toLowerCase() + ':' + definition.versionNumber;
const selectedDefinition = computed(() =>
  definitions.value.find(item => versionKey(item) === selectedDefinitionVersionKey.value));

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);
const busy = computed(() => creating.value || downloading.value || cancelling.value || definitionsLoading.value);
type Receipt = { sequence: number; kind: 'create' | 'cancel'; definitionId: string; versionNumber: number; taskId?: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode?: string };
const receipt = ref<Receipt>(); let receiptSequence = 0, listSequence = 0, recoverySequence = 0;
const creationBlocks = ref(new Map<string, number>()), cancellationBlocks = ref(new Map<string, ReportingExportTask>()), confirmedCancellations = ref(new Map<string, ReportingExportTaskDetail>());
const recoveryConfirmation = ref<{ token: number; confirm: () => void; update: (open: boolean) => void }>();
type Regeneration = Readonly<{ taskId: string; sourceStatusKey: string; definitionId: string; definitionName: string; versionNumber: number; parameters: readonly Readonly<ReportingExportTaskParameterValue>[] }>;
const regeneration = ref<Regeneration>();
const recoveryText = (key: Parameters<typeof translateReportingExportRecovery>[1]) => translateReportingExportRecovery(locale.value, key);
const normalizedParameters = (parameters: readonly Readonly<ReportingExportTaskParameterValue>[]) => parameters.map(parameter => [parameter.parameterKey, parameter.value] as const)
  .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0);
const creationKey = (id: string, version: number, parameters: readonly Readonly<ReportingExportTaskParameterValue>[]) => JSON.stringify([id.toLowerCase() + ':' + version, 'excel', ...normalizedParameters(parameters)]);
const createIntent = computed(() => regeneration.value ? creationKey(regeneration.value.definitionId, regeneration.value.versionNumber, regeneration.value.parameters)
  : selectedDefinition.value ? creationKey(selectedDefinition.value.id, selectedDefinition.value.versionNumber, parameterSchema.value.map(parameter => ({ parameterKey: parameter.parameterKey, value: parameterValues.get(parameter.parameterKey) ?? null }))) : '');
const createBlocked = computed(() => creationBlocks.value.has(createIntent.value));
let creatingIntent: { key: string; receipt: Receipt } | undefined;
let cancellingTask: { task: ReportingExportTask; receipt: Receipt } | undefined;
let lastQuery: { page: number; pageSize: number } | undefined;
interface ListControls { token: number; refresh: () => void; create: () => void; page: (value: number) => void; size: (value: number) => void; change: (nextPage: number, nextSize: number) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  const token = ++listSequence, owned = () => token === listControls.value.token && canRead();
  return { token, refresh: () => { if (owned()) refreshResults(); }, create: () => { if (owned()) openCreateDialog(); }, page: value => { if (owned() && Number.isSafeInteger(value) && value > 0) page.value = value; }, size: value => { if (owned() && [10, 20, 50].includes(value)) pageSize.value = value; },
    change: (nextPage, nextSize) => { if (!owned() || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10, 20, 50].includes(nextSize)) return;
      page.value = lastQuery?.pageSize !== nextSize ? 1 : nextPage; pageSize.value = nextSize; if (lastQuery?.page !== page.value || lastQuery.pageSize !== nextSize) void load(); } };
}
type ReportingExportTaskRow = ReportingExportTask & { detail: () => void; download: () => void; cancel: () => void };
const tableRows = computed<ReportingExportTaskRow[]>(() => {
  const token = listControls.value.token;
  return items.value.map(row => { const task = { ...row }, owned = () => token === listControls.value.token && canRead() && !!currentTask(task);
    return { ...task, detail: () => { if (owned()) void openDetail(task.id); }, download: () => { if (owned()) void downloadTask(task); }, cancel: () => { if (owned()) requestCancellation(task); } }; });
});
let createRequest: ReturnType<typeof scope.begin>;
function cancelCreate(): void {
  if (createRequest?.current() && creatingIntent) { creationBlocks.value = new Map(creationBlocks.value).set(creatingIntent.key, creatingIntent.receipt.sequence); receipt.value = { ...creatingIntent.receipt, outcome: 'unconfirmed' }; }
  createRequest?.cancel(); createRequest = undefined; creatingIntent = undefined; creating.value = false;
}
// 关闭只取消客户端接入，不承诺撤销已经提交到服务端的导出任务。
watch(createDialogVisible, visible => {
  createOwner.value++; recoveryConfirmation.value = undefined;
  if (!visible) { cancelCreate(); regeneration.value = undefined; resetParameters(); }
}, {flush:'sync'});
watch(selectedDefinitionVersionKey, resetParameters, { flush: 'sync' });
watch(() => JSON.stringify(selectedDefinition.value), resetParameters, { flush: 'sync' });
watch(createIntent, () => { cancelCreate(); createOwner.value++; recoveryConfirmation.value = undefined; }, { flush: 'sync' });
const createControls = computed(() => {
  const owner = createOwner.value, open = createDialogVisible.value, intent = createIntent.value;
  const owned = () => owner === createOwner.value && open && createDialogVisible.value && intent === createIntent.value && canCreate();
  return { update: (value: boolean) => { if (!value && owned()) createDialogVisible.value = false; }, close: () => { if (owned()) createDialogVisible.value = false; },
    select: (value: string) => { if (owned() && !busy.value && !regeneration.value) selectedDefinitionVersionKey.value = value; }, submit: () => { if (owned()) void submitCreate(); }, refresh: () => { if (owned() && !busy.value && !regeneration.value) void loadDefinitions(); }, recovery: () => { if (owned()) acknowledgeCreate(); },
    parameters: parameterSchema.value.map(parameter => ({ ...parameter, input: (value: string) => { if (owned() && !busy.value) parameterValues.set(parameter.parameterKey, value); } })) };
});
const scope = useAuthorizedViewScope(session, () => {
  linkBlocked = true; linkedTaskId = undefined; detailVisible.value = false; selectedTask.value = undefined; detailLoading.value = false;
  cancelConfirmation.value = undefined; cancelling.value = false;
  unavailableFiles.reset();
  items.value = []; definitions.value = []; selectedDefinitionVersionKey.value = ''; total.value = 0; page.value = 1;
  problem.value = undefined; createDialogVisible.value = false; loading.value = false; creating.value = false; downloading.value = false;
  regeneration.value = undefined; resetParameters();
  detailOwner.value++; detailTargetId.value = ''; detailProblem.value = undefined; definitionsProblem.value = undefined; definitionsLoading.value = false;
  receipt.value = undefined; creationBlocks.value = new Map(); cancellationBlocks.value = new Map(); confirmedCancellations.value = new Map(); creatingIntent = undefined; cancellingTask = undefined; recoveryConfirmation.value = undefined;
  lastQuery = undefined; listControls.value = makeListControls();
}, async () => { openLinkedTask(); await load(); });
let loadRequest: ReturnType<typeof scope.begin>;
let definitionsRequest: ReturnType<typeof scope.begin>;
let detailRequest: ReturnType<typeof scope.begin>; let downloadRequest: ReturnType<typeof scope.begin>; let cancelRequest: ReturnType<typeof scope.begin>;
function clearDetail(): void {
  if (cancelRequest?.current() && cancellingTask) markCancelUnconfirmed();
  cancellingTask = undefined; detailOwner.value++; detailTargetId.value = ''; detailProblem.value = undefined;
  detailRequest?.cancel(); downloadRequest?.cancel(); cancelRequest?.cancel();
  selectedTask.value = undefined; detailLoading.value = false; downloading.value = false; cancelling.value = false; cancelConfirmation.value = undefined;
}
watch(detailVisible, visible => {
  if (!visible) { clearDetail(); linkBlocked = true; linkedTaskId = undefined; }
}, { flush: 'sync' });
function openLinkedTask(): void {
  const id = route?.query.taskId;
  if (!pageActive.value || linkBlocked || route?.name !== 'reporting-export-tasks' || !canRead() || !isGuid(id)
    || id === '00000000-0000-0000-0000-000000000000' || linkedTaskId === id.toLowerCase()) return;
  linkedTaskId = id.toLowerCase(); void openDetail(linkedTaskId);
}
watch(() => JSON.stringify([route?.name, route?.query.taskId]), () => {
  const wasBlocked = linkBlocked;
  clearDetail(); detailVisible.value = false; linkedTaskId = undefined; createDialogVisible.value = false;
  linkBlocked = route?.name !== 'reporting-export-tasks';
  if (linkBlocked) {
    loadRequest?.cancel(); definitionsRequest?.cancel(); items.value = []; definitions.value = []; total.value = 0; loading.value = false;
    definitionsLoading.value = false; definitionsProblem.value = undefined; lastQuery = undefined; listControls.value = makeListControls();
  } else {
    openLinkedTask();
    if (wasBlocked && pageActive.value) void load();
  }
}, { flush: 'sync' });
async function openDetail(id: string): Promise<void> {
  if (!canRead() || !isGuid(id) || id === '00000000-0000-0000-0000-000000000000') return;
  createDialogVisible.value = false; clearDetail(); detailTargetId.value = id; await refreshDetail(id, true);
}
async function refreshDetail(id: string, open = false): Promise<void> {
  if (!canRead() || !isGuid(id) || !sameId(id, detailTargetId.value) || !open && !detailVisible.value) return;
  detailRequest?.cancel(); const request = begin('reporting.export_tasks.read'); if (!request) return; detailRequest = request;
  if (open) detailVisible.value = true; detailLoading.value = true; detailProblem.value = undefined;
  try {
    const task = await getReportingExportTask(id, request.signal);
    if (!request.current()) return;
    if (task.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_reporting_export_task_identity');
    if ([cancellationBlocks.value.get(id.toLowerCase()), confirmedCancellations.value.get(id.toLowerCase()), selectedTask.value].some(baseline => baseline && !sameTaskSource(task, baseline))) throw new Error('client.invalid_reporting_export_task_identity');
    selectedTask.value = confirmedCancellations.value.get(id.toLowerCase()) ?? task;
    const next = new Map(cancellationBlocks.value); next.delete(id.toLowerCase()); cancellationBlocks.value = next;
  } catch (error) {
    if (request.current()) detailProblem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally { if (request.current()) detailLoading.value = false; request.finish(); }
}

function statusTagType(statusKey: string): 'success' | 'danger' | 'info' {
  if (statusKey === 'succeeded') {
    return 'success';
  }
  if (statusKey === 'failed') {
    return 'danger';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  if (statusKey === 'queued') return t('importExportTasks.status.queued');
  if (statusKey === 'canceled') return t('importExportTasks.status.cancelled');
  const key = `reportingExportTasks.status.${statusKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function resetParameters(): void {
  parameterValues.clear();
  for (const parameter of parameterSchema.value) {
    parameterValues.set(parameter.parameterKey, parameter.defaultValue ?? '');
  }
}

async function loadDefinitions(keyword = ''): Promise<void> {
  if (creating.value || downloading.value || cancelling.value) return;
  definitionsRequest?.cancel();
  if (!canCreate()) return;
  const request = begin('reporting.published_definitions.read'); if (!request) return; definitionsRequest = request;
  definitionsLoading.value = true; definitionsProblem.value = undefined;
  try {
    const result = await listReportingPublishedDefinitions(keyword.trim(), request.signal);
    const values = result.items;
    if (!request.current()) return;
    if (new Set(values.map(row => row.id.toLowerCase())).size !== values.length || values.some(row => !isGuid(row.id) || row.id === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1
      || row.parameterSchema.some(parameter => !parameter.parameterKey.trim()) || new Set(row.parameterSchema.map(parameter => parameter.parameterKey)).size !== row.parameterSchema.length)) throw new Error('client.invalid_reporting_definition_list');
    const previous = selectedDefinitionVersionKey.value;
    definitions.value = values;
    selectedDefinitionVersionKey.value = definitions.value.some(row => versionKey(row) === previous) ? previous : definitions.value[0] ? versionKey(definitions.value[0]) : '';
  } catch (error: unknown) {
    if (request.current()) definitionsProblem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally { if (request.current()) definitionsLoading.value = false; request.finish(); }
}

const isRunning = (task?: ReportingExportTask) => task?.statusKey === 'queued' || task?.statusKey === 'processing';
const progress = useBoundedPolling({
  enabled: () => canRead() && !busy.value && !createDialogVisible.value && !cancelDialogVisible.value,
  pending: () => items.value.some(isRunning) || detailVisible.value && isRunning(selectedTask.value),
  key: () => [session.currentUser?.sessionId, session.currentUser?.tenantId, page.value, pageSize.value].join('|'),
  intervalMs: 5000,
  read: async signal => {
    const cancel = () => { loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false; };
    signal.addEventListener('abort', cancel, { once: true });
    try {
      if (!await load() || signal.aborted) throw new Error('client.background_refresh_failed');
      const task = selectedTask.value;
      if (detailVisible.value && isRunning(task) && task && !cancellationBlocks.value.has(task.id.toLowerCase())) await refreshDetail(task.id);
      if (detailProblem.value) throw new Error('client.background_refresh_failed');
    } finally { signal.removeEventListener('abort', cancel); }
  },
  accept: () => {},
  failed: error => { problem.value ??= toProblem(error, 'reportingExportTasks.loadFailed'); }
});
const progressPaused = progress.paused;
watch(() => session.switching, switching => {
  scope.invalidate(); if (!switching && canRead()) void load();
}, { flush: 'sync' });

async function load(recoverPage = true): Promise<boolean> {
  if (!canRead() || !Number.isSafeInteger(page.value) || page.value < 1 || ![10, 20, 50].includes(pageSize.value)) return false;
  loadRequest?.cancel(); const request = begin('reporting.export_tasks.read'); loadRequest = request;
  if (!request) return false;
  const query = { page: page.value, pageSize: pageSize.value };
  if (!lastQuery || lastQuery.page !== query.page || lastQuery.pageSize !== query.pageSize) {
    downloadRequest?.cancel(); downloading.value = false; items.value = []; total.value = 0; listControls.value = makeListControls();
  }
  lastQuery = query;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listReportingExportTasks(query.page, query.pageSize, undefined, request.signal);
    if (!request.current()) return false;
    if (result.page !== query.page || result.pageSize !== query.pageSize) throw new Error('client.invalid_reporting_export_task_page');
    const lastPage = Math.max(1, Math.ceil(result.total / query.pageSize));
    if (recoverPage && !result.items.length && query.page > lastPage) { page.value = lastPage; return await load(false); }
    items.value = result.items.map(row => { const confirmed = confirmedCancellations.value.get(row.id.toLowerCase()); if (confirmed && !sameTaskSource(row, confirmed)) throw new Error('client.invalid_reporting_export_task_identity'); return confirmed ?? row; });
    total.value = result.total;
    return request.current();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
    return false;
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
  }
}

async function openCreateDialog(): Promise<void> {
  if (!canCreate() || creating.value || downloading.value || cancelling.value || cancelDialogVisible.value) return;
  detailVisible.value = false; regeneration.value = undefined;
  createDialogVisible.value = true;
  resetParameters();
  await loadDefinitions();
}

async function submitCreate(): Promise<void> {
  const original = regeneration.value;
  const selected = selectedDefinition.value;
  const definition = original ? { id: original.definitionId, versionNumber: original.versionNumber }
    : selected ? { id: selected.id, versionNumber: selected.versionNumber } : undefined;
  if (!canCreate() || !session.can('reporting.published_definitions.read') || !createDialogVisible.value || createBlocked.value || !definition || busy.value || cancelDialogVisible.value) {
    return;
  }
  const request = begin('reporting.export_tasks.create'); if (!request) return; createRequest = request;

  creating.value = true;
  problem.value = undefined;
  receipt.value = { sequence: ++receiptSequence, kind: 'create', definitionId: definition.id, versionNumber: definition.versionNumber, outcome: 'processing' };
  creatingIntent = { key: createIntent.value, receipt: { ...receipt.value } };
  loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  try {
    const parameters: ReportingExportTaskParameterValue[] = original ? original.parameters.map(parameter => ({ ...parameter })) : parameterSchema.value.map(parameter => ({
      parameterKey: parameter.parameterKey,
      value: parameterValues.get(parameter.parameterKey) ?? null
    }));
    const task = await createReportingExportTask({
      definitionId: definition.id,
      versionNumber: definition.versionNumber,
      formatKey: 'excel',
      parameters
    }, request.signal);
    if (!request.current()) return;
    // 创建成功必须对应选中的发布版本和格式，不能将其他任务误认为本次结果。
    if (task.definitionId.toLowerCase() !== definition.id.toLowerCase() || task.versionNumber !== definition.versionNumber || task.formatKey !== 'excel'
      || !isGuid(task.id) || task.id === '00000000-0000-0000-0000-000000000000' || task.requestedByUserId.toLowerCase() !== session.currentUser?.id.toLowerCase()
      || JSON.stringify(normalizedParameters(task.parameters)) !== JSON.stringify(normalizedParameters(parameters)) || original && sameId(task.id, original.taskId))
      throw new Error('client.invalid_reporting_export_task_identity');
    receipt.value = { ...receipt.value!, taskId: task.id, outcome: 'succeeded' };
    creatingIntent = undefined; createRequest = undefined; creating.value = false; loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    ElMessage.success(t('reportingExportTasks.createSuccess'));
    createDialogVisible.value = false;
    // 创建回包已经是权威详情，直接显示本次任务，避免额外 GET 或自动触发下载。
    clearDetail(); detailTargetId.value = task.id; detailVisible.value = true; selectedTask.value = task;
    if (page.value === 1) { const existed = items.value.some(row => sameId(row.id, task.id)); items.value = [task, ...items.value.filter(row => !sameId(row.id, task.id))].slice(0, pageSize.value); if (!existed) total.value++; }
    await load();
  } catch (error: unknown) {
    if (request.current()) {
      const failure = toProblem(error, 'reportingExportTasks.createFailed');
      if (creatingIntent) {
        if (isUnknown(error)) { creationBlocks.value = new Map(creationBlocks.value).set(creatingIntent.key, creatingIntent.receipt.sequence); receipt.value = { ...creatingIntent.receipt, outcome: 'unconfirmed', errorCode: failure.code }; }
        else receipt.value = { ...creatingIntent.receipt, outcome: 'failed', errorCode: failure.code };
      }
      problem.value = failure;
    }
  } finally {
    if (createRequest === request) { if (request.current()) creating.value = false; createRequest = undefined; creatingIntent = undefined; } request.finish();
  }
}
const sameId = (left?: string | null, right?: string | null) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
function sameTaskSource(left: ReportingExportTask, right: ReportingExportTask): boolean {
  return sameId(left.id, right.id) && sameId(left.definitionId, right.definitionId) && left.versionNumber === right.versionNumber && left.formatKey === right.formatKey
    && sameId(left.requestedByUserId, right.requestedByUserId) && left.createdAtUtc === right.createdAtUtc;
}
const isUnknown = (error: unknown) => !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
function canRegenerate(task: ReportingExportTaskDetail) {
  return canCreate() && (['failed', 'canceled'].includes(task.statusKey) || task.statusKey === 'succeeded' && unavailableFiles.isUnavailable(task.id))
    && task.formatKey === 'excel' && sameId(task.requestedByUserId, session.currentUser?.id)
    && !busy.value && !detailLoading.value && !detailProblem.value && !cancelDialogVisible.value;
}
async function prepareRegeneration(source: ReportingExportTaskDetail) {
  if (!canRegenerate(source) || !detailVisible.value || !currentTask(source)) return;
  const original = { ...source, parameters: source.parameters.map(parameter => ({ ...parameter })) }; const owner = detailOwner.value;
  detailRequest?.cancel(); const request = begin('reporting.export_tasks.read'); if (!request) return; detailRequest = request; detailLoading.value = true;
  try {
    const current = await getReportingExportTask(original.id, request.signal);
    if (!request.current() || owner !== detailOwner.value || !detailVisible.value || !canCreate()) return;
    if (!sameTaskSource(current, original) || current.statusKey !== original.statusKey || current.formatKey !== 'excel'
      || !isGuid(current.id) || current.id === '00000000-0000-0000-0000-000000000000' || !isGuid(current.definitionId) || current.definitionId === '00000000-0000-0000-0000-000000000000'
      || !Number.isSafeInteger(current.version) || current.version < original.version
      || current.outputFileId !== original.outputFileId || !sameId(current.requestedByUserId, session.currentUser?.id)
      || !Number.isSafeInteger(current.versionNumber) || current.versionNumber < 1
      || new Set(current.parameters.map(parameter => parameter.parameterKey)).size !== current.parameters.length
      || current.parameters.some(parameter => !parameter.parameterKey.trim() || parameter.value !== null && typeof parameter.value !== 'string')
      || JSON.stringify(normalizedParameters(current.parameters)) !== JSON.stringify(normalizedParameters(original.parameters)))
      throw { type: 'about:blank', status: 409, code: 'client.original_export_changed', title: recoveryText('changed') };
    // 原参数与发布版本只读冻结；重生成仍创建新任务，不复活旧文件或切换到最新目录。
    definitionsRequest?.cancel(); definitionsLoading.value = false; definitionsProblem.value = undefined;
    regeneration.value = Object.freeze({ taskId: current.id, sourceStatusKey: current.statusKey, definitionId: current.definitionId, definitionName: current.definitionName, versionNumber: current.versionNumber,
      parameters: Object.freeze(current.parameters.map(parameter => Object.freeze({ ...parameter }))) });
    detailRequest = undefined; detailLoading.value = false; detailVisible.value = false; createDialogVisible.value = true;
  } catch (error) { if (request.current() && owner === detailOwner.value) detailProblem.value = isFullNetProblemDetails(error) ? error : toProblem(error, 'reportingExportTasks.loadFailed'); }
  finally { if (request.current() && owner === detailOwner.value) detailLoading.value = false; request.finish(); }
}
function acknowledgeCreate(): void {
  if (!canCreate() || !createDialogVisible.value || busy.value || !createBlocked.value) return;
  const key = createIntent.value, sequence = creationBlocks.value.get(key), owner = createOwner.value, token = ++recoverySequence;
  const owned = () => recoveryConfirmation.value?.token === token && owner === createOwner.value && createDialogVisible.value && canCreate() && creationBlocks.value.get(key) === sequence;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { const next = new Map(creationBlocks.value); next.delete(key); creationBlocks.value = next; recoveryConfirmation.value = undefined; } }, update: open => { if (!open && owned()) recoveryConfirmation.value = undefined; } };
}
const detailControls = computed(() => {
  const owner = detailOwner.value, id = detailTargetId.value, task = selectedTask.value ? { ...selectedTask.value } : undefined;
  const owned = () => owner === detailOwner.value && detailVisible.value && canRead() && (!task || !!currentTask(task));
  return { update: (open: boolean) => { if (!open && owner === detailOwner.value && canRead()) detailVisible.value = false; }, refresh: () => { if (owned() && !busy.value && !detailLoading.value) { unavailableFiles.reset(); void refreshDetail(id); } },
    download: () => { if (owned() && task) void downloadTask(task); }, cancel: () => { if (owned() && task) requestCancellation(task); },
    regenerate: () => { if (owned() && task && 'parameters' in task) void prepareRegeneration(task as ReportingExportTaskDetail); } };
});

// 行事件只能操作当前目录/详情的同一结果快照，不接受失效行或替换后的文件。
function findTask(id: string): ReportingExportTask | undefined {
  return detailVisible.value && selectedTask.value?.id.toLowerCase() === id.toLowerCase()
    ? selectedTask.value : items.value.find(item => item.id.toLowerCase() === id.toLowerCase());
}
function resultFileId(task: ReportingExportTask): string | undefined {
  // 列表协议只公开文件摘要，详情额外公开文件标识；只复核当前协议实际提供的字段。
  return 'outputFileId' in task && typeof task.outputFileId === 'string' ? task.outputFileId.toLowerCase() : undefined;
}
function taskVersion(task: ReportingExportTask): number | undefined {
  return 'version' in task && typeof task.version === 'number' ? task.version : undefined;
}
function currentTask(target: ReportingExportTask): ReportingExportTask | undefined {
  const current = findTask(target.id);
  // 详情确认带有并发版本；列表摘要没有该字段时，再由权威详情提供提交版本。
  if (current && taskVersion(current) !== undefined && taskVersion(target) !== undefined && taskVersion(current) !== taskVersion(target)) return undefined;
  const currentFile = current && resultFileId(current), targetFile = resultFileId(target);
  return current && current.statusKey === target.statusKey && current.definitionId.toLowerCase() === target.definitionId.toLowerCase()
    && current.versionNumber === target.versionNumber && current.formatKey === target.formatKey
    && current.requestedByUserId.toLowerCase() === target.requestedByUserId.toLowerCase()
    && current.outputFileName === target.outputFileName && current.completedAtUtc === target.completedAtUtc
    && current.rowCount === target.rowCount && (!currentFile || !targetFile || currentFile === targetFile) ? current : undefined;
}
async function downloadTask(row: ReportingExportTask): Promise<void> {
  const current = currentTask(row); if (!current) return;
  const task = { ...current };
  if (!canDownload() || !sameId(task.requestedByUserId, session.currentUser?.id) || task.statusKey !== 'succeeded' || detailLoading.value || creating.value || createDialogVisible.value || cancelDialogVisible.value || downloading.value || cancelling.value || unavailableFiles.isUnavailable(task.id)) {
    return;
  }
  const request = begin('reporting.export_tasks.download'); if (!request) return; downloadRequest = request; downloading.value = true;
  const fileRequest = unavailableFiles.begin(task.id);

  try {
    const blob = await downloadReportingExportTask(task.id, request.signal);
    if (!request.current() || !fileRequest.current() || !currentTask(task)) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a');
      anchor.href = url; anchor.download = task.outputFileName ?? 'reporting-export.xlsx'; anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error: unknown) {
    if (request.current() && fileRequest.current() && currentTask(task)) problem.value = fileRequest.markUnavailable(error)
      ? { title: t('dataOutput.resultUnavailable'), status: 404, code: 'files.file.not_found' }
      : toProblem(error, 'reportingExportTasks.downloadFailed');
  } finally {
    if (request.current()) downloading.value = false; request.finish();
  }
}

// 用户刷新可重新确认恢复后的文件，状态轮询不反复开启已确定缺失的下载入口。
function refreshResults() {
  if (downloading.value || creating.value || cancelling.value) return;
  unavailableFiles.reset(); progress.restart(); void load();
  if (detailVisible.value && detailTargetId.value && !detailLoading.value) void refreshDetail(detailTargetId.value);
}

const isCancellable = (task: ReportingExportTask) => canRead() && task.statusKey === 'queued'
  && task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
  && session.can('reporting.export_tasks.cancel') && !cancellationBlocks.value.has(task.id.toLowerCase());

function requestCancellation(task: ReportingExportTask) {
  const current = currentTask(task);
  if (!current || !isCancellable(current) || detailLoading.value || cancelling.value || creating.value || downloading.value || createDialogVisible.value || cancelDialogVisible.value) return;
  const token = ++cancelSequence;
  cancelConfirmation.value = { ...current };
  // 函数捕获本次确认令牌；旧弹窗事件不能批准后续任务的取消。
  const dismiss = () => { if (token === cancelSequence) cancelConfirmation.value = undefined; };
  cancelHandlers.value = { confirm: () => { void confirmCancellation(token); }, dismiss, update: open => { if (!open) dismiss(); } };
}

async function confirmCancellation(token = cancelSequence) {
  if (token !== cancelSequence) return;
  const confirmed = cancelConfirmation.value;
  cancelConfirmation.value = undefined;
  const row = confirmed && currentTask(confirmed);
  const current = row ? { ...row } : undefined;
  if (!current || !isCancellable(current) || detailLoading.value || cancelling.value || creating.value || downloading.value || createDialogVisible.value) return;
  const request = begin('reporting.export_tasks.cancel'); if (!request) return; cancelRequest = request;
  cancelling.value = true; problem.value = undefined;
  receipt.value = { sequence: ++receiptSequence, kind: 'cancel', definitionId: current.definitionId, versionNumber: current.versionNumber, taskId: current.id, outcome: 'processing' };
  cancellingTask = { task: current, receipt: { ...receipt.value } }; loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  try {
    // 列表只有摘要；取消先读取权威详情并冻结并发版本，不为最小列表扩充敏感字段。
    const detail = selectedTask.value && sameTaskSource(selectedTask.value, current)
      ? selectedTask.value : await getReportingExportTask(current.id, request.signal);
    if (!request.current()) return;
    if (!currentTask(current) || !sameTaskSource(detail, current) || detail.statusKey !== 'queued'
      || !Number.isSafeInteger(detail.version) || detail.version < 1) throw new Error('client.invalid_reporting_export_task_identity');
    const result = await cancelReportingExportTask(current.id, detail.version, request.signal);
    if (!request.current()) return;
    if (!sameTaskSource(result, current) || result.statusKey !== 'canceled') throw new Error('client.invalid_reporting_export_task_cancellation');
    cancellingTask = undefined; loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    receipt.value = { ...receipt.value!, outcome: 'succeeded' };
    confirmedCancellations.value = new Map(confirmedCancellations.value).set(result.id.toLowerCase(), result);
    items.value = items.value.map(item => item.id.toLowerCase() === current.id.toLowerCase() ? result : item);
    if (detailVisible.value && selectedTask.value?.id.toLowerCase() === current.id.toLowerCase()) selectedTask.value = result;
    ElMessage.success(t('reportingExportTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (request.current()) {
      const failure = toProblem(error, 'reportingExportTasks.cancelFailed');
      if (cancellingTask) { cancellingTask.receipt = { ...cancellingTask.receipt, errorCode: failure.code }; if (isUnknown(error) || failure.status === 409) markCancelUnconfirmed(); else receipt.value = { ...cancellingTask.receipt, outcome: 'failed' }; }
      problem.value = failure;
    }
  } finally {
    if (cancelRequest === request) { if (request.current()) cancelling.value = false; cancelRequest = undefined; cancellingTask = undefined; } request.finish();
  }
}
function markCancelUnconfirmed(): void {
  if (!cancellingTask) return;
  loadRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  cancellationBlocks.value = new Map(cancellationBlocks.value).set(cancellingTask.task.id.toLowerCase(), { ...cancellingTask.task });
  receipt.value = { ...cancellingTask.receipt, outcome: 'unconfirmed' };
}

</script>

<template>
  <div v-if="canRead()" class="reporting-export-tasks-view art-page-stack art-full-height">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('reportingExportTasks.title') }}</h1>
    <div v-if="receipt" role="status" data-testid="reporting-export-receipt">
      <p>{{ t('reportingExportTasks.receiptTarget', {definition:receipt.definitionId,version:String(receipt.versionNumber)}) }} · {{ t(receipt.kind === 'create' ? 'reportingExportTasks.create' : 'common.cancel') }}</p>
      <p>{{ t(receipt.outcome === 'succeeded' ? 'reportingExportTasks.resultSucceeded' : receipt.outcome === 'failed' ? 'reportingExportTasks.resultFailed' : receipt.outcome === 'processing' ? 'reportingExportTasks.resultProcessing' : 'reportingExportTasks.resultUnconfirmed') }} <code v-if="receipt.taskId" translate="no">{{ receipt.taskId }}</code> <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code></p>
      <p v-if="receipt.kind === 'cancel' && receipt.outcome === 'unconfirmed'">{{ t('reportingExportTasks.cancelRecoveryHint') }}</p>
    </div>
    <ElCard shadow="never" class="art-table-card art-full-height">
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
            <PermissionGate code="reporting.export_tasks.create">
              <ElButton
                type="primary"
                plain
                :icon="Plus"
                :disabled="!canCreate()"
                data-testid="reporting-export-create"
                v-on="{ click: listControls.create }"
              >
                {{ t('reportingExportTasks.create') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ElAlert
          v-if="problem"
          type="error"
          :title="problem.title"
          show-icon
          class="mb-4"
        />

      <ElAlert v-if="progressPaused" type="info" :closable="false" :title="t('reportingExportTasks.refreshPaused')" />
      <div ref="tableMainRef" class="art-crud-table-main">
        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <ElTable
            v-loading="loading"
            :data="tableRows"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
          <ElTableColumn prop="definitionName" :label="t('reportingExportTasks.fieldDefinition')" min-width="160" />
          <ElTableColumn prop="definitionKey" :label="t('reportingExportTasks.fieldDefinitionKey')" min-width="140" />
          <ElTableColumn prop="versionNumber" :label="t('reportingExportTasks.fieldVersion')" width="90" />
          <ElTableColumn prop="formatKey" :label="t('reportingExportTasks.fieldFormat')" width="90" />
          <ElTableColumn :label="t('reportingExportTasks.fieldStatus')" width="120">
            <template #default="{ row }">
              <ElTag :type="statusTagType(row.statusKey)">
                {{ statusLabel(row.statusKey) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="errorMessage" :label="t('reportingExportTasks.failureReason')" min-width="200" show-overflow-tooltip />
          <ElTableColumn prop="rowCount" :label="t('reportingExportTasks.fieldRowCount')" width="100" />
          <ElTableColumn prop="createdAtUtc" :label="t('reportingExportTasks.fieldCreatedAt')" min-width="180" />
          <!-- @vue-generic {ReportingExportTaskRow} -->
          <ElTableColumn :label="t('reportingExportTasks.actions')" width="200" :fixed="narrowViewport ? undefined : 'right'">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.export_tasks.read"><ElButton link type="primary" data-testid="reporting-export-detail"
                  v-on="{ click: row.detail }">{{ t('importExportTasks.viewDetail') }}</ElButton></PermissionGate>
                <PermissionGate code="reporting.export_tasks.download">
                  <ElButton
                    v-if="row.statusKey === 'succeeded' && sameId(row.requestedByUserId, session.currentUser?.id)"
                    link
                    type="primary"
                    data-testid="reporting-export-download"
                    :data-task-id="row.id"
                    :disabled="downloading || cancelling || unavailableFiles.isUnavailable(row.id)"
                    v-on="{ click: row.download }"
                  >
                    {{ t('reportingExportTasks.download') }}
                  </ElButton>
                </PermissionGate>
                <ElButton v-if="isCancellable(row)" link type="warning" data-testid="reporting-export-cancel"
                  :data-task-id="row.id" :disabled="cancelling || creating || downloading" v-on="{ click: row.cancel }">
                  {{ t('common.cancel') }}
                </ElButton>
              </ArtTableActionGroup>
              <p v-if="canDownload() && unavailableFiles.isUnavailable(row.id)" class="reporting-result-hint" data-testid="reporting-result-unavailable" role="status">{{ t('dataOutput.resultUnavailable') }}</p>
            </template>
          </ElTableColumn>
        </ElTable>
        </div>

      <ElPagination
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[10, 20, 50]"
        class="art-table-pagination center custom-pagination"
        layout="total, sizes, prev, pager, next"
        :total="total"
        v-on="{ 'update:currentPage': listControls.page, 'update:pageSize': listControls.size, change: listControls.change }"
      />
      </div>
    </ElCard>

    <ElDrawer :model-value="detailVisible" v-on="{ 'update:modelValue': detailControls.update }" :title="t('reportingExportTasks.detailTitle')" size="min(680px, 100vw)" destroy-on-close>
      <div v-loading="detailLoading">
        <ElAlert v-if="detailProblem" data-testid="reporting-export-detail-problem" type="error" :closable="false" :title="detailProblem.title" :description="detailProblem.code" />
        <ElButton data-testid="reporting-export-detail-refresh" :disabled="busy || detailLoading" v-on="{ click: detailControls.refresh }">{{ t('reportingExportTasks.refreshDetail') }}</ElButton>
        <ElDescriptions v-if="selectedTask" :column="1" border>
          <ElDescriptionsItem :label="t('reportingExportTasks.fieldDefinition')">{{ selectedTask.definitionName }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('reportingExportTasks.fieldVersion')">{{ selectedTask.versionNumber }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('reportingExportTasks.fieldStatus')">{{ statusLabel(selectedTask.statusKey) }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('reportingExportTasks.fieldRowCount')">{{ selectedTask.rowCount }}</ElDescriptionsItem>
          <ElDescriptionsItem v-if="selectedTask.errorMessage || selectedTask.errorCode" :label="t('importExportTasks.failureReason')">
            {{ selectedTask.errorMessage || selectedTask.errorCode }}
          </ElDescriptionsItem>
        </ElDescriptions>
        <template v-if="selectedTask">
          <ElAlert v-if="selectedTask.statusKey === 'canceled'" type="info" :closable="false" :title="t('reportingExportTasks.cancelDone')" />
          <ElAlert v-else-if="selectedTask.statusKey === 'processing' && sameId(selectedTask.requestedByUserId, session.currentUser?.id) && session.can('reporting.export_tasks.cancel')" type="info" :closable="false" :title="t('reportingExportTasks.cancelProcessing')" />
          <ElTable :data="selectedTask.parameters" class="mt-4"><ElTableColumn prop="parameterKey" :label="t('reportingExportTasks.parameterKey')" /><ElTableColumn prop="value" :label="t('reportingExportTasks.parameterValue')" /></ElTable>

          <PermissionGate code="reporting.export_tasks.download"><ElButton v-if="selectedTask.statusKey === 'succeeded' && sameId(selectedTask.requestedByUserId, session.currentUser?.id)"
            data-testid="reporting-export-detail-download" :data-task-id="selectedTask.id" :disabled="detailLoading || downloading || cancelling || unavailableFiles.isUnavailable(selectedTask.id)"
            v-on="{ click: detailControls.download }">{{ t('reportingExportTasks.download') }}</ElButton></PermissionGate>
          <span v-if="unavailableFiles.isUnavailable(selectedTask.id)" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
          <ElButton v-if="(['failed', 'canceled'].includes(selectedTask.statusKey) || selectedTask.statusKey === 'succeeded' && unavailableFiles.isUnavailable(selectedTask.id)) && canCreate() && sameId(selectedTask.requestedByUserId, session.currentUser?.id)"
            data-testid="reporting-export-regenerate" :disabled="!canRegenerate(selectedTask)" v-on="{ click: detailControls.regenerate }">{{ recoveryText('action') }}</ElButton>
          <ElButton v-if="isCancellable(selectedTask) || selectedTask.statusKey === 'queued' && sameId(selectedTask.requestedByUserId, session.currentUser?.id) && session.can('reporting.export_tasks.cancel')" data-testid="reporting-export-detail-cancel" :disabled="detailLoading || cancelling || creating || downloading || cancellationBlocks.has(selectedTask.id.toLowerCase())"
            v-on="{ click: detailControls.cancel }">{{ t('common.cancel') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
    <ElDialog
      :model-value="createDialogVisible"
      v-on="{ 'update:modelValue': createControls.update }"
      :title="regeneration ? recoveryText('title') : t('reportingExportTasks.createTitle')"
      width="min(520px, calc(100vw - 32px))"
    >
      <ElAlert v-if="definitionsProblem" data-testid="reporting-export-definitions-problem" type="error" :closable="false" :title="definitionsProblem.title" :description="definitionsProblem.code" />
      <PermissionGate v-if="!regeneration" code="reporting.published_definitions.read"><ElButton data-testid="reporting-export-definitions-refresh" :disabled="busy" v-on="{ click: createControls.refresh }">{{ t('reportingExportTasks.refreshDefinitions') }}</ElButton></PermissionGate>
      <ElAlert v-if="createBlocked" type="warning" :closable="false" :title="t('reportingExportTasks.recoveryHint')" />
      <ElButton v-if="createBlocked" data-testid="reporting-export-recovery" :disabled="busy" v-on="{ click: createControls.recovery }">{{ t('reportingExportTasks.reviewCreation') }}</ElButton>
      <div v-if="regeneration" data-testid="reporting-export-regeneration-snapshot" class="export-regeneration">
        <p>{{ recoveryText(regeneration.sourceStatusKey === 'succeeded' ? 'notice' : 'terminalNotice') }}</p><p>{{ recoveryText('source') }} <code>{{ regeneration.taskId }}</code></p>
        <p>{{ t('reportingExportTasks.fieldStatus') }} {{ statusLabel(regeneration.sourceStatusKey) }}</p>
        <p>{{ t('reportingExportTasks.fieldDefinition') }} {{ regeneration.definitionName }} <code>{{ regeneration.definitionId }}</code></p>
        <p>{{ recoveryText('version') }} {{ regeneration.versionNumber }}</p>
        <ElTable :data="[...regeneration.parameters]"><ElTableColumn prop="parameterKey" :label="t('reportingExportTasks.parameterKey')" /><ElTableColumn :label="t('reportingExportTasks.parameterValue')"><template #default="{ row }"><span class="regeneration-parameter-value">{{ row.value === null ? recoveryText('nullValue') : row.value === '' ? recoveryText('emptyValue') : row.value }}</span></template></ElTableColumn></ElTable>
      </div>
      <ElForm v-else label-width="120px">
        <ElFormItem :label="t('reportingExportTasks.fieldDefinition')" required>
          <ElSelect
            :model-value="selectedDefinitionVersionKey"
            filterable remote :remote-method="loadDefinitions"
            data-testid="reporting-export-definition"
            class="w-full"
            :loading="definitionsLoading"
            :disabled="busy"
            v-on="{ 'update:modelValue': createControls.select }"
          >
            <ElOption
              v-for="definition in definitions"
              :key="versionKey(definition)"
              :label="definition.name"
              :value="versionKey(definition)"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem
          v-for="parameter in createControls.parameters"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
          :required="parameter.isRequired"
        >
          <ElInput :model-value="parameterValues.get(parameter.parameterKey)" :disabled="busy" v-on="{ 'update:modelValue': parameter.input }" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton v-on="{ click: createControls.close }">
          {{ t('common.cancel') }}
        </ElButton>
        <PermissionGate code="reporting.export_tasks.create"><ElButton
          type="primary"
          :loading="creating"
          :disabled="(!regeneration && !selectedDefinition) || busy || createBlocked || !session.can('reporting.published_definitions.read')"
          data-testid="reporting-export-submit"
          v-on="{ click: createControls.submit }"
        >
          {{ t('reportingExportTasks.submitCreate') }}
        </ElButton></PermissionGate>
      </template>
    </ElDialog>
    <ArtFormDialog width="min(520px, calc(100vw - 32px))" v-if="recoveryConfirmation && canCreate()" :key="recoveryConfirmation.token" :open="true" :title="t('reportingExportTasks.reviewCreation')"
      :confirm-label="t('reportingExportTasks.allowCreation')" :cancel-label="t('common.cancel')" confirm-test-id="reporting-export-recovery-confirm"
      v-on="{ confirm: recoveryConfirmation.confirm, 'update:open': recoveryConfirmation.update }">
      <p>{{ t('reportingExportTasks.recoveryConfirm') }}</p>
    </ArtFormDialog>
    <ElDialog v-if="cancelConfirmation && cancelHandlers" :key="cancelSequence" :model-value="true"
      :title="t('common.cancel')" width="min(460px, calc(100vw - 32px))" :close-on-click-modal="false"
      v-on="{ 'update:modelValue': cancelHandlers.update }">
      <p>{{ t('reportingExportTasks.cancelConfirm') }}</p>
      <template #footer>
        <ElButton data-testid="reporting-cancel-dismiss" v-on="{ click: cancelHandlers.dismiss }">{{ t('common.cancel') }}</ElButton>
        <ElButton type="warning" data-testid="reporting-cancel-confirm" v-on="{ click: cancelHandlers.confirm }">{{ t('common.confirm') }}</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.export-regeneration { overflow-wrap: anywhere; }
.regeneration-parameter-value { white-space: pre-wrap; }
.reporting-result-hint { margin: 4px 0 0; line-height: 1.4; overflow-wrap: anywhere; }
</style>
