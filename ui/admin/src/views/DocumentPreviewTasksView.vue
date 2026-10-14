<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
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
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
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
  listDocumentPreviewTasks,
  openDocumentPreviewTaskContent
} from '../api/document-preview-tasks';

defineOptions({ name: 'DocumentPreviewTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentPreviewTaskResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const cancelConfirmation = ref<HostDocumentPreviewTaskResponse>();
const cancelDialogVisible = computed({ get: () => !!cancelConfirmation.value, set: value => { if (!value) cancelConfirmation.value = undefined; } });
const unavailableFiles = useUnavailableResultFiles();
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const filterDocumentItemId = ref('');
const editorOpen = ref(false);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  documentItemId: '',
  versionId: ''
});
const scope = useAuthorizedViewScope(session, () => {
  cancelConfirmation.value = undefined;
  unavailableFiles.reset();
  items.value = []; total.value = 0; page.value = 1; problem.value = undefined;
  editorOpen.value = false; filterDocumentItemId.value = ''; editorForm.documentItemId = ''; editorForm.versionId = '';
  loading.value = false; changing.value = false;
}, load);
let listRequest: ReturnType<typeof scope.begin>;
let createRequest: ReturnType<typeof scope.begin>;
// 关闭同步撤销本弹窗创建的接入资格；已到服务端的写入仍由服务端完成。
watch(editorOpen, open => {
  if (!open) {
    createRequest?.cancel(); changing.value = false;
    editorForm.documentItemId = ''; editorForm.versionId = '';
  }
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

const canCreate = () => session.can('document.host_preview_tasks.create');
const canRead = () => session.can('document.host_preview_tasks.read');

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

useTaskStatusRefresh(() => canRead() && !problem.value
  && items.value.some(task => task.statusKey === 'pending' || task.statusKey === 'processing'), async () => {
  if (loading.value || changing.value || editorOpen.value || cancelDialogVisible.value) return;
  await load();
});

async function load() {
  listRequest?.cancel(); const request = scope.begin('document.host_preview_tasks.read'); listRequest = request;
  if (!request) return;
  loading.value = true;
  problem.value = undefined;
  try {
    const documentItemId = filterDocumentItemId.value.trim() || undefined;
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

function openCreate() {
  if (!canCreate() || changing.value || cancelDialogVisible.value) return;
  editorForm.documentItemId = filterDocumentItemId.value.trim();
  editorForm.versionId = '';
  editorOpen.value = true;
}

async function submitCreate() {
  if (changing.value) return;
  const documentItemId = editorForm.documentItemId.trim();
  const versionId = editorForm.versionId.trim();
  if (!documentItemId) {
    return;
  }
  const request = scope.begin('document.host_preview_tasks.create'); if (!request) return; createRequest = request;

  changing.value = true;
  try {
    await createDocumentPreviewTask({
      documentItemId,
      versionId: versionId || null
    }, request.signal);
    if (!request.current()) return;
    editorOpen.value = false;
    ElMessage.success(t('documentPreviewTasks.createSuccess'));
    await load();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    if (request.current()) changing.value = false; request.finish();
  }
}

async function openPdf(row: HostDocumentPreviewTaskResponse) {
  if (row.statusKey !== 'succeeded' || changing.value || editorOpen.value || cancelDialogVisible.value || !canRead() || unavailableFiles.isUnavailable(row.id)) {
    return;
  }
  const request = scope.begin('document.host_preview_tasks.read'); if (!request) return;
  const fileRequest = unavailableFiles.begin(row.id);

  changing.value = true;
  try {
    await openDocumentPreviewTaskContent(row.id, request.signal);
  } catch (error) {
    if (request.current() && fileRequest.current()) problem.value = fileRequest.markUnavailable(error)
      ? { title: t('dataOutput.resultUnavailable'), status: 404, code: 'files.file.not_found' }
      : toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    if (request.current()) changing.value = false; request.finish();
  }
}

function refreshResults() {
  if (changing.value) return;
  unavailableFiles.reset(); void load();
}

const isCancellable = (task: HostDocumentPreviewTaskResponse) => task.statusKey === 'pending'
  && task.requestedByUserId.toLowerCase() === session.currentUser?.id.toLowerCase()
  && session.can('document.host_preview_tasks.cancel');

function requestCancellation(task: HostDocumentPreviewTaskResponse) {
  if (isCancellable(task) && !changing.value && !editorOpen.value) cancelConfirmation.value = { ...task };
}

async function confirmCancellation() {
  const confirmed = cancelConfirmation.value;
  cancelConfirmation.value = undefined;
  const current = confirmed && items.value.find(item => item.id === confirmed.id);
  if (!current || !isCancellable(current) || changing.value || editorOpen.value) return;
  if (current.version !== confirmed.version) { ElMessage.warning(t('documentPreviewTasks.cancelStateChanged')); return; }
  const request = scope.begin('document.host_preview_tasks.cancel'); if (!request) return;
  changing.value = true; problem.value = undefined;
  try {
    const result = await cancelDocumentPreviewTask(current.id, request.signal);
    if (!request.current()) return;
    items.value = items.value.map(item => item.id === result.id ? result : item);
    ElMessage.success(t('documentPreviewTasks.cancelSuccess'));
    await load();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'documentPreviewTasks.cancelFailed');
  } finally {
    if (request.current()) changing.value = false; request.finish();
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
  <section class="document-preview-tasks-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
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
        <el-form class="document-module-inline-query" :inline="true" @submit.prevent="load">
          <el-form-item :label="t('documentPreviewTasks.documentItemId')">
            <el-input
              v-model="filterDocumentItemId"
              class="document-preview-tasks-view__filter"
              :placeholder="t('documentPreviewTasks.filterDocumentItemId')"
              clearable
              data-testid="document-preview-task-filter"
              @keyup.enter="load"
              @clear="load"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" @click="load">{{ t('documentPermissions.query') }}</el-button>
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
            <el-table-column :label="t('documentPreviewTasks.errorCode')" prop="errorCode" min-width="180" />
            <el-table-column :label="t('documentPreviewTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
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
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>

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
    <ElDialog v-model="cancelDialogVisible" :title="t('common.cancel')" width="460px" :close-on-click-modal="false">
      <p>{{ t('documentPreviewTasks.cancelConfirm') }}</p>
      <template #footer>
        <ElButton data-testid="document-preview-cancel-dismiss" @click="cancelConfirmation = undefined">{{ t('common.cancel') }}</ElButton>
        <ElButton type="warning" data-testid="document-preview-cancel-confirm" @click="confirmCancellation">{{ t('common.confirm') }}</ElButton>
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
