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
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, OrganizationUnit } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOrganizationUnit,
  disableOrganizationUnit,
  listOrganizationUnits,
  updateOrganizationUnit
} from '../api/org-units';
import {
  buildOrganizationUnitParentOptions,
  buildOrganizationUnitTree,
  filterOrganizationUnitsForTree,
  wouldCreateOrganizationUnitCycle,
  type OrganizationUnitTreeNode
} from '../organization/org-unit-tree';

defineOptions({ name: 'OrgUnitsView' });

type EditorMode = 'create' | 'edit';
type OrgUnitTableColumnKey = 'status' | 'displayOrder';

interface AppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
}

const UNIT_CODE_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allUnits = ref<OrganizationUnit[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ code: '', name: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingUnit = ref<OrganizationUnit | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ code: '', name: '', parentId: '' as string | null });
const fieldErrors = reactive({ code: '', name: '', parentId: '' });
const columnVisibility = ref<Record<OrgUnitTableColumnKey, boolean>>({
  status: true,
  displayOrder: true
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

const filteredUnits = computed(() => {
  const filters = appliedFilters.value;
  return filterOrganizationUnitsForTree(allUnits.value, unit => {
    if (filters.code.trim()) {
      const keyword = filters.code.trim().toLowerCase();
      if (!unit.code.toLowerCase().includes(keyword)) {
        return false;
      }
    }

    if (filters.name.trim()) {
      const keyword = filters.name.trim().toLowerCase();
      if (!unit.name.toLowerCase().includes(keyword)) {
        return false;
      }
    }

    if (filters.status === 'active' && !unit.isActive) {
      return false;
    }
    if (filters.status === 'inactive' && unit.isActive) {
      return false;
    }

    return true;
  });
});

const unitTree = computed(() => buildOrganizationUnitTree(filteredUnits.value));

const { page, pageSize, total, pagedItems: pagedUnits, resetPage } = useArtClientPagination(unitTree);

const parentOptions = computed(() =>
  buildOrganizationUnitParentOptions(
    allUnits.value,
    editorMode.value === 'edit' ? editingUnit.value?.id ?? null : null
  )
);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    {
      key: 'displayOrder',
      label: t('users.columnSortOrder'),
      visible: columnVisibility.value.displayOrder
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as OrgUnitTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('orgUnits.code'),
    placeholder: t('orgUnits.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('orgUnits.name'),
    placeholder: t('orgUnits.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('orgUnits.searchStatusPlaceholder'),
    options: [
      { label: t('orgUnits.active'), value: 'active' },
      { label: t('orgUnits.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('organization.units.create'));
const canUpdate = computed(() => session.can('organization.units.update'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: OrgUnitTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizeUnitCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-+/, '');
}

function clearFieldErrors(): void {
  fieldErrors.code = '';
  fieldErrors.name = '';
  fieldErrors.parentId = '';
}

function validateCode(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const code = normalizeUnitCode(editorForm.code);
  if (!code) {
    return t('orgUnits.codeRequired');
  }
  if (!UNIT_CODE_PATTERN.test(code)) {
    return t('orgUnits.codeInvalid');
  }
  return '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('orgUnits.nameRequired');
  }
  if (name.length > 128) {
    return t('orgUnits.nameInvalid');
  }
  return '';
}

function validateParentId(): string {
  const parentId = editorForm.parentId || null;
  if (!parentId) {
    return '';
  }

  const parent = allUnits.value.find(unit => unit.id === parentId);
  if (!parent || !parent.isActive) {
    return t('orgUnits.parentInvalid');
  }

  if (
    editorMode.value === 'edit'
    && editingUnit.value
    && wouldCreateOrganizationUnitCycle(allUnits.value, editingUnit.value.id, parentId)
  ) {
    return t('orgUnits.parentCycle');
  }

  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.code = validateCode();
  fieldErrors.name = validateName();
  fieldErrors.parentId = validateParentId();
  return !fieldErrors.code && !fieldErrors.name && !fieldErrors.parentId;
}

async function fetchAllUnits(): Promise<OrganizationUnit[]> {
  const pageLimit = 100;
  const firstPage = await listOrganizationUnits(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listOrganizationUnits(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allUnits.value = await fetchAllUnits();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUnits.loadFailed');
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

function openCreate(parentId: string | null = null): void {
  editorMode.value = 'create';
  editingUnit.value = null;
  editorForm.code = '';
  editorForm.name = '';
  editorForm.parentId = parentId;
  clearFieldErrors();
  editorOpen.value = true;
}

function openCreateChild(unit: OrganizationUnitTreeNode): void {
  if (!unit.isActive) {
    return;
  }
  openCreate(unit.id);
}

function openEdit(unit: OrganizationUnitTreeNode): void {
  if (changing.value || !unit.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingUnit.value = unit;
  editorForm.code = unit.code;
  editorForm.name = unit.name;
  editorForm.parentId = unit.parentId;
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorCodeBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.code = normalizeUnitCode(editorForm.code);
  fieldErrors.code = validateCode();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.code = normalizeUnitCode(editorForm.code);
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
    await createOrganizationUnit(
      editorForm.code,
      editorForm.name,
      10,
      editorForm.parentId || null
    );
    editorOpen.value = false;
    ElMessage.success(t('orgUnits.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const unit = editingUnit.value;
  if (!canUpdate.value || !unit) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationUnit(
      unit.id,
      editorForm.name,
      unit.displayOrder,
      unit.version,
      editorForm.parentId || null
    );
    editorOpen.value = false;
    ElMessage.success(t('orgUnits.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(unit: OrganizationUnitTreeNode): Promise<void> {
  if (changing.value || !unit.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('orgUnits.confirmDisable', { name: unit.code }),
      t('orgUnits.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUnits.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableOrganizationUnit(unit.id);
    ElMessage.success(t('orgUnits.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUnits.loadFailed' | 'orgUnits.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_unit_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-units-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUnits.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('orgUnits.query')"
      :reset-label="t('orgUnits.reset')"
      :expand-label="t('orgUnits.expand')"
      :collapse-label="t('orgUnits.collapse')"
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
            <PermissionGate code="organization.units.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="org-units-action-create"
                @click="openCreate()"
              >
                {{ t('orgUnits.addUnit') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedUnits.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedUnits"
            row-key="id"
            :tree-props="{ children: 'children' }"
            default-expand-all
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            data-testid="org-units-tree-table"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('orgUnits.code')" min-width="200">
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
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'orgUnits.active' : 'orgUnits.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('displayOrder')"
              :label="t('users.columnSortOrder')"
              width="88"
              align="center"
              prop="displayOrder"
            />

            <el-table-column
              :label="t('users.columnActions')"
              width="160"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate code="organization.units.create">
                    <ArtTableActionButton
                      type="add"
                      test-id="org-units-action-add-child"
                      :title="t('orgUnits.addChild')"
                      :disabled="changing || !row.isActive"
                  @click="openCreateChild(row as OrganizationUnitTreeNode)"
                    />
                  </PermissionGate>
                  <PermissionGate code="organization.units.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="org-units-action-edit"
                      :title="t('orgUnits.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as OrganizationUnitTreeNode)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="organization.units.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="org-units-action-disable"
                      :title="t('orgUnits.disable')"
                      :disabled="changing"
                  @click="disable(row as OrganizationUnitTreeNode)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('orgUnits.emptyDirectory') }}</template>
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
      :title="editorMode === 'create' ? t('orgUnits.createDialogTitle') : t('orgUnits.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="org-units-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="org-units-editor-form"
        :model="editorForm"
        label-width="96px"
        class="org-units-editor-form"
      >
        <el-form-item
          :label="t('orgUnits.parent')"
          prop="parentId"
          :error="fieldErrors.parentId || undefined"
        >
          <el-select
            v-model="editorForm.parentId"
            clearable
            filterable
            data-testid="org-units-editor-parent"
            :placeholder="t('orgUnits.parentPlaceholder')"
            @update:model-value="fieldErrors.parentId = validateParentId()"
          >
            <el-option
              v-for="option in parentOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
              :disabled="option.disabled"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('orgUnits.code')"
          prop="code"
          required
          :error="fieldErrors.code || undefined"
        >
          <el-input
            v-model="editorForm.code"
            :placeholder="t('orgUnits.codePlaceholder')"
            @blur="onEditorCodeBlur"
            @update:model-value="fieldErrors.code = validateCode()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('orgUnits.code')">
          <el-input v-model="editorForm.code" disabled />
        </el-form-item>
        <el-form-item
          :label="t('orgUnits.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            :placeholder="t('orgUnits.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.org-units-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.org-units-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.org-units-editor-form {
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
