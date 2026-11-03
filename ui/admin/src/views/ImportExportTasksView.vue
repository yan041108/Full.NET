<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { useImportTaskDetailPolling } from '../composables/useImportTaskDetailPolling';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useTaskDetailRoute } from '../composables/useTaskDetailRoute';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElPagination,
  ElPopconfirm,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, ImportExportTaskDetailResponse, ImportExportTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import ImportTaskCreateDialog from '../components/ImportTaskCreateDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  downloadImportExportTaskErrorReceipt,
  executeImportExportTask,
  getImportExportTask,
  listImportExportTasks,
  resumeImportExportTask,
  retryImportExportTask,
  cancelImportExportTask
} from '../api/import-export-tasks';

defineOptions({ name: 'ImportExportTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<ImportExportTaskResponse[]>([]);
const loading = ref(false);
const detailLoading = ref(false);
const actionLoading = ref(false);
const cancelReloadRequired = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const drawerVisible = ref(false);
const createVisible = ref(false);
const selectedTask = ref<ImportExportTaskDetailResponse>();
let generation = 0, listGeneration = 0, detailGeneration = 0, cancellation = new AbortController();
let detailCancellation: AbortController | undefined;

/** 任务列表、详情与执行结果均绑定当前会话，切换租户后不能回填旧任务。 */
function reset() {
  generation++; listGeneration++; detailGeneration++; cancellation.abort(); cancellation = new AbortController();
  detailCancellation?.abort(); detailCancellation = undefined;
  items.value = []; selectedTask.value = undefined; total.value = 0; page.value = 1;
  drawerVisible.value = false; createVisible.value = false; loading.value = false; detailLoading.value = false; actionLoading.value = false;
  problem.value = undefined;
  cancelReloadRequired.value = false;
}

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

const canCreate = () => session.can('import_export.import_tasks.create');
const canRead = () => session.can('import_export.import_tasks.read');
const canExecute = () => session.can('import_export.import_tasks.execute');
const ownsSelectedTask = () => !!session.currentUser?.id && !!selectedTask.value
  && selectedTask.value.requestedByUserId.toLowerCase() === session.currentUser.id.toLowerCase();
const canCancelSelected = () => ownsSelectedTask() && session.can('import_export.import_tasks.cancel')
  && !!selectedTask.value && ['preview_succeeded', 'queued', 'execution_partial'].includes(selectedTask.value.statusKey);
const polling = useImportTaskDetailPolling({
  enabled: () => drawerVisible.value && canRead() && !session.switching && !detailLoading.value && !actionLoading.value && !cancelReloadRequired.value,
  task: () => selectedTask.value,
  read: getImportExportTask,
  accept: task => {
    selectedTask.value = task;
    items.value = items.value.map(item => item.id === task.id ? task : item);
  },
  failed: error => showProblem(error, t('importExportTasks.loadFailed'))
});

/** 关闭抽屉即使未切换租户，也不能接纳仍在途的详情或操作结果。 */
watch(drawerVisible, open => {
  if (!open) { detailGeneration++; detailCancellation?.abort(); selectedTask.value = undefined; cancelReloadRequired.value = false; taskDetailRoute.clear(); }
}, { flush: 'sync' });

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

async function load() {
  if (!canRead() || session.switching) {
    return;
  }

  loading.value = true;
  const current = generation, request = ++listGeneration;
  problem.value = undefined;
  try {
    const result = await listImportExportTasks(page.value, pageSize.value, undefined, cancellation.signal);
    if (current !== generation || request !== listGeneration) return;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    if (current === generation && request === listGeneration) problem.value = toProblem(error);
  } finally {
    if (current === generation && request === listGeneration) loading.value = false;
  }
}

async function openDetail(taskId: string) {
  if (!canRead() || session.switching) return;
  const current = generation, request = ++detailGeneration;
  detailCancellation?.abort(); const controller = new AbortController(); detailCancellation = controller;
  selectedTask.value = undefined;
  detailLoading.value = true;
  drawerVisible.value = true;
  try {
    const result = await getImportExportTask(taskId, controller.signal);
    if (current === generation && request === detailGeneration) { selectedTask.value = result; cancelReloadRequired.value = false; }
  } catch (error) {
    if (current === generation && request === detailGeneration) {
      drawerVisible.value = false;
      showProblem(error, t('importExportTasks.loadFailed'));
    }
  } finally {
    if (current === generation && request === detailGeneration) detailLoading.value = false;
  }
}

async function runAction(action: (signal: AbortSignal) => Promise<ImportExportTaskDetailResponse>) {
  if (!selectedTask.value || actionLoading.value || cancelReloadRequired.value || !canExecute() || !ownsSelectedTask() || session.switching) {
    return;
  }

  actionLoading.value = true;
  const current = generation, request = detailGeneration;
  try {
    const result = await action(cancellation.signal);
    if (current !== generation) return;
    if (drawerVisible.value && request === detailGeneration) selectedTask.value = result;
    showSuccess(t('importExportTasks.actionSuccess'));
    await load();
  } catch (error) {
    if (current === generation) showProblem(error, t('importExportTasks.actionFailed'));
  } finally {
    if (current === generation) actionLoading.value = false;
  }
}

/** 未知取消结果与 CAS 冲突必须先读取详情，不能用旧版本反复覆盖 Worker 的推进。 */
async function cancelSelected() {
  if (!selectedTask.value || !canCancelSelected() || actionLoading.value || cancelReloadRequired.value || session.switching) return;
  const current = generation, request = detailGeneration, task = selectedTask.value; actionLoading.value = true;
  try {
    const result = await cancelImportExportTask(task.id, task.version, cancellation.signal);
    if (current !== generation || request !== detailGeneration || !drawerVisible.value) return;
    selectedTask.value = result; showSuccess(t('importExportTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (current !== generation || request !== detailGeneration) return;
    cancelReloadRequired.value = !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500;
    showProblem(error, t('importExportTasks.actionFailed'));
  } finally { if (current === generation) actionLoading.value = false; }
}

async function downloadErrorReceipt() {
  if (!selectedTask.value || actionLoading.value || !canExecute() || session.switching) {
    return;
  }

  actionLoading.value = true;
  const current = generation, sourceFileName = selectedTask.value.sourceFileName;
  try {
    const blob = await downloadImportExportTaskErrorReceipt(selectedTask.value.id, cancellation.signal);
    if (current !== generation) return;
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = sourceFileName?.replace(/\.xlsx$/i, '-errors.xlsx')
      ?? 'import-task-errors.xlsx';
    anchor.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    if (current === generation) showProblem(error, t('importExportTasks.actionFailed'));
  } finally {
    if (current === generation) actionLoading.value = false;
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t('importExportTasks.loadFailed'), status: 500, code: 'importExportTasks.loadFailed' };
}

const taskDetailRoute = useTaskDetailRoute({ routeName: 'import-export-tasks',
  enabled: () => canRead() && !session.switching,
  context: () => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId].join('|'),
  open: openDetail, close: () => { drawerVisible.value = false; }
});
onMounted(load);
onDeactivated(reset);
onActivated(() => { if (!items.value.length && !loading.value) void load(); });
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.permissions.join('|'), session.switching], () => {
  reset(); if (!session.switching) void load();
}, { flush: 'sync' });
onBeforeUnmount(reset);
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('importExportTasks.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-table-card" shadow="never">
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
            <PermissionGate code="import_export.import_tasks.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="import-export-task-create"
                v-if="canCreate() && session.can('import_export.static_schemas.read') && !!session.currentUser?.tenantId && !session.switching"
                @click="createVisible = true"
              >
                {{ t('importExportTasks.addTask') }}
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
            <el-table-column :label="t('importExportTasks.errorCode')" prop="errorCode" min-width="180" />
            <el-table-column :label="t('importExportTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <el-button
                    link
                    type="primary"
                    data-testid="import-export-task-detail"
                    @click="openDetail(row.id)"
                  >
                    {{ t('importExportTasks.viewDetail') }}
                  </el-button>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('importExportTasks.emptyDirectory') }}</template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next"
              :page-sizes="[10, 20, 50]"
              @current-change="load"
              @size-change="load"
            />
          </div>
        </div>
      </div>
    </el-card>

    <ImportTaskCreateDialog v-model="createVisible" @created="task => { selectedTask = task; drawerVisible = true; void load(); }" />

    <el-drawer
      v-model="drawerVisible"
      :title="t('importExportTasks.detailTitle')"
      size="min(480px, 100vw)"
      destroy-on-close
    >
      <div v-loading="detailLoading">
        <template v-if="selectedTask">
          <el-alert v-if="cancelReloadRequired" type="warning" :closable="false" :title="t('importExportTasks.cancelReload')" />
          <el-alert v-else-if="selectedTask.statusKey === 'canceled'" type="info" :closable="false" :title="t('importExportTasks.cancelDone')" />
          <el-alert v-else-if="ownsSelectedTask() && session.can('import_export.import_tasks.cancel') && selectedTask.statusKey === 'executing'"
            type="info" :closable="false" :title="t('importExportTasks.cancelExecuting')" />
          <el-alert v-if="polling.paused.value" type="info" :closable="false" :title="t('importExportTasks.refreshPaused')" />
          <el-button data-testid="import-export-task-refresh" :disabled="detailLoading || actionLoading" @click="openDetail(selectedTask.id)">
            {{ t('importExportTasks.refreshDetail') }}
          </el-button>
          <el-descriptions :column="1" border>
            <el-descriptions-item :label="t('importExportTasks.schema')">
              {{ selectedTask.schemaDisplayName }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.status')">
              <el-tag :type="statusTagType(selectedTask.statusKey)" size="small" :class="{ 'task-canceled-tag': selectedTask.statusKey === 'canceled' }">
                {{ statusLabel(selectedTask.statusKey) }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.rows')">
              {{ previewStats }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.executionProgress')">
              {{ executionStats }}
            </el-descriptions-item>
            <el-descriptions-item :label="t('importExportTasks.errorCode')">
              {{ selectedTask.errorCode ?? '-' }}
            </el-descriptions-item>
          </el-descriptions>

          <template v-if="selectedTask.columnMappings?.length">
            <p>{{ t('importProfiles.frozen') }}</p>
            <dl data-testid="import-task-frozen-mapping">
              <template v-for="column in selectedTask.columnMappings" :key="column.targetColumn">
                <dt>{{ column.targetColumn }}</dt><dd>{{ column.sourceHeader }}</dd>
              </template>
            </dl>
          </template>
          <el-table v-if="selectedTask.previewRows.length" :data="selectedTask.previewRows" max-height="280">
            <el-table-column :label="t('importExportTasks.lineNumber')" prop="lineNumber" width="90" />
            <el-table-column :label="t('importExportTasks.validationMessage')" min-width="200">
              <template #default="{ row }">{{ row.message ?? row.errorCode ?? t('importExportTasks.rowValid') }}</template>
            </el-table-column>
          </el-table>

          <div class="art-drawer-actions">
            <el-popconfirm v-if="canCancelSelected()" :title="t('importExportTasks.cancelConfirm')"
              :disabled="actionLoading || cancelReloadRequired" :confirm-button-text="t('importExportTasks.cancelConfirmAction')"
              :cancel-button-text="t('common.cancel')" width="280" @confirm="cancelSelected">
              <template #reference><el-button type="danger" plain data-testid="import-export-task-cancel" :loading="actionLoading"
                :disabled="actionLoading || cancelReloadRequired">{{ t('importExportTasks.cancel') }}</el-button></template>
            </el-popconfirm>
            <PermissionGate code="import_export.import_tasks.execute">
              <el-button
                v-if="ownsSelectedTask() && selectedTask.statusKey === 'preview_succeeded'"
                type="primary"
                data-testid="import-export-task-execute"
                :loading="actionLoading"
                :disabled="!canExecute() || cancelReloadRequired"
                @click="runAction(signal => executeImportExportTask(selectedTask!.id, signal))"
              >
                {{ t('importExportTasks.execute') }}
              </el-button>
              <el-button
                v-if="ownsSelectedTask() && selectedTask.statusKey === 'execution_partial'"
                type="primary"
                data-testid="import-export-task-resume"
                :loading="actionLoading"
                :disabled="!canExecute() || cancelReloadRequired"
                @click="runAction(signal => resumeImportExportTask(selectedTask!.id, signal))"
              >
                {{ t('importExportTasks.resume') }}
              </el-button>
              <el-button
                v-if="ownsSelectedTask() && (selectedTask.statusKey === 'execution_partial' || selectedTask.statusKey === 'execution_failed')"
                data-testid="import-export-task-retry"
                :loading="actionLoading"
                :disabled="!canExecute() || cancelReloadRequired"
                @click="runAction(signal => retryImportExportTask(selectedTask!.id, signal))"
              >
                {{ t('importExportTasks.retry') }}
              </el-button>
              <el-button
                v-if="selectedTask.hasErrorReceipt"
                data-testid="import-export-task-error-receipt"
                :loading="actionLoading"
                :disabled="!canExecute()"
                @click="downloadErrorReceipt"
              >
                {{ t('importExportTasks.downloadErrorReceipt') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>
      </div>
    </el-drawer>
  </section>
</template>

<style scoped>
.task-canceled-tag { color: var(--el-text-color-primary); background-color: var(--el-fill-color-light); border-color: var(--el-border-color); }
.art-drawer-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
}
</style>
