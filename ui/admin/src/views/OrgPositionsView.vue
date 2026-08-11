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
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type OrganizationPosition,
  type OrganizationPositionLevel,
  type OrganizationUnit
} from '@fullnet/client-contracts';
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
  assignOrganizationPositionLevel,
  assignOrganizationPositionUnit,
  createOrganizationPosition,
  disableOrganizationPosition,
  listOrganizationPositions,
  updateOrganizationPosition
} from '../api/org-positions';
import { listOrganizationPositionLevels } from '../api/org-position-levels';
import { listOrganizationUnits } from '../api/org-units';

defineOptions({ name: 'OrgPositionsView' });

type EditorMode = 'create' | 'edit';
type PositionTableColumnKey = 'status' | 'displayOrder' | 'unit' | 'positionLevel';

interface AppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
}

const POSITION_CODE_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allPositions = ref<OrganizationPosition[]>([]);
const units = ref<OrganizationUnit[]>([]);
const positionLevels = ref<OrganizationPositionLevel[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ code: '', name: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingPosition = ref<OrganizationPosition | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ code: '', name: '' });
const fieldErrors = reactive({ code: '', name: '' });
const columnVisibility = ref<Record<PositionTableColumnKey, boolean>>({
  status: true,
  displayOrder: true,
  unit: true,
  positionLevel: true
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

const canBindUnits = computed(() => (
  session.can('organization.positions.assign_unit')
  && session.can('organization.units.read')
));
const canBindPositionLevels = computed(() => (
  session.can('organization.positions.assign_position_level')
  && session.can('organization.position_levels.read')
));
const canCreate = computed(() => session.can('organization.positions.create'));
const canUpdate = computed(() => session.can('organization.positions.update'));

const filteredPositions = computed(() => {
  let rows = allPositions.value;
  const filters = appliedFilters.value;

  if (filters.code.trim()) {
    const keyword = filters.code.trim().toLowerCase();
    rows = rows.filter(position => position.code.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(position => position.name.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(position => position.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(position => !position.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedPositions, resetPage } = useArtClientPagination(filteredPositions);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    {
      key: 'displayOrder',
      label: t('users.columnSortOrder'),
      visible: columnVisibility.value.displayOrder
    },
    { key: 'unit', label: t('orgPositions.unit'), visible: columnVisibility.value.unit },
    {
      key: 'positionLevel',
      label: t('orgPositions.positionLevel'),
      visible: columnVisibility.value.positionLevel
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as PositionTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('orgPositions.code'),
    placeholder: t('orgPositions.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('orgPositions.name'),
    placeholder: t('orgPositions.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('orgPositions.searchStatusPlaceholder'),
    options: [
      { label: t('orgPositions.active'), value: 'active' },
      { label: t('orgPositions.inactive'), value: 'inactive' }
    ]
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: PositionTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizePositionCode(value: string): string {
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
}

function validateCode(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const code = normalizePositionCode(editorForm.code);
  if (!code) {
    return t('orgPositions.codeRequired');
  }
  if (!POSITION_CODE_PATTERN.test(code)) {
    return t('orgPositions.codeInvalid');
  }
  return '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('orgPositions.nameRequired');
  }
  if (name.length > 128) {
    return t('orgPositions.nameInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.code = validateCode();
  fieldErrors.name = validateName();
  return !fieldErrors.code && !fieldErrors.name;
}

async function fetchAllPositions(): Promise<OrganizationPosition[]> {
  const pageLimit = 100;
  const firstPage = await listOrganizationPositions(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listOrganizationPositions(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const unitPageRequest = canBindUnits.value
      ? listOrganizationUnits(1, 100).catch(() => null)
      : Promise.resolve(null);
    const positionLevelPageRequest = canBindPositionLevels.value
      ? listOrganizationPositionLevels(1, 100).catch(() => null)
      : Promise.resolve(null);
    const [positions, unitPage, positionLevelPage] = await Promise.all([
      fetchAllPositions(),
      unitPageRequest,
      positionLevelPageRequest
    ]);
    allPositions.value = positions;
    // 机构或职级目录权限、网络失败不应阻断职位列表的只读展示。
    units.value = unitPage?.items.filter(unit => unit.isActive) ?? [];
    positionLevels.value = positionLevelPage?.items.filter(level => level.isActive) ?? [];
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.loadFailed');
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
  editingPosition.value = null;
  editorForm.code = '';
  editorForm.name = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(position: OrganizationPosition): void {
  if (changing.value || !position.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingPosition.value = position;
  editorForm.code = position.code;
  editorForm.name = position.name;
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorCodeBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.code = normalizePositionCode(editorForm.code);
  fieldErrors.code = validateCode();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.code = normalizePositionCode(editorForm.code);
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
    await createOrganizationPosition(editorForm.code, editorForm.name);
    editorOpen.value = false;
    ElMessage.success(t('orgPositions.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const position = editingPosition.value;
  if (!canUpdate.value || !position) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationPosition(
      position.id,
      editorForm.name,
      position.displayOrder,
      position.version
    );
    editorOpen.value = false;
    ElMessage.success(t('orgPositions.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function assignPositionLevel(position: OrganizationPosition, value: unknown): Promise<void> {
  if (changing.value || !position.isActive) {
    return;
  }
  const positionLevelId = typeof value === 'string' && value.length > 0 ? value : null;
  changing.value = true;
  problem.value = undefined;
  try {
    await assignOrganizationPositionLevel(position.id, positionLevelId, position.version);
    ElMessage.success(t('orgPositions.positionLevelUpdateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function assignUnit(position: OrganizationPosition, value: unknown): Promise<void> {
  if (changing.value || !position.isActive) {
    return;
  }
  const unitId = typeof value === 'string' && value.length > 0 ? value : null;
  changing.value = true;
  problem.value = undefined;
  try {
    await assignOrganizationPositionUnit(position.id, unitId, position.version);
    ElMessage.success(t('orgPositions.unitUpdateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(position: OrganizationPosition): Promise<void> {
  if (changing.value || !position.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('orgPositions.confirmDisable', { name: position.code }),
      t('orgPositions.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgPositions.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableOrganizationPosition(position.id);
    ElMessage.success(t('orgPositions.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgPositions.loadFailed' | 'orgPositions.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_position_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-positions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgPositions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('orgPositions.query')"
      :reset-label="t('orgPositions.reset')"
      :expand-label="t('orgPositions.expand')"
      :collapse-label="t('orgPositions.collapse')"
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
            <PermissionGate code="organization.positions.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="org-positions-action-create"
                @click="openCreate"
              >
                {{ t('orgPositions.addPosition') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedPositions.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedPositions"
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

            <el-table-column :label="t('orgPositions.code')" min-width="200">
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
                  {{ t(row.isActive ? 'orgPositions.active' : 'orgPositions.inactive') }}
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
              v-if="isColumnVisible('unit') && canBindUnits"
              :label="t('orgPositions.unit')"
              min-width="180"
            >
              <template #default="{ row }">
                <el-select
                  v-if="row.isActive"
                  :model-value="row.unitId ?? ''"
                  :aria-label="t('orgPositions.unit')"
                  :disabled="changing"
                  size="small"
                @change="assignUnit(row as OrganizationPosition, $event)"
                >
                  <el-option :label="t('orgPositions.unitUnassigned')" value="" />
                  <el-option
                    v-for="unit in units"
                    :key="unit.id"
                    :label="`${unit.name} (${unit.code})`"
                    :value="unit.id"
                  />
                </el-select>
                <span v-else translate="no">{{ row.unitName ?? t('orgPositions.unitUnassigned') }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('positionLevel') && canBindPositionLevels"
              :label="t('orgPositions.positionLevel')"
              min-width="180"
            >
              <template #default="{ row }">
                <el-select
                  v-if="row.isActive"
                  :model-value="row.positionLevelId ?? ''"
                  :aria-label="t('orgPositions.positionLevel')"
                  :disabled="changing"
                  size="small"
                @change="assignPositionLevel(row as OrganizationPosition, $event)"
                >
                  <el-option :label="t('orgPositions.positionLevelUnassigned')" value="" />
                  <el-option
                    v-for="positionLevel in positionLevels"
                    :key="positionLevel.id"
                    :label="`${positionLevel.name} (${positionLevel.code})`"
                    :value="positionLevel.id"
                  />
                </el-select>
                <span v-else translate="no">
                  {{ row.positionLevelName ?? t('orgPositions.positionLevelUnassigned') }}
                </span>
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
                  <PermissionGate code="organization.positions.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="org-positions-action-edit"
                      :title="t('orgPositions.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as OrganizationPosition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="organization.positions.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="org-positions-action-disable"
                      :title="t('orgPositions.disable')"
                      :disabled="changing"
                  @click="disable(row as OrganizationPosition)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('orgPositions.emptyDirectory') }}</template>
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
      :title="editorMode === 'create' ? t('orgPositions.createDialogTitle') : t('orgPositions.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="org-positions-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="org-positions-editor-form"
        :model="editorForm"
        label-width="96px"
        class="org-positions-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('orgPositions.code')"
          prop="code"
          required
          :error="fieldErrors.code || undefined"
        >
          <el-input
            v-model="editorForm.code"
            :placeholder="t('orgPositions.codePlaceholder')"
            @blur="onEditorCodeBlur"
            @update:model-value="fieldErrors.code = validateCode()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('orgPositions.code')">
          <el-input v-model="editorForm.code" disabled />
        </el-form-item>
        <el-form-item
          :label="t('orgPositions.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            :placeholder="t('orgPositions.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.org-positions-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.org-positions-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.org-positions-editor-form {
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
