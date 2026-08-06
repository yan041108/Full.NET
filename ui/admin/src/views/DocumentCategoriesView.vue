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
  ElTableColumn
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentCategory } from '@fullnet/client-contracts';
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
  createHostDocumentCategory,
  deleteHostDocumentCategory,
  listHostDocumentCategories,
  updateHostDocumentCategory
} from '../api/host-document-categories';

defineOptions({ name: 'DocumentCategoriesView' });

type EditorMode = 'create' | 'edit';
type CategoryTableColumnKey = 'sortOrder';

interface AppliedFilters {
  name: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allCategories = ref<HostDocumentCategory[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ name: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingCategory = ref<HostDocumentCategory | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ name: '', sortOrder: '0' });
const fieldErrors = reactive({ name: '', sortOrder: '' });
const columnVisibility = ref<Record<CategoryTableColumnKey, boolean>>({
  sortOrder: true
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

const filteredCategories = computed(() => {
  let rows = allCategories.value;
  const keyword = appliedFilters.value.name.trim().toLowerCase();
  if (keyword) {
    rows = rows.filter(category => category.name.toLowerCase().includes(keyword));
  }
  return rows;
});

const { page, pageSize, total, pagedItems: pagedCategories, resetPage } =
  useArtClientPagination(filteredCategories);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    {
      key: 'sortOrder',
      label: t('documentCategories.sortOrder'),
      visible: columnVisibility.value.sortOrder
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as CategoryTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'name',
    label: t('documentCategories.name'),
    placeholder: t('documentCategories.searchNamePlaceholder')
  }
]);

const canCreate = computed(() => session.can('document.categories.create'));
const canUpdate = computed(() => session.can('document.categories.update'));
const canDelete = computed(() => session.can('document.categories.delete'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: CategoryTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function clearFieldErrors(): void {
  fieldErrors.name = '';
  fieldErrors.sortOrder = '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('documentCategories.nameRequired');
  }
  return '';
}

function validateSortOrder(): string {
  const value = editorForm.sortOrder.trim();
  if (!value) {
    return t('documentCategories.sortOrderInvalid');
  }
  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed)) {
    return t('documentCategories.sortOrderInvalid');
  }
  return '';
}

function parseSortOrder(): number {
  return Number.parseInt(editorForm.sortOrder.trim(), 10) || 0;
}

function applyFieldErrors(): boolean {
  fieldErrors.name = validateName();
  fieldErrors.sortOrder = validateSortOrder();
  return !fieldErrors.name && !fieldErrors.sortOrder;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allCategories.value = await listHostDocumentCategories();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentCategories.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { name: params.name ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { name: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingCategory.value = null;
  editorForm.name = '';
  editorForm.sortOrder = '0';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(category: HostDocumentCategory): void {
  if (changing.value) {
    return;
  }
  editorMode.value = 'edit';
  editingCategory.value = category;
  editorForm.name = category.name;
  editorForm.sortOrder = String(category.sortOrder);
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
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
    await createHostDocumentCategory(
      editorForm.name,
      null,
      parseSortOrder()
    );
    editorOpen.value = false;
    ElMessage.success(t('documentCategories.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const category = editingCategory.value;
  if (!canUpdate.value || !category) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostDocumentCategory(
      category.id,
      editorForm.name,
      category.parentId,
      parseSortOrder(),
      category.version
    );
    editorOpen.value = false;
    ElMessage.success(t('documentCategories.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(category: HostDocumentCategory): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('documentCategories.confirmDelete', { name: category.name }),
      t('documentCategories.delete'),
      {
        type: 'warning',
        confirmButtonText: t('documentCategories.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await deleteHostDocumentCategory(category.id, category.version);
    ElMessage.success(t('documentCategories.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentCategories.loadFailed' | 'documentCategories.operationFailed' = 'documentCategories.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_category_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="document-categories-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentCategories.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="1"
      :search-label="t('documentCategories.query')"
      :reset-label="t('documentCategories.reset')"
      :expand-label="t('documentCategories.expand')"
      :collapse-label="t('documentCategories.collapse')"
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
            <PermissionGate code="document.categories.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-category-create"
                @click="openCreate"
              >
                {{ t('documentCategories.addCategory') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedCategories.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedCategories"
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

            <el-table-column :label="t('documentCategories.name')" min-width="240">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.name.slice(0, 2).toUpperCase() }}</span>
                  <div class="art-crud-table-row__name" translate="no">{{ row.name }}</div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('sortOrder')"
              :label="t('documentCategories.sortOrder')"
              width="100"
              align="center"
              prop="sortOrder"
            />

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate code="document.categories.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-category-edit"
                      :title="t('documentCategories.edit')"
                      :disabled="changing"
                  @click="openEdit(row as HostDocumentCategory)"
                    />
                  </PermissionGate>
                  <PermissionGate code="document.categories.delete">
                    <ArtTableActionButton
                      type="delete"
                      test-id="document-category-delete"
                      :title="t('documentCategories.delete')"
                      :disabled="changing"
                  @click="remove(row as HostDocumentCategory)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('documentCategories.emptyDirectory') }}</template>
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
      :title="editorMode === 'create' ? t('documentCategories.createDialogTitle') : t('documentCategories.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-category-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="document-category-editor-form"
        :model="editorForm"
        label-width="96px"
        class="document-categories-editor-form"
      >
        <el-form-item
          :label="t('documentCategories.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            data-testid="document-category-name"
            :placeholder="t('documentCategories.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
        <el-form-item
          :label="t('documentCategories.sortOrder')"
          prop="sortOrder"
          required
          :error="fieldErrors.sortOrder || undefined"
        >
          <el-input
            v-model="editorForm.sortOrder"
            data-testid="document-category-sort-order"
            type="number"
            @update:model-value="fieldErrors.sortOrder = validateSortOrder()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.document-categories-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-categories-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-categories-editor-form {
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
