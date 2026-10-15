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
const scope = useAuthorizedViewScope(session, () => {
  linkBlocked = true; linkedTaskId = undefined; detailVisible.value = false; clearDetail();
  cancelConfirmation.value = undefined;
  unavailableFiles.reset();
  items.value = []; total.value = 0; page.value = 1; problem.value = undefined;
  editorOpen.value = false; filterDocumentItemId.value = ''; appliedDocumentItemId.value = ''; editorForm.documentItemId = ''; editorForm.versionId = '';
  loading.value = false; changing.value = false;
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
  request?.cancel(); if (request && actionRequest === request) { actionRequest = undefined; changing.value = false; }
}
function finishAction(request: NonNullable<typeof actionRequest>): void {
  if (request.current() && actionRequest === request) { actionRequest = undefined; changing.value = false; }
  request.finish();
}
// 关闭同步撤销本弹窗创建的接入资格；已到服务端的写入仍由服务端完成。
watch(editorOpen, open => {
  if (!open) {
    cancelAction(createRequest); createRequest = undefined;
    editorForm.documentItemId = ''; editorForm.versionId = '';
  }
}, { flush: 'sync' });

function clearDetail(): void {
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
  } else {
    openLinkedTask(); if (wasBlocked && pageActive.value) void load();
  }
}, { flush: 'sync' });
async function openDetail(id: string): Promise<void> {
  if (!canRead() || !isGuid(id) || id === '00000000-0000-0000-0000-000000000000') return;
  clearDetail(); editorOpen.value = false; await refreshDetail(id, true);
}
async function refreshDetail(id: string, open = false): Promise<void> {
  detailRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); if (!request) return;
  detailRequest = request; if (open) detailVisible.value = true; detailLoading.value = true;
  try {
    const task = await getDocumentPreviewTask(id, request.signal);
    if (!request.current()) return;
    if (task.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_document_preview_task_identity');
    selectedTask.value = task;
  } catch (error) {
    if (request.current()) { problem.value = toProblem(error); detailVisible.value = false; }
  } finally { if (request.current()) detailLoading.value = false; request.finish(); }
}
const sameId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
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
  if (current() && !problem.value && detailVisible.value && task && isRunning(task) && !detailLoading.value) await refreshDetail(task.id);
});

async function load() {
  listRequest?.cancel(); const request = begin('document.host_preview_tasks.read'); listRequest = request;
  if (!request) return;
  loading.value = true;
  problem.value = undefined;
  try {
    const documentItemId = appliedDocumentItemId.value || undefined;
    const result = await listDocumentPreviewTasks(page.value, pageSize.value, documentItemId, request.signal);
    if (!request.current()) return;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch (error) {
    if (request.current()) problem.value = toProblem(error);
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
  }
}

async function applyFilter(): Promise<void> {
  if (!canRead()) return;
  // 输入是草稿，分页、轮询与刷新只能复用已明确应用的查询。
  cancelAction(pdfRequest); pdfRequest = undefined; pdfTarget = undefined;
  appliedDocumentItemId.value = filterDocumentItemId.value.trim(); page.value = 1;
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
  if (changing.value || !editorOpen.value || !canCreate()) return;
  const documentItemId = editorForm.documentItemId.trim();
  const versionId = editorForm.versionId.trim();
  if (!documentItemId) {
    return;
  }
  const request = beginAction('document.host_preview_tasks.create'); if (!request) return; createRequest = request;
  try {
    const created = await createDocumentPreviewTask({
      documentItemId,
      versionId: versionId || null
    }, request.signal);
    if (!request.current()) return;
    editorOpen.value = false;
    ElMessage.success(t('documentPreviewTasks.createSuccess'));
    await Promise.all([load(), openDetail(created.id)]);
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    finishAction(request);
  }
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
  await Promise.all([load(), detailVisible.value && selectedTask.value && !detailLoading.value ? refreshDetail(selectedTask.value.id) : Promise.resolve()]);
}

const isCancellable = (task: HostDocumentPreviewTaskResponse) => canRead() && task.statusKey === 'pending'
  && task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
  && session.can('document.host_preview_tasks.cancel');

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
  if (detailVisible.value && sameId(selectedTask.value?.id, taskId)) detailActionRequest = request;
  try {
    const result = await cancelDocumentPreviewTask(taskId, request.signal);
    if (!request.current()) return;
    if (result.id.toLowerCase() !== taskId.toLowerCase() || result.statusKey !== 'cancelled') throw new Error('client.invalid_document_preview_task_cancellation');
    items.value = items.value.map(item => sameId(item.id, taskId) ? result : item);
    if (detailVisible.value && sameId(selectedTask.value?.id, taskId)) selectedTask.value = result;
    ElMessage.success(t('documentPreviewTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'documentPreviewTasks.cancelFailed');
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

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPreviewTasks.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" @click="refreshResults">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>
      <div ref="tableMainRef" class="art-crud-table-main">
        <el-form class="document-module-inline-query" :inline="true" @submit.prevent="applyFilter">
          <el-form-item :label="t('documentPreviewTasks.documentItemId')">
            <el-input
              v-model="filterDocumentItemId"
              class="document-preview-tasks-view__filter"
              :placeholder="t('documentPreviewTasks.filterDocumentItemId')"
              clearable
              data-testid="document-preview-task-filter"
              @keydown.enter.prevent="applyFilter"
              @clear="applyFilter"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" @click="applyFilter">{{ t('documentPermissions.query') }}</el-button>
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
          @refresh="refreshResults"
        >
          <template #left>
            <PermissionGate code="document.host_preview_tasks.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-preview-task-create"
                @click="openCreate"
              >
                {{ t('documentPreviewTasks.addTask') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            v-loading="loading"
            :data="items"
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
                    :title="t('documentPreviewTasks.details')" @click="openDetail(row.id)" />
                  <PermissionGate v-if="row.statusKey === 'succeeded'" code="document.host_preview_tasks.read">
                    <ArtTableActionButton
                      type="view"
                      test-id="document-preview-task-open-pdf"
                      :title="t('documentPreviewTasks.openPdf')"
                      :disabled="changing || editorOpen || cancelDialogVisible || unavailableFiles.isUnavailable(row.id)"
                      @click="openPdf(row as HostDocumentPreviewTaskResponse)"
                    />
                    <span v-if="unavailableFiles.isUnavailable(row.id)" data-testid="document-result-unavailable" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
                  </PermissionGate>
                  <ElButton v-if="isCancellable(row as HostDocumentPreviewTaskResponse)" link type="warning"
                    data-testid="document-preview-task-cancel" :data-task-id="row.id" :disabled="changing || editorOpen"
                    @click="requestCancellation(row as HostDocumentPreviewTaskResponse)">{{ t('common.cancel') }}</ElButton>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPreviewTasks.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          @change="load"
        />
      </div>
    </el-card>

    <ElDrawer v-model="detailVisible" :title="t('documentPreviewTasks.detailTitle')" size="560px">
      <div v-loading="detailLoading" data-testid="document-preview-detail">
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
            @click="openPdf(selectedTask)">{{ t('documentPreviewTasks.openPdf') }}</ElButton>
          <span v-if="unavailableFiles.isUnavailable(selectedTask.id)" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
          <ElButton v-if="isCancellable(selectedTask)" data-testid="document-preview-detail-cancel"
            :disabled="detailLoading || changing || editorOpen" @click="requestCancellation(selectedTask)">{{ t('common.cancel') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
    <ArtFormDialog
      v-model:open="editorOpen"
      :title="t('documentPreviewTasks.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-preview-task-editor-submit"
      :show-confirm="canCreate()"
      @confirm="submitCreate"
    >
      <el-form ref="editorFormRef" data-testid="document-preview-task-editor-form" :model="editorForm" label-width="140px">
        <el-form-item :label="t('documentPreviewTasks.documentItemId')" required>
          <el-input v-model="editorForm.documentItemId" :disabled="changing" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentPreviewTasks.versionIdOptional')">
          <el-input v-model="editorForm.versionId" :disabled="changing" autocomplete="off" />
        </el-form-item>
      </el-form>
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
