<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onActivated, onDeactivated, onBeforeUnmount, reactive, ref, watch } from 'vue';
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
  ElOption,
  ElPagination,
  ElPopconfirm,
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
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listReportingPublishedDefinitions } from '../api/reporting-publications';
import { useBoundedPolling } from '../composables/useBoundedPolling';
import { showSuccess } from '../feedback/fullNetMessage';
import { useTaskDetailRoute } from '../composables/useTaskDetailRoute';
import {
  createReportingExportTask,
  downloadReportingExportTask,
  getReportingExportTask,
  listReportingExportTasks,
  cancelReportingExportTask
} from '../api/reporting-export-tasks';

defineOptions({ name: 'ReportingExportTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<ReportingExportTask[]>([]);
const definitions = ref<ReportingPublishedDefinition[]>([]);
const selectedDefinition = ref<ReportingPublishedDefinition>();
const loading = ref(false);
const creating = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const createDialogVisible = ref(false);
const selectedDefinitionId = ref('');
const parameterValues = reactive<Record<string, string>>({});
const detailOpen = ref(false), detailLoading = ref(false), detailTask = ref<ReportingExportTaskDetail>();
const detailProblem = ref<FullNetProblemDetails>();
const canceling = ref(false), cancelReloadRequired = ref(false);
let detailVersion = 0, detailRequest: AbortController | undefined;

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

const active = ref(true);
const available = () => active.value && !session.switching && !!session.currentUser?.tenantId;
const canCreate = () => available() && session.can('reporting.export_tasks.create') && session.can('reporting.published_definitions.read');
const canRead = () => available() && session.can('reporting.export_tasks.read');
const canDownload = () => available() && session.can('reporting.export_tasks.download');
const canCancel = () => available() && session.can('reporting.export_tasks.cancel') && !!detailTask.value
  && detailTask.value.requestedByUserId === session.currentUser?.id && detailTask.value.statusKey === 'queued';
const scopeKey = () => [session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.id].join('|');
let generation = 0, listVersion = 0, searchVersion = 0;
let requests = new AbortController(), searchRequest: AbortController | undefined;
const progress = useBoundedPolling({
  enabled: () => canRead() && !loading.value,
  pending: () => items.value.some(item => ['queued', 'processing'].includes(item.statusKey)),
  key: () => scopeKey() + '|' + page.value + '|' + pageSize.value,
  intervalMs: 5000,
  read: signal => listReportingExportTasks(page.value, pageSize.value, undefined, signal),
  accept: result => { items.value = result.items; total.value = result.total; },
  failed: error => { problem.value = toProblem(error, 'reportingExportTasks.loadFailed'); }
});
const progressPaused = progress.paused;
const detailProgress = useBoundedPolling({
  enabled: () => canRead() && detailOpen.value && !detailLoading.value && !canceling.value && !cancelReloadRequired.value,
  pending: () => !!detailTask.value && ['queued', 'processing'].includes(detailTask.value.statusKey),
  key: () => scopeKey() + '|' + detailTask.value?.id,
  intervalMs: 5000,
  read: signal => getReportingExportTask(detailTask.value!.id, signal),
  accept: task => { if (task.id === detailTask.value?.id) detailTask.value = task; },
  failed: error => { detailProblem.value = toProblem(error, 'reportingExportTasks.loadFailed'); }
});
const detailRefreshPaused = detailProgress.paused;

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);

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

function onDefinitionChanged(): void {
  selectedDefinition.value = definitions.value.find(item => item.id === selectedDefinitionId.value);
  resetParameters();
}
async function searchDefinitions(keyword: string): Promise<void> {
  if (!canCreate()) return;
  searchRequest?.abort(); const controller = new AbortController(); searchRequest = controller;
  const current = generation, version = ++searchVersion;
  try {
    const result = await listReportingPublishedDefinitions(keyword, controller.signal);
    if (current === generation && version === searchVersion && !controller.signal.aborted && canCreate()) definitions.value = result.items;
  } catch (error) {
    if (current === generation && !controller.signal.aborted) problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  }
}
function resetParameters(): void {
  for (const key of Object.keys(parameterValues)) delete parameterValues[key];
  for (const parameter of parameterSchema.value) parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
}
async function load(): Promise<void> {
  if (!canRead()) return;
  progress.stop(); const current = generation, version = ++listVersion;
  loading.value = true; problem.value = undefined;
  try {
    const result = await listReportingExportTasks(page.value, pageSize.value, undefined, requests.signal);
    if (current !== generation || version !== listVersion || !canRead()) return;
    items.value = result.items; page.value = result.page; pageSize.value = result.pageSize; total.value = result.total;
    updateTableHeight();
  } catch (error) {
    if (current === generation && version === listVersion) problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally {
    if (current === generation && version === listVersion) { loading.value = false; progress.restart(); }
  }
}
async function openCreateDialog(): Promise<void> {
  if (!canCreate()) return;
  createDialogVisible.value = true; selectedDefinitionId.value = ''; selectedDefinition.value = undefined; resetParameters();
  await searchDefinitions('');
}
async function submitCreate(): Promise<void> {
  if (!canCreate() || creating.value || !selectedDefinition.value || !createDialogVisible.value) return;
  const current = generation; creating.value = true; problem.value = undefined;
  try {
    const parameters: ReportingExportTaskParameterValue[] = parameterSchema.value.map(parameter => ({
      parameterKey: parameter.parameterKey, value: parameterValues[parameter.parameterKey] ?? null
    }));
    await createReportingExportTask({ definitionId: selectedDefinition.value.id, versionNumber: selectedDefinition.value.versionNumber,
      formatKey: 'excel', parameters }, requests.signal);
    if (current !== generation || !canCreate()) return;
    showSuccess(t('reportingExportTasks.createSuccess')); createDialogVisible.value = false; await load();
  } catch (error) {
    if (current === generation) problem.value = toProblem(error, 'reportingExportTasks.createFailed');
  } finally { if (current === generation) creating.value = false; }
}
async function downloadTask(task: ReportingExportTask): Promise<void> {
  if (!canDownload() || task.statusKey !== 'succeeded' || task.requestedByUserId !== session.currentUser?.id) return;
  const current = generation;
  try {
    const blob = await downloadReportingExportTask(task.id, requests.signal);
    if (current !== generation || !canDownload()) return;
    const url = URL.createObjectURL(blob); const anchor = document.createElement('a');
    anchor.href = url; anchor.download = task.outputFileName ?? 'reporting-export.xlsx'; anchor.click(); URL.revokeObjectURL(url);
  } catch (error) { if (current === generation) problem.value = toProblem(error, 'reportingExportTasks.downloadFailed'); }
}
async function openDetail(id: string): Promise<void> {
  if (!canRead()) return;
  const current = generation, version = ++detailVersion;
  detailRequest?.abort(); const controller = new AbortController(); detailRequest = controller;
  detailTask.value = undefined; detailProblem.value = undefined; detailOpen.value = true; detailLoading.value = true;
  try {
    const task = await getReportingExportTask(id, controller.signal);
    if (current === generation && version === detailVersion && detailOpen.value && canRead()) { detailTask.value = task; cancelReloadRequired.value = false; }
  } catch (error) { if (current === generation && version === detailVersion) detailProblem.value = toProblem(error, 'reportingExportTasks.loadFailed'); }
  finally { if (current === generation && version === detailVersion) detailLoading.value = false; }
}
function reset(): void {
  generation++; listVersion++; searchVersion++; requests.abort(); requests = new AbortController(); searchRequest?.abort(); progress.stop();
  items.value = []; total.value = 0; page.value = 1; definitions.value = []; selectedDefinitionId.value = ''; selectedDefinition.value = undefined;
  createDialogVisible.value = false; resetParameters(); problem.value = undefined; loading.value = false; creating.value = false;
  detailVersion++; detailRequest?.abort(); detailProgress.stop(); detailTask.value = undefined; detailOpen.value = false; detailLoading.value = false;
  canceling.value = false; cancelReloadRequired.value = false;
}

/** 未知结果和领取竞争只能先重读，不将旧页面版本重放到处理中任务。 */
async function cancelDetail(): Promise<void> {
  if (!canCancel() || !detailTask.value || canceling.value || cancelReloadRequired.value) return;
  const current = generation, requestVersion = detailVersion, task = detailTask.value; canceling.value = true;
  try {
    const result = await cancelReportingExportTask(task.id, task.version, requests.signal);
    if (current !== generation || requestVersion !== detailVersion || !detailOpen.value) return;
    detailTask.value = result; showSuccess(t('reportingExportTasks.cancelSuccess')); await load();
  } catch (error) {
    if (current !== generation || requestVersion !== detailVersion) return;
    cancelReloadRequired.value = !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500;
    detailProblem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally { if (current === generation) canceling.value = false; }
}
watch(() => [scopeKey(), session.currentUser?.permissions.join('|'), session.switching, active.value], () => {
  reset(); if (canRead()) void load();
}, { immediate: true, flush: 'sync' });
const taskDetailRoute = useTaskDetailRoute({ routeName: 'reporting-export-tasks', enabled: canRead, context: scopeKey,
  open: openDetail, close: () => { detailOpen.value = false; }
});
watch(detailOpen, open => {
  if (!open) { detailVersion++; detailRequest?.abort(); detailTask.value = undefined; detailProblem.value = undefined; detailLoading.value = false; cancelReloadRequired.value = false; taskDetailRoute.clear(); }
}, { flush: 'sync' });
watch(createDialogVisible, open => { if (!open) { searchVersion++; searchRequest?.abort(); } });
onActivated(() => { active.value = true; });
onDeactivated(() => { active.value = false; });
onBeforeUnmount(reset);
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
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="reporting.export_tasks.create">
              <ElButton
                type="primary"
                plain
                :icon="Plus"
                data-testid="reporting-export-create"
                :disabled="!canCreate()"
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

        <ElAlert v-if="progressPaused" type="info" :closable="false" :title="t('reportingExportTasks.refreshPaused')" class="mb-4" />
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
              <ElTag :type="statusTagType(row.statusKey)" :class="{ 'task-canceled-tag': row.statusKey === 'canceled' }">
                {{ statusLabel(row.statusKey) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="rowCount" :label="t('reportingExportTasks.fieldRowCount')" width="100" />
          <ElTableColumn prop="errorMessage" :label="t('reportingExportTasks.failureReason')" min-width="200" show-overflow-tooltip />
          <ElTableColumn prop="createdAtUtc" :label="t('reportingExportTasks.fieldCreatedAt')" min-width="180" />
          <!-- @vue-generic {ReportingExportTask} -->
          <ElTableColumn :label="t('reportingExportTasks.actions')" width="120" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <ElButton link type="primary" data-testid="reporting-export-detail" @click="openDetail(row.id)">{{ t('reportingExportTasks.detail') }}</ElButton>
                <PermissionGate code="reporting.export_tasks.download">
                  <ElButton
                    v-if="row.statusKey === 'succeeded' && row.requestedByUserId === session.currentUser?.id"
                    link
                    type="primary"
                    data-testid="reporting-export-download"
                    @click="downloadTask(row)"
                  >
                    {{ t('reportingExportTasks.download') }}
                  </ElButton>
                </PermissionGate>
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

    <ElDialog
      v-model="createDialogVisible"
      :title="t('reportingExportTasks.createTitle')"
      width="min(520px, 96vw)"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('reportingExportTasks.fieldDefinition')">
          <ElSelect
            v-model="selectedDefinitionId"
            filterable remote :remote-method="searchDefinitions"
            data-testid="reporting-export-definition"
            class="w-full"
            @change="onDefinitionChanged"
          >
            <ElOption
              v-for="definition in definitions"
              :key="definition.id"
              :label="definition.name"
              :value="definition.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem
          v-for="parameter in parameterSchema"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
        >
          <ElInput v-model="parameterValues[parameter.parameterKey]" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <ElButton
          type="primary"
          :loading="creating"
          :disabled="!canCreate() || !selectedDefinition"
          data-testid="reporting-export-submit"
          @click="submitCreate"
        >
          {{ t('reportingExportTasks.submitCreate') }}
        </ElButton>
      </template>
    </ElDialog>
    <ElDrawer v-model="detailOpen" :title="t('reportingExportTasks.detail')" size="min(680px, 100vw)">
      <div v-loading="detailLoading" data-testid="reporting-export-detail-panel">
        <ElAlert v-if="detailProblem" type="error" :closable="false" :title="detailProblem.detail ?? detailProblem.title" />
        <ElAlert v-if="detailRefreshPaused" type="info" :closable="false" :title="t('reportingExportTasks.refreshPaused')" />
        <template v-if="detailTask">
          <ElAlert v-if="cancelReloadRequired" type="warning" :closable="false" :title="t('reportingExportTasks.cancelReload')" />
          <ElAlert v-else-if="detailTask.statusKey === 'canceled'" type="info" :closable="false" :title="t('reportingExportTasks.cancelDone')" />
          <ElAlert v-else-if="detailTask.statusKey === 'processing' && detailTask.requestedByUserId === session.currentUser?.id && session.can('reporting.export_tasks.cancel')"
            type="info" :closable="false" :title="t('reportingExportTasks.cancelProcessing')" />
          <ElDescriptions :column="1" border>
            <ElDescriptionsItem :label="t('reportingExportTasks.fieldDefinition')">{{ detailTask.definitionName }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('reportingExportTasks.fieldVersion')">{{ detailTask.versionNumber }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('reportingExportTasks.fieldStatus')">{{ statusLabel(detailTask.statusKey) }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('reportingExportTasks.fieldRowCount')">{{ detailTask.rowCount }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('reportingExportTasks.failureReason')">{{ detailTask.errorMessage ?? detailTask.errorCode ?? '-' }}</ElDescriptionsItem>
          </ElDescriptions>
          <ElTable :data="detailTask.parameters" class="mt-4">
            <ElTableColumn prop="parameterKey" :label="t('reportingExportTasks.parameterKey')" />
            <ElTableColumn prop="value" :label="t('reportingExportTasks.parameterValue')" />
          </ElTable>
          <ElButton class="mt-4" :disabled="canceling" data-testid="reporting-export-detail-refresh" @click="openDetail(detailTask.id)">{{ t('common.refresh') }}</ElButton>
          <ElPopconfirm v-if="canCancel()" :title="t('reportingExportTasks.cancelConfirm')" :disabled="canceling || cancelReloadRequired"
            :confirm-button-text="t('reportingExportTasks.cancelConfirmAction')" :cancel-button-text="t('common.cancel')" width="280" @confirm="cancelDetail">
            <template #reference><ElButton type="danger" plain :loading="canceling" :disabled="canceling || cancelReloadRequired"
              data-testid="reporting-export-detail-cancel">{{ t('reportingExportTasks.cancel') }}</ElButton></template>
          </ElPopconfirm>
          <ElButton v-if="canDownload() && detailTask.statusKey === 'succeeded' && detailTask.requestedByUserId === session.currentUser?.id"
            data-testid="reporting-export-detail-download" @click="downloadTask(detailTask)">{{ t('reportingExportTasks.download') }}</ElButton>
        </template>
      </div>
    </ElDrawer>
  </div>
</template>

<style scoped>
.task-canceled-tag { color: var(--el-text-color-primary); background-color: var(--el-fill-color-light); border-color: var(--el-border-color); }
</style>
