<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElMessage,
  ElPagination,
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
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import {
  downloadImportExportTaskErrorReceipt,
  executeImportExportTask,
  getImportExportTask,
  listImportExportTasks,
  resumeImportExportTask,
  retryImportExportTask
} from '../api/import-export-tasks';

defineOptions({ name: 'ImportExportTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<ImportExportTaskResponse[]>([]);
const loading = ref(false);
const detailLoading = ref(false);
const actionLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const drawerVisible = ref(false);
const selectedTask = ref<ImportExportTaskDetailResponse>();
const createOpen = ref(false);
const scope = useAuthorizedViewScope(session, () => {
  items.value = []; selectedTask.value = undefined; problem.value = undefined; drawerVisible.value = false;
  page.value = 1; total.value = 0; createOpen.value = false; loading.value = false; detailLoading.value = false; actionLoading.value = false;
}, load);
let listRequest: ReturnType<typeof scope.begin>;
let detailRequest: ReturnType<typeof scope.begin>;
let actionRequest: ReturnType<typeof scope.begin>;

// 抽屉关闭或重新选择任务后，旧详情、执行和下载均失去接入资格。
function cancelDetail(): void {
  detailRequest?.cancel(); actionRequest?.cancel(); selectedTask.value = undefined;
  detailLoading.value = false; actionLoading.value = false;
}
watch(drawerVisible, visible => { if (!visible) cancelDetail(); }, { flush: 'sync' });

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
function onCreated(task: ImportExportTaskDetailResponse): void {
  if (!canRead()) return;
  cancelDetail(); selectedTask.value = task; drawerVisible.value = true; void load();
}

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
  listRequest?.cancel(); const request = scope.begin('import_export.import_tasks.read'); listRequest = request;
  if (!request) return;

  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listImportExportTasks(page.value, pageSize.value, undefined, request.signal);
    if (!request.current()) return;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error);
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

async function openDetail(taskId: string) {
  cancelDetail(); const request = scope.begin('import_export.import_tasks.read'); detailRequest = request;
  if (!request) return;
  detailLoading.value = true;
  drawerVisible.value = true;
  try {
    const detail = await getImportExportTask(taskId, request.signal);
    if (request.current()) selectedTask.value = detail;
  } catch (error) {
    if (!request.current()) return;
    ElMessage.error(toProblem(error).title);
    drawerVisible.value = false;
  } finally {
    if (request.current()) detailLoading.value = false; request.finish();
  }
}

async function runAction(kind: 'execute' | 'resume' | 'retry') {
  const task = selectedTask.value;
  if (!drawerVisible.value || !task || actionLoading.value || detailLoading.value) return;
  const eligible = kind === 'execute' ? task.statusKey === 'preview_succeeded'
    : kind === 'resume' ? task.statusKey === 'execution_partial'
    : task.statusKey === 'execution_partial' || task.statusKey === 'execution_failed';
  if (!eligible) return;
  const request = scope.begin('import_export.import_tasks.execute'); if (!request) return; actionRequest = request;
  const action = kind === 'execute' ? executeImportExportTask : kind === 'resume' ? resumeImportExportTask : retryImportExportTask;

  actionLoading.value = true;
  try {
    const detail = await action(task.id, request.signal);
    if (!request.current()) return;
    selectedTask.value = detail;
    ElMessage.success(t('importExportTasks.actionSuccess'));
    await load();
  } catch (error) {
    if (request.current()) ElMessage.error(toProblem(error).title || t('importExportTasks.actionFailed'));
  } finally {
    if (request.current()) actionLoading.value = false; request.finish();
  }
}

async function downloadErrorReceipt() {
  const task = selectedTask.value;
  if (!drawerVisible.value || !task?.hasErrorReceipt || actionLoading.value || detailLoading.value) return;
  const request = scope.begin('import_export.import_tasks.execute'); if (!request) return; actionRequest = request;

  actionLoading.value = true;
  try {
    const blob = await downloadImportExportTaskErrorReceipt(task.id, request.signal);
    if (!request.current()) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = task.sourceFileName?.replace(/\.xlsx$/i, '-errors.xlsx') ?? 'import-task-errors.xlsx';
      anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error) {
    if (request.current()) ElMessage.error(toProblem(error).title || t('importExportTasks.actionFailed'));
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
              <PermissionGate code="import_export.static_schemas.read">
              <el-button
                v-if="session.currentUser?.scope === 'tenant' && session.currentUser.tenantId"
                type="primary"
                plain
                :icon="Plus"
                data-testid="import-export-task-create"
                @click="createOpen = true"
              >
                {{ t('importExportTasks.addTask') }}
              </el-button>
              </PermissionGate>
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
                  <PermissionGate code="import_export.import_tasks.read"><el-button
                    link
                    type="primary"
                    data-testid="import-export-task-detail"
                    @click="openDetail(row.id)"
                  >
                    {{ t('importExportTasks.viewDetail') }}
                  </el-button></PermissionGate>
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

    <ImportTaskCreateDialog v-model:open="createOpen" @created="onCreated" />
    <el-drawer
      v-model="drawerVisible"
      :title="t('importExportTasks.detailTitle')"
      size="480px"
      destroy-on-close
    >
      <div v-loading="detailLoading">
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
            <el-descriptions-item :label="t('importExportTasks.errorCode')">
              {{ selectedTask.errorCode ?? '-' }}
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
            <PermissionGate code="import_export.import_tasks.execute">
              <el-button
                v-if="selectedTask.statusKey === 'preview_succeeded'"
                type="primary"
                data-testid="import-export-task-execute"
                :loading="actionLoading"
                :disabled="!canExecute()"
                @click="runAction('execute')"
              >
                {{ t('importExportTasks.execute') }}
              </el-button>
              <el-button
                v-if="selectedTask.statusKey === 'execution_partial'"
                type="primary"
                data-testid="import-export-task-resume"
                :loading="actionLoading"
                :disabled="!canExecute()"
                @click="runAction('resume')"
              >
                {{ t('importExportTasks.resume') }}
              </el-button>
              <el-button
                v-if="selectedTask.statusKey === 'execution_partial' || selectedTask.statusKey === 'execution_failed'"
                data-testid="import-export-task-retry"
                :loading="actionLoading"
                :disabled="!canExecute()"
                @click="runAction('retry')"
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
.art-drawer-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
}
</style>
