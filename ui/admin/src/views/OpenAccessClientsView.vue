<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, OpenAccessClient } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOpenAccessClient,
  disableOpenAccessClient,
  listOpenAccessClients,
  rotateOpenAccessClient,
  updateOpenAccessClient
} from '../api/open-access-clients';

defineOptions({ name: 'OpenAccessClientsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<OpenAccessClient[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ name: '', userId: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingClient = ref<OpenAccessClient | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  userId: '',
  name: '',
  description: '',
  remark: '',
  permissionsText: '',
  expiresAt: ''
});
const secret = ref('');

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

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'name', label: t('openAccessClients.fieldName'), type: 'input' },
  { key: 'userId', label: t('openAccessClients.fieldUserId'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOpenAccessClients({
      page: page.value,
      pageSize: pageSize.value,
      nameContains: appliedFilters.value.name,
      userId: appliedFilters.value.userId
    });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'openAccessClients.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  appliedFilters.value = {
    name: searchForm.value.name?.trim() ?? '',
    userId: searchForm.value.userId?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { name: '', userId: '' };
  page.value = 1;
  void load();
}

function openCreate() {
  editorMode.value = 'create';
  editingClient.value = null;
  editorForm.userId = '';
  editorForm.name = '';
  editorForm.description = '';
  editorForm.remark = '';
  editorForm.permissionsText = '';
  editorForm.expiresAt = '';
  editorOpen.value = true;
}

function openEdit(row: OpenAccessClient) {
  editorMode.value = 'edit';
  editingClient.value = row;
  editorForm.userId = row.userId;
  editorForm.name = row.name;
  editorForm.description = row.description ?? '';
  editorForm.remark = row.remark ?? '';
  editorForm.permissionsText = row.permissions.join('\n');
  editorForm.expiresAt = row.expiresAtUtc ?? '';
  editorOpen.value = true;
}

function parsePermissions(): string[] {
  return editorForm.permissionsText
    .split(/[\n,]+/u)
    .map((item) => item.trim())
    .filter(Boolean);
}

async function submitEditor() {
  changing.value = true;
  problem.value = undefined;
  try {
    const permissions = parsePermissions();
    if (editorMode.value === 'create') {
      const created = await createOpenAccessClient({
        userId: editorForm.userId.trim(),
        name: editorForm.name.trim(),
        description: editorForm.description.trim() || null,
        remark: editorForm.remark.trim() || null,
        permissions,
        expiresAtUtc: editorForm.expiresAt.trim() || null
      });
      secret.value = created.secret;
      ElMessage.success(t('openAccessClients.createSuccess'));
    } else if (editingClient.value) {
      await updateOpenAccessClient(editingClient.value.id, {
        name: editorForm.name.trim(),
        description: editorForm.description.trim() || null,
        remark: editorForm.remark.trim() || null,
        permissions,
        expiresAtUtc: editorForm.expiresAt.trim() || null,
        version: editingClient.value.version
      });
      ElMessage.success(t('openAccessClients.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmRotate(row: OpenAccessClient) {
  await ElMessageBox.confirm(
    t('openAccessClients.confirmRotate', { name: row.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    const rotated = await rotateOpenAccessClient(row.id);
    secret.value = rotated.secret;
    ElMessage.success(t('openAccessClients.rotateSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmDisable(row: OpenAccessClient) {
  await ElMessageBox.confirm(
    t('openAccessClients.confirmDisable', { name: row.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await disableOpenAccessClient(row.id);
    ElMessage.success(t('openAccessClients.disableSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function copySecret() {
  if (!secret.value) {
    return;
  }
  await navigator.clipboard.writeText(secret.value);
  ElMessage.success(t('openAccessClients.copySuccess'));
}

function toProblem(
  error: unknown,
  fallbackKey: 'openAccessClients.loadFailed' | 'openAccessClients.operationFailed' = 'openAccessClients.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.open_access_client_failed', title: t(fallbackKey) };
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="open-access-clients-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('openAccessClients.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card v-if="secret" class="art-form-card" shadow="never" data-testid="open-access-client-secret">
      <h2>{{ t('openAccessClients.secretTitle') }}</h2>
      <p role="alert">{{ t('openAccessClients.secretWarning') }}</p>
      <code translate="no">{{ secret }}</code>
      <el-button type="primary" plain @click="copySecret">{{ t('openAccessClients.copy') }}</el-button>
    </el-card>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :search-label="t('openAccessClients.query')"
      :reset-label="t('openAccessClients.reset')"
      @search="handleSearch"
      @reset="resetSearch"
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
          layout="refresh,size,fullscreen,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="identity.open_access_clients.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="open-access-clients-action-create"
                @click="openCreate"
              >
                {{ t('openAccessClients.create') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <el-table
          v-loading="loading"
          :data="items"
          row-key="id"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column :label="t('users.columnIndex')" width="72" align="center">
            <template #default="{ $index }">{{ rowIndex($index) }}</template>
          </el-table-column>
          <el-table-column :label="t('openAccessClients.fieldName')" min-width="180" prop="name" />
          <el-table-column :label="t('openAccessClients.accessKeyId')" min-width="140" prop="accessKeyId" />
          <el-table-column :label="t('openAccessClients.permissions')" min-width="220">
            <template #default="{ row }">{{ row.permissions.join(', ') }}</template>
          </el-table-column>
          <el-table-column :label="t('openAccessClients.status')" width="100">
            <template #default="{ row }">
              <el-tag :type="row.isActive ? 'success' : 'info'">
                {{ row.isActive ? t('openAccessClients.statusActive') : t('openAccessClients.statusDisabled') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('users.columnActions')" width="220" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="identity.open_access_clients.update">
                  <ArtTableActionButton
                    :label="t('openAccessClients.edit')"
                    test-id="open-access-clients-action-edit"
                    @click="openEdit(row)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.open_access_clients.rotate">
                  <ArtTableActionButton
                    :label="t('openAccessClients.rotate')"
                    :disabled="!row.isActive"
                    test-id="open-access-clients-action-rotate"
                    @click="confirmRotate(row)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.open_access_clients.disable">
                  <ArtTableActionButton
                    :label="t('openAccessClients.disable')"
                    :disabled="!row.isActive"
                    test-id="open-access-clients-action-disable"
                    @click="confirmDisable(row)"
                  />
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          layout="total, prev, pager, next, sizes"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>

    <ArtFormDialog
      v-model="editorOpen"
      :title="editorMode === 'create' ? t('openAccessClients.createTitle') : t('openAccessClients.editTitle')"
      :confirm-label="editorMode === 'create' ? t('openAccessClients.create') : t('openAccessClients.save')"
      :loading="changing"
      confirm-test-id="open-access-clients-editor-submit"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" label-position="top" class="open-access-clients-editor-form">
        <el-form-item v-if="editorMode === 'create'" :label="t('openAccessClients.fieldUserId')">
          <el-input v-model="editorForm.userId" translate="no" />
        </el-form-item>
        <el-form-item :label="t('openAccessClients.fieldName')">
          <el-input v-model="editorForm.name" />
        </el-form-item>
        <el-form-item :label="t('openAccessClients.fieldDescription')">
          <el-input v-model="editorForm.description" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item :label="t('openAccessClients.fieldRemark')">
          <el-input v-model="editorForm.remark" />
        </el-form-item>
        <el-form-item :label="t('openAccessClients.fieldPermissions')">
          <el-input v-model="editorForm.permissionsText" type="textarea" :rows="4" />
        </el-form-item>
        <el-form-item :label="t('openAccessClients.fieldExpiresAt')">
          <el-input v-model="editorForm.expiresAt" placeholder="2026-12-31T00:00:00Z" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
