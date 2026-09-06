<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElDialog,
  ElButton,
  ElCard,
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
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  HostTenant,
  HostTenantMember,
  HostTenantPackage
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listHostTenantPackages } from '../api/tenant-packages';
import {
  assignHostTenantPackage,
  createHostTenant,
  disableHostTenant,
  enableHostTenant,
  listHostTenantAdministrators,
  listHostTenantMembers,
  listHostTenants,
  updateHostTenant
} from '../api/tenants';
import {
  fetchTenantBrandingLogoBlob,
  getTenantBranding,
  removeTenantBrandingLogo,
  updateTenantBranding,
  uploadTenantBrandingLogo
} from '../api/tenant-branding';

defineOptions({ name: 'TenantsView' });

type EditorMode = 'create' | 'edit';
type TenantTableColumnKey = 'domain' | 'package' | 'status';

interface AppliedFilters {
  identifier: string;
  name: string;
  domain: string;
  status: '' | 'active' | 'inactive';
}

const TENANT_IDENTIFIER_PATTERN = /^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/;

const session = useSessionStore();
const router = useRouter();
const { t } = useAdminI18n();
const allTenants = ref<HostTenant[]>([]);
const packages = ref<HostTenantPackage[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  identifier: '',
  name: '',
  domain: '',
  status: ''
});
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingTenant = ref<HostTenant | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  identifier: '',
  name: '',
  domain: '',
  packageId: ''
});
const fieldErrors = reactive({
  identifier: '',
  name: '',
  domain: ''
});
const columnVisibility = ref<Record<TenantTableColumnKey, boolean>>({
  domain: true,
  package: true,
  status: true
});
const membersVisible = ref(false);
const administratorsVisible = ref(false);
const directoryItems = ref<HostTenantMember[]>([]);
const directoryTenant = ref<HostTenant | null>(null);
const brandingVisible = ref(false);
const brandingTenant = ref<HostTenant | null>(null);
const brandingSaving = ref(false);
const brandingUploadingLogo = ref(false);
const brandingRemovingLogo = ref(false);
const brandingLogoPreviewUrl = ref<string | null>(null);
const brandingVersion = ref(0);
const brandingForm = reactive({
  systemTitle: '',
  contactPhone: '',
  contactEmail: '',
  contactAddress: '',
  copyright: ''
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

const filteredTenants = computed(() => {
  let rows = allTenants.value;
  const filters = appliedFilters.value;

  if (filters.identifier.trim()) {
    const keyword = filters.identifier.trim().toLowerCase();
    rows = rows.filter(tenant => tenant.identifier.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(tenant => tenant.name.toLowerCase().includes(keyword));
  }

  if (filters.domain.trim()) {
    const keyword = filters.domain.trim().toLowerCase();
    rows = rows.filter(tenant => tenant.domain.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(tenant => tenant.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(tenant => !tenant.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedTenants, resetPage } =
  useArtClientPagination(filteredTenants);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'domain', label: t('tenants.domain'), visible: columnVisibility.value.domain },
    { key: 'package', label: t('tenants.packageLabel'), visible: columnVisibility.value.package },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as TenantTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'identifier',
    label: t('tenants.identifier'),
    placeholder: t('tenants.searchIdentifierPlaceholder')
  },
  {
    key: 'name',
    label: t('tenants.name'),
    placeholder: t('tenants.searchNamePlaceholder')
  },
  {
    key: 'domain',
    label: t('tenants.domain'),
    placeholder: t('tenants.searchDomainPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('tenants.searchStatusPlaceholder'),
    options: [
      { label: t('tenants.active'), value: 'active' },
      { label: t('tenants.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('tenancy.tenants.create'));
const canUpdate = computed(() => session.can('tenancy.tenants.update'));
const canEnable = computed(() => session.can('tenancy.tenants.enable'));
const canReadDirectory = computed(() => session.can('tenancy.tenants.read_directory'));
const canAssignPackage = computed(() => session.can('tenancy.tenants.assign_package'));
const canSwitchTenant = computed(() => session.can('tenancy.tenants.switch'));

watchLoading(loading);

onMounted(() => {
  void Promise.all([load(), loadPackages()]);
});

function isColumnVisible(key: TenantTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizeIdentifier(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-+/, '')
    .replace(/-+$/, '');
}

function normalizeDomain(value: string): string {
  return value.trim().toLowerCase();
}

function clearFieldErrors(): void {
  fieldErrors.identifier = '';
  fieldErrors.name = '';
  fieldErrors.domain = '';
}

function validateIdentifier(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const identifier = normalizeIdentifier(editorForm.identifier);
  if (!identifier) {
    return t('tenants.identifierRequired');
  }
  if (!TENANT_IDENTIFIER_PATTERN.test(identifier)) {
    return t('tenants.identifierInvalid');
  }
  return '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('tenants.nameRequired');
  }
  if (name.length > 128) {
    return t('tenants.nameInvalid');
  }
  return '';
}

function validateDomain(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const domain = normalizeDomain(editorForm.domain);
  if (!domain) {
    return t('tenants.domainRequired');
  }
  if (domain.length > 253) {
    return t('tenants.domainInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.identifier = validateIdentifier();
  fieldErrors.name = validateName();
  fieldErrors.domain = validateDomain();
  return !fieldErrors.identifier && !fieldErrors.name && !fieldErrors.domain;
}

async function fetchAllTenants(): Promise<HostTenant[]> {
  const pageLimit = 100;
  const firstPage = await listHostTenants(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostTenants(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function loadPackages(): Promise<void> {
  try {
    const pageResult = await listHostTenantPackages(1, 100);
    packages.value = pageResult.items.filter(pkg => pkg.isActive);
  } catch {
    packages.value = [];
  }
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allTenants.value = await fetchAllTenants();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    identifier: params.identifier ?? '',
    name: params.name ?? '',
    domain: params.domain ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { identifier: '', name: '', domain: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingTenant.value = null;
  editorForm.identifier = '';
  editorForm.name = '';
  editorForm.domain = '';
  editorForm.packageId = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(tenant: HostTenant): void {
  if (changing.value || !tenant.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingTenant.value = tenant;
  editorForm.identifier = tenant.identifier;
  editorForm.name = tenant.name;
  editorForm.domain = tenant.domain;
  editorForm.packageId = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorIdentifierBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.identifier = normalizeIdentifier(editorForm.identifier);
  fieldErrors.identifier = validateIdentifier();
}

function onEditorDomainBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.domain = normalizeDomain(editorForm.domain);
  fieldErrors.domain = validateDomain();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.identifier = normalizeIdentifier(editorForm.identifier);
    editorForm.domain = normalizeDomain(editorForm.domain);
  }
  editorForm.name = editorForm.name.trim();
  if (!applyFieldErrors()) {
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
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostTenant(
      editorForm.identifier,
      editorForm.name,
      editorForm.domain,
      editorForm.packageId || null
    );
    editorOpen.value = false;
    ElMessage.success(t('tenants.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const tenant = editingTenant.value;
  if (!canUpdate.value || !tenant) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostTenant(tenant.id, editorForm.name, tenant.version);
    editorOpen.value = false;
    ElMessage.success(t('tenants.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function assignPackage(
  tenant: HostTenant,
  packageId: string | null
): Promise<void> {
  if (changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await assignHostTenantPackage(tenant.id, packageId, tenant.version);
    ElMessage.success(t('tenants.packageAssignSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(tenant: HostTenant): Promise<void> {
  if (changing.value || !tenant.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('tenants.confirmDisable', { name: tenant.identifier }),
      t('tenants.disable'),
      {
        type: 'warning',
        confirmButtonText: t('tenants.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableHostTenant(tenant.id);
    ElMessage.success(t('tenants.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function enable(tenant: HostTenant): Promise<void> {
  if (changing.value || tenant.isActive || !canEnable.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('tenants.confirmEnable', { name: tenant.identifier }),
      t('tenants.enable'),
      {
        type: 'warning',
        confirmButtonText: t('tenants.enable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await enableHostTenant(tenant.id);
    ElMessage.success(t('tenants.enableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openMembers(tenant: HostTenant): Promise<void> {
  if (changing.value || !canReadDirectory.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const page = await listHostTenantMembers(tenant.id, 1, 100);
    directoryTenant.value = tenant;
    directoryItems.value = [...page.items];
    membersVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

function revokeBrandingPreview(): void {
  if (brandingLogoPreviewUrl.value) {
    URL.revokeObjectURL(brandingLogoPreviewUrl.value);
    brandingLogoPreviewUrl.value = null;
  }
}

async function refreshBrandingLogoPreview(tenantId: string, hasLogo: boolean): Promise<void> {
  revokeBrandingPreview();
  if (!hasLogo) {
    return;
  }

  try {
    const blob = await fetchTenantBrandingLogoBlob(tenantId);
    brandingLogoPreviewUrl.value = URL.createObjectURL(blob);
  } catch {
    brandingLogoPreviewUrl.value = null;
  }
}

async function openBranding(tenant: HostTenant): Promise<void> {
  if (changing.value || !canUpdate.value || !tenant.isActive) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const branding = await getTenantBranding(tenant.id);
    brandingTenant.value = tenant;
    brandingVersion.value = branding.version;
    brandingForm.systemTitle = branding.systemTitle ?? '';
    brandingForm.contactPhone = branding.contactPhone ?? '';
    brandingForm.contactEmail = branding.contactEmail ?? '';
    brandingForm.contactAddress = branding.contactAddress ?? '';
    brandingForm.copyright = branding.copyright ?? '';
    await refreshBrandingLogoPreview(tenant.id, branding.logoFileId !== null);
    brandingVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveBranding(): Promise<void> {
  if (!brandingTenant.value) {
    return;
  }

  brandingSaving.value = true;
  try {
    const branding = await updateTenantBranding(brandingTenant.value.id, {
      systemTitle: brandingForm.systemTitle || null,
      contactPhone: brandingForm.contactPhone || null,
      contactEmail: brandingForm.contactEmail || null,
      contactAddress: brandingForm.contactAddress || null,
      copyright: brandingForm.copyright || null,
      version: brandingVersion.value
    });
    brandingVersion.value = branding.version;
    ElMessage.success(t('tenantBranding.saveSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    brandingSaving.value = false;
  }
}

async function handleBrandingLogoSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file || !brandingTenant.value) {
    return;
  }

  brandingUploadingLogo.value = true;
  try {
    const branding = await uploadTenantBrandingLogo(brandingTenant.value.id, file);
    brandingVersion.value = branding.version;
    await refreshBrandingLogoPreview(brandingTenant.value.id, true);
    ElMessage.success(t('tenantBranding.logoUploadSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    brandingUploadingLogo.value = false;
  }
}

async function removeBrandingLogo(): Promise<void> {
  if (!brandingTenant.value) {
    return;
  }

  brandingRemovingLogo.value = true;
  try {
    const branding = await removeTenantBrandingLogo(brandingTenant.value.id);
    brandingVersion.value = branding.version;
    await refreshBrandingLogoPreview(brandingTenant.value.id, false);
    ElMessage.success(t('tenantBranding.logoRemoveSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    brandingRemovingLogo.value = false;
  }
}

async function openAdministrators(tenant: HostTenant): Promise<void> {
  if (changing.value || !canReadDirectory.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const page = await listHostTenantAdministrators(tenant.id, 1, 100);
    directoryTenant.value = tenant;
    directoryItems.value = [...page.items];
    administratorsVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function manageTenantUsers(tenant: HostTenant): Promise<void> {
  if (changing.value || !tenant.isActive || !canSwitchTenant.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await session.switchTenant(tenant.id);
    await router.push('/identity/users');
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'tenants.loadFailed' | 'tenants.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_tenant_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="tenants-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenants.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="4"
      :search-label="t('tenants.query')"
      :reset-label="t('tenants.reset')"
      :expand-label="t('tenants.expand')"
      :collapse-label="t('tenants.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:columns="tableColumns"
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,columns,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="tenancy.tenants.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="tenants-action-create"
                @click="openCreate"
              >
                {{ t('tenants.addTenant') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedTenants.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedTenants"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('tenants.identifier')" min-width="220">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.identifier.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.identifier }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.name }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('domain')"
              :label="t('tenants.domain')"
              min-width="160"
              prop="domain"
            >
              <template #default="{ row }">
                <span translate="no">{{ row.domain }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('package')"
              :label="t('tenants.packageLabel')"
              min-width="180"
            >
              <template #default="{ row }">
                <el-select
                  v-if="canAssignPackage"
                  :model-value="row.tenantPackageId ?? ''"
                  :placeholder="t('tenants.packageUnassigned')"
                  :disabled="changing"
                  size="small"
                @change="value => assignPackage(row as HostTenant, value ? String(value) : null)"
                >
                  <el-option :label="t('tenants.packageUnassigned')" value="" />
                  <el-option v-for="pkg in packages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
                </el-select>
                <span v-else translate="no">
                  {{ row.tenantPackageName ?? t('tenants.packageUnassigned') }}
                </span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'tenants.active' : 'tenants.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="320"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="tenancy.tenants.read_directory">
                    <el-button
                      v-if="row.isActive"
                      link
                      type="primary"
                      data-testid="tenants-action-members"
                      @click="openMembers(row as HostTenant)"
                    >
                      {{ t('tenants.members') }}
                    </el-button>
                    <el-button
                      v-if="row.isActive"
                      link
                      type="primary"
                      data-testid="tenants-action-administrators"
                      @click="openAdministrators(row as HostTenant)"
                    >
                      {{ t('tenants.administrators') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="tenancy.tenants.switch">
                    <el-button
                      v-if="row.isActive"
                      link
                      type="primary"
                      data-testid="tenants-action-manage-users"
                      @click="manageTenantUsers(row as HostTenant)"
                    >
                      {{ t('tenants.manageUsers') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="tenancy.tenants.update">
                    <el-button
                      v-if="row.isActive"
                      link
                      type="primary"
                      data-testid="tenants-action-branding"
                      :disabled="changing"
                      @click="openBranding(row as HostTenant)"
                    >
                      {{ t('tenants.branding') }}
                    </el-button>
                    <ArtTableActionButton
                      type="edit"
                      test-id="tenants-action-edit"
                      :title="t('tenants.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as HostTenant)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="!row.isActive" code="tenancy.tenants.enable">
                    <el-button
                      link
                      type="primary"
                      data-testid="tenants-action-enable"
                      :disabled="changing"
                      @click="enable(row as HostTenant)"
                    >
                      {{ t('tenants.enable') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="tenancy.tenants.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="tenants-action-disable"
                      :title="t('tenants.disable')"
                      :disabled="changing"
                  @click="disable(row as HostTenant)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('tenants.emptyDirectory') }}</template>
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

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('tenants.createDialogTitle') : t('tenants.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="tenants-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="tenants-editor-form"
        :model="editorForm"
        label-width="96px"
        class="tenants-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('tenants.identifier')"
          prop="identifier"
          required
          :error="fieldErrors.identifier || undefined"
        >
          <el-input
            v-model="editorForm.identifier"
            :placeholder="t('tenants.identifierPlaceholder')"
            @blur="onEditorIdentifierBlur"
            @update:model-value="fieldErrors.identifier = validateIdentifier()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('tenants.identifier')">
          <el-input v-model="editorForm.identifier" disabled />
        </el-form-item>
        <el-form-item
          :label="t('tenants.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            :placeholder="t('tenants.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('tenants.domain')"
          prop="domain"
          required
          :error="fieldErrors.domain || undefined"
        >
          <el-input
            v-model="editorForm.domain"
            :placeholder="t('tenants.domainPlaceholder')"
            @blur="onEditorDomainBlur"
            @update:model-value="fieldErrors.domain = validateDomain()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('tenants.domain')">
          <el-input v-model="editorForm.domain" disabled />
        </el-form-item>
        <el-form-item v-if="editorMode === 'create'" :label="t('tenants.packageLabel')">
          <el-select
            v-model="editorForm.packageId"
            :placeholder="t('tenants.packageUnassigned')"
            clearable
          >
            <el-option v-for="pkg in packages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
          </el-select>
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <el-dialog v-model="membersVisible" :title="t('tenants.membersTitle')" width="560px">
      <p v-if="directoryItems.length === 0" class="art-dialog-hint">
        {{ t('tenants.directoryEmpty') }}
      </p>
      <ul v-else class="tenants-directory-list">
        <li v-for="item in directoryItems" :key="item.userId">
          <span translate="no">{{ item.displayName }}</span>
          <code translate="no">{{ item.username }}</code>
        </li>
      </ul>
      <template #footer>
        <el-button @click="membersVisible = false">{{ t('users.cancel') }}</el-button>
        <PermissionGate v-if="directoryTenant?.isActive" code="tenancy.tenants.switch">
          <el-button
            type="primary"
            data-testid="tenants-members-manage-users"
            @click="directoryTenant && manageTenantUsers(directoryTenant)"
          >
            {{ t('tenants.manageUsers') }}
          </el-button>
        </PermissionGate>
      </template>
    </el-dialog>

    <el-dialog v-model="administratorsVisible" :title="t('tenants.administratorsTitle')" width="560px">
      <p class="art-dialog-hint">{{ t('tenants.manageUsersHint') }}</p>
      <p v-if="directoryItems.length === 0" class="art-dialog-hint">
        {{ t('tenants.directoryEmpty') }}
      </p>
      <ul v-else class="tenants-directory-list">
        <li v-for="item in directoryItems" :key="item.userId">
          <span translate="no">{{ item.displayName }}</span>
          <code translate="no">{{ item.username }}</code>
        </li>
      </ul>
      <template #footer>
        <el-button @click="administratorsVisible = false">{{ t('users.cancel') }}</el-button>
        <PermissionGate v-if="directoryTenant?.isActive" code="tenancy.tenants.switch">
          <el-button
            type="primary"
            data-testid="tenants-administrators-manage-users"
            @click="directoryTenant && manageTenantUsers(directoryTenant)"
          >
            {{ t('tenants.manageUsers') }}
          </el-button>
        </PermissionGate>
      </template>
    </el-dialog>

    <el-dialog
      v-model="brandingVisible"
      :title="t('tenantBranding.title')"
      width="640px"
      @closed="revokeBrandingPreview()"
    >
      <p class="art-dialog-hint">{{ t('tenantBranding.caption') }}</p>
      <el-form label-width="120px">
        <el-form-item :label="t('tenantBranding.logo')">
          <div class="tenants-branding__logo-row">
            <div v-if="brandingLogoPreviewUrl" class="tenants-branding__logo-preview">
              <img :src="brandingLogoPreviewUrl" alt="" />
            </div>
            <div class="tenants-branding__logo-actions">
              <label>
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,image/svg+xml"
                  hidden
                  :disabled="brandingUploadingLogo || brandingRemovingLogo"
                  @change="handleBrandingLogoSelected"
                />
                <el-button :loading="brandingUploadingLogo">{{ t('tenantBranding.uploadLogo') }}</el-button>
              </label>
              <el-button
                v-if="brandingLogoPreviewUrl"
                :loading="brandingRemovingLogo"
                @click="removeBrandingLogo"
              >
                {{ t('tenantBranding.removeLogo') }}
              </el-button>
            </div>
          </div>
        </el-form-item>
        <el-form-item :label="t('tenantBranding.systemTitle')">
          <el-input v-model="brandingForm.systemTitle" maxlength="128" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactPhone')">
          <el-input v-model="brandingForm.contactPhone" maxlength="32" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactEmail')">
          <el-input v-model="brandingForm.contactEmail" maxlength="256" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactAddress')">
          <el-input v-model="brandingForm.contactAddress" maxlength="512" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.copyright')">
          <el-input v-model="brandingForm.copyright" maxlength="256" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="brandingVisible = false">{{ t('users.cancel') }}</el-button>
        <el-button type="primary" :loading="brandingSaving" @click="saveBranding">
          {{ t('users.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.tenants-branding__logo-row {
  display: flex;
  gap: 16px;
  align-items: center;
  flex-wrap: wrap;
}

.tenants-branding__logo-preview {
  width: 72px;
  height: 72px;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.tenants-branding__logo-preview img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}

.tenants-branding__logo-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.tenants-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.tenants-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.tenants-editor-form {
  padding-top: 8px;
}

.tenants-directory-list {
  margin: 0;
  padding-left: 1.25rem;
}

.tenants-directory-list li {
  margin-bottom: 8px;
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
</style>
