<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
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
  const key = `documentPreviewTasks.status.${statusKey}` as const;
  const translated = t(key);
  return translated === key ? statusKey : translated;
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const documentItemId = filterDocumentItemId.value.trim() || undefined;
    const result = await listDocumentPreviewTasks(page.value, pageSize.value, documentItemId);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function openCreate() {
  editorForm.documentItemId = filterDocumentItemId.value.trim();
  editorForm.versionId = '';
  editorOpen.value = true;
}

async function submitCreate() {
  const documentItemId = editorForm.documentItemId.trim();
  const versionId = editorForm.versionId.trim();
  if (!documentItemId) {
    return;
  }

  changing.value = true;
  try {
    await createDocumentPreviewTask({
      documentItemId,
      versionId: versionId || null
    });
    editorOpen.value = false;
    ElMessage.success(t('documentPreviewTasks.createSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openPdf(row: HostDocumentPreviewTaskResponse) {
  if (row.statusKey !== 'succeeded' || changing.value || !canRead()) {
    return;
  }

  changing.value = true;
  try {
    await openDocumentPreviewTaskContent(row.id);
  } catch (error) {
    problem.value = toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentPreviewTasks.loadFailed' | 'documentPreviewTasks.operationFailed' = 'documentPreviewTasks.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

onMounted(load);
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPreviewTasks.title') }}</h1>

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
            <el-input
              v-model="filterDocumentItemId"
              class="document-preview-tasks-view__filter"
              :placeholder="t('documentPreviewTasks.filterDocumentItemId')"
              clearable
              data-testid="document-preview-task-filter"
              @keyup.enter="load"
              @clear="load"
            />
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
                      :disabled="changing"
                      @click="openPdf(row as HostDocumentPreviewTaskResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPreviewTasks.emptyDirectory') }}</template>
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
        <el-form-item :label="t('documentPreviewTasks.documentItemId')">
          <el-input v-model="editorForm.documentItemId" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentPreviewTasks.versionIdOptional')">
          <el-input v-model="editorForm.versionId" autocomplete="off" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.document-preview-tasks-view__filter {
  width: 280px;
  margin-right: 12px;
}
</style>
