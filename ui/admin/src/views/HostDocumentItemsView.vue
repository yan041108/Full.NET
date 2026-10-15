<script setup lang="ts">
import { computed, onActivated, onDeactivated, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Plus } from '@element-plus/icons-vue';
import {
  ElButton,
  ElCard,
  ElDrawer,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
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
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useSessionStore } from '../auth/session';
import { showSuccess } from '../feedback/fullNetMessage';
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

const viewActive = ref(true);
onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; });
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
let confirmationSequence = 0;
const confirmation = ref<{ token: number; title: string; message: string; confirmLabel: string;
  confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
let settleConfirmation: ((confirmed: boolean) => void) | undefined;
function resolveConfirmation(confirmed: boolean, token = confirmation.value?.token): void {
  if (!confirmation.value || confirmation.value.token !== token) return;
  const settle = settleConfirmation; settleConfirmation = undefined; confirmation.value = undefined; settle?.(confirmed);
}
function askConfirmation(title: string, message: string, confirmLabel = t('common.confirm')): Promise<boolean> {
  // 仅清理本页面的确认，不关闭其它组件的全局消息框；失效时同时结束等待。
  resolveConfirmation(false);
  return new Promise(resolve => {
    const token = ++confirmationSequence; settleConfirmation = resolve;
    const dismiss = () => resolveConfirmation(false, token);
    confirmation.value = { token, title, message, confirmLabel, confirm: () => resolveConfirmation(true, token),
      dismiss, update: open => { if (!open) dismiss(); } };
  });
}
const versionFile = ref<File | null>(null);
const versionTargetId = ref<string>();
const versionHistoryVisible = ref(false);
const versionHistoryItem = ref<HostDocumentItem>();
const versionHistory = ref<HostDocumentVersionResponse[]>([]);
const versionHistoryLoading = ref(false);
const editingId = ref<string>();
const editingVersion = ref<number>();
const recentlyDeleted = ref<DeletedDocumentEntry[]>([]);
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '' });
const categories = ref<HostDocumentCategoryResponse[]>([]);
const selectedCategoryId = ref<string | null>(null);
const ALL_CATEGORIES_NODE_ID = '__all__';
const inHost = () => viewActive.value && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
// 每个子操作都必须保留本页 Read，孤立操作权限不能启动后台读写。
const allowed = (permission: string) => inHost() && session.can('document.host_documents.read') && session.can(permission);
const canCreate = computed(() => allowed('document.host_documents.create'));
const canUpdate = computed(() => allowed('document.host_documents.update'));
const canAddVersion = computed(() => allowed('document.host_documents.add_version'));
const canDelete = computed(() => allowed('document.host_documents.delete'));
const canRestore = computed(() => allowed('document.host_documents.restore'));
const canRollbackVersion = computed(() => allowed('document.host_documents.rollback_version'));
const canDeleteVersion = computed(() => allowed('document.host_documents.delete_version'));
const canDownload = computed(() => allowed('document.host_documents.download'));
const canCreatePreviewTask = computed(() => allowed('document.host_preview_tasks.create'));
const canCreateShare = computed(() => inHost()
  && session.can('document.host_documents.read') && session.can('document.host_shares.create'));
const canRead = computed(() => allowed('document.host_documents.read'));
const shareDialogOpen = ref(false);
const shareTarget = ref<HostDocumentItem | null>(null);
const shareBatchTargets = ref<HostDocumentItem[]>([]);
const selectedItems = ref<HostDocumentItem[]>([]);
// 分享单独持有代次，切换分享目标不能取消文档库的列表或编辑请求。
const shareScope = useAuthorizedViewScope(session, () => {
  shareDialogOpen.value = false; shareTarget.value = null; shareBatchTargets.value = []; selectedItems.value = [];
}, () => {});
const editingItem = computed(() => items.value.find(entry => entry.id.toLowerCase() === editingId.value?.toLowerCase() && entry.version === editingVersion.value));

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

// 整页内容与写入都属于当前 Host 会话；停用或作用域变化后同步清除敏感快照。
const pageScope = useAuthorizedViewScope(session, () => {
  resolveConfirmation(false);
  items.value = []; categories.value = []; hotTags.value = []; allTags.value = []; recentlyDeleted.value = [];
  selectedCategoryId.value = null; selectedTagFilterId.value = null; appliedFilters.value = { title: '' }; searchForm.value = {};
  resetPage(); createDialogOpen.value = false; cancelCreate(); cancelEdit(); versionHistoryVisible.value = false;
  versionHistoryItem.value = undefined; versionHistory.value = []; versionHistoryLoading.value = false;
  versionFile.value = null; versionTargetId.value = undefined; loading.value = false; changing.value = false; problem.value = undefined;
}, async () => { await Promise.all([load(), loadCategories(), loadHotTags(), loadAllTags()]); });
type PageRequest = ReturnType<typeof pageScope.begin>;
let listRequest: PageRequest; let categoryRequest: PageRequest; let hotTagRequest: PageRequest; let tagRequest: PageRequest;
let actionRequest: PageRequest; let createRequest: PageRequest; let editRequest: PageRequest;
let historyRequest: PageRequest; let versionActionRequest: PageRequest; let contentRequest: PageRequest;
function begin(permission: string): PageRequest { return allowed(permission) ? pageScope.begin(permission) : undefined; }
function beginAction(permission: string): PageRequest {
  if (changing.value) return;
  const request = begin(permission); if (!request) return;
  actionRequest = request; changing.value = true; problem.value = undefined; return request;
}
function finishAction(request: NonNullable<PageRequest>): void {
  if (request === actionRequest && request.current()) changing.value = false;
  request.finish();
}
function cancelAction(request: PageRequest): void {
  request?.cancel(); if (request && request === actionRequest) { changing.value = false; actionRequest = undefined; }
}
const sameId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
const findCurrentItem = (item: HostDocumentItem) => items.value.find(value => sameId(value.id, item.id) && value.version === item.version);
const currentItem = (item: HostDocumentItem) => !!findCurrentItem(item);
function snapshotItem(item: HostDocumentItem): HostDocumentItem {
  return { ...item, currentVersion: item.currentVersion ? { ...item.currentVersion } : null, tags: item.tags.map(tag => ({ ...tag })) };
}
watch(items, () => {
  if (versionHistoryVisible.value && versionHistoryItem.value && !currentItem(versionHistoryItem.value)) versionHistoryVisible.value = false;
}, { flush: 'sync', deep: true });
watch(createDialogOpen, open => { if (!open) { cancelAction(createRequest); createRequest = undefined; } }, { flush: 'sync' });
watch(editingId, (id, previous) => { if (id !== previous) { cancelAction(editRequest); editRequest = undefined; } }, { flush: 'sync' });
// 列表刷新为新版本时关闭旧表单，不能拿新版本号提交用户基于旧内容填写的字段。
watch(editingItem, item => { if (!item && editingId.value) cancelEdit(); }, { flush: 'sync' });
watch(versionHistoryVisible, open => {
  if (!open) {
    if (versionActionRequest && versionActionRequest === actionRequest) resolveConfirmation(false);
    historyRequest?.cancel(); cancelAction(versionActionRequest); cancelAction(contentRequest);
    versionHistory.value = []; versionHistoryItem.value = undefined; versionHistoryLoading.value = false;
  }
}, { flush: 'sync' });

async function loadHotTags(): Promise<void> {
  if (!canRead.value) return;
  hotTagRequest?.cancel(); const request = begin('document.tags.read'); if (!request) return; hotTagRequest = request;
  try {
    const values = await listDocumentTags({ isHot: true }, request.signal); if (request.current()) hotTags.value = values;
  } catch {
    if (request.current()) hotTags.value = [];
  } finally { request.finish(); }
}

async function loadAllTags(): Promise<void> {
  if (!canRead.value) return;
  tagRequest?.cancel(); const request = begin('document.tags.read'); if (!request) return; tagRequest = request;
  try {
    const values = await listDocumentTags({}, request.signal); if (request.current()) allTags.value = values;
  } catch {
    if (request.current()) allTags.value = [];
  } finally { request.finish(); }
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
  if (!canRead.value) return;
  categoryRequest?.cancel(); const request = begin('document.categories.read'); if (!request) return; categoryRequest = request;
  try {
    const values = await listDocumentCategories(request.signal); if (request.current()) categories.value = values;
  } catch {
    if (request.current()) categories.value = [];
  } finally { request.finish(); }
}

function onCategorySelected(node: CategoryTreeNode): void {
  selectedCategoryId.value = node.id === ALL_CATEGORIES_NODE_ID ? null : node.id;
  resetPage();
  void syncTableLayout();
}

function openShareDialog(item: HostDocumentItem): void {
  if (!canCreateShare.value) return;
  shareScope.invalidate();
  shareBatchTargets.value = [];
  shareTarget.value = item;
  shareDialogOpen.value = true;
}

function openBatchShareDialog(): void {
  if (!canCreateShare.value || selectedItems.value.length === 0) {
    return;
  }
  const targets = [...selectedItems.value];
  shareScope.invalidate();
  shareTarget.value = null;
  shareBatchTargets.value = targets;
  shareDialogOpen.value = true;
}

function onTableSelectionChange(rows: HostDocumentItem[]): void {
  selectedItems.value = rows;
}

const DOCUMENT_SHARE_BATCH_HINT_KEY = 'documentShares.recentBatch';

function onShareBatchCreated(succeeded: number, total: number): void {
  if (!canCreateShare.value) return;
  showSuccess(t('documentShares.batchCreateResult', { succeeded, total }));
  try {
    sessionStorage.setItem(
      DOCUMENT_SHARE_BATCH_HINT_KEY,
      JSON.stringify({ succeeded, total, userId: session.currentUser?.id, sessionId: session.currentUser?.sessionId })
    );
  } catch {
    // 忽略 sessionStorage 不可用场景
  }
  selectedItems.value = [];
  // 逐文档回执仍在创建弹窗内展示，保持预设不变，不在结果到达时自动跳页。
}

function viewCreatedShares(): void {
  if (!canCreateShare.value || !session.can('document.host_shares.read')) return;
  const request = shareScope.begin('document.host_shares.read'); if (!request) return;
  request.finish(); shareDialogOpen.value = false;
  void router.push({ name: 'document-shares' });
}

async function onShareCreated(_share: HostDocumentShareResponse, shareUrl: string): Promise<void> {
  if (!canCreateShare.value) return;
  const request = shareScope.begin('document.host_shares.create');
  if (!request) return;
  try {
    await navigator.clipboard.writeText(shareUrl);
    if (request.current()) showSuccess(t('documentShares.createdWithLink'));
  } catch {
    if (request.current()) showSuccess(t('documentShares.createSuccess'));
  }
  if (request.current()) shareTarget.value = null;
  request.finish();
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function deletedRowIndex(index: number): number {
  return (deletedPage.value - 1) * deletedPageSize.value + index + 1;
}

async function load(): Promise<void> {
  listRequest?.cancel(); const request = begin('document.host_documents.read'); if (!request) return; listRequest = request;
  loading.value = true;
  problem.value = undefined;
  try {
    const pageResult = await listDocumentItems(1, 100, {
      tagId: selectedTagFilterId.value
    }, request.signal);
    if (request.current()) items.value = pageResult.items;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error);
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
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
  if (!canCreate.value || changing.value) return;
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
  if (!createDialogOpen.value || !canCreate.value || !createTitle.value.trim()) {
    return;
  }
  const request = beginAction('document.host_documents.create'); if (!request) return; createRequest = request;
  try {
    await createDocumentItem(createTitle.value.trim(), createDescription.value.trim() || null, {
      categoryId: selectedCategoryId.value,
      tagIds: createTagIds.value
    }, request.signal);
    if (!request.current()) return; createRequest = undefined;
    createDialogOpen.value = false;
    createTitle.value = '';
    createDescription.value = '';
    createTagIds.value = [];
    ElMessage.success(t('hostDocumentItems.createSuccess'));
    await load();
  } catch (error: unknown) {
    if (!request.current()) return;
    if (!applyTitleConflictError(error, 'create') && !applyInvalidTagError(error)) {
      problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
    }
  } finally {
    finishAction(request);
  }
}

function onVersionFileSelected(event: Event, itemId: string): void {
  if (!canAddVersion.value || changing.value || !items.value.some(item => item.id === itemId)) return;
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
    || !currentItem(item)
  ) {
    return;
  }
  const request = beginAction('document.host_documents.add_version'); if (!request) return;
  try {
    await uploadDocumentVersion(item.id, versionFile.value, request.signal);
    if (!request.current()) return;
    versionFile.value = null;
    versionTargetId.value = undefined;
    ElMessage.success(t('hostDocumentItems.versionSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    finishAction(request);
  }
}

async function remove(row: HostDocumentItem): Promise<void> {
  const current = findCurrentItem(row);
  if (!canDelete.value || !current) return;
  // 确认冻结当前目录快照，原行对象就地变化不能迁移这次删除意图。
  const item = snapshotItem(current);
  const request = beginAction('document.host_documents.delete'); if (!request) return;
  try {
    if (!await askConfirmation(t('hostDocumentItems.delete'),
      t('hostDocumentItems.confirmDelete', { name: item.title }), t('hostDocumentItems.delete'))) return;
    if (!request.current() || !currentItem(item)) return;
    listRequest?.cancel(); loading.value = false;
    const removed = await deleteDocumentItem(item.id, item.version, request.signal);
    if (!request.current()) return;
    if (removed !== true) throw new Error('client.invalid_document_item');
    listRequest?.cancel(); loading.value = false;
    // 已确认删除先退出本地目录，补读失败仍可使用原删除修订发起恢复。
    // 若目录已观察到删除之后的更高修订，保留该状态，不凭旧回包生成过期恢复凭据。
    const observed = items.value.find(entry => sameId(entry.id, item.id));
    if (!observed || observed.version <= item.version + 1) {
      items.value = items.value.filter(entry => !sameId(entry.id, item.id));
      recentlyDeleted.value = [{ item, restoreVersion: item.version + 1 },
        ...recentlyDeleted.value.filter(entry => !sameId(entry.item.id, item.id))];
    }
    ElMessage.success(t('hostDocumentItems.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (!request.current() || error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally { finishAction(request); }
}

function startEdit(item: HostDocumentItem): void {
  if (!canUpdate.value || changing.value || !currentItem(item)) return;
  cancelEdit();
  editingVersion.value = item.version;
  editingId.value = item.id;
  editTitle.value = item.title;
  editDescription.value = item.description ?? '';
  editTitleError.value = '';
  editTagIds.value = item.tags?.map(tag => tag.tagId) ?? [];
}

function cancelEdit(): void {
  editingId.value = undefined;
  editingVersion.value = undefined;
  editTitle.value = '';
  editDescription.value = '';
  editTitleError.value = '';
  editTagIds.value = [];
}

async function saveEdit(item: HostDocumentItem): Promise<void> {
  if (!canUpdate.value || !sameId(editingId.value, item.id) || editingVersion.value !== item.version || !currentItem(item)) {
    return;
  }
  const request = beginAction('document.host_documents.update'); if (!request) return; editRequest = request;
  try {
    await updateDocumentItem(item, editTitle.value.trim(), editDescription.value.trim() || null, {
      tagIds: editTagIds.value
    }, request.signal);
    if (!request.current()) return; editRequest = undefined;
    editingId.value = undefined;
    editTitle.value = '';
    editDescription.value = '';
    editTagIds.value = [];
    ElMessage.success(t('hostDocumentItems.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (!request.current()) return;
    if (!applyTitleConflictError(error, 'edit') && !applyInvalidTagError(error)) {
      problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
    }
  } finally {
    finishAction(request);
  }
}

async function restoreDeleted(entry: DeletedDocumentEntry): Promise<void> {
  const owned = recentlyDeleted.value.find(value => sameId(value.item.id, entry.item.id) && value.restoreVersion === entry.restoreVersion);
  if (!canRestore.value || !owned) return;
  const itemId = owned.item.id, restoreVersion = owned.restoreVersion;
  const request = beginAction('document.host_documents.restore'); if (!request) return;
  try {
    listRequest?.cancel(); loading.value = false;
    const restored = await restoreDocumentItem(itemId, restoreVersion, request.signal);
    if (!request.current()) return;
    if (!sameId(restored.id, itemId)) throw new Error('client.invalid_document_item_identity');
    listRequest?.cancel(); loading.value = false;
    recentlyDeleted.value = recentlyDeleted.value.filter(candidate => !sameId(candidate.item.id, itemId));
    // 恢复回包是权威结果；本地筛选继续计算，服务端标签筛选只加入匹配的恢复项。
    const existing = items.value.find(candidate => sameId(candidate.id, itemId));
    if (!existing || existing.version <= restored.version) {
      items.value = items.value.filter(candidate => !sameId(candidate.id, itemId));
      if (!selectedTagFilterId.value || restored.tags.some(tag => sameId(tag.tagId, selectedTagFilterId.value))) items.value = [restored, ...items.value];
    }
    ElMessage.success(t('hostDocumentItems.restoreSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally { finishAction(request); }
}

async function downloadFile(itemId: string): Promise<void> {
  if (!canDownload.value || !items.value.some(item => item.id === itemId)) {
    return;
  }
  const request = beginAction('document.host_documents.download'); if (!request) return; contentRequest = request;
  try {
    const blob = await downloadDocumentContent(itemId, request.signal);
    if (request.current()) openDocumentBlob(blob);
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    finishAction(request);
  }
}

async function previewFile(item: HostDocumentItem, versionId?: string): Promise<void> {
  if (!canRead.value || !item.currentVersion || !currentItem(item)
    || versionId && (!versionHistoryVisible.value || versionHistoryItem.value?.id !== item.id || !versionHistory.value.some(value => value.id === versionId))) {
    return;
  }
  const request = beginAction('document.host_documents.read'); if (!request) return; contentRequest = request;
  try {
    const blob = await previewDocumentContent(item.id, versionId, request.signal);
    if (request.current()) openDocumentBlob(blob);
  } catch (error: unknown) {
    if (!request.current()) return;
    if (isFullNetProblemDetails(error) && error.code === 'document.host_document.preview_not_supported') {
      ElMessage.warning(t('hostDocumentItems.previewNotSupported'));
      return;
    }
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    finishAction(request);
  }
}

function canSubmitOfficePreview(item: HostDocumentItem): boolean {
  return Boolean(item.currentVersion);
}

async function submitOfficePreviewTask(item: HostDocumentItem): Promise<void> {
  if (!canCreatePreviewTask.value || !canSubmitOfficePreview(item) || !currentItem(item)) {
    return;
  }

  const request = beginAction('document.host_preview_tasks.create'); if (!request) return;
  try {
    const task = await createDocumentPreviewTask({
      documentItemId: item.id,
      versionId: item.currentVersion?.id ?? null
    }, request.signal);
    if (!request.current()) return;
    ElMessage.success(t('hostDocumentItems.officePreviewSubmitted'));
    // 创建权限不隐式授予任务读取；有独立 Read 才定位详情，由任务页重新读取授权结果。
    if (request.current() && allowed('document.host_preview_tasks.read'))
      await router.push({ name: 'document-preview-tasks', query: { taskId: task.id } });
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    finishAction(request);
  }
}

async function openVersionHistory(row: HostDocumentItem): Promise<void> {
  const current = findCurrentItem(row);
  if (!canRead.value || changing.value || !current) return;
  const item = snapshotItem(current);
  historyRequest?.cancel(); const request = begin('document.host_documents.read'); if (!request) return; historyRequest = request;
  versionHistoryItem.value = item; versionHistoryVisible.value = true; versionHistoryLoading.value = true; versionHistory.value = [];
  try {
    const values = await listDocumentVersions(item.id, request.signal);
    if (request.current() && versionHistoryVisible.value && sameId(versionHistoryItem.value?.id, item.id)) versionHistory.value = values;
  } catch (error) {
    if (request.current()) { versionHistoryVisible.value = false; problem.value = toProblem(error, 'hostDocumentItems.versionHistoryLoadFailed'); }
  } finally { if (request.current()) versionHistoryLoading.value = false; request.finish(); }
}

function isCurrentVersion(item: HostDocumentItem, version: unknown): boolean {
  return typeof version === 'object'
    && version !== null
    && 'id' in version
    && typeof version.id === 'string' && sameId(item.currentVersion?.id, version.id);
}

async function rollbackVersion(version: HostDocumentVersionResponse): Promise<void> { await changeVersion(version, 'rollback'); }
async function deleteVersion(version: HostDocumentVersionResponse): Promise<void> { await changeVersion(version, 'delete'); }

async function changeVersion(row: HostDocumentVersionResponse, kind: 'rollback' | 'delete'): Promise<void> {
  const openedItem = versionHistoryItem.value;
  const selectedVersion = versionHistory.value.find(value => sameId(value.id, row.id));
  if (!openedItem || !selectedVersion || versionHistoryLoading.value || !versionHistoryVisible.value
    || !currentItem(openedItem) || isCurrentVersion(openedItem, selectedVersion)) return;
  const item = snapshotItem(openedItem), version = { ...selectedVersion };
  const request = beginAction(kind === 'rollback' ? 'document.host_documents.rollback_version' : 'document.host_documents.delete_version');
  if (!request) return; versionActionRequest = request;
  let writeConfirmed = false;
  try {
    if (!await askConfirmation(t(kind === 'rollback' ? 'hostDocumentItems.rollbackVersion' : 'hostDocumentItems.deleteVersion'),
      t(kind === 'rollback' ? 'hostDocumentItems.confirmRollback' : 'hostDocumentItems.confirmDeleteVersion', { version: version.versionNumber }))) return;
    // 确认只属于打开时的文档、版本和权限代次，不能用于随后切换的抽屉。
    if (!request.current() || !versionHistoryVisible.value || !sameId(versionHistoryItem.value?.id, item.id)
      || versionHistoryItem.value?.version !== item.version || !currentItem(item) || !versionHistory.value.some(value => sameId(value.id, version.id) && value.versionNumber === version.versionNumber)) return;
    const updated = kind === 'rollback' ? await rollbackDocumentVersion(item.id, version.id, item.version, request.signal)
      : await deleteDocumentVersion(item.id, version.id, item.version, request.signal);
    if (!request.current()) return;
    if (updated.id.toLowerCase() !== item.id.toLowerCase()) throw new Error('client.invalid_document_item_identity');
    listRequest?.cancel(); loading.value = false;
    versionHistoryItem.value = snapshotItem(updated);
    const index = items.value.findIndex(entry => sameId(entry.id, item.id)); if (index >= 0) items.value[index] = updated;
    if (kind === 'delete') versionHistory.value = versionHistory.value.filter(value => !sameId(value.id, version.id));
    // 写入已确认与历史补读分别反馈，补读失败不能把成功的写入重新描述成失败。
    writeConfirmed = true;
    ElMessage.success(t(kind === 'rollback' ? 'hostDocumentItems.rollbackSuccess' : 'hostDocumentItems.deleteVersionSuccess'));
    const values = await listDocumentVersions(item.id, request.signal);
    if (!request.current()) return; versionHistory.value = values;
  } catch (error) {
    if (request.current() && error !== 'cancel' && error !== 'close') problem.value = toProblem(error, writeConfirmed ? 'hostDocumentItems.versionHistoryLoadFailed' : 'hostDocumentItems.operationFailed');
  } finally { finishAction(request); }
}

function findDeletedEntry(item: HostDocumentItem): DeletedDocumentEntry | undefined {
  return recentlyDeleted.value.find(entry => sameId(entry.item.id, item.id));
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
                  <PermissionGate v-if="canCreateShare" code="document.host_shares.create">
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

    <ElDialog v-if="confirmation" :key="confirmation.token" :model-value="true" :title="confirmation.title" width="460px" :close-on-click-modal="false"
      v-on="{ 'update:modelValue': confirmation.update }">
      <p data-testid="host-document-confirm-message">{{ confirmation?.message }}</p>
      <template #footer>
        <ElButton data-testid="host-document-confirm-dismiss" v-on="{ click: confirmation.dismiss }">{{ t('common.cancel') }}</ElButton>
        <ElButton type="warning" data-testid="host-document-confirm-accept" v-on="{ click: confirmation.confirm }">{{ confirmation?.confirmLabel }}</ElButton>
      </template>
    </ElDialog>
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
      parent-read-permission="document.host_documents.read"
      v-if="canCreateShare"
      v-model:open="shareDialogOpen"
      :preset-document="shareTarget"
      :preset-documents="shareBatchTargets.length > 0 ? shareBatchTargets : null"
      @created="onShareCreated"
      @batch-created="onShareBatchCreated"
      @view-shares="viewCreatedShares"
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
