<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
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
import type { FullNetProblemDetails, HostTenantPackage } from '@fullnet/client-contracts';
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
import {
  createHostTenantPackage,
  disableHostTenantPackage,
  listHostTenantPackages,
  updateHostTenantPackage
} from '../api/tenant-packages';

defineOptions({ name: 'TenantPackagesView' });

type EditorMode = 'create' | 'edit';
type PackageTableColumnKey = 'description' | 'assignedTenantCount' | 'status';

interface AppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
}

const PACKAGE_CODE_PATTERN = /^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allPackages = ref<HostTenantPackage[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ code: '', name: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingPackage = ref<HostTenantPackage | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ code: '', name: '', description: '' });
const fieldErrors = reactive({ code: '', name: '' });
const columnVisibility = ref<Record<PackageTableColumnKey, boolean>>({
  description: true,
  assignedTenantCount: true,
  status: true
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

const filteredPackages = computed(() => {
  let rows = allPackages.value;
  const filters = appliedFilters.value;

  if (filters.code.trim()) {
    const keyword = filters.code.trim().toLowerCase();
    rows = rows.filter(pkg => pkg.code.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(pkg => pkg.name.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(pkg => pkg.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(pkg => !pkg.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedPackages, resetPage } =
  useArtClientPagination(filteredPackages);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    {
      key: 'description',
      label: t('tenantPackages.descriptionLabel'),
      visible: columnVisibility.value.description
    },
    {
      key: 'assignedTenantCount',
      label: t('tenantPackages.assignedTenantCount'),
      visible: columnVisibility.value.assignedTenantCount
    },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as PackageTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('tenantPackages.code'),
    placeholder: t('tenantPackages.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('tenantPackages.name'),
    placeholder: t('tenantPackages.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('tenantPackages.searchStatusPlaceholder'),
    options: [
      { label: t('tenantPackages.active'), value: 'active' },
      { label: t('tenantPackages.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('tenancy.tenant_packages.create'));
const canUpdate = computed(() => session.can('tenancy.tenant_packages.update'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: PackageTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizeCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-+/, '')
    .replace(/-+$/, '');
}

function clearFieldErrors(): void {
  fieldErrors.code = '';
  fieldErrors.name = '';
}

function validateCode(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const code = normalizeCode(editorForm.code);
  if (!code) {
    return t('tenantPackages.codeRequired');
  }
  if (!PACKAGE_CODE_PATTERN.test(code)) {
    return t('tenantPackages.codeInvalid');
  }
  return '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('tenantPackages.nameRequired');
  }
  if (name.length > 128) {
    return t('tenantPackages.nameInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.code = validateCode();
  fieldErrors.name = validateName();
  return !fieldErrors.code && !fieldErrors.name;
}

async function fetchAllPackages(): Promise<HostTenantPackage[]> {
  const pageLimit = 100;
  const firstPage = await listHostTenantPackages(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostTenantPackages(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allPackages.value = await fetchAllPackages();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenantPackages.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    code: params.code ?? '',
    name: params.name ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { code: '', name: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingPackage.value = null;
  editorForm.code = '';
  editorForm.name = '';
  editorForm.description = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(pkg: HostTenantPackage): void {
  if (changing.value || !pkg.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingPackage.value = pkg;
  editorForm.code = pkg.code;
  editorForm.name = pkg.name;
  editorForm.description = pkg.description ?? '';
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorCodeBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.code = normalizeCode(editorForm.code);
  fieldErrors.code = validateCode();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.code = normalizeCode(editorForm.code);
  }
  editorForm.name = editorForm.name.trim();
  editorForm.description = editorForm.description.trim();
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
    await createHostTenantPackage(
      editorForm.code,
      editorForm.name,
      editorForm.description || null
    );
    editorOpen.value = false;
    ElMessage.success(t('tenantPackages.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const pkg = editingPackage.value;
  if (!canUpdate.value || !pkg) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostTenantPackage(
      pkg.id,
      editorForm.name,
      editorForm.description || null,
      pkg.version
    );
    editorOpen.value = false;
    ElMessage.success(t('tenantPackages.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(pkg: HostTenantPackage): Promise<void> {
  if (changing.value || !pkg.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('tenantPackages.confirmDisable', { name: pkg.code }),
      t('tenantPackages.disable'),
      {
        type: 'warning',
        confirmButtonText: t('tenantPackages.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableHostTenantPackage(pkg.id);
    ElMessage.success(t('tenantPackages.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'tenantPackages.loadFailed' | 'tenantPackages.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.host_tenant_package_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="tenant-packages-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenantPackages.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('tenantPackages.query')"
      :reset-label="t('tenantPackages.reset')"
      :expand-label="t('tenantPackages.expand')"
      :collapse-label="t('tenantPackages.collapse')"
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
            <PermissionGate code="tenancy.tenant_packages.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="tenant-packages-action-create"
                @click="openCreate"
              >
                {{ t('tenantPackages.addPackage') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedPackages.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedPackages"
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

            <el-table-column :label="t('tenantPackages.code')" min-width="220">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.code.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.code }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.name }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('description')"
              :label="t('tenantPackages.descriptionLabel')"
              min-width="160"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.description ?? '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('assignedTenantCount')"
              :label="t('tenantPackages.assignedTenantCount')"
              width="120"
              align="center"
              prop="assignedTenantCount"
            />

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'tenantPackages.active' : 'tenantPackages.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="tenancy.tenant_packages.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="tenant-packages-action-edit"
                      :title="t('tenantPackages.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as HostTenantPackage)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="tenancy.tenant_packages.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="tenant-packages-action-disable"
                      :title="t('tenantPackages.disable')"
                      :disabled="changing"
                  @click="disable(row as HostTenantPackage)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('tenantPackages.emptyDirectory') }}</template>
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
      :title="editorMode === 'create' ? t('tenantPackages.createDialogTitle') : t('tenantPackages.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="tenant-packages-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="tenant-packages-editor-form"
        :model="editorForm"
        label-width="96px"
        class="tenant-packages-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('tenantPackages.code')"
          prop="code"
          required
          :error="fieldErrors.code || undefined"
        >
          <el-input
            v-model="editorForm.code"
            :placeholder="t('tenantPackages.codePlaceholder')"
            @blur="onEditorCodeBlur"
            @update:model-value="fieldErrors.code = validateCode()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('tenantPackages.code')">
          <el-input v-model="editorForm.code" disabled />
        </el-form-item>
        <el-form-item
          :label="t('tenantPackages.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            :placeholder="t('tenantPackages.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
        <el-form-item :label="t('tenantPackages.descriptionLabel')">
          <el-input
            v-model="editorForm.description"
            :placeholder="t('tenantPackages.descriptionPlaceholder')"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.tenant-packages-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.tenant-packages-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.tenant-packages-editor-form {
  padding-top: 8px;
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
