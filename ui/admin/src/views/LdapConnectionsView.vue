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
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  LdapConnection,
  LdapSyncPreviewEntry,
  PreviewLdapSyncResponse,
  TestLdapAuthenticationResult,
  TestLdapConnectionResult
} from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createLdapConnection,
  deleteLdapConnection,
  disableLdapConnection,
  listLdapConnections,
  previewLdapSync,
  testLdapAuthentication,
  testLdapConnection,
  updateLdapConnection
} from '../api/ldap-connections';

defineOptions({ name: 'LdapConnectionsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<LdapConnection[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ tenantId: '', name: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingConnection = ref<LdapConnection | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  tenantId: '',
  name: '',
  host: '',
  port: '389',
  useTls: false,
  baseDn: '',
  bindDn: '',
  bindPassword: '',
  userSearchFilter: '(sAMAccountName={0})',
  userAccountAttribute: 'sAMAccountName',
  employeeIdAttribute: '',
  departmentCodeAttribute: '',
  syncSearchBaseDn: '',
  isEnabled: true
});
const authDialogOpen = ref(false);
const authTarget = ref<LdapConnection | null>(null);
const authForm = reactive({ account: '', password: '' });
const authResult = ref<TestLdapAuthenticationResult | null>(null);
const previewDialogOpen = ref(false);
const previewTarget = ref<LdapConnection | null>(null);
const previewForm = reactive({ searchBaseDn: '', maxEntries: '50' });
const previewResult = ref<PreviewLdapSyncResponse | null>(null);
const previewLoading = ref(false);

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
  { key: 'tenantId', label: t('ldapConnections.fieldTenantId'), type: 'input' },
  { key: 'name', label: t('ldapConnections.fieldName'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

function entryKindLabel(entry: LdapSyncPreviewEntry) {
  return entry.entryKind === 'organizationalUnit'
    ? t('ldapConnections.previewKindOrganizationalUnit')
    : t('ldapConnections.previewKindUser');
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listLdapConnections({
      page: page.value,
      pageSize: pageSize.value,
      tenantId: appliedFilters.value.tenantId,
      nameContains: appliedFilters.value.name
    });
    items.value = result.items;
    total.value = result.total;
    updateTableHeight();
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

function applySearch(values: Record<string, string | undefined>) {
  appliedFilters.value = {
    tenantId: values.tenantId?.trim() ?? '',
    name: values.name?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { tenantId: '', name: '' };
  page.value = 1;
  void load();
}

function openCreate() {
  editorMode.value = 'create';
  editingConnection.value = null;
  Object.assign(editorForm, {
    tenantId: appliedFilters.value.tenantId,
    name: '',
    host: '',
    port: '389',
    useTls: false,
    baseDn: '',
    bindDn: '',
    bindPassword: '',
    userSearchFilter: '(sAMAccountName={0})',
    userAccountAttribute: 'sAMAccountName',
    employeeIdAttribute: '',
    departmentCodeAttribute: '',
    syncSearchBaseDn: '',
    isEnabled: true
  });
  editorOpen.value = true;
}

function openEdit(connection: LdapConnection) {
  editorMode.value = 'edit';
  editingConnection.value = connection;
  Object.assign(editorForm, {
    tenantId: connection.tenantId ?? '',
    name: connection.name,
    host: connection.host,
    port: String(connection.port),
    useTls: connection.useTls,
    baseDn: connection.baseDn,
    bindDn: connection.bindDn,
    bindPassword: '',
    userSearchFilter: connection.userSearchFilter,
    userAccountAttribute: connection.userAccountAttribute,
    employeeIdAttribute: connection.employeeIdAttribute ?? '',
    departmentCodeAttribute: connection.departmentCodeAttribute ?? '',
    syncSearchBaseDn: connection.syncSearchBaseDn,
    isEnabled: connection.isEnabled
  });
  editorOpen.value = true;
}

async function submitEditor() {
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createLdapConnection({
        tenantId: editorForm.tenantId.trim() || null,
        name: editorForm.name.trim(),
        host: editorForm.host.trim(),
        port: Number(editorForm.port) || 389,
        useTls: editorForm.useTls,
        baseDn: editorForm.baseDn.trim(),
        bindDn: editorForm.bindDn.trim(),
        bindPassword: editorForm.bindPassword,
        userSearchFilter: editorForm.userSearchFilter.trim(),
        userAccountAttribute: editorForm.userAccountAttribute.trim(),
        employeeIdAttribute: editorForm.employeeIdAttribute.trim() || null,
        departmentCodeAttribute: editorForm.departmentCodeAttribute.trim() || null,
        syncSearchBaseDn: editorForm.syncSearchBaseDn.trim(),
        isEnabled: editorForm.isEnabled
      });
      ElMessage.success(t('ldapConnections.createSuccess'));
    } else if (editingConnection.value) {
      await updateLdapConnection(editingConnection.value.id, {
        name: editorForm.name.trim(),
        host: editorForm.host.trim(),
        port: Number(editorForm.port) || 389,
        useTls: editorForm.useTls,
        baseDn: editorForm.baseDn.trim(),
        bindDn: editorForm.bindDn.trim(),
        bindPassword: editorForm.bindPassword.trim() || null,
        userSearchFilter: editorForm.userSearchFilter.trim(),
        userAccountAttribute: editorForm.userAccountAttribute.trim(),
        employeeIdAttribute: editorForm.employeeIdAttribute.trim() || null,
        departmentCodeAttribute: editorForm.departmentCodeAttribute.trim() || null,
        syncSearchBaseDn: editorForm.syncSearchBaseDn.trim(),
        isEnabled: editorForm.isEnabled,
        version: editingConnection.value.version
      });
      ElMessage.success(t('ldapConnections.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } finally {
    changing.value = false;
  }
}

async function runConnectionTest(connection: LdapConnection) {
  changing.value = true;
  try {
    const result: TestLdapConnectionResult = await testLdapConnection(connection.id);
    if (result.succeeded) {
      ElMessage.success(result.message);
    } else {
      ElMessage.error(result.message);
    }
  } finally {
    changing.value = false;
  }
}

function openAuthDialog(connection: LdapConnection) {
  authTarget.value = connection;
  authForm.account = '';
  authForm.password = '';
  authResult.value = null;
  authDialogOpen.value = true;
}

async function submitAuthTest() {
  if (!authTarget.value) {
    return;
  }
  changing.value = true;
  try {
    authResult.value = await testLdapAuthentication(authTarget.value.id, {
      account: authForm.account.trim(),
      password: authForm.password
    });
  } finally {
    changing.value = false;
  }
}

function openPreviewDialog(connection: LdapConnection) {
  previewTarget.value = connection;
  previewForm.searchBaseDn = connection.syncSearchBaseDn;
  previewForm.maxEntries = '50';
  previewResult.value = null;
  previewDialogOpen.value = true;
}

async function submitPreview() {
  if (!previewTarget.value) {
    return;
  }
  previewLoading.value = true;
  try {
    previewResult.value = await previewLdapSync(previewTarget.value.id, {
      searchBaseDn: previewForm.searchBaseDn.trim() || null,
      maxEntries: Number(previewForm.maxEntries) || 50
    });
  } finally {
    previewLoading.value = false;
  }
}

async function confirmDisable(connection: LdapConnection) {
  await ElMessageBox.confirm(
    t('ldapConnections.confirmDisable', { name: connection.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await disableLdapConnection(connection.id);
    ElMessage.success(t('ldapConnections.disableSuccess'));
    await load();
  } finally {
    changing.value = false;
  }
}

async function confirmDelete(connection: LdapConnection) {
  await ElMessageBox.confirm(
    t('ldapConnections.confirmDelete', { name: connection.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await deleteLdapConnection(connection.id);
    ElMessage.success(t('ldapConnections.deleteSuccess'));
    await load();
  } finally {
    changing.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="ldap-connections-view art-page-stack art-full-height" :aria-busy="loading">
    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :loading="loading"
      @search="applySearch"
      @reset="resetSearch"
    />

    <ElCard class="art-table-card art-full-height" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main ldap-connections-table-main">
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="ldap-connections-table-main"
          layout="refresh,size,fullscreen,settings"
          @refresh="load"
        >
          <template #left>
            <strong>{{ t('ldapConnections.title') }}</strong>
          </template>
          <template #right>
            <PermissionGate code="identity.ldap_connections.create">
              <ElButton
                type="primary"
                :icon="Plus"
                data-testid="ldap-connections-action-create"
                @click="openCreate"
              >
                {{ t('ldapConnections.create') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ElTable
          v-loading="loading"
          :data="items"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :height="tableHeight"
          :header-cell-style="tableHeaderCellStyle"
          :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
        >
        <ElTableColumn type="index" :index="rowIndex" width="64" />
        <ElTableColumn prop="name" :label="t('ldapConnections.fieldName')" min-width="140" />
        <ElTableColumn prop="host" :label="t('ldapConnections.fieldHost')" min-width="160" />
        <ElTableColumn prop="tenantId" :label="t('ldapConnections.fieldTenantId')" min-width="220" />
        <ElTableColumn :label="t('ldapConnections.status')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('ldapConnections.statusEnabled') : t('ldapConnections.statusDisabled') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <!-- @vue-generic {LdapConnection} -->
          <ElTableColumn fixed="right" width="360">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate code="identity.ldap_connections.test">
                <ArtTableActionButton type="view"
                  :title="t('ldapConnections.testConnection')"
                  test-id="ldap-connections-action-test-connection"
                  @click="runConnectionTest(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.ldap_connections.test">
                <ArtTableActionButton type="view"
                  :title="t('ldapConnections.testAuthentication')"
                  test-id="ldap-connections-action-test-auth"
                  @click="openAuthDialog(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.ldap_connections.preview_sync">
                <ArtTableActionButton type="view"
                  :title="t('ldapConnections.previewSync')"
                  test-id="ldap-connections-action-preview"
                  @click="openPreviewDialog(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.ldap_connections.update">
                <ArtTableActionButton type="edit"
                  :title="t('ldapConnections.edit')"
                  test-id="ldap-connections-action-edit"
                  @click="openEdit(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.ldap_connections.update">
                <ArtTableActionButton type="delete"
                  :title="t('ldapConnections.disable')"
                  test-id="ldap-connections-action-disable"
                  @click="confirmDisable(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.ldap_connections.delete">
                <ArtTableActionButton type="delete"
                  :title="t('ldapConnections.delete')"
                  test-id="ldap-connections-action-delete"
                  @click="confirmDelete(row)"
                />
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
        </ElTable>

        <ElPagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination"
          layout="total, sizes, prev, pager, next"
          :total="total"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </ElCard>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('ldapConnections.createTitle') : t('ldapConnections.editTitle')"
      :saving="changing"
      confirm-test-id="ldap-connections-editor-submit"
      @confirm="submitEditor"
    >
      <ElForm ref="editorFormRef" label-position="top">
        <ElFormItem v-if="editorMode === 'create'" :label="t('ldapConnections.fieldTenantId')">
          <ElInput v-model="editorForm.tenantId" :placeholder="t('ldapConnections.hostScopeHint')" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldName')" required>
          <ElInput v-model="editorForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldHost')" required>
          <ElInput v-model="editorForm.host" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldPort')" required>
          <ElInput v-model="editorForm.port" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldUseTls')">
          <ElSwitch v-model="editorForm.useTls" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldBaseDn')" required>
          <ElInput v-model="editorForm.baseDn" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldBindDn')" required>
          <ElInput v-model="editorForm.bindDn" />
        </ElFormItem>
        <ElFormItem
          :label="editorMode === 'create' ? t('ldapConnections.fieldBindPassword') : t('ldapConnections.fieldBindPasswordOptional')"
          :required="editorMode === 'create'"
        >
          <ElInput v-model="editorForm.bindPassword" type="password" show-password />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldUserSearchFilter')" required>
          <ElInput v-model="editorForm.userSearchFilter" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldUserAccountAttribute')" required>
          <ElInput v-model="editorForm.userAccountAttribute" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldEmployeeIdAttribute')">
          <ElInput v-model="editorForm.employeeIdAttribute" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldDepartmentCodeAttribute')">
          <ElInput v-model="editorForm.departmentCodeAttribute" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldSyncSearchBaseDn')" required>
          <ElInput v-model="editorForm.syncSearchBaseDn" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.status')">
          <ElSwitch v-model="editorForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="authDialogOpen"
      :title="t('ldapConnections.testAuthenticationTitle')"
      :saving="changing"
      confirm-test-id="ldap-connections-auth-submit"
      @confirm="submitAuthTest"
    >
      <ElForm label-position="top">
        <ElFormItem :label="t('ldapConnections.fieldAccount')" required>
          <ElInput v-model="authForm.account" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldPassword')" required>
          <ElInput v-model="authForm.password" type="password" show-password />
        </ElFormItem>
        <ElFormItem v-if="authResult" :label="t('ldapConnections.testResult')">
          <ElTag :type="authResult.succeeded ? 'success' : 'danger'">
            {{ authResult.message }}
          </ElTag>
          <div v-if="authResult.matchedDn" class="ldap-test-result-dn">{{ authResult.matchedDn }}</div>
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="previewDialogOpen"
      :title="t('ldapConnections.previewSyncTitle')"
      :saving="previewLoading"
      confirm-test-id="ldap-connections-preview-submit"
      @confirm="submitPreview"
    >
      <ElForm label-position="top">
        <ElFormItem :label="t('ldapConnections.fieldSearchBaseDn')">
          <ElInput v-model="previewForm.searchBaseDn" />
        </ElFormItem>
        <ElFormItem :label="t('ldapConnections.fieldMaxEntries')">
          <ElInput v-model="previewForm.maxEntries" />
        </ElFormItem>
      </ElForm>
      <ElTable v-if="previewResult" :data="previewResult.entries" size="small" max-height="320">
        <!-- @vue-generic {LdapSyncPreviewEntry} -->
          <ElTableColumn prop="entryKind" :label="t('ldapConnections.previewKind')" width="120">
          <template #default="{ row }">{{ entryKindLabel(row) }}</template>
        </ElTableColumn>
        <ElTableColumn prop="dn" :label="t('ldapConnections.previewDn')" min-width="220" />
        <ElTableColumn prop="account" :label="t('ldapConnections.previewAccount')" width="120" />
        <ElTableColumn prop="displayName" :label="t('ldapConnections.previewDisplayName')" width="140" />
      </ElTable>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.ldap-test-result-dn {
  margin-top: 8px;
  word-break: break-all;
  color: var(--el-text-color-secondary);
}
</style>
