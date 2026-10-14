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
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useTaskStatusRefresh } from '../composables/useTaskStatusRefresh';
import { useUnavailableResultFiles } from '../composables/useUnavailableResultFiles';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
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
let pageActive = false; let linkBlocked = false; let linkedTaskId: string | undefined;
onMounted(() => { pageActive = true; }); onActivated(() => { pageActive = true; });
onDeactivated(() => { pageActive = false; }); onBeforeUnmount(() => { pageActive = false; });
const { t } = useAdminI18n();
const items = ref<ReportingExportTask[]>([]);
const definitions = ref<ReportingPublishedDefinition[]>([]);
const loading = ref(false);
const creating = ref(false);
const downloading = ref(false);
const cancelling = ref(false);
const cancelConfirmation = ref<ReportingExportTask>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const createDialogVisible = ref(false);
const detailVisible = ref(false); const detailLoading = ref(false); const selectedTask = ref<ReportingExportTaskDetail>();
const selectedDefinitionVersionKey = ref('');
const parameterValues = reactive<Record<string, string>>({});

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

watchLoading(loading);

const canCreate = () => session.can('reporting.export_tasks.create');
const canRead = () => session.can('reporting.export_tasks.read');
const canDownload = () => session.can('reporting.export_tasks.download');

// 同一定义的每个不可变发布版本都是独立授权资源，选择身份同时包含定义和版本。
const versionKey = (definition: ReportingPublishedDefinition) => definition.definitionId + ':' + definition.versionNumber;
const selectedDefinition = computed(() =>
  definitions.value.find(item => versionKey(item) === selectedDefinitionVersionKey.value));

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);
let createRequest: ReturnType<typeof scope.begin>;
// 关闭只取消客户端接入，不承诺撤销已经提交到服务端的导出任务。
watch(createDialogVisible, visible => {
  if (!visible) { createRequest?.cancel(); creating.value = false; }
}, {flush:'sync'});
const scope = useAuthorizedViewScope(session, () => {
  linkBlocked = true; linkedTaskId = undefined; detailVisible.value = false; selectedTask.value = undefined; detailLoading.value = false;
  cancelConfirmation.value = undefined; cancelling.value = false;
  unavailableFiles.reset();
  items.value = []; definitions.value = []; selectedDefinitionVersionKey.value = ''; total.value = 0; page.value = 1;
  problem.value = undefined; createDialogVisible.value = false; loading.value = false; creating.value = false; downloading.value = false;
  resetParameters();
}, async () => { openLinkedTask(); await Promise.all([loadDefinitions(), load()]); });
let loadRequest: ReturnType<typeof scope.begin>;
let detailRequest: ReturnType<typeof scope.begin>; let downloadRequest: ReturnType<typeof scope.begin>; let cancelRequest: ReturnType<typeof scope.begin>;
function clearDetail(): void {
  detailRequest?.cancel(); downloadRequest?.cancel(); cancelRequest?.cancel();
  selectedTask.value = undefined; detailLoading.value = false; downloading.value = false; cancelling.value = false; cancelConfirmation.value = undefined;
}
watch(detailVisible, visible => {
  if (!visible) { clearDetail(); linkBlocked = true; linkedTaskId = undefined; }
}, { flush: 'sync' });
function openLinkedTask(): void {
  const id = route?.query.taskId;
  if (!pageActive || linkBlocked || route?.name !== 'reporting-export-tasks' || !canRead() || !isGuid(id)
    || id === '00000000-0000-0000-0000-000000000000' || linkedTaskId === id.toLowerCase()) return;
  linkedTaskId = id.toLowerCase(); void openDetail(linkedTaskId);
}
watch(() => JSON.stringify([route?.name, route?.query.taskId]), () => {
  clearDetail(); detailVisible.value = false; linkedTaskId = undefined; createDialogVisible.value = false;
  linkBlocked = route?.name !== 'reporting-export-tasks'; if (!linkBlocked) openLinkedTask();
}, { flush: 'sync' });
async function openDetail(id: string): Promise<void> { clearDetail(); await refreshDetail(id, true); }
async function refreshDetail(id: string, open = false): Promise<void> {
  detailRequest?.cancel(); const request = scope.begin('reporting.export_tasks.read'); if (!request) return; detailRequest = request;
  if (open) detailVisible.value = true; detailLoading.value = true;
  try {
    const task = await getReportingExportTask(id, request.signal);
    if (request.current()) selectedTask.value = task;
  } catch (error) {
    if (request.current()) { problem.value = toProblem(error, 'reportingExportTasks.loadFailed'); detailVisible.value = false; }
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
  if (statusKey === 'cancelled') return t('importExportTasks.status.cancelled');
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
  for (const key of Object.keys(parameterValues)) {
    delete parameterValues[key];
  }
  for (const parameter of parameterSchema.value) {
    parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
  }
}

async function loadDefinitions(): Promise<void> {
  if (!canCreate()) return;
  const request = scope.begin('reporting.executions.run'); if (!request) return;
  try {
    const values = await listReportingPublishedDefinitions(request.signal);
    if (!request.current()) return;
    definitions.value = values.filter((item, index, all) => all.findIndex(candidate => versionKey(candidate) === versionKey(item)) === index);
    selectedDefinitionVersionKey.value = definitions.value[0] ? versionKey(definitions.value[0]) : ''; resetParameters();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally { request.finish(); }
}

const isRunning = (task?: ReportingExportTask) => task?.statusKey === 'queued' || task?.statusKey === 'processing';
useTaskStatusRefresh(() => canRead() && !problem.value
  && (items.value.some(isRunning) || detailVisible.value && isRunning(selectedTask.value)), async current => {
  if (loading.value || detailLoading.value || creating.value || downloading.value || cancelling.value || createDialogVisible.value || cancelDialogVisible.value) return;
  await load();
  const task = selectedTask.value;
  if (current() && !loading.value && !problem.value && detailVisible.value && task && isRunning(task) && !detailLoading.value)
    await refreshDetail(task.id);
});

async function load(): Promise<void> {
  if (!canRead()) {
    return;
  }
  loadRequest?.cancel(); const request = scope.begin('reporting.export_tasks.read'); loadRequest = request;
  if (!request) return;

  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listReportingExportTasks(page.value, pageSize.value, undefined, request.signal);
    if (!request.current()) return;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    updateTableHeight();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

function openCreateDialog(): void {
  if (cancelling.value) return;
  createDialogVisible.value = true;
  resetParameters();
}

async function submitCreate(): Promise<void> {
  const definition = selectedDefinition.value;
  if (!definition || creating.value || cancelling.value) {
    return;
  }
  const request = scope.begin('reporting.export_tasks.create'); if (!request) return; createRequest = request;

  creating.value = true;
  problem.value = undefined;
  try {
    const parameters: ReportingExportTaskParameterValue[] = parameterSchema.value.map(parameter => ({
      parameterKey: parameter.parameterKey,
      value: parameterValues[parameter.parameterKey] ?? null
    }));
    const task = await createReportingExportTask({
      definitionId: definition.definitionId,
      versionNumber: definition.versionNumber,
      formatKey: 'excel',
      parameters
    }, request.signal);
    if (!request.current()) return;
    // 创建成功必须对应选中的发布版本和格式，不能将其他任务误认为本次结果。
    if (task.definitionId !== definition.definitionId || task.versionNumber !== definition.versionNumber || task.formatKey !== 'excel')
      throw new Error('client.invalid_reporting_export_task_identity');
    ElMessage.success(t('reportingExportTasks.createSuccess'));
    createDialogVisible.value = false;
    await load();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.createFailed');
  } finally {
    if (request.current()) creating.value = false; request.finish();
  }
}

async function downloadTask(task: ReportingExportTask): Promise<void> {
  if (!canDownload() || task.statusKey !== 'succeeded' || downloading.value || cancelling.value || unavailableFiles.isUnavailable(task.id)) {
    return;
  }
  const request = scope.begin('reporting.export_tasks.download'); if (!request) return; downloadRequest = request; downloading.value = true;
  const fileRequest = unavailableFiles.begin(task.id);

  try {
    const blob = await downloadReportingExportTask(task.id, request.signal);
    if (!request.current() || !fileRequest.current()) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a');
      anchor.href = url; anchor.download = task.outputFileName ?? 'reporting-export.xlsx'; anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error: unknown) {
    if (request.current() && fileRequest.current()) problem.value = fileRequest.markUnavailable(error)
      ? { title: t('dataOutput.resultUnavailable'), status: 404, code: 'files.file.not_found' }
      : toProblem(error, 'reportingExportTasks.downloadFailed');
  } finally {
    if (request.current()) downloading.value = false; request.finish();
  }
}

// 用户刷新可重新确认恢复后的文件，状态轮询不反复开启已确定缺失的下载入口。
function refreshResults() {
  if (downloading.value || creating.value || cancelling.value) return;
  unavailableFiles.reset(); void load();
  if (detailVisible.value && selectedTask.value && !detailLoading.value) void refreshDetail(selectedTask.value.id);
}

const isCancellable = (task: ReportingExportTask) => task.statusKey === 'queued'
  && task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
  && session.can('reporting.export_tasks.cancel');

function requestCancellation(task: ReportingExportTask) {
  if (isCancellable(task) && !cancelling.value && !creating.value && !downloading.value) cancelConfirmation.value = { ...task };
}

async function confirmCancellation() {
  const confirmed = cancelConfirmation.value;
  cancelConfirmation.value = undefined;
  const current = confirmed && (detailVisible.value && selectedTask.value?.id === confirmed.id
    ? selectedTask.value : items.value.find(item => item.id === confirmed.id));
  if (!current || !isCancellable(current) || cancelling.value || creating.value || downloading.value) return;
  const request = scope.begin('reporting.export_tasks.cancel'); if (!request) return; cancelRequest = request;
  cancelling.value = true; problem.value = undefined;
  try {
    const result = await cancelReportingExportTask(current.id, request.signal);
    if (!request.current()) return;
    items.value = items.value.map(item => item.id === result.id ? result : item);
    if (detailVisible.value && selectedTask.value?.id === result.id) selectedTask.value = result;
    ElMessage.success(t('reportingExportTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.cancelFailed');
  } finally {
    if (request.current()) cancelling.value = false; request.finish();
  }
}

</script>

<template>
  <div class="reporting-export-tasks-view">
    <ElCard shadow="never" class="art-table-card">
      <div ref="tableMainRef" class="art-crud-table-main">
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
            <PermissionGate code="reporting.export_tasks.create">
              <ElButton
                type="primary"
                plain
                :icon="Plus"
                data-testid="reporting-export-create"
                @click="openCreateDialog"
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

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <ElTable
            v-loading="loading"
            :data="items"
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
          <ElTableColumn prop="rowCount" :label="t('reportingExportTasks.fieldRowCount')" width="100" />
          <ElTableColumn prop="createdAtUtc" :label="t('reportingExportTasks.fieldCreatedAt')" min-width="180" />
          <!-- @vue-generic {ReportingExportTask} -->
          <ElTableColumn :label="t('reportingExportTasks.actions')" width="120" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.export_tasks.read"><ElButton link type="primary" data-testid="reporting-export-detail"
                  @click="openDetail(row.id)">{{ t('importExportTasks.viewDetail') }}</ElButton></PermissionGate>
                <PermissionGate code="reporting.export_tasks.download">
                  <ElButton
                    v-if="row.statusKey === 'succeeded'"
                    link
                    type="primary"
                    data-testid="reporting-export-download"
                    :data-task-id="row.id"
                    :disabled="downloading || cancelling || unavailableFiles.isUnavailable(row.id)"
                    @click="downloadTask(row)"
                  >
                    {{ t('reportingExportTasks.download') }}
                  </ElButton>
                  <span v-if="unavailableFiles.isUnavailable(row.id)" data-testid="reporting-result-unavailable" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
                </PermissionGate>
                <ElButton v-if="isCancellable(row)" link type="warning" data-testid="reporting-export-cancel"
                  :data-task-id="row.id" :disabled="cancelling || creating || downloading" @click="requestCancellation(row)">
                  {{ t('common.cancel') }}
                </ElButton>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
        </div>
      </div>

      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        class="mt-4"
        layout="total, sizes, prev, pager, next"
        :total="total"
        @change="load"
        @size-change="load"
      />
    </ElCard>

    <ElDrawer v-model="detailVisible" :title="t('reportingExportTasks.detailTitle')" size="520px" destroy-on-close>
      <div v-loading="detailLoading">
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
          <PermissionGate code="reporting.export_tasks.download"><ElButton v-if="selectedTask.statusKey === 'succeeded'"
            data-testid="reporting-export-detail-download" :disabled="detailLoading || downloading || cancelling || unavailableFiles.isUnavailable(selectedTask.id)"
            @click="downloadTask(selectedTask)">{{ t('reportingExportTasks.download') }}</ElButton></PermissionGate>
          <span v-if="unavailableFiles.isUnavailable(selectedTask.id)" role="status">{{ t('dataOutput.resultUnavailable') }}</span>
          <ElButton v-if="isCancellable(selectedTask)" data-testid="reporting-export-detail-cancel" :disabled="detailLoading || cancelling || creating || downloading"
            @click="requestCancellation(selectedTask)">{{ t('common.cancel') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
    <ElDialog
      v-model="createDialogVisible"
      :title="t('reportingExportTasks.createTitle')"
      width="520px"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('reportingExportTasks.fieldDefinition')" required>
          <ElSelect
            v-model="selectedDefinitionVersionKey"
            data-testid="reporting-export-definition"
            class="w-full"
            :disabled="creating"
            @change="resetParameters"
          >
            <ElOption
              v-for="definition in definitions"
              :key="versionKey(definition)"
              :label="`${definition.name} · v${definition.versionNumber}`"
              :value="versionKey(definition)"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem
          v-for="parameter in parameterSchema"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
          :required="parameter.isRequired"
        >
          <ElInput v-model="parameterValues[parameter.parameterKey]" :disabled="creating" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <PermissionGate code="reporting.export_tasks.create"><ElButton
          type="primary"
          :loading="creating"
          :disabled="!selectedDefinition"
          data-testid="reporting-export-submit"
          @click="submitCreate"
        >
          {{ t('reportingExportTasks.submitCreate') }}
        </ElButton></PermissionGate>
      </template>
    </ElDialog>
    <ElDialog v-model="cancelDialogVisible" :title="t('common.cancel')" width="460px" :close-on-click-modal="false">
      <p>{{ t('reportingExportTasks.cancelConfirm') }}</p>
      <template #footer>
        <ElButton data-testid="reporting-cancel-dismiss" @click="cancelConfirmation = undefined">{{ t('common.cancel') }}</ElButton>
        <ElButton type="warning" data-testid="reporting-cancel-confirm" @click="confirmCancellation">{{ t('common.confirm') }}</ElButton>
      </template>
    </ElDialog>
  </div>
</template>
