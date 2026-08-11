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
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type OrganizationPositionLevel
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
  createOrganizationPositionLevel,
  disableOrganizationPositionLevel,
  listOrganizationPositionLevels,
  updateOrganizationPositionLevel
} from '../api/org-position-levels';

defineOptions({ name: 'OrgPositionLevelsView' });

type EditorMode = 'create' | 'edit';
type LevelTableColumnKey = 'status' | 'displayOrder';

interface AppliedFilters {
  code: string;
  name: string;
  status: '' | 'active' | 'inactive';
}

const LEVEL_CODE_PATTERN = /^[a-z][a-z0-9-]{2,63}$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allLevels = ref<OrganizationPositionLevel[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ code: '', name: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingLevel = ref<OrganizationPositionLevel | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ code: '', name: '' });
const fieldErrors = reactive({ code: '', name: '' });
const columnVisibility = ref<Record<LevelTableColumnKey, boolean>>({
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

const filteredLevels = computed(() => {
  let rows = allLevels.value;
  const filters = appliedFilters.value;

  if (filters.code.trim()) {
    const keyword = filters.code.trim().toLowerCase();
    rows = rows.filter(level => level.code.toLowerCase().includes(keyword));
  }

  if (filters.name.trim()) {
    const keyword = filters.name.trim().toLowerCase();
    rows = rows.filter(level => level.name.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(level => level.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(level => !level.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedLevels, resetPage } = useArtClientPagination(filteredLevels);

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
        columnVisibility.value[column.key as LevelTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'code',
    label: t('orgPositionLevels.code'),
    placeholder: t('orgPositionLevels.searchCodePlaceholder')
  },
  {
    key: 'name',
    label: t('orgPositionLevels.name'),
    placeholder: t('orgPositionLevels.searchNamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('orgPositionLevels.searchStatusPlaceholder'),
    options: [
      { label: t('orgPositionLevels.active'), value: 'active' },
      { label: t('orgPositionLevels.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('organization.position_levels.create'));
const canUpdate = computed(() => session.can('organization.position_levels.update'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: LevelTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizeLevelCode(value: string): string {
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
  const code = normalizeLevelCode(editorForm.code);
  if (!code) {
    return t('orgPositionLevels.codeRequired');
  }
  if (!LEVEL_CODE_PATTERN.test(code)) {
    return t('orgPositionLevels.codeInvalid');
  }
  return '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('orgPositionLevels.nameRequired');
  }
  if (name.length > 128) {
    return t('orgPositionLevels.nameInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.code = validateCode();
  fieldErrors.name = validateName();
  return !fieldErrors.code && !fieldErrors.name;
}

async function fetchAllLevels(): Promise<OrganizationPositionLevel[]> {
  const pageLimit = 100;
  const firstPage = await listOrganizationPositionLevels(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listOrganizationPositionLevels(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allLevels.value = await fetchAllLevels();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositionLevels.loadFailed');
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
  editingLevel.value = null;
  editorForm.code = '';
  editorForm.name = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(level: OrganizationPositionLevel): void {
  if (changing.value || !level.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingLevel.value = level;
  editorForm.code = level.code;
  editorForm.name = level.name;
  clearFieldErrors();
  editorOpen.value = true;
}

function onEditorCodeBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.code = normalizeLevelCode(editorForm.code);
  fieldErrors.code = validateCode();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.code = normalizeLevelCode(editorForm.code);
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
    await createOrganizationPositionLevel(editorForm.code, editorForm.name);
    editorOpen.value = false;
    ElMessage.success(t('orgPositionLevels.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const level = editingLevel.value;
  if (!canUpdate.value || !level) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationPositionLevel(level.id, editorForm.name, level.displayOrder, level.version);
    editorOpen.value = false;
    ElMessage.success(t('orgPositionLevels.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(level: OrganizationPositionLevel): Promise<void> {
  if (changing.value || !level.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('orgPositionLevels.confirmDisable', { name: level.code }),
      t('orgPositionLevels.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgPositionLevels.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableOrganizationPositionLevel(level.id);
    ElMessage.success(t('orgPositionLevels.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgPositionLevels.loadFailed' | 'orgPositionLevels.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_position_level_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-position-levels-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">
      {{ t('orgPositionLevels.title') }}
    </h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('orgPositionLevels.query')"
      :reset-label="t('orgPositionLevels.reset')"
      :expand-label="t('orgPositionLevels.expand')"
      :collapse-label="t('orgPositionLevels.collapse')"
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
            <PermissionGate code="organization.position_levels.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="org-position-levels-action-create"
                @click="openCreate"
              >
                {{ t('orgPositionLevels.addLevel') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedLevels.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedLevels"
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

            <el-table-column :label="t('orgPositionLevels.code')" min-width="200">
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
                  {{ t(row.isActive ? 'orgPositionLevels.active' : 'orgPositionLevels.inactive') }}
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
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="organization.position_levels.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="org-position-levels-action-edit"
                      :title="t('orgPositionLevels.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as OrganizationPositionLevel)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive" code="organization.position_levels.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="org-position-levels-action-disable"
                      :title="t('orgPositionLevels.disable')"
                      :disabled="changing"
                  @click="disable(row as OrganizationPositionLevel)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('orgPositionLevels.emptyDirectory') }}</template>
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
      :title="editorMode === 'create' ? t('orgPositionLevels.createDialogTitle') : t('orgPositionLevels.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="org-position-levels-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="org-position-levels-editor-form"
        :model="editorForm"
        label-width="96px"
        class="org-position-levels-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('orgPositionLevels.code')"
          prop="code"
          required
          :error="fieldErrors.code || undefined"
        >
          <el-input
            v-model="editorForm.code"
            :placeholder="t('orgPositionLevels.codePlaceholder')"
            @blur="onEditorCodeBlur"
            @update:model-value="fieldErrors.code = validateCode()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('orgPositionLevels.code')">
          <el-input v-model="editorForm.code" disabled />
        </el-form-item>
        <el-form-item
          :label="t('orgPositionLevels.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            :placeholder="t('orgPositionLevels.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.org-position-levels-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.org-position-levels-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.org-position-levels-editor-form {
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
