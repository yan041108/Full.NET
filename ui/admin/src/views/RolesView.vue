<script setup lang="ts">
import {
  computed,
  nextTick,
  onActivated,
  onMounted,
  onUnmounted,
  reactive,
  ref,
  watch
} from 'vue';
import {
  ElButton,
  ElCard,
  ElCheckbox,
  ElCheckboxGroup,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTree
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance, FormRules, TreeInstance, TreeKey } from 'element-plus';
import {
  ROLE_DATA_SCOPE_KINDS,
  type FieldProjectionFieldDefinition,
  type FullNetProblemDetails,
  type HostRole,
  type OrganizationUnit,
  type RoleDataScopeKind
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import {
  applyPermissionNodeCheck,
  buildPermissionTreeNodes,
  collectCatalogPermissionCodes,
  findUnknownPermissionCodes,
  permissionCodesToCheckedNodeIds,
  type PermissionTreeNode
} from '../auth/authorization-tree-selection';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostRole,
  disableHostRole,
  getAuthorizationTree,
  getFieldProjectionCatalog,
  getHostRoleDataScope,
  getHostRoleFieldGrants,
  listHostRoles,
  replaceHostRolePermissions,
  replaceHostRoleFieldGrants,
  updateHostRole,
  updateHostRoleDataScope
} from '../api/roles';
import { listOrganizationUnits } from '../api/org-units';

defineOptions({ name: 'RolesView' });

interface AppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
  roleType: '' | 'system' | 'custom';
}

type EditorMode = 'create' | 'edit';
type RoleTableColumnKey = 'code' | 'status' | 'roleType' | 'permissionCount' | 'createdAt';

const session = useSessionStore();
const { t } = useAdminI18n();
const allRoles = ref<HostRole[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  code: '',
  name: '',
  status: '',
  roleType: ''
});
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const tableMainRef = ref<HTMLElement | null>(null);
const tableHeight = ref(360);
const tableSize = ref<'large' | 'default' | 'small'>('default');
const tableZebra = ref(true);
const tableBorder = ref(true);
const tableHeaderBackground = ref(true);
const columnVisibility = ref<Record<RoleTableColumnKey, boolean>>({
  code: true,
  status: true,
  roleType: true,
  permissionCount: true,
  createdAt: true
});
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingRole = ref<HostRole | null>(null);
const editorForm = reactive({
  code: '',
  name: ''
});
const editorFormRef = ref<FormInstance>();
const permissionsVisible = ref(false);
const dataScopeVisible = ref(false);
const fieldGrantsVisible = ref(false);
const permissionTreeNodes = ref<PermissionTreeNode[]>([]);
const selectedPermissions = ref<string[]>([]);
const unknownPermissions = ref<string[]>([]);
const permissionTreeRef = ref<TreeInstance>();
const selectedDataScopeKind = ref<RoleDataScopeKind>('identity.data_scope.all');
const selectedUnitIds = ref<string[]>([]);
const dataScopeVersion = ref(0);
const fieldGrantVersion = ref(0);
const fieldGrantResourceKey = 'identity.host_users';
const assignableFields = ref<FieldProjectionFieldDefinition[]>([]);
const selectedFieldKeys = ref<string[]>([]);
const orgUnits = ref<OrganizationUnit[]>([]);
const dataScopeKinds = ROLE_DATA_SCOPE_KINDS;
const ROLE_CODE_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const editorFormRules = computed<FormRules>(() => {
  const rules: FormRules = {
    name: [
      {
        validator: (_rule, value, callback) => {
          const name = String(value ?? '').trim();
          if (!name) {
            callback(new Error(t('roles.nameRequired')));
            return;
          }
          if (name.length > 128) {
            callback(new Error(t('roles.nameInvalid')));
            return;
          }
          callback();
        },
        trigger: ['blur', 'change']
      }
    ]
  };

  if (editorMode.value === 'create') {
    rules.code = [
      {
        validator: (_rule, value, callback) => {
          const code = normalizeRoleCode(String(value ?? ''));
          if (!code) {
            callback(new Error(t('roles.codeRequired')));
            return;
          }
          if (!ROLE_CODE_PATTERN.test(code)) {
            callback(new Error(t('roles.codeInvalid')));
            return;
          }
          callback();
        },
        trigger: ['blur', 'change']
      }
    ];
  }

  return rules;
});

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'code', label: t('roles.code'), visible: columnVisibility.value.code },
    { key: 'status', label: t('roles.active'), visible: columnVisibility.value.status },
    { key: 'roleType', label: t('roles.roleType'), visible: columnVisibility.value.roleType },
    {
      key: 'permissionCount',
      label: t('roles.permissionCount'),
      visible: columnVisibility.value.permissionCount
    },
    { key: 'createdAt', label: t('roles.createdAt'), visible: columnVisibility.value.createdAt }
  ],
  set: (columns) => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as RoleTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const tableHeaderCellStyle = computed(() => ({
  background: tableHeaderBackground.value
    ? 'var(--art-gray-100)'
    : 'var(--art-default-box-color)'
}));

const canCreate = computed(() => session.can('identity.roles.create'));
const canUpdate = computed(() => session.can('identity.roles.update'));
const canSavePermissions = computed(() => unknownPermissions.value.length === 0);
const canReadFieldGrants = computed(() => session.can('identity.role_field_grants.read'));
const inTenantContext = computed(() => !!session.currentUser?.tenantId);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('roles.code'),
    placeholder: t('roles.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('roles.name'),
    placeholder: t('roles.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('roles.searchStatusPlaceholder'),
    options: [
      { label: t('roles.active'), value: 'active' },
      { label: t('roles.inactive'), value: 'inactive' }
    ]
  },
  {
    key: 'roleType',
    label: t('roles.roleType'),
    type: 'select',
    placeholder: t('roles.searchTypePlaceholder'),
    options: [
      { label: t('roles.roleTypeSystem'), value: 'system' },
      { label: t('roles.roleTypeCustom'), value: 'custom' }
    ]
  }
]);

const filteredRoles = computed(() => {
  let rows = allRoles.value;
  const filters = appliedFilters.value;

  if (filters.code.trim()) {
    const keyword = filters.code.trim().toLowerCase();
    rows = rows.filter(role => role.code.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(role => role.name.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(role => role.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(role => !role.isActive);
  }

  if (filters.roleType === 'system') {
    rows = rows.filter(role => role.isSystem);
  } else if (filters.roleType === 'custom') {
    rows = rows.filter(role => !role.isSystem);
  }

  return rows;
});

const pagedRoles = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return filteredRoles.value.slice(start, start + pageSize.value);
});

watch(filteredRoles, rows => {
  total.value = rows.length;
  const maxPage = Math.max(1, Math.ceil(rows.length / pageSize.value) || 1);
  if (page.value > maxPage) {
    page.value = maxPage;
  }
});

onMounted(() => {
  void load();
  updateTableHeight();
  window.addEventListener('resize', updateTableHeight);
});

onActivated(() => {
  void nextTick(updateTableHeight);
});

onUnmounted(() => {
  window.removeEventListener('resize', updateTableHeight);
});

watch(loading, () => {
  void nextTick(updateTableHeight);
});

function isColumnVisible(key: RoleTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function updateTableHeight(): void {
  const container = tableMainRef.value;
  if (!container) {
    return;
  }
  const top = container.getBoundingClientRect().top;
  tableHeight.value = Math.max(240, window.innerHeight - top - 68);
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allRoles.value = await fetchAllRoles();
    total.value = filteredRoles.value.length;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function fetchAllRoles(): Promise<HostRole[]> {
  const pageSizeLimit = 100;
  const firstPage = await listHostRoles(1, pageSizeLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageSizeLimit);

  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostRoles(current, pageSizeLimit);
    items.push(...nextPage.items);
  }

  return items;
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    code: params.code ?? '',
    name: params.name ?? '',
    status: (params.status as AppliedFilters['status']) ?? '',
    roleType: (params.roleType as AppliedFilters['roleType']) ?? ''
  };
  page.value = 1;
}

function resetSearch(): void {
  appliedFilters.value = {
    code: '',
    name: '',
    status: '',
    roleType: ''
  };
  page.value = 1;
}

function openCreate(): void {
  editorMode.value = 'create';
  editingRole.value = null;
  editorForm.code = '';
  editorForm.name = '';
  editorOpen.value = true;
  void nextTick(() => editorFormRef.value?.clearValidate());
}

function openEdit(role: HostRole): void {
  if (changing.value || role.isSystem) {
    return;
  }
  editorMode.value = 'edit';
  editingRole.value = role;
  editorForm.code = role.code;
  editorForm.name = role.name;
  editorOpen.value = true;
  void nextTick(() => editorFormRef.value?.clearValidate());
}

function normalizeRoleCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-+/, '');
}

function onEditorCodeBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.code = normalizeRoleCode(editorForm.code);
  void editorFormRef.value?.validateField('code').catch(() => undefined);
}

async function validateEditorForm(): Promise<boolean> {
  if (editorMode.value === 'create') {
    editorForm.code = normalizeRoleCode(editorForm.code);
  }

  const form = editorFormRef.value;
  if (!form) {
    return false;
  }

  try {
    await form.validate();
    editorForm.name = editorForm.name.trim();
    return true;
  } catch {
    return false;
  }
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }

  await nextTick();
  if (!(await validateEditorForm())) {
    return;
  }

  if (editorMode.value === 'create') {
    await create();
    return;
  }

  await saveEdit();
}

async function create(): Promise<void> {
  if (!canCreate.value) {
    return;
  }

  const code = normalizeRoleCode(editorForm.code);
  const name = editorForm.name.trim();

  changing.value = true;
  problem.value = undefined;
  try {
    await createHostRole(code, name);
    editorOpen.value = false;
    ElMessage.success(t('roles.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
    if (isFullNetProblemDetails(error) && error.code === 'validation.failed') {
      ElMessage.warning(t('roles.codeInvalid'));
    }
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const role = editingRole.value;
  if (!canUpdate.value || !role || !editorForm.name.trim()) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostRole(role.id, editorForm.name.trim(), role.version);
    editorOpen.value = false;
    ElMessage.success(t('roles.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openPermissions(role: HostRole): Promise<void> {
  if (role.isSystem || changing.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const modules = await getAuthorizationTree();
    permissionTreeNodes.value = buildPermissionTreeNodes(modules);
    const catalog = collectCatalogPermissionCodes(modules);
    selectedPermissions.value = [...role.permissionCodes];
    unknownPermissions.value = findUnknownPermissionCodes(role.permissionCodes, catalog);
    editingRole.value = role;
    permissionsVisible.value = true;
    await nextTick();
    syncPermissionTreeChecks();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
    if (error instanceof Error && error.message === 'client.invalid_authorization_tree') {
      ElMessage.error(t('roles.authorizationTreeInvalid'));
    }
  } finally {
    changing.value = false;
  }
}

function syncPermissionTreeChecks(): void {
  const checkedNodeIds = permissionCodesToCheckedNodeIds(
    new Set(selectedPermissions.value),
    permissionTreeNodes.value
  );
  permissionTreeRef.value?.setCheckedKeys(checkedNodeIds, false);
}

function onPermissionTreeCheck(
  node: PermissionTreeNode,
  state: { checkedKeys: TreeKey[] }
): void {
  const checked = state.checkedKeys.map(String).includes(node.id);
  selectedPermissions.value = [
    ...applyPermissionNodeCheck(new Set(selectedPermissions.value), node, checked)
  ];
  void nextTick(() => syncPermissionTreeChecks());
}

async function savePermissions(): Promise<void> {
  const role = editingRole.value;
  if (
    !role
    || changing.value
    || !canSavePermissions.value
    || !session.can('identity.roles.assign_permissions')
  ) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await replaceHostRolePermissions(
      role.id,
      [...selectedPermissions.value].sort(),
      role.version
    );
    permissionsVisible.value = false;
    editingRole.value = null;
    ElMessage.success(t('roles.permissionsSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function dataScopeKindLabel(kind: RoleDataScopeKind): string {
  const labels: Record<
    RoleDataScopeKind,
    | 'roles.dataScopeKindAll'
    | 'roles.dataScopeKindOrg'
    | 'roles.dataScopeKindOrgSubtree'
    | 'roles.dataScopeKindSelf'
    | 'roles.dataScopeKindCustom'
  > = {
    'identity.data_scope.all': 'roles.dataScopeKindAll',
    'identity.data_scope.org': 'roles.dataScopeKindOrg',
    'identity.data_scope.org_subtree': 'roles.dataScopeKindOrgSubtree',
    'identity.data_scope.self': 'roles.dataScopeKindSelf',
    'identity.data_scope.custom': 'roles.dataScopeKindCustom'
  };
  return t(labels[kind]);
}

async function openDataScope(role: HostRole): Promise<void> {
  if (role.isSystem || changing.value) {
    return;
  }

  editingRole.value = role;
  problem.value = undefined;
  try {
    const scope = await getHostRoleDataScope(role.id);
    selectedDataScopeKind.value = scope.dataScopeKind;
    selectedUnitIds.value = [...scope.unitIds];
    dataScopeVersion.value = scope.version;
    if (scope.dataScopeKind === 'identity.data_scope.custom' && inTenantContext.value) {
      await loadOrgUnits();
    } else {
      orgUnits.value = [];
    }
    dataScopeVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  }
}

async function loadOrgUnits(): Promise<void> {
  const pageResult = await listOrganizationUnits(1, 100);
  orgUnits.value = pageResult.items;
}

async function onDataScopeKindChange(kind: RoleDataScopeKind): Promise<void> {
  selectedDataScopeKind.value = kind;
  if (kind === 'identity.data_scope.custom' && inTenantContext.value) {
    await loadOrgUnits();
    return;
  }
  selectedUnitIds.value = [];
  orgUnits.value = [];
}

async function saveDataScope(): Promise<void> {
  const role = editingRole.value;
  if (!role || changing.value || !session.can('identity.roles.assign_data_scope')) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const unitIds = selectedDataScopeKind.value === 'identity.data_scope.custom'
      ? [...selectedUnitIds.value]
      : null;
    await updateHostRoleDataScope(
      role.id,
      selectedDataScopeKind.value,
      unitIds,
      dataScopeVersion.value,
      session.currentUser?.tenantId ?? null
    );
    dataScopeVisible.value = false;
    editingRole.value = null;
    ElMessage.success(t('roles.dataScopeSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openFieldGrants(role: HostRole): Promise<void> {
  if (role.isSystem || changing.value || !canReadFieldGrants.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const [catalog, grants] = await Promise.all([
      getFieldProjectionCatalog(),
      getHostRoleFieldGrants(role.id, fieldGrantResourceKey)
    ]);
    const resource = catalog.find(item => item.resourceKey === fieldGrantResourceKey);
    assignableFields.value = resource?.fields.filter(field => field.assignable) ?? [];
    selectedFieldKeys.value = [...grants.fieldKeys];
    fieldGrantVersion.value = grants.version;
    editingRole.value = role;
    fieldGrantsVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveFieldGrants(): Promise<void> {
  const role = editingRole.value;
  if (!role || changing.value || !session.can('identity.role_field_grants.replace')) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await replaceHostRoleFieldGrants(
      role.id,
      fieldGrantResourceKey,
      [...selectedFieldKeys.value].sort(),
      fieldGrantVersion.value
    );
    fieldGrantsVisible.value = false;
    editingRole.value = null;
    ElMessage.success(t('roles.fieldGrantsSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(role: HostRole): Promise<void> {
  if (changing.value || !role.isActive || role.isSystem) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('roles.confirmDisable', { name: role.code }),
      t('roles.disable'),
      {
        type: 'warning',
        confirmButtonText: t('roles.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableHostRole(role.id);
    ElMessage.success(t('roles.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function avatarText(role: HostRole): string {
  return role.code.slice(0, 2).toUpperCase();
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString();
}

function roleTypeLabel(role: HostRole): string {
  return t(role.isSystem ? 'roles.roleTypeSystem' : 'roles.roleTypeCustom');
}

function toProblem(
  error: unknown,
  fallbackKey: 'roles.loadFailed' | 'roles.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_role_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="roles-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('roles.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="4"
      :search-label="t('roles.query')"
      :reset-label="t('roles.reset')"
      :expand-label="t('roles.expand')"
      :collapse-label="t('roles.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="roles-table-main">
        <ArtTableHeader
          v-model:columns="tableColumns"
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="roles-table-main"
          layout="refresh,size,fullscreen,columns,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="identity.roles.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="roles-action-create"
                @click="openCreate"
              >
                {{ t('roles.addRole') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedRoles.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedRoles"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="roles-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            style="width: 100%"
          >
            <el-table-column :label="t('roles.columnIndex')" width="72" align="center">
              <template #default="{ $index }">
                {{ rowIndex($index) }}
              </template>
            </el-table-column>

            <el-table-column :label="t('roles.name')" min-width="220">
              <template #default="{ row }">
                <div class="roles-table-role">
                  <span class="roles-table-role__avatar">{{ avatarText(row as HostRole) }}</span>
                  <div>
                    <div class="roles-table-role__name" translate="no">{{ row.name }}</div>
                    <div v-if="isColumnVisible('code')" class="roles-table-role__sub" translate="no">
                      {{ row.code }}
                    </div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('roleType')"
              :label="t('roles.roleType')"
              width="120"
              align="center"
            >
              <template #default="{ row }">
                <el-tag size="small" :type="row.isSystem ? 'warning' : 'info'">
                  {{ roleTypeLabel(row as HostRole) }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag size="small" :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'roles.active' : 'roles.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('permissionCount')"
              :label="t('roles.permissionCount')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                {{ row.permissionCodes.length }}
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('createdAt')"
              :label="t('roles.createdAt')"
              width="180"
              align="center"
            >
              <template #default="{ row }">
                {{ formatDate(row.createdAtUtc) }}
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="220"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="roles-table-actions">
                  <PermissionGate code="identity.roles.update">
                    <ArtTableActionButton
                      v-if="!row.isSystem"
                      type="edit"
                      test-id="roles-action-edit"
                      :title="t('roles.edit')"
                  @click="openEdit(row as HostRole)"
                    />
                  </PermissionGate>
                  <PermissionGate code="identity.roles.assign_permissions">
                    <el-button
                      v-if="!row.isSystem"
                      link
                      type="primary"
                      data-testid="role-open-permissions"
                  @click="openPermissions(row as HostRole)"
                    >
                      {{ t('roles.permissions') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="identity.roles.assign_data_scope">
                    <el-button
                      v-if="!row.isSystem"
                      link
                      type="primary"
                      data-testid="roles-action-data-scope"
                  @click="openDataScope(row as HostRole)"
                    >
                      {{ t('roles.dataScope') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="identity.role_field_grants.read">
                    <el-button
                      v-if="!row.isSystem"
                      link
                      type="primary"
                      data-testid="roles-action-field-grants"
                  @click="openFieldGrants(row as HostRole)"
                    >
                      {{ t('roles.fieldGrants') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="identity.roles.disable">
                    <ArtTableActionButton
                      v-if="!row.isSystem"
                      type="delete"
                      test-id="roles-action-disable"
                      :title="t('roles.disable')"
                  @click="disable(row as HostRole)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>
              {{ t('roles.emptyDirectory') }}
            </template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next, jumper"
              :page-sizes="[10, 20, 50, 100]"
            />
          </div>
        </div>
      </div>
    </el-card>

    <el-dialog
      v-model="editorOpen"
      width="520px"
      class="roles-editor-dialog"
      destroy-on-close
      :show-close="false"
      append-to-body
      align-center
    >
      <template #header>
        <div class="roles-editor-dialog__header">
          <span>
            {{ editorMode === 'create' ? t('roles.createTitle') : t('roles.editTitle') }}
          </span>
          <button type="button" class="roles-editor-dialog__close" @click="editorOpen = false">
            x
          </button>
        </div>
      </template>

      <el-form
        ref="editorFormRef"
        data-testid="roles-create-form"
        :model="editorForm"
        :rules="editorFormRules"
        label-width="96px"
        class="roles-editor-dialog__form"
        @submit.prevent="submitEditor"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('roles.code')"
          prop="code"
        >
          <el-input
            v-model="editorForm.code"
            :placeholder="t('roles.codePlaceholder')"
            @blur="onEditorCodeBlur"
          />
        </el-form-item>
        <el-form-item
          v-else
          :label="t('roles.code')"
        >
          <el-input v-model="editorForm.code" disabled />
        </el-form-item>
        <el-form-item :label="t('roles.name')" prop="name">
          <el-input
            v-model="editorForm.name"
            :placeholder="t('roles.namePlaceholder')"
            @keyup.enter="submitEditor"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="roles-editor-dialog__footer">
          <el-button @click="editorOpen = false">{{ t('users.cancel') }}</el-button>
          <el-button
            type="primary"
            native-type="button"
            :loading="changing"
            data-testid="roles-editor-submit"
            @click="submitEditor"
          >
            {{ t(editorMode === 'create' ? 'roles.create' : 'roles.edit') }}
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="permissionsVisible"
      :title="t('roles.permissionsTitle')"
      width="560px"
      class="roles-permissions-dialog"
      align-center
      destroy-on-close
    >
      <div class="roles-permissions-dialog__content">
        <p
          v-if="unknownPermissions.length > 0"
          class="art-inline-alert roles-permissions-dialog__alert"
          role="alert"
          data-testid="role-unknown-permissions"
        >
          <strong>{{ t('roles.unknownPermissionsTitle') }}</strong>
          <span>{{ t('roles.unknownPermissionsHint') }}</span>
          <code v-for="permission in unknownPermissions" :key="permission" translate="no">
            {{ permission }}
          </code>
        </p>
        <el-tree
          ref="permissionTreeRef"
          data-testid="role-permission-tree"
          class="roles-permissions-dialog__tree"
          :data="permissionTreeNodes"
          node-key="id"
          show-checkbox
          check-strictly
          default-expand-all
          :props="{ label: 'label', children: 'children' }"
          @check="onPermissionTreeCheck"
        />
      </div>
      <template #footer>
        <el-button @click="permissionsVisible = false">{{ t('status.back') }}</el-button>
        <PermissionGate code="identity.roles.assign_permissions">
          <el-button
            data-testid="role-save-permissions"
            type="primary"
            :loading="changing"
            :disabled="!canSavePermissions"
            @click="savePermissions"
          >
            {{ t('roles.savePermissions') }}
          </el-button>
        </PermissionGate>
      </template>
    </el-dialog>

    <el-dialog v-model="dataScopeVisible" :title="t('roles.dataScopeTitle')" width="560px">
      <label class="art-dialog-field">
        <span>{{ t('roles.dataScopeKind') }}</span>
        <el-select :model-value="selectedDataScopeKind" @update:model-value="onDataScopeKindChange">
          <el-option
            v-for="kind in dataScopeKinds"
            :key="kind"
            :label="dataScopeKindLabel(kind)"
            :value="kind"
          />
        </el-select>
      </label>
      <p
        v-if="selectedDataScopeKind === 'identity.data_scope.custom' && !inTenantContext"
        class="art-dialog-hint"
      >
        {{ t('roles.dataScopeTenantRequired') }}
      </p>
      <section
        v-if="selectedDataScopeKind === 'identity.data_scope.custom' && inTenantContext"
        class="art-dialog-grid"
      >
        <span>{{ t('roles.dataScopeUnits') }}</span>
        <el-checkbox-group v-model="selectedUnitIds">
          <el-checkbox v-for="unit in orgUnits" :key="unit.id" :label="unit.id">
            <span translate="no">{{ unit.name }}</span>
            <code translate="no">{{ unit.code }}</code>
          </el-checkbox>
        </el-checkbox-group>
      </section>
      <template #footer>
        <el-button @click="dataScopeVisible = false">{{ t('status.back') }}</el-button>
        <PermissionGate code="identity.roles.assign_data_scope">
          <el-button
            data-testid="roles-save-data-scope"
            type="primary"
            :loading="changing"
            @click="saveDataScope"
          >
            {{ t('roles.saveDataScope') }}
          </el-button>
        </PermissionGate>
      </template>
    </el-dialog>

    <el-dialog v-model="fieldGrantsVisible" :title="t('roles.fieldGrantsTitle')" width="560px">
      <el-checkbox-group v-model="selectedFieldKeys" class="art-dialog-grid">
        <el-checkbox v-for="field in assignableFields" :key="field.fieldKey" :label="field.fieldKey">
          {{ field.displayName }}
          <code translate="no">{{ field.fieldKey }}</code>
        </el-checkbox>
      </el-checkbox-group>
      <template #footer>
        <el-button @click="fieldGrantsVisible = false">{{ t('status.back') }}</el-button>
        <PermissionGate code="identity.role_field_grants.replace">
          <el-button
            type="primary"
            :loading="changing"
            data-testid="roles-save-field-grants"
            @click="saveFieldGrants"
          >
            {{ t('roles.saveFieldGrants') }}
          </el-button>
        </PermissionGate>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.roles-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.roles-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.roles-table-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.roles-table-role {
  display: flex;
  align-items: center;
  gap: 10px;
}

.roles-table-role__avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border-radius: 8px;
  background: var(--art-gray-100);
  color: var(--art-theme-color);
  font-size: 12px;
  font-weight: 600;
}

.roles-table-role__name {
  font-weight: 600;
}

.roles-table-role__sub {
  color: var(--art-gray-500);
  font-size: 12px;
}

.roles-table-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 4px;
}

.art-sr-heading {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.roles-editor-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: -8px -8px 0;
  padding: 12px 16px;
  border-radius: 8px 8px 0 0;
  background: var(--art-theme-color);
  color: #fff;
  font-size: 15px;
  font-weight: 600;
}

.roles-editor-dialog__close {
  border: 0;
  background: transparent;
  color: inherit;
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
}

.roles-editor-dialog__form {
  padding-top: 8px;
}

.roles-editor-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>

<style>
.roles-permissions-dialog.el-dialog {
  display: flex;
  flex-direction: column;
  height: 95vh;
  max-height: 95vh;
  margin: 2.5vh auto;
}

.roles-permissions-dialog .el-dialog__header {
  flex-shrink: 0;
  margin-right: 0;
}

.roles-permissions-dialog .el-dialog__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  padding-top: 8px;
}

.roles-permissions-dialog .el-dialog__footer {
  flex-shrink: 0;
}

.roles-permissions-dialog__content {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}

.roles-permissions-dialog__alert {
  flex-shrink: 0;
  margin-bottom: 12px;
}

.roles-permissions-dialog__tree {
  flex: 1;
  min-height: 0;
}
</style>
