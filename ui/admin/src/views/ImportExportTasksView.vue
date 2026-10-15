<script setup lang="ts">
import { importReceiptFailureLabel } from './importReceiptFailureLabel';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElDialog,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, ImportExportTaskDetailResponse, ImportExportTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import ImportTaskCreateDialog from '../components/ImportTaskCreateDialog.vue';
import { useSessionStore } from '../auth/session';
import { isTenantSessionContext } from '../auth/tenant-session-context';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useTaskStatusRefresh } from '../composables/useTaskStatusRefresh';
import { useUnavailableResultFiles } from '../composables/useUnavailableResultFiles';
import {
  cancelImportExportTask,
  downloadImportExportTaskErrorReceipt,
  executeImportExportTask,
  getImportExportTask,
  listImportExportTasks,
  resumeImportExportTask,
  retryImportExportTask
} from '../api/import-export-tasks';

defineOptions({ name: 'ImportExportTasksView' });

const session = useSessionStore();
const route = inject(routeLocationKey, undefined);
let linkBlocked = false; let linkedTaskId: string | undefined;
const pageActive = ref(false);
// 先登记激活状态，确保作用域恢复读取完成后只在当前可见页面解释链接。
onMounted(() => { pageActive.value = true; }); onActivated(() => { pageActive.value = true; });
onDeactivated(() => { pageActive.value = false; }); onBeforeUnmount(() => { pageActive.value = false; });
const { t } = useAdminI18n();
const items = ref<ImportExportTaskResponse[]>([]);
const loading = ref(false);
const detailLoading = ref(false);
const actionLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const detailProblem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const drawerVisible = ref(false);
const selectedTask = ref<ImportExportTaskDetailResponse>();
const createOpen = ref(false);
const detailTargetId = ref(''); const detailOwner = ref(0); let cancelSequence = 0;
const cancelHandlers = ref<{ confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
const cancelConfirmation = ref<ImportExportTaskDetailResponse>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const receiptKey = (task: ImportExportTaskDetailResponse) => task.id + ':' + task.version;
type ActionKind = 'execute' | 'resume' | 'retry' | 'cancel';
const actionReceipt = ref<{ taskId: string; kind: ActionKind; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode: string | null }>();
const unconfirmedTasks = ref(new Map<string, ImportExportTaskResponse>());
let mutation: { task: ImportExportTaskResponse; kind: ActionKind; dispatched: boolean } | undefined;
const sameId = (left?: string | null, right?: string | null) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
const validId = (id: string) => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000';
const canRead = () => pageActive.value && (!route || route.name === 'import-export-tasks') && isTenantSessionContext(session.currentUser) && session.can('import_export.import_tasks.read');
const begin = (permission: string) => canRead() ? scope.begin(permission) : undefined;
let controlsSequence = 0;
interface ListControls { token: number; refresh: () => void; create: () => void; page: (value: number) => void; size: (value: number) => void; change: (nextPage: number, nextSize: number) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  const token = ++controlsSequence, owned = () => listControls.value.token === token && canRead();
  return { token, refresh: () => { if (owned()) refreshResults(); }, create: () => { if (owned() && canCreate()) createOpen.value = true; },
    page: value => { if (owned() && Number.isSafeInteger(value) && value > 0) page.value = value; },
    size: value => { if (owned() && [10, 20, 50].includes(value)) pageSize.value = value; },
    change: (nextPage, nextSize) => {
      if (!owned() || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10, 20, 50].includes(nextSize)) return;
      const sizeChanged = lastQuery?.pageSize !== nextSize; page.value = sizeChanged ? 1 : nextPage; pageSize.value = nextSize;
      if (lastQuery?.page !== page.value || lastQuery.pageSize !== nextSize) void load();
    } };
}
const tableRows = computed(() => {
  const token = listControls.value.token;
  return items.value.map(row => { const { id, version } = row; return { ...row, open: () => { if (listControls.value.token === token && canRead() && items.value.some(item => sameId(item.id, id) && item.version === version)) void openDetail(id); } }; });
});
const scope = useAuthorizedViewScope(session, () => {
  linkBlocked = true; linkedTaskId = undefined;
  cancelConfirmation.value = undefined;
  detailOwner.value++; detailTargetId.value = ''; mutation = undefined; actionReceipt.value = undefined; unconfirmedTasks.value = new Map(); lastQuery = undefined; listControls.value = makeListControls();
  unavailableFiles.reset();
  items.value = []; selectedTask.value = undefined; problem.value = undefined; detailProblem.value = undefined; drawerVisible.value = false;
  page.value = 1; total.value = 0; createOpen.value = false; loading.value = false; detailLoading.value = false; actionLoading.value = false;
}, async () => { openLinkedTask(); await load(); });
let listRequest: ReturnType<typeof scope.begin>;
let detailRequest: ReturnType<typeof scope.begin>;
let actionRequest: ReturnType<typeof scope.begin>;
let lastQuery: { page: number; pageSize: number } | undefined;

// 抽屉关闭或重新选择任务后，旧详情、执行和下载均失去接入资格。
function cancelDetail(): void {
  cancelConfirmation.value = undefined;
  if (actionRequest?.current() && mutation?.dispatched) markUnconfirmed(mutation.task, mutation.kind);
  detailOwner.value++; detailTargetId.value = ''; mutation = undefined;
  detailProblem.value = undefined;
  detailRequest?.cancel(); actionRequest?.cancel(); selectedTask.value = undefined;
  detailLoading.value = false; actionLoading.value = false;
}
watch(drawerVisible, visible => {
  if (!visible) { cancelDetail(); linkBlocked = true; linkedTaskId = undefined; }
}, { flush: 'sync' });

// 链接仅提供任务身份，读取仍使用当前可信租户和已有精确权限；不自动执行任务。
function openLinkedTask(): void {
  const id = route?.query.taskId;
  if (!pageActive.value || linkBlocked || route?.name !== 'import-export-tasks' || !isTenantSessionContext(session.currentUser)
    || !session.can('import_export.import_tasks.read') || !isGuid(id) || id === '00000000-0000-0000-0000-000000000000') return;
  if (linkedTaskId === id.toLowerCase()) return;
  linkedTaskId = id.toLowerCase(); void openDetail(linkedTaskId);
}
watch(() => JSON.stringify([route?.name, route?.query.taskId]), () => {
  const wasBlocked = linkBlocked;
  cancelDetail(); drawerVisible.value = false; linkedTaskId = undefined;
  createOpen.value = false;
  linkBlocked = route?.name !== 'import-export-tasks';
  if (linkBlocked) { listRequest?.cancel(); items.value = []; total.value = 0; loading.value = false; lastQuery = undefined; listControls.value = makeListControls(); }
  else { openLinkedTask(); if (wasBlocked && pageActive.value) void load(); }
}, { flush: 'sync' });

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

const canCreate = () => canRead() && session.can('import_export.static_schemas.read') && session.can('import_export.import_tasks.create');
const canExecute = () => canRead() && session.can('import_export.import_tasks.execute');
function onCreated(task: ImportExportTaskDetailResponse): void {
  if (!canCreate() || !createOpen.value || !sameId(task.tenantId, session.currentUser?.tenantId) || !validId(task.id)) return;
  cancelDetail(); detailTargetId.value = task.id; selectedTask.value = task; drawerVisible.value = true; void load();
}
let createSequence = 0;
watch(createOpen, () => { createSequence++; }, { flush: 'sync' });
const createHandlers = computed(() => {
  const open = createOpen.value, token = createSequence;
  return { created: (task: ImportExportTaskDetailResponse) => { if (open && token === createSequence) onCreated(task); }, update: (value: boolean) => { if (token === createSequence && canRead()) createOpen.value = value; } };
});

function statusTagType(statusKey: string): 'success' | 'warning' | 'danger' | 'info' {
  if (statusKey === 'preview_succeeded' || statusKey === 'execution_succeeded') {
    return 'success';
  }
  if (statusKey === 'preview_failed' || statusKey === 'execution_failed') {
    return 'danger';
  }
  if (statusKey === 'execution_partial') {
    return 'warning';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  const key = `importExportTasks.status.${statusKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}

const previewStats = computed(() => {
  if (!selectedTask.value) {
    return '';
  }
  return t('importExportTasks.previewStats', {
    valid: selectedTask.value.validRowCount,
    total: selectedTask.value.totalRows,
    invalid: selectedTask.value.invalidRowCount
  });
});

const executionStats = computed(() => {
  if (!selectedTask.value) {
    return '';
  }
  return t('importExportTasks.executionStats', {
    succeeded: selectedTask.value.succeededRowCount,
    failed: selectedTask.value.executionFailedRowCount,
    processed: selectedTask.value.processedRowCount
  });
});

async function load(recoverPage = true): Promise<boolean> {
  if (!canRead() || !Number.isSafeInteger(page.value) || page.value < 1 || ![10, 20, 50].includes(pageSize.value)) return false;
  listRequest?.cancel(); const request = begin('import_export.import_tasks.read'); listRequest = request;
  if (!request) return false;
  const query = { page: page.value, pageSize: pageSize.value };
  if (!lastQuery || lastQuery.page !== query.page || lastQuery.pageSize !== query.pageSize) { items.value = []; total.value = 0; listControls.value = makeListControls(); }
  lastQuery = query;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listImportExportTasks(query.page, query.pageSize, undefined, request.signal);
    if (!request.current()) return false;
    if (result.page !== query.page || result.pageSize !== query.pageSize || result.items.some(row => !sameId(row.tenantId, session.currentUser?.tenantId))) throw new Error('client.invalid_import_export_task_page');
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

async function openDetail(taskId: string) {
  if (!canRead() || !validId(taskId)) return;
  cancelDetail(); detailTargetId.value = taskId; drawerVisible.value = true; await refreshDetail(taskId);
}

// 进度读取保留已展示内容；关闭抽屉及操作仍由原请求租约取消旧结果。
async function refreshDetail(taskId: string) {
  if (!canRead() || !drawerVisible.value || !validId(taskId) || !sameId(taskId, detailTargetId.value)) return;
  detailRequest?.cancel(); const request = begin('import_export.import_tasks.read'); detailRequest = request;
  if (!request) return;
  detailLoading.value = true; detailProblem.value = undefined;
  try {
    const detail = await getImportExportTask(taskId, request.signal);
    if (!request.current()) return;
    if (!sameId(detail.id, taskId) || !sameId(detail.tenantId, session.currentUser?.tenantId)
      || selectedTask.value && !sameTaskIdentity(detail, selectedTask.value)) throw new Error('client.invalid_import_export_task_identity');
    // 关闭抽屉会清展示内容，未知提交的来源与最低版本仍需跨抽屉保留。
    const uncertain = unconfirmedTasks.value.get(taskId.toLowerCase());
    if (uncertain && (!sameTaskIdentity(detail, uncertain) || detail.version < uncertain.version)) throw new Error('client.invalid_import_export_task_identity');
    if (selectedTask.value && detail.version < selectedTask.value.version) return;
    selectedTask.value = detail;
    const next = new Map(unconfirmedTasks.value); next.delete(taskId.toLowerCase()); unconfirmedTasks.value = next;
  } catch (error) {
    if (!request.current()) return;
    detailProblem.value = toProblem(error);
  } finally {
    if (request.current()) detailLoading.value = false; request.finish();
  }
}

// 同一任务的来源和业务定位不可随详情或写响应改变。
function sameTaskIdentity(left: ImportExportTaskResponse, right: ImportExportTaskResponse): boolean {
  return sameId(left.id, right.id) && sameId(left.tenantId, right.tenantId) && left.schemaKey === right.schemaKey && left.worksheetKey === right.worksheetKey
    && sameId(left.sourceFileId, right.sourceFileId) && sameId(left.requestedByUserId, right.requestedByUserId);
}
const taskSnapshot = (task?: ImportExportTaskDetailResponse) => task ? JSON.stringify([task.id.toLowerCase(), task.tenantId.toLowerCase(), task.version, task.statusKey, task.schemaKey, task.worksheetKey, task.sourceFileId?.toLowerCase(), task.hasErrorReceipt, task.requestedByUserId.toLowerCase()]) : '';
const detailControls = computed(() => {
  const owner = detailOwner.value, snapshot = taskSnapshot(selectedTask.value), target = detailTargetId.value;
  const owned = () => owner === detailOwner.value && canRead() && drawerVisible.value && snapshot === taskSnapshot(selectedTask.value);
  return { update: (open: boolean) => { if (!open && owner === detailOwner.value && canRead()) drawerVisible.value = false; },
    refresh: () => { if (owned() && !actionLoading.value && !detailLoading.value) { unavailableFiles.reset(); void refreshDetail(target); } },
    execute: () => { if (owned()) void runAction('execute'); }, resume: () => { if (owned()) void runAction('resume'); }, retry: () => { if (owned()) void runAction('retry'); },
    cancel: () => { if (owned()) requestCancellation(); }, download: () => { if (owned()) void downloadErrorReceipt(); } };
});

function markUnconfirmed(task: ImportExportTaskResponse, kind: ActionKind): void {
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  unconfirmedTasks.value = new Map(unconfirmedTasks.value).set(task.id.toLowerCase(), { ...task });
  actionReceipt.value = { taskId: task.id, kind, outcome: 'unconfirmed', errorCode: sameId(actionReceipt.value?.taskId, task.id) ? actionReceipt.value?.errorCode ?? null : null };
}

const isRunning = (statusKey?: string) => statusKey === 'queued' || statusKey === 'executing';
useTaskStatusRefresh(() => canRead() && !problem.value && (items.value.some(task => isRunning(task.statusKey))
  || (drawerVisible.value && isRunning(selectedTask.value?.statusKey))), async current => {
  if (loading.value || detailLoading.value || actionLoading.value || createOpen.value) return;
  const loaded = await load();
  const task = selectedTask.value;
  // 列表被手动刷新替换时，本轮不得继续详情读取，即使页面仍处于同一授权代次。
  if (loaded && current() && !loading.value && !problem.value && !detailProblem.value && drawerVisible.value && task && isRunning(task.statusKey) && !actionLoading.value && !detailLoading.value) {
    await refreshDetail(task.id);
  }
});

const isCancellable = (task: ImportExportTaskDetailResponse) =>
  task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
  && ['preview_succeeded', 'queued', 'execution_partial', 'execution_failed'].includes(task.statusKey);

async function runAction(kind: ActionKind) {
  const task = selectedTask.value;
  if (!canRead() || !drawerVisible.value || !task || !sameId(task.id, detailTargetId.value) || !sameId(task.tenantId, session.currentUser?.tenantId)
    || actionLoading.value || detailLoading.value || unconfirmedTasks.value.has(task.id.toLowerCase())) return;
  const eligible = kind === 'cancel' ? isCancellable(task) : kind === 'execute' ? task.statusKey === 'preview_succeeded'
    : kind === 'resume' ? task.statusKey === 'execution_partial'
    : task.statusKey === 'execution_partial' || task.statusKey === 'execution_failed';
  if (!eligible) return;
  const request = begin(kind === 'cancel' ? 'import_export.import_tasks.cancel' : 'import_export.import_tasks.execute'); if (!request) return; actionRequest = request;
  const action = kind === 'cancel' ? cancelImportExportTask : kind === 'execute' ? executeImportExportTask : kind === 'resume' ? resumeImportExportTask : retryImportExportTask;
  const target = { ...task }, snapshot = taskSnapshot(task);
  listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
  mutation = { task: target, kind, dispatched: true };
  actionReceipt.value = { taskId: target.id, kind, outcome: 'processing', errorCode: null };
  actionLoading.value = true;
  try {
    const detail = await action(target.id, request.signal);
    if (!request.current()) return;
    if (snapshot !== taskSnapshot(selectedTask.value) || !sameTaskIdentity(detail, target) || !Number.isSafeInteger(detail.version) || detail.version < target.version) throw new Error('client.invalid_import_export_task_identity');
    mutation.dispatched = false; listRequest?.cancel(); detailRequest?.cancel(); loading.value = false; detailLoading.value = false;
    selectedTask.value = detail;
    items.value = items.value.map(row => sameId(row.id, detail.id) && row.version <= detail.version ? { ...row, ...detail } : row);
    actionReceipt.value = { taskId: target.id, kind, outcome: 'succeeded', errorCode: null };
    ElMessage.success(t('importExportTasks.actionSuccess'));
    await load();
  } catch (error) {
    if (request.current()) {
      const failure = toProblem(error), unknown = !isFullNetProblemDetails(error) || failure.status < 400 || failure.status >= 500 || failure.status === 408;
      actionReceipt.value = { taskId: target.id, kind, outcome: unknown ? 'unconfirmed' : 'failed', errorCode: failure.code };
      if (unknown) markUnconfirmed(target, kind);
      ElMessage.error(failure.title || t('importExportTasks.actionFailed'));
    }
  } finally {
    if (actionRequest === request) { if (request.current()) actionLoading.value = false; actionRequest = undefined; mutation = undefined; } request.finish();
  }
}

// 确认属于当前详情及版本；换任务、撤权和关闭抽屉都同步撤销确认，不能在新范围发送旧操作。
function requestCancellation() {
  const task = selectedTask.value;
  if (canRead() && drawerVisible.value && task && isCancellable(task) && !unconfirmedTasks.value.has(task.id.toLowerCase()) && session.can('import_export.import_tasks.cancel') && !actionLoading.value && !detailLoading.value) {
    cancelConfirmation.value = { ...task };
    const token = ++cancelSequence, current = () => token === cancelSequence && !!cancelConfirmation.value;
    cancelHandlers.value = { confirm: () => { if (current()) void confirmCancellation(); }, dismiss: () => { if (current()) cancelConfirmation.value = undefined; }, update: open => { if (!open && current()) cancelConfirmation.value = undefined; } };
  }
}

async function confirmCancellation() {
  const confirmed = cancelConfirmation.value, task = selectedTask.value;
  cancelConfirmation.value = undefined;
  if (!canRead() || !confirmed || !task || !drawerVisible.value || !session.can('import_export.import_tasks.cancel')) return;
  if (!sameTaskIdentity(confirmed, task) || confirmed.version !== task.version || confirmed.statusKey !== task.statusKey || !isCancellable(task)) {
    ElMessage.warning(t('importExportTasks.cancelStateChanged')); return;
  }
  await runAction('cancel');
}

async function downloadErrorReceipt() {
  const task = selectedTask.value;
  if (!canRead() || !drawerVisible.value || !task?.hasErrorReceipt || unavailableFiles.isUnavailable(receiptKey(task)) || actionLoading.value || detailLoading.value) return;
  const fileRequest = unavailableFiles.begin(receiptKey(task));
  const request = begin('import_export.import_tasks.execute'); if (!request) return; actionRequest = request;
  const snapshot = taskSnapshot(task);

  actionLoading.value = true;
  try {
    const blob = await downloadImportExportTaskErrorReceipt(task.id, request.signal);
    if (!request.current() || !fileRequest.current() || snapshot !== taskSnapshot(selectedTask.value)) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = task.sourceFileName?.replace(/\.xlsx$/i, '-errors.xlsx') ?? 'import-task-errors.xlsx';
      anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error) {
    if (request.current() && fileRequest.current() && snapshot === taskSnapshot(selectedTask.value)) {
      const missing = fileRequest.markUnavailable(error);
      ElMessage.error(missing ? t('dataOutput.resultUnavailable') : toProblem(error).title || t('importExportTasks.actionFailed'));
    }
  } finally {
    if (request.current()) actionLoading.value = false; request.finish();
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t('importExportTasks.loadFailed'), status: 500, code: 'importExportTasks.loadFailed' };
}

function refreshResults() {
  if (actionLoading.value || detailLoading.value) return;
  unavailableFiles.reset(); void load();
}

</script>

<template>
  <section v-if="canRead()" class="art-page-stack art-full-height">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('importExportTasks.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <div v-if="actionReceipt" role="status" data-testid="import-task-action-receipt">
      <p>{{ t('importExportTasks.receiptTarget', {task:actionReceipt.taskId}) }} · {{ t(`importExportTasks.${actionReceipt.kind}`) }}</p>
      <p>{{ t(actionReceipt.outcome === 'succeeded' ? 'importExportTasks.resultSucceeded' : actionReceipt.outcome === 'failed' ? 'importExportTasks.resultFailed' : actionReceipt.outcome === 'processing' ? 'importExportTasks.resultProcessing' : 'importExportTasks.resultUnconfirmed') }} <code v-if="actionReceipt.errorCode" translate="no">{{ actionReceipt.errorCode }}</code></p>
      <p v-if="actionReceipt.outcome === 'unconfirmed'">{{ t('importExportTasks.recoveryHint') }}</p>
    </div>
    <el-card class="art-table-card" shadow="never">
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
          <PermissionGate code="import_export.import_tasks.create">
            <PermissionGate code="import_export.static_schemas.read">
            <el-button
              v-if="isTenantSessionContext(session.currentUser)"
              type="primary"
              plain
              :icon="Plus"
              data-testid="import-export-task-create"
              v-on="{ click: listControls.create }"
            >
              {{ t('importExportTasks.addTask') }}
            </el-button>
            </PermissionGate>
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
            <el-table-column :label="t('importExportTasks.schema')" prop="schemaDisplayName" min-width="160" />
            <el-table-column :label="t('importExportTasks.worksheet')" prop="worksheetKey" width="140" />
            <el-table-column :label="t('importExportTasks.status')" width="160">
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.statusKey)" size="small">
                  {{ statusLabel(row.statusKey) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('importExportTasks.rows')" width="120">
              <template #default="{ row }">
                {{ row.validRowCount }}/{{ row.totalRows }}
              </template>
            </el-table-column>
            <el-table-column :label="t('importExportTasks.failureReason')" min-width="180">
              <template #default="{ row }">{{ importReceiptFailureLabel(row.errorCode, t) }}</template>
            </el-table-column>
            <el-table-column :label="t('importExportTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="import_export.import_tasks.read"><el-button
                    link
                    type="primary"
                    data-testid="import-export-task-detail"
                    v-on="{ click: row.open }"
                  >
                    {{ t('importExportTasks.viewDetail') }}
                  </el-button></PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('importExportTasks.emptyDirectory') }}</template>
          </el-table>

        </div>
        <el-pagination
          :current-page="page"
          :page-size="pageSize"
          class="art-table-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          v-on="{ 'update:currentPage': listControls.page, 'update:pageSize': listControls.size, change: listControls.change }"
        />
      </div>
    </el-card>

    <ImportTaskCreateDialog :open="createOpen" v-on="{ 'update:open': createHandlers.update, created: createHandlers.created }" />
    <el-drawer
      :model-value="drawerVisible"
      v-on="{ 'update:modelValue': detailControls.update }"
      :title="t('importExportTasks.detailTitle')"
      size="480px"
      destroy-on-close
    >
      <div v-loading="detailLoading">
        <ElAlert v-if="detailProblem" type="error" :closable="false" data-testid="import-task-detail-problem" :title="detailProblem.title" :description="detailProblem.code" />
        <ElButton data-testid="import-export-task-detail-refresh" :disabled="actionLoading || detailLoading" v-on="{ click: detailControls.refresh }">{{ t('importExportTasks.refreshDetail') }}</ElButton>
        <template v-if="selectedTask">
          <el-descriptions :column="1" border>
            <el-descriptions-item :label="t('importExportTasks.schema')">
              {{ selectedTask.schemaDisplayName }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.status')">
              <el-tag :type="statusTagType(selectedTask.statusKey)" size="small">
                {{ statusLabel(selectedTask.statusKey) }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.rows')">
              {{ previewStats }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.executionStats')">
              {{ executionStats }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.failureReason')">
              {{ importReceiptFailureLabel(selectedTask.errorCode, t) }}
            </el-descriptions-item>
          </el-descriptions>
          <el-table v-if="selectedTask.previewRows.length" :data="selectedTask.previewRows" data-testid="import-task-preview-rows">
            <el-table-column prop="lineNumber" :label="t('users.importResultLine')" width="80" />
            <el-table-column :label="t('importExportTasks.status')" width="100">
              <template #default="{ row }">{{ row.isValid ? t('importExportTasks.status.preview_succeeded') : t('importExportTasks.status.preview_failed') }}</template>
            </el-table-column>
            <el-table-column prop="errorCode" :label="t('importExportTasks.errorCode')" min-width="150" />
            <el-table-column prop="message" :label="t('users.importResultMessage')" min-width="150" />
          </el-table>

          <div class="art-drawer-actions">
            <PermissionGate code="import_export.import_tasks.cancel">
              <el-button v-if="isCancellable(selectedTask)" data-testid="import-export-task-cancel"
                :loading="actionLoading" :disabled="detailLoading || unconfirmedTasks.has(selectedTask.id.toLowerCase())" v-on="{ click: detailControls.cancel }">
                {{ t('importExportTasks.cancel') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="import_export.import_tasks.execute">
              <el-button
                v-if="selectedTask.statusKey === 'preview_succeeded'"
                type="primary"
                data-testid="import-export-task-execute"
                :loading="actionLoading"
                :disabled="!canExecute() || detailLoading || unconfirmedTasks.has(selectedTask.id.toLowerCase())"
                v-on="{ click: detailControls.execute }"
              >
                {{ t('importExportTasks.execute') }}
              </el-button>
              <el-button
                v-if="selectedTask.statusKey === 'execution_partial'"
                type="primary"
                data-testid="import-export-task-resume"
                :loading="actionLoading"
                :disabled="!canExecute() || detailLoading || unconfirmedTasks.has(selectedTask.id.toLowerCase())"
                v-on="{ click: detailControls.resume }"
              >
                {{ t('importExportTasks.resume') }}
              </el-button>
              <el-button
                v-if="selectedTask.statusKey === 'execution_partial' || selectedTask.statusKey === 'execution_failed'"
                data-testid="import-export-task-retry"
                :loading="actionLoading"
                :disabled="!canExecute() || detailLoading || unconfirmedTasks.has(selectedTask.id.toLowerCase())"
                v-on="{ click: detailControls.retry }"
              >
                {{ t('importExportTasks.retry') }}
              </el-button>
              <el-button
                v-if="selectedTask.hasErrorReceipt && !unavailableFiles.isUnavailable(receiptKey(selectedTask))"
                data-testid="import-export-task-error-receipt"
                :loading="actionLoading"
                :disabled="!canExecute()"
                v-on="{ click: detailControls.download }"
              >
                {{ t('importExportTasks.downloadErrorReceipt') }}
              </el-button>
            </PermissionGate>
          </div>
          <el-alert v-if="unavailableFiles.isUnavailable(receiptKey(selectedTask))" type="warning" :closable="false"
            data-testid="import-result-unavailable" :title="t('dataOutput.resultUnavailable')" />
        </template>
      </div>
    </el-drawer>
    <ElDialog v-if="cancelConfirmation && cancelHandlers" :key="cancelSequence" :model-value="cancelDialogVisible" v-on="{ 'update:modelValue': cancelHandlers.update }" :title="t('importExportTasks.cancel')" width="460px">
      <p>{{ t('importExportTasks.cancelConfirm') }}</p>
      <template #footer>
        <ElButton data-testid="import-cancel-dismiss" v-on="{ click: cancelHandlers.dismiss }">{{ t('common.cancel') }}</ElButton>
        <ElButton type="danger" data-testid="import-cancel-confirm" v-on="{ click: cancelHandlers.confirm }">{{ t('importExportTasks.cancel') }}</ElButton>
      </template>
    </ElDialog>
  </section>
</template>

<style scoped>
.art-drawer-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
}
</style>
