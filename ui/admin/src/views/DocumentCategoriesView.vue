<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElTable,
  ElTableColumn
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
// 为避免 barrel 层重复标识符冲突，此处用新版 Response 类型别名旧名
import type { FullNetProblemDetails, HostDocumentCategoryResponse as HostDocumentCategory } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtClientPagination } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { showSuccess, showProblem, showWarning } from '../feedback/fullNetMessage';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createDocumentCategory,
  deleteDocumentCategory,
  listDocumentCategories,
  updateDocumentCategory
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
// 删除确认由本页持有，撤权与离页同步回收，避免独立弹窗泄露旧目录名称。
const confirmation = ref<{ message: string; resolve: (confirmed: boolean) => void }>();
function finishConfirmation(confirmed: boolean): void {
  const pending = confirmation.value;
  confirmation.value = undefined;
  pending?.resolve(confirmed);
}
const editorMode = ref<EditorMode>('create');
const editingCategory = ref<HostDocumentCategory | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  name: '',
  sortOrder: '0',
  code: null as string | null,
  icon: null as string | null,
  color: null as string | null,
  description: null as string | null
});
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
  syncTableLayout
} = useArtPagedTableInCard(loading);

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

const canRead = computed(() => session.currentUser?.scope === 'host' && session.can('document.categories.read'));
const canCreate = computed(() => canRead.value && session.can('document.categories.create'));
const canUpdate = computed(() => canRead.value && session.can('document.categories.update'));
const canDelete = computed(() => canRead.value && session.can('document.categories.delete'));
const scope = useAuthorizedViewScope(session, () => {
  finishConfirmation(false); closeEditor(); allCategories.value = []; loading.value = false; changing.value = false;
  problem.value = undefined; searchForm.value = {};
  appliedFilters.value = { name: '' };
  resetPage(); pageSize.value = 20;
}, () => { if (canRead.value) void load(); });
let listRequest: ReturnType<typeof scope.begin>;
let editorRequest: ReturnType<typeof scope.begin>;
const editorModel = computed({ get: () => editorOpen.value, set: open => { if (!open) closeEditor(); } });
function closeEditor(): void {
  editorRequest?.cancel();
  if (editorRequest) changing.value = false;
  editorRequest = undefined; editorOpen.value = false; editingCategory.value = null;
  Object.assign(editorForm, { name: '', sortOrder: '0', code: null, icon: null, color: null, description: null }); clearFieldErrors();
}

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
  const parsed = Number(value);
  if (!/^[+-]?\d+$/.test(value) || !Number.isInteger(parsed) || parsed < -2147483648 || parsed > 2147483647) {
    return t('documentCategories.sortOrderInvalid');
  }
  return '';
}

function parseSortOrder(): number {
  return Number(editorForm.sortOrder.trim());
}

function applyFieldErrors(): boolean {
  fieldErrors.name = validateName();
  fieldErrors.sortOrder = validateSortOrder();
  return !fieldErrors.name && !fieldErrors.sortOrder;
}

async function load(): Promise<void> {
  listRequest?.cancel();
  if (!canRead.value) return;
  const request = scope.begin('document.categories.read');
  if (!request) return;
  listRequest = request; loading.value = true; problem.value = undefined;
  try {
    const rows = await listDocumentCategories(request.signal);
    if (request.current()) allCategories.value = rows;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'documentCategories.loadFailed');
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); }
    request.finish();
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { name: params.name ?? '' };
  resetPage();
  void syncTableLayout();
}

function resetSearch(): void {
  appliedFilters.value = { name: '' };
  resetPage();
}

function openCreate(): void {
  if (changing.value || !canCreate.value) return;
  closeEditor();
  editorMode.value = 'create';
  editingCategory.value = null;
  editorForm.name = '';
  editorForm.sortOrder = '0';
  editorForm.code = null;
  editorForm.icon = null;
  editorForm.color = null;
  editorForm.description = null;
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(category: HostDocumentCategory): void {
  if (changing.value || !canUpdate.value) {
    return;
  }
  editorMode.value = 'edit';
  editingCategory.value = category;
  editorForm.name = category.name;
  editorForm.sortOrder = String(category.sortOrder);
  editorForm.code = category.code;
  editorForm.icon = category.icon;
  editorForm.color = category.color;
  editorForm.description = category.description;
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value || !editorOpen.value || !(editorMode.value === 'create' ? canCreate.value : canUpdate.value)) {
    return;
  }
  editorForm.name = editorForm.name.trim();
  if (!applyFieldErrors()) {
    showWarning(fieldErrors.name || fieldErrors.sortOrder);
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
  const request = scope.begin('document.categories.create');
  if (!request) return;
  editorRequest = request;
  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentCategory(
      editorForm.name,
      null,
      parseSortOrder(),
      editorForm.code ?? null,
      editorForm.icon ?? null,
      editorForm.color ?? null,
      editorForm.description ?? null,
      request.signal
    );
    if (!request.current()) return;
    closeEditor();
    showSuccess(t('documentCategories.createSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentCategories.operationFailed'), t('documentCategories.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

async function saveEdit(): Promise<void> {
  const category = editingCategory.value;
  if (!canUpdate.value || !category) {
    return;
  }
  const request = scope.begin('document.categories.update');
  if (!request) return;
  editorRequest = request;
  changing.value = true;
  problem.value = undefined;
  try {
    await updateDocumentCategory(
      category.id,
      editorForm.name,
      category.parentId,
      parseSortOrder(),
      editorForm.code ?? null,
      editorForm.icon ?? null,
      editorForm.color ?? null,
      editorForm.description ?? null,
      category.version,
      request.signal
    );
    if (!request.current()) return;
    closeEditor();
    showSuccess(t('documentCategories.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentCategories.operationFailed'), t('documentCategories.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

async function remove(category: HostDocumentCategory): Promise<void> {
  if (changing.value || !canDelete.value) return;
  const request = scope.begin('document.categories.delete');
  if (!request) return;
  // 确认等待本身也是在途操作，不能再次弹窗或借旧权限进入写入。
  changing.value = true;
  const id = category.id; const version = category.version;
  try {
    const confirmed = await new Promise<boolean>(resolve => {
      confirmation.value = { message: t('documentCategories.confirmDelete', { name: category.name }), resolve };
    });
    if (!confirmed || !request.current()) return;
    const removed = await deleteDocumentCategory(id, version, request.signal);
    if (!request.current()) return;
    if (!removed) throw new Error('client.document_category_delete_failed');
    showSuccess(t('documentCategories.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentCategories.operationFailed'), t('documentCategories.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
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
  <section v-if="canRead" class="document-categories-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentCategories.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card class="document-module-query-card" shadow="never">
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
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
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
                <ArtTableActionGroup>
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
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('documentCategories.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
        />
      </div>
    </el-card>

    <ArtFormDialog
      v-if="confirmation"
      :open="true"
      :title="t('documentCategories.delete')"
      :confirm-label="t('documentCategories.delete')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-category-delete-confirm"
      @confirm="finishConfirmation(true)"
      @update:open="open => { if (!open) finishConfirmation(false); }"
    >
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="editorModel"
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
        :disabled="changing"
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

.document-categories-view {
  flex: 1;
  min-height: 0;
}

.document-categories-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-categories-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
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
