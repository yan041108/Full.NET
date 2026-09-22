<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Plus } from '@element-plus/icons-vue';
import {
  ElButton,
  ElCard,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTree
} from 'element-plus';
// 为避免 barrel 层重复标识符冲突，此处用新版 Response 类型别名旧名
import type {
  FullNetProblemDetails,
  HostDocumentItemResponse as HostDocumentItem,
  HostDocumentVersionResponse
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtClientPagination } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createDocumentItem,
  deleteDocumentItem,
  downloadDocumentContent,
  listDocumentItems,
  listDocumentVersions,
  openDocumentBlob,
  previewDocumentContent,
  restoreDocumentItem,
  rollbackDocumentVersion,
  deleteDocumentVersion,
  updateDocumentItem,
  uploadDocumentVersion
} from '../api/host-document-items';
import { createDocumentPreviewTask } from '../api/document-preview-tasks';
import { listDocumentCategories } from '../api/host-document-categories';
import type { HostDocumentCategoryResponse, HostDocumentShareResponse } from '@fullnet/client-contracts';
import DocumentShareCreateDialog from '../components/DocumentShareCreateDialog.vue';
import { listDocumentTags, type HostDocumentTagResponse } from '../api/host-document-tags';

defineOptions({ name: 'HostDocumentItemsView' });

interface CategoryTreeNode {
  id: string;
  label: string;
  children?: CategoryTreeNode[];
}

interface AppliedFilters {
  title: string;
}

interface DeletedDocumentEntry {
  item: HostDocumentItem;
  restoreVersion: number;
}

const session = useSessionStore();
const router = useRouter();
const { t } = useAdminI18n();
const items = ref<HostDocumentItem[]>([]);
const createDialogOpen = ref(false);
const createTitle = ref('');
const createDescription = ref('');
const createTitleError = ref('');
const editTitle = ref('');
const editDescription = ref('');
const editTitleError = ref('');
const hotTags = ref<HostDocumentTagResponse[]>([]);
const allTags = ref<HostDocumentTagResponse[]>([]);
const createTagIds = ref<string[]>([]);
const editTagIds = ref<string[]>([]);
const selectedTagFilterId = ref<string | null>(null);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const versionFile = ref<File | null>(null);
const versionTargetId = ref<string>();
const versionHistoryVisible = ref(false);
const versionHistoryItem = ref<HostDocumentItem>();
const versionHistory = ref<HostDocumentVersionResponse[]>([]);
const versionHistoryLoading = ref(false);
const editingId = ref<string>();
const recentlyDeleted = ref<DeletedDocumentEntry[]>([]);
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '' });
const categories = ref<HostDocumentCategoryResponse[]>([]);
const selectedCategoryId = ref<string | null>(null);
const ALL_CATEGORIES_NODE_ID = '__all__';
const canCreate = computed(() => session.can('document.host_documents.create'));
const canUpdate = computed(() => session.can('document.host_documents.update'));
const canAddVersion = computed(() => session.can('document.host_documents.add_version'));
const canDelete = computed(() => session.can('document.host_documents.delete'));
const canRestore = computed(() => session.can('document.host_documents.restore'));
const canRollbackVersion = computed(() => session.can('document.host_documents.rollback_version'));
const canDeleteVersion = computed(() => session.can('document.host_documents.delete_version'));
const canDownload = computed(() => session.can('document.host_documents.download'));
const canCreatePreviewTask = computed(() => session.can('document.host_preview_tasks.create'));
const canCreateShare = computed(() => session.can('document.host_shares.create'));
const canRead = computed(() => session.can('document.host_documents.read'));
const shareDialogOpen = ref(false);
const shareTarget = ref<HostDocumentItem | null>(null);
const shareBatchTargets = ref<HostDocumentItem[]>([]);
const selectedItems = ref<HostDocumentItem[]>([]);
const editingItem = computed(() => items.value.find(entry => entry.id === editingId.value));

const editDialogOpen = computed({
  get: () => editingId.value !== undefined,
  set: (open: boolean) => {
    if (!open) {
      cancelEdit();
    }
  }
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

const categoryTree = computed<CategoryTreeNode[]>(() => {
  const nodes = new Map<string, CategoryTreeNode>();
  for (const category of categories.value) {
    nodes.set(category.id, { id: category.id, label: category.name, children: [] });
  }
  const roots: CategoryTreeNode[] = [];
  for (const category of categories.value) {
    const node = nodes.get(category.id);
    if (!node) {
      continue;
    }
    if (category.parentId && nodes.has(category.parentId)) {
      nodes.get(category.parentId)?.children?.push(node);
    } else {
      roots.push(node);
    }
  }
  const pruneEmptyChildren = (node: CategoryTreeNode) => {
    if (node.children?.length === 0) {
      delete node.children;
    } else {
      node.children?.forEach(pruneEmptyChildren);
    }
  };
  roots.forEach(pruneEmptyChildren);
  return [
    { id: ALL_CATEGORIES_NODE_ID, label: t('hostDocumentItems.allCategories') },
    ...roots
  ];
});

const filteredItems = computed(() => {
  let list = items.value;
  if (selectedCategoryId.value) {
    list = list.filter(item => item.categoryId === selectedCategoryId.value);
  }
  const keyword = appliedFilters.value.title.trim().toLowerCase();
  if (!keyword) {
    return list;
  }
  return list.filter(item => item.title.toLowerCase().includes(keyword));
});

const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

const deletedItems = computed(() => recentlyDeleted.value.map(entry => entry.item));
const {
  page: deletedPage,
  pageSize: deletedPageSize,
  total: deletedTotal,
  pagedItems: pagedDeleted
} = useArtClientPagination(deletedItems);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'title',
    label: t('hostDocumentItems.titleLabel'),
    placeholder: t('hostDocumentItems.searchPlaceholder')
  }
]);

watch([filteredItems, page, pageSize], () => {
  void syncTableLayout();
});

onMounted(() => {
  void load();
  void loadCategories();
  void loadHotTags();
  void loadAllTags();
});

async function loadHotTags(): Promise<void> {
  try {
    hotTags.value = await listDocumentTags({ isHot: true });
  } catch {
    hotTags.value = [];
  }
}

async function loadAllTags(): Promise<void> {
  try {
    allTags.value = await listDocumentTags();
  } catch {
    allTags.value = [];
  }
}

function toggleTagFilter(tagId: string): void {
  selectedTagFilterId.value = selectedTagFilterId.value === tagId ? null : tagId;
  resetPage();
  void load();
}

function clearTagFilter(): void {
  selectedTagFilterId.value = null;
  resetPage();
  void load();
}

async function loadCategories(): Promise<void> {
  try {
    categories.value = await listDocumentCategories();
  } catch {
    categories.value = [];
  }
}

function onCategorySelected(node: CategoryTreeNode): void {
  selectedCategoryId.value = node.id === ALL_CATEGORIES_NODE_ID ? null : node.id;
  resetPage();
  void syncTableLayout();
}

function openShareDialog(item: HostDocumentItem): void {
  shareBatchTargets.value = [];
  shareTarget.value = item;
  shareDialogOpen.value = true;
}

function openBatchShareDialog(): void {
  if (selectedItems.value.length === 0) {
    return;
  }
  shareTarget.value = null;
  shareBatchTargets.value = [...selectedItems.value];
  shareDialogOpen.value = true;
}

function onTableSelectionChange(rows: HostDocumentItem[]): void {
  selectedItems.value = rows;
}

const DOCUMENT_SHARE_BATCH_HINT_KEY = 'documentShares.recentBatch';

function onShareBatchCreated(succeeded: number, total: number): void {
  ElMessage.success(t('documentShares.batchCreateResult', { succeeded, total }));
  try {
    sessionStorage.setItem(
      DOCUMENT_SHARE_BATCH_HINT_KEY,
      JSON.stringify({ succeeded, total })
    );
  } catch {
    // 忽略 sessionStorage 不可用场景
  }
  shareBatchTargets.value = [];
  selectedItems.value = [];
  void router.push({ name: 'document-shares' });
}

async function onShareCreated(_share: HostDocumentShareResponse, shareUrl: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(shareUrl);
    ElMessage.success(t('documentShares.createdWithLink'));
  } catch {
    ElMessage.success(t('documentShares.createSuccess'));
  }
  shareTarget.value = null;
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function deletedRowIndex(index: number): number {
  return (deletedPage.value - 1) * deletedPageSize.value + index + 1;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const pageResult = await listDocumentItems(1, 100, {
      tagId: selectedTagFilterId.value
    });
    items.value = pageResult.items;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { title: params.title ?? '' };
  resetPage();
  void syncTableLayout();
}

function resetSearch(): void {
  appliedFilters.value = { title: '' };
  resetPage();
  void syncTableLayout();
}

async function confirmEdit(): Promise<void> {
  const item = editingItem.value;
  if (item) {
    await saveEdit(item);
  }
}

function openCreate(): void {
  createTitle.value = '';
  createDescription.value = '';
  createTitleError.value = '';
  createTagIds.value = [];
  createDialogOpen.value = true;
}

function cancelCreate(): void {
  createDialogOpen.value = false;
  createTitle.value = '';
  createDescription.value = '';
  createTitleError.value = '';
  createTagIds.value = [];
}

function applyTitleConflictError(
  error: unknown,
  target: 'create' | 'edit'
): boolean {
  if (!isFullNetProblemDetails(error) || error.code !== 'document.host_document.title_conflict') {
    return false;
  }
  const message = t('hostDocumentItems.titleConflict');
  if (target === 'create') {
    createTitleError.value = message;
    createDialogOpen.value = true;
  } else {
    editTitleError.value = message;
  }
  return true;
}

function applyInvalidTagError(error: unknown): boolean {
  if (!isFullNetProblemDetails(error) || error.code !== 'document.host_tag.invalid') {
    return false;
  }
  ElMessage.error(t('hostDocumentItems.invalidTag'));
  return true;
}

async function confirmCreate(): Promise<void> {
  await create();
}

async function create(): Promise<void> {
  if (changing.value || !canCreate.value || !createTitle.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentItem(createTitle.value.trim(), createDescription.value.trim() || null, {
      categoryId: selectedCategoryId.value,
      tagIds: createTagIds.value
    });
    createDialogOpen.value = false;
    createTitle.value = '';
    createDescription.value = '';
    createTagIds.value = [];
    ElMessage.success(t('hostDocumentItems.createSuccess'));
    await load();
  } catch (error: unknown) {
    if (!applyTitleConflictError(error, 'create') && !applyInvalidTagError(error)) {
      problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

function onVersionFileSelected(event: Event, itemId: string): void {
  const input = event.target as HTMLInputElement;
  versionFile.value = input.files?.[0] ?? null;
  versionTargetId.value = itemId;
}

async function uploadVersion(item: HostDocumentItem): Promise<void> {
  if (
    changing.value
    || !canAddVersion.value
    || !versionFile.value
    || versionTargetId.value !== item.id
  ) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await uploadDocumentVersion(item.id, versionFile.value);
    versionFile.value = null;
    versionTargetId.value = undefined;
    ElMessage.success(t('hostDocumentItems.versionSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(item: HostDocumentItem): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostDocumentItems.confirmDelete', { name: item.title }),
      t('hostDocumentItems.delete'),
      { type: 'warning', confirmButtonText: t('hostDocumentItems.delete'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await deleteDocumentItem(item.id, item.version);
    recentlyDeleted.value = [
      { item, restoreVersion: item.version + 1 },
      ...recentlyDeleted.value.filter(entry => entry.item.id !== item.id)
    ];
    ElMessage.success(t('hostDocumentItems.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function startEdit(item: HostDocumentItem): void {
  editingId.value = item.id;
  editTitle.value = item.title;
  editDescription.value = item.description ?? '';
  editTitleError.value = '';
  editTagIds.value = item.tags?.map(tag => tag.tagId) ?? [];
}

function cancelEdit(): void {
  editingId.value = undefined;
  editTitle.value = '';
  editDescription.value = '';
  editTitleError.value = '';
  editTagIds.value = [];
}

async function saveEdit(item: HostDocumentItem): Promise<void> {
  if (changing.value || !canUpdate.value || editingId.value !== item.id) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateDocumentItem(item, editTitle.value.trim(), editDescription.value.trim() || null, {
      tagIds: editTagIds.value
    });
    editingId.value = undefined;
    editTitle.value = '';
    editDescription.value = '';
    editTagIds.value = [];
    ElMessage.success(t('hostDocumentItems.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (!applyTitleConflictError(error, 'edit') && !applyInvalidTagError(error)) {
      problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

async function restoreDeleted(entry: DeletedDocumentEntry): Promise<void> {
  if (changing.value || !canRestore.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await restoreDocumentItem(entry.item.id, entry.restoreVersion);
    recentlyDeleted.value = recentlyDeleted.value.filter(
      candidate => candidate.item.id !== entry.item.id
    );
    ElMessage.success(t('hostDocumentItems.restoreSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function downloadFile(itemId: string): Promise<void> {
  if (changing.value || !canDownload.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const blob = await downloadDocumentContent(itemId);
    openDocumentBlob(blob);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function previewFile(item: HostDocumentItem, versionId?: string): Promise<void> {
  if (changing.value || !canRead.value || !item.currentVersion) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const blob = await previewDocumentContent(item.id, versionId);
    openDocumentBlob(blob);
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error) && error.code === 'document.host_document.preview_not_supported') {
      ElMessage.warning(t('hostDocumentItems.previewNotSupported'));
      return;
    }
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function canSubmitOfficePreview(item: HostDocumentItem): boolean {
  return Boolean(item.currentVersion);
}

async function submitOfficePreviewTask(item: HostDocumentItem): Promise<void> {
  if (changing.value || !canCreatePreviewTask.value || !canSubmitOfficePreview(item)) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentPreviewTask({
      documentItemId: item.id,
      versionId: item.currentVersion?.id ?? null
    });
    ElMessage.success(t('hostDocumentItems.officePreviewSubmitted'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openVersionHistory(item: HostDocumentItem): Promise<void> {
  versionHistoryItem.value = item;
  versionHistoryVisible.value = true;
  versionHistoryLoading.value = true;
  versionHistory.value = [];
  try {
    versionHistory.value = await listDocumentVersions(item.id);
  } catch (error: unknown) {
    versionHistoryVisible.value = false;
    problem.value = toProblem(error, 'hostDocumentItems.versionHistoryLoadFailed');
  } finally {
    versionHistoryLoading.value = false;
  }
}

function isCurrentVersion(item: HostDocumentItem, version: unknown): boolean {
  return typeof version === 'object'
    && version !== null
    && 'id' in version
    && item.currentVersion?.id === version.id;
}

async function rollbackVersion(version: HostDocumentVersionResponse): Promise<void> {
  const item = versionHistoryItem.value;
  if (!item || changing.value || !canRollbackVersion.value || isCurrentVersion(item, version)) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostDocumentItems.confirmRollback', { version: version.versionNumber }),
      t('hostDocumentItems.rollbackVersion'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const updated = await rollbackDocumentVersion(item.id, version.id, item.version);
    versionHistoryItem.value = updated;
    const index = items.value.findIndex(entry => entry.id === item.id);
    if (index >= 0) {
      items.value[index] = updated;
    }
    versionHistory.value = await listDocumentVersions(item.id);
    ElMessage.success(t('hostDocumentItems.rollbackSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function deleteVersion(version: HostDocumentVersionResponse): Promise<void> {
  const item = versionHistoryItem.value;
  if (!item || changing.value || !canDeleteVersion.value || isCurrentVersion(item, version)) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostDocumentItems.confirmDeleteVersion', { version: version.versionNumber }),
      t('hostDocumentItems.deleteVersion'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const updated = await deleteDocumentVersion(item.id, version.id, item.version);
    versionHistoryItem.value = updated;
    const index = items.value.findIndex(entry => entry.id === item.id);
    if (index >= 0) {
      items.value[index] = updated;
    }
    versionHistory.value = await listDocumentVersions(item.id);
    ElMessage.success(t('hostDocumentItems.deleteVersionSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function findDeletedEntry(item: HostDocumentItem): DeletedDocumentEntry | undefined {
  return recentlyDeleted.value.find(entry => entry.item.id === item.id);
}

function toProblem(
  error: unknown,
  fallbackKey:
    | 'hostDocumentItems.loadFailed'
    | 'hostDocumentItems.operationFailed'
    | 'hostDocumentItems.versionHistoryLoadFailed' = 'hostDocumentItems.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_item_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-document-items-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostDocumentItems.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="document-module-split">
      <el-card shadow="never" class="document-module-split__aside">
        <h2 class="host-document-items-view__tree-title">{{ t('hostDocumentItems.categoryTreeTitle') }}</h2>
        <el-tree
          :data="categoryTree"
          node-key="id"
          default-expand-all
          highlight-current
          :current-node-key="selectedCategoryId ?? ALL_CATEGORIES_NODE_ID"
          @node-click="onCategorySelected"
        />
      </el-card>

      <div class="document-module-split__main">
        <el-card class="document-module-query-card" shadow="never">
          <ArtSearchBar
            v-model="searchForm"
            :items="searchItems"
            :default-visible-count="1"
            :show-expand="false"
            :search-label="t('hostDocumentItems.query')"
            :reset-label="t('hostDocumentItems.reset')"
            @search="handleSearch"
            @reset="resetSearch"
          />
          <div v-if="hotTags.length || selectedTagFilterId" class="host-document-items-view__hot-tags">
            <span class="host-document-items-view__hot-tags-label">{{ t('hostDocumentItems.filterByTag') }}</span>
            <el-tag
              v-for="tag in hotTags"
              :key="tag.id"
              size="small"
              :type="selectedTagFilterId === tag.id ? 'primary' : 'danger'"
              :effect="selectedTagFilterId === tag.id ? 'dark' : 'plain'"
              class="host-document-items-view__hot-tag"
              translate="no"
              @click="toggleTagFilter(tag.id)"
            >
              {{ tag.name }}
            </el-tag>
            <el-button
              v-if="selectedTagFilterId"
              link
              type="primary"
              size="small"
              @click="clearTagFilter"
            >
              {{ t('hostDocumentItems.clearTagFilter') }}
            </el-button>
          </div>
        </el-card>

        <el-card class="art-table-card art-full-height" shadow="never">
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
            <PermissionGate code="document.host_documents.create">
              <el-button
                type="primary"
                :icon="Plus"
                data-testid="host-document-item-create"
                :disabled="changing"
                @click="openCreate"
              >
                {{ t('hostDocumentItems.create') }}
              </el-button>
            </PermissionGate>
            <PermissionGate v-if="canCreateShare" code="document.host_shares.create">
              <el-button
                plain
                data-testid="host-document-item-batch-share"
                :disabled="changing || selectedItems.length === 0"
                @click="openBatchShareDialog"
              >
                {{ t('documentShares.batchShare') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedItems.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedItems"
            row-key="id"
            @selection-change="onTableSelectionChange"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <el-table-column
              v-if="canCreateShare"
              type="selection"
              width="48"
              reserve-selection
            />
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('hostDocumentItems.titleLabel')" min-width="200">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.title.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.title }}</div>
                    <div v-if="row.description" class="art-crud-table-row__sub" translate="no">{{ row.description }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('hostDocumentItems.versionLabel')" width="120" align="center">
              <template #default="{ row }">
                {{ row.currentVersion?.versionNumber ?? '—' }}
              </template>
            </el-table-column>

            <el-table-column :label="t('hostDocumentItems.tagsLabel')" min-width="160">
              <template #default="{ row }">
                <span v-if="!row.tags?.length" class="host-document-items-view__no-tags">—</span>
                <div v-else class="host-document-items-view__row-tags">
                  <el-tag
                    v-for="tag in row.tags"
                    :key="tag.tagId"
                    size="small"
                    translate="no"
                  >
                    {{ tag.tagName }}
                  </el-tag>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('users.columnActions')" width="580" fixed="right" align="center">
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate v-if="row.currentVersion" code="document.host_documents.read">
                    <el-button plain size="small" data-testid="host-document-item-preview" :disabled="changing" @click="previewFile(row as HostDocumentItem)">
                      {{ t('hostDocumentItems.preview') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="document.host_documents.read">
                    <el-button plain size="small" data-testid="host-document-item-version-history" :disabled="changing" @click="openVersionHistory(row as HostDocumentItem)">
                      {{ t('hostDocumentItems.versionHistory') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="row.currentVersion" code="document.host_documents.download">
                    <el-button plain size="small" data-testid="host-document-item-download" :disabled="changing" @click="downloadFile(row.id)">
                      {{ t('hostDocumentItems.download') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="canSubmitOfficePreview(row as HostDocumentItem)" code="document.host_preview_tasks.create">
                    <el-button
                      plain
                      size="small"
                      data-testid="host-document-item-create-preview-task"
                      :disabled="changing"
                      @click="submitOfficePreviewTask(row as HostDocumentItem)"
                    >
                      {{ t('hostDocumentItems.submitOfficePreview') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="document.host_documents.update">
              <el-button plain size="small" data-testid="host-document-item-edit" @click="startEdit(row as HostDocumentItem)">
                      {{ t('hostDocumentItems.edit') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="document.host_shares.create">
                    <el-button
                      plain
                      size="small"
                      data-testid="host-document-item-share"
                      :disabled="changing"
                      @click="openShareDialog(row as HostDocumentItem)"
                    >
                      {{ t('hostDocumentItems.share') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate v-if="canAddVersion" code="document.host_documents.add_version">
                    <label class="host-document-items-view__version-input">
                      <span class="art-sr-heading">{{ t('hostDocumentItems.chooseVersionFile') }}</span>
                      <input type="file" data-testid="host-document-item-version-file" @change="onVersionFileSelected($event, row.id)" />
                    </label>
                    <el-button
                      plain
                      size="small"
                      data-testid="host-document-item-upload-version"
                      :disabled="changing || !versionFile || versionTargetId !== row.id"
                  @click="uploadVersion(row as HostDocumentItem)"
                    >
                      {{ t('hostDocumentItems.uploadVersion') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="document.host_documents.delete">
              <el-button type="danger" plain size="small" data-testid="host-document-item-delete" :disabled="changing" @click="remove(row as HostDocumentItem)">
                      {{ t('hostDocumentItems.delete') }}
                    </el-button>
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('hostDocumentItems.emptyDirectory') }}</template>
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

    <el-card v-if="recentlyDeleted.length && canRestore" class="art-table-card host-document-items-view__deleted" shadow="never">
      <template #header>
        <h2>{{ t('hostDocumentItems.recentlyDeletedTitle') }}</h2>
      </template>

      <div class="art-table" :class="{ 'is-empty': pagedDeleted.length === 0 }">
        <el-table
          :data="pagedDeleted"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          class="art-crud-data-table"
        >
          <el-table-column :label="t('users.columnIndex')" width="72" align="center">
            <template #default="{ $index }">{{ deletedRowIndex($index) }}</template>
          </el-table-column>

          <el-table-column :label="t('hostDocumentItems.titleLabel')" min-width="200">
            <template #default="{ row }">
              <strong translate="no">{{ row.title }}</strong>
            </template>
          </el-table-column>

          <el-table-column :label="t('users.columnActions')" width="120" align="center">
            <template #default="{ row }">
              <PermissionGate code="document.host_documents.restore">
                <el-button
                  plain
                  size="small"
                  data-testid="host-document-item-restore"
                  :disabled="changing"
                    @click="findDeletedEntry(row as HostDocumentItem) && restoreDeleted(findDeletedEntry(row as HostDocumentItem)!)"
                >
                  {{ t('hostDocumentItems.restore') }}
                </el-button>
              </PermissionGate>
            </template>
          </el-table-column>
        </el-table>

        <el-pagination
          v-model:current-page="deletedPage"
          v-model:page-size="deletedPageSize"
          class="art-table-pagination center custom-pagination"
          :total="deletedTotal"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
        />
      </div>
    </el-card>
      </div>
    </div>

    <ArtFormDialog
      v-if="canCreate"
      v-model:open="createDialogOpen"
      :title="t('hostDocumentItems.createTitle')"
      :saving="changing"
      :confirm-label="t('hostDocumentItems.create')"
      :cancel-label="t('hostDocumentItems.cancel')"
      confirm-test-id="host-document-item-create-submit"
      @confirm="confirmCreate"
      @cancel="cancelCreate"
    >
      <el-form label-width="96px" class="host-document-items-view__editor-form" @submit.prevent>
        <el-form-item
          :label="t('hostDocumentItems.titleLabel')"
          required
          :error="createTitleError || undefined"
        >
          <el-input
            v-model="createTitle"
            data-testid="host-document-item-title"
            :placeholder="t('hostDocumentItems.titlePlaceholder')"
            @update:model-value="createTitleError = ''"
          />
        </el-form-item>
        <el-form-item :label="t('hostDocumentItems.descriptionLabel')">
          <el-input
            v-model="createDescription"
            data-testid="host-document-item-description"
            type="textarea"
            :rows="3"
            :placeholder="t('hostDocumentItems.descriptionPlaceholder')"
          />
        </el-form-item>
        <el-form-item :label="t('hostDocumentItems.tagsLabel')">
          <el-select
            v-model="createTagIds"
            data-testid="host-document-item-create-tags"
            multiple
            filterable
            collapse-tags
            collapse-tags-tooltip
            :placeholder="t('hostDocumentItems.tagsPlaceholder')"
            class="host-document-items-view__tag-select"
          >
            <el-option
              v-for="tag in allTags"
              :key="tag.id"
              :label="tag.name"
              :value="tag.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-if="canUpdate"
      v-model:open="editDialogOpen"
      :title="t('hostDocumentItems.editTitle')"
      :saving="changing"
      :confirm-label="t('hostDocumentItems.save')"
      :cancel-label="t('hostDocumentItems.cancel')"
      confirm-test-id="host-document-item-save"
      @confirm="confirmEdit"
      @cancel="cancelEdit"
    >
      <el-form label-width="96px" class="host-document-items-view__editor-form" @submit.prevent>
        <el-form-item
          :label="t('hostDocumentItems.titleLabel')"
          required
          :error="editTitleError || undefined"
        >
          <el-input
            v-model="editTitle"
            data-testid="host-document-item-edit-title"
            @update:model-value="editTitleError = ''"
          />
        </el-form-item>
        <el-form-item :label="t('hostDocumentItems.descriptionLabel')">
          <el-input v-model="editDescription" data-testid="host-document-item-edit-description" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item :label="t('hostDocumentItems.tagsLabel')">
          <el-select
            v-model="editTagIds"
            data-testid="host-document-item-edit-tags"
            multiple
            filterable
            collapse-tags
            collapse-tags-tooltip
            :placeholder="t('hostDocumentItems.tagsPlaceholder')"
            class="host-document-items-view__tag-select"
          >
            <el-option
              v-for="tag in allTags"
              :key="tag.id"
              :label="tag.name"
              :value="tag.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <el-drawer
      v-model="versionHistoryVisible"
      :title="t('hostDocumentItems.versionHistoryTitle')"
      size="520px"
      data-testid="host-document-item-version-drawer"
    >
      <el-table
        v-loading="versionHistoryLoading"
        :data="versionHistory"
        size="small"
        class="art-crud-data-table"
      >
        <el-table-column :label="t('hostDocumentItems.versionLabel')" width="100" align="center">
          <template #default="{ row }">
            <span translate="no">{{ row.versionNumber }}</span>
            <el-tag
              v-if="versionHistoryItem && isCurrentVersion(versionHistoryItem, row)"
              size="small"
              type="success"
              class="host-document-items-view__current-tag"
            >
              {{ t('hostDocumentItems.currentVersionTag') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('hostDocumentItems.changeDescription')" min-width="160">
          <template #default="{ row }">
            <span translate="no">{{ row.changeDescription ?? '—' }}</span>
          </template>
        </el-table-column>
        <!-- @vue-generic {HostDocumentVersionResponse} -->
          <el-table-column :label="t('users.columnActions')" width="280" align="center">
          <template #default="{ row }">
            <PermissionGate code="document.host_documents.read">
              <el-button
                plain
                size="small"
                data-testid="host-document-item-version-preview"
                :disabled="changing || !versionHistoryItem"
                @click="versionHistoryItem && previewFile(versionHistoryItem, row.id)"
              >
                {{ t('hostDocumentItems.preview') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="document.host_documents.rollback_version">
              <el-button
                v-if="versionHistoryItem && !isCurrentVersion(versionHistoryItem, row)"
                plain
                size="small"
                type="warning"
                data-testid="host-document-item-version-rollback"
                :disabled="changing"
                @click="rollbackVersion(row)"
              >
                {{ t('hostDocumentItems.rollbackVersion') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="document.host_documents.delete_version">
              <el-button
                v-if="versionHistoryItem && !isCurrentVersion(versionHistoryItem, row)"
                plain
                size="small"
                type="danger"
                data-testid="host-document-item-version-delete"
                :disabled="changing"
                @click="deleteVersion(row)"
              >
                {{ t('hostDocumentItems.deleteVersion') }}
              </el-button>
            </PermissionGate>
          </template>
        </el-table-column>
      </el-table>
    </el-drawer>

    <DocumentShareCreateDialog
      v-if="canCreateShare"
      v-model:open="shareDialogOpen"
      :preset-document="shareTarget"
      :preset-documents="shareBatchTargets.length > 1 ? shareBatchTargets : null"
      @created="onShareCreated"
      @batch-created="onShareBatchCreated"
    />
  </section>
</template>

<style scoped>
.host-document-items-view {
  flex: 1;
  min-height: 0;
}

.host-document-items-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.host-document-items-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.host-document-items-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.host-document-items-view__deleted {
  flex: none;
}

.host-document-items-view__tree-title {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
}

.host-document-items-view .document-module-split__main {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.host-document-items-view__version-input {
  display: inline-flex;
  align-items: center;
}

.host-document-items-view__hot-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-top: 12px;
}

.host-document-items-view__hot-tags-label {
  font-size: 13px;
  color: var(--art-gray-600);
}

.host-document-items-view__hot-tag {
  cursor: pointer;
}

.host-document-items-view__row-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.host-document-items-view__no-tags {
  color: var(--art-gray-500);
}

.host-document-items-view__tag-select {
  width: 100%;
}
</style>
