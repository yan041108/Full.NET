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
  ElSwitch,
  ElTable,
  ElTableColumn
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentShareResponse } from '@fullnet/client-contracts';
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
  createDocumentShare,
  listDocumentShares,
  updateDocumentShareStatus
} from '../api/document-shares';

defineOptions({ name: 'DocumentSharesView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentShareResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const editorOpen = ref(false);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  documentId: '',
  validDays: '7',
  password: '',
  maxAccessCount: ''
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

const canCreate = () => session.can('document.host_shares.create');
const canUpdateStatus = () => session.can('document.host_shares.update_status');

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listDocumentShares(page.value, pageSize.value);
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
  editorForm.documentId = '';
  editorForm.validDays = '7';
  editorForm.password = '';
  editorForm.maxAccessCount = '';
  editorOpen.value = true;
}

async function submitCreate() {
  const validDays = Number(editorForm.validDays);
  const maxAccessCount = editorForm.maxAccessCount.trim()
    ? Number(editorForm.maxAccessCount)
    : null;
  changing.value = true;
  try {
    await createDocumentShare({
      documentId: editorForm.documentId.trim(),
      validDays,
      password: editorForm.password.trim() || null,
      maxAccessCount: Number.isFinite(maxAccessCount) ? maxAccessCount : null
    });
    editorOpen.value = false;
    ElMessage.success(t('documentShares.createSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentShares.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function toggleStatus(row: HostDocumentShareResponse) {
  changing.value = true;
  try {
    await updateDocumentShareStatus(row.id, {
      isEnabled: !row.isEnabled,
      version: row.version
    });
    ElMessage.success(t('documentShares.updateSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentShares.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentShares.loadFailed' | 'documentShares.operationFailed' = 'documentShares.loadFailed'
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
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentShares.title') }}</h1>

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
            <PermissionGate code="document.host_shares.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-share-create"
                @click="openCreate"
              >
                {{ t('documentShares.addShare') }}
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
            <el-table-column :label="t('documentShares.shareCode')" prop="shareCode" min-width="160" />
            <el-table-column :label="t('documentShares.documentId')" prop="documentId" min-width="280" />
            <el-table-column :label="t('documentShares.accessCount')" prop="accessCount" width="120" />
            <el-table-column :label="t('documentShares.enabled')" width="120">
              <template #default="{ row }">
                <el-switch :model-value="row.isEnabled" disabled />
              </template>
            </el-table-column>
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.host_shares.update_status">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-share-toggle"
                      :title="t('documentShares.toggleStatus')"
                      :disabled="changing"
                      @click="toggleStatus(row as HostDocumentShareResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentShares.emptyDirectory') }}</template>
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
      :title="t('documentShares.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-share-editor-submit"
      :show-confirm="canCreate()"
      @confirm="submitCreate"
    >
      <el-form ref="editorFormRef" data-testid="document-share-editor-form" :model="editorForm" label-width="120px">
        <el-form-item :label="t('documentShares.documentId')">
          <el-input v-model="editorForm.documentId" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentShares.validDays')">
          <el-input v-model="editorForm.validDays" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentShares.passwordOptional')">
          <el-input v-model="editorForm.password" type="password" show-password autocomplete="new-password" />
        </el-form-item>
        <el-form-item :label="t('documentShares.maxAccessCount')">
          <el-input v-model="editorForm.maxAccessCount" autocomplete="off" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
