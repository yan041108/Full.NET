<script setup lang="ts">
import { computed, onActivated, onDeactivated, ref, watch, onMounted, onUnmounted, onBeforeUnmount } from 'vue';
import { useRouter } from 'vue-router';
import { Plus } from '@element-plus/icons-vue';
import { ElButton, ElCard, ElDrawer, ElDialog, ElForm, ElFormItem, ElInput, ElMessage, ElPagination, ElOption, ElSelect, ElTable, ElTableColumn, ElTag, ElTree, ElInputNumber, ElAlert, ElMessageBox } from 'element-plus';
import { type FullNetProblemDetails, type HostDocumentItemResponse as HostDocumentItem, type HostDocumentVersionResponse, isFullNetProblemDetails, type HostDocumentCategoryResponse, type HostDocumentShareResponse } from '@fullnet/client-contracts';
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
import { createDocumentItem, deleteDocumentItem, downloadDocumentContent, listDocumentItems, listDocumentVersions, openDocumentBlob, previewDocumentContent, restoreDocumentItem, rollbackDocumentVersion, deleteDocumentVersion, updateDocumentItem, uploadDocumentVersion, changeDocumentStatus } from '../api/host-document-items';
import { createDocumentPreviewTask, getDocumentPreviewCapabilities } from '../api/document-preview-tasks';
import { listDocumentCategories } from '../api/host-document-categories';
import DocumentShareCreateDialog from '../components/DocumentShareCreateDialog.vue';
import { listDocumentTags, type HostDocumentTagResponse } from '../api/host-document-tags';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import DocumentSearchDetail from '../components/DocumentSearchDetail.vue';
const editCategoryId = ref<string | null>(null);
const editThumbnail = ref('');
const editSort = ref(0);
const compactLayout = ref(window.innerWidth < 768);
function syncViewport(): void { compactLayout.value = window.innerWidth < 768; }
const versionHistoryPage = ref(1);
const versionHistoryTotal = ref(0);
const previewCapability = ref<{ enabled: boolean; allowedMimeTypes: readonly string[]; maxInputBytes: number }>();
function statusLabel(value: number): string {
  return value === 0 ? t('hostDocumentItems.draft') : value === 1 ? t('hostDocumentItems.published') : t('hostDocumentItems.archived');
}
function typeLabel(value: number): string {
  const labels: Record<number, string> = { 0: t('hostDocumentItems.typeUnknown'), 1: t('hostDocumentItems.typeDocument'),
    2: t('hostDocumentItems.typeImage'), 3: t('hostDocumentItems.typeVideo'), 4: t('hostDocumentItems.typeAudio'),
    5: t('hostDocumentItems.typeArchive'), 99: t('hostDocumentItems.typeOther') };
  return labels[value] ?? labels[0]!;
}
function changePageSize(): void { resetPage(); void load(); }
defineOptions({ name: 'HostDocumentItemsView' });

interface CategoryTreeNode {
  id: string;
  label: string;
  children?: CategoryTreeNode[];
}

interface AppliedFilters {
  title: string;
  status?: string;
  documentType?: string;
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

const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const pagedItems = computed(() => items.value);
function resetPage(): void { page.value = 1; }

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
  },
  { key: 'status', label: t('hostDocumentItems.statusFilter'), type: 'select', options:
    [0, 1, 2].map(value => ({ value: String(value), label: statusLabel(value) })) },
  { key: 'documentType', label: t('hostDocumentItems.typeFilter'), type: 'select', options:
    [0, 1, 2, 3, 4, 5, 99].map(value => ({ value: String(value), label: typeLabel(value) })) }
]);

watch([items, page, pageSize], () => {
  void syncTableLayout();
});

const pageScope = useAuthorizedViewScope(session, () => {
  resolveConfirmation(false);
  items.value = []; categories.value = []; hotTags.value = []; allTags.value = []; recentlyDeleted.value = [];
  selectedCategoryId.value = null; selectedTagFilterId.value = null; appliedFilters.value = { title: '' }; searchForm.value = {};
  resetPage(); createDialogOpen.value = false; cancelCreate(); cancelEdit(); versionHistoryVisible.value = false;
  versionHistoryItem.value = undefined; versionHistory.value = []; versionHistoryLoading.value = false;
  versionFile.value = null; versionTargetId.value = undefined; loading.value = false; changing.value = false; problem.value = undefined;
}, async () => { await Promise.all([load(), loadCategories(), loadHotTags(), loadAllTags()]); });

type PageRequest = ReturnType<typeof pageScope.begin>;

let listRequest: PageRequest;

let categoryRequest: PageRequest;

let hotTagRequest: PageRequest;

let tagRequest: PageRequest;

let actionRequest: PageRequest;

let createRequest: PageRequest;

let editRequest: PageRequest;

let historyRequest: PageRequest;

let versionActionRequest: PageRequest;

let contentRequest: PageRequest;

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
  void load();
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
    const pageResult = await listDocumentItems(page.value, pageSize.value, {
      tagId: selectedTagFilterId.value, categoryId: selectedCategoryId.value, keyword: appliedFilters.value.title,
      status: appliedFilters.value.status === undefined ? undefined : Number(appliedFilters.value.status),
      documentType: appliedFilters.value.documentType === undefined ? undefined : Number(appliedFilters.value.documentType)
    }, request.signal);
    if (request.current()) {
      items.value=pageResult.items; total.value=pageResult.total;
      const lastPage=Math.max(1,Math.ceil(pageResult.total/pageSize.value));
      if(page.value>lastPage) { page.value=lastPage; void load(); }
    }
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error);
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish();
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { title: params.title ?? '', status: params.status, documentType: params.documentType };
  resetPage();
  void load();
}

function resetSearch(): void {
  appliedFilters.value = { title: '' };
  resetPage();
  void load();
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
  editCategoryId.value=item.categoryId; editThumbnail.value=item.thumbnail??''; editSort.value=item.sort;
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
      tagIds: editTagIds.value, categoryId: editCategoryId.value, thumbnail: editThumbnail.value.trim() || null, sort: editSort.value
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
  const capability = previewCapability.value;
  const version = item.currentVersion;
  return Boolean(capability?.enabled && version && capability.allowedMimeTypes.includes(item.mimeType?.split(';', 1)[0]?.trim().toLowerCase() ?? '') && version.sizeBytes <= capability.maxInputBytes);
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
  versionHistoryPage.value=1; versionHistoryItem.value = item; versionHistoryVisible.value = true; versionHistoryLoading.value = true; versionHistory.value = [];
  try {
    const result = await listDocumentVersions(item.id, request.signal, versionHistoryPage.value, 20);
    const values=result.items; versionHistoryTotal.value=result.total;
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
    const result = await listDocumentVersions(item.id, request.signal, versionHistoryPage.value, 20);
    const values=result.items; versionHistoryTotal.value=result.total;
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
onMounted(() => { window.addEventListener('resize', syncViewport); void getDocumentPreviewCapabilities().then(value => { previewCapability.value=value; }).catch(() => { previewCapability.value=undefined; }); });
onBeforeUnmount(() => window.removeEventListener('resize',syncViewport));
async function transitionStatus(row: HostDocumentItem, action: 'publish' | 'archive' | 'unarchive'): Promise<void> {
  const item=findCurrentItem(row); if(!item) return;
  const request=beginAction('document.host_documents.'+action); if(!request) return;
  try { await changeDocumentStatus(item,action,request.signal); if(request.current()) await load(); }
  catch(error) { if(request.current()) problem.value=toProblem(error); }
  finally { finishAction(request); }
}
async function loadVersionHistory(): Promise<void> {
  const item=versionHistoryItem.value; if(!item || !versionHistoryVisible.value || !currentItem(item)) return;
  historyRequest?.cancel(); const request=begin('document.host_documents.read'); if(!request) return; historyRequest=request;
  versionHistoryLoading.value=true;
  try { const result=await listDocumentVersions(item.id,request.signal,versionHistoryPage.value,20); if(request.current()) { versionHistory.value=result.items; versionHistoryTotal.value=result.total; } }
  catch(error) { if(request.current()) problem.value=toProblem(error); }
  finally { if(request.current()) versionHistoryLoading.value=false; request.finish(); }
}

</script>

<template>
  <section class="host-document-items-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <DocumentSearchDetail />
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
            <el-table-column v-if="!compactLayout" :label="t('users.columnIndex')" width="72" align="center">
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

            <el-table-column :label="t('hostDocumentItems.statusFilter')" width="100"><template #default="{ row }"><el-tag>{{ statusLabel(row.status) }}</el-tag></template></el-table-column>
            <el-table-column :label="t('hostDocumentItems.typeFilter')" width="90"><template #default="{ row }">{{ typeLabel(row.documentType) }}</template></el-table-column>
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

            <el-table-column v-if="canAddVersion" :label="t('hostDocumentItems.uploadVersion')" min-width="230">
              <template #default="{ row }">
                  <div v-if="canAddVersion && row.status !== 2" class="host-document-items-view__version-input">
                    <label>
                      <span class="art-sr-heading">{{ t('hostDocumentItems.chooseVersionFile') }}</span>
                      <input :disabled="changing" type="file" data-testid="host-document-item-version-file" @change="onVersionFileSelected($event, row.id)" />
                    </label>
                    <el-button size="small" data-testid="host-document-item-upload-version" :disabled="changing || !versionFile || versionTargetId !== row.id" @click="uploadVersion(row as HostDocumentItem)">{{ t('hostDocumentItems.uploadVersion') }}</el-button>
                  </div>
              </template>
            </el-table-column>
            <el-table-column :label="t('users.columnActions')" :width="compactLayout ? 92 : ART_TABLE_ACTION_COLUMN_WIDTH" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup :max-visible="compactLayout ? 1 : 4">
                  <ArtTableActionButton :disabled="changing" v-if="canUpdate && row.status !== 2" type="edit" :title="t('hostDocumentItems.edit')" test-id="host-document-item-edit" @click="startEdit(row as HostDocumentItem)" />
                  <ArtTableActionButton :disabled="changing" v-if="canRead && row.currentVersion" type="view" :title="t('hostDocumentItems.preview')" test-id="host-document-item-preview" @click="previewFile(row as HostDocumentItem)" />
                  <ArtTableActionButton :disabled="changing" v-if="row.status === 0 && row.currentVersion && session.can('document.host_documents.publish')" type="add" :title="t('hostDocumentItems.publish')" test-id="host-document-item-publish" @click="transitionStatus(row as HostDocumentItem, 'publish')" />
                  <ArtTableActionButton :disabled="changing" v-if="row.status === 1 && session.can('document.host_documents.archive')" type="view" :title="t('hostDocumentItems.archive')" test-id="host-document-item-archive" @click="transitionStatus(row as HostDocumentItem, 'archive')" />
                  <ArtTableActionButton :disabled="changing" v-if="row.status === 2 && session.can('document.host_documents.unarchive')" type="edit" :title="t('hostDocumentItems.unarchive')" test-id="host-document-item-unarchive" @click="transitionStatus(row as HostDocumentItem, 'unarchive')" />
                  <ArtTableActionButton :disabled="changing" v-if="canRead" type="view" :title="t('hostDocumentItems.versionHistory')" test-id="host-document-item-version-history" @click="openVersionHistory(row as HostDocumentItem)" />
                  <ArtTableActionButton :disabled="changing" v-if="canDownload && row.currentVersion" type="view" :title="t('hostDocumentItems.download')" test-id="host-document-item-download" @click="downloadFile(row.id)" />
                  <ArtTableActionButton :disabled="changing" v-if="canCreatePreviewTask && canSubmitOfficePreview(row as HostDocumentItem)" type="view" :title="t('hostDocumentItems.submitOfficePreview')" test-id="host-document-item-create-preview-task" @click="submitOfficePreviewTask(row as HostDocumentItem)" />
                  <ArtTableActionButton :disabled="changing" v-if="canCreateShare && row.status === 1" type="add" :title="t('hostDocumentItems.share')" test-id="host-document-item-share" @click="openShareDialog(row as HostDocumentItem)" />

                  <ArtTableActionButton :disabled="changing" v-if="canDelete" type="delete" :title="t('hostDocumentItems.delete')" test-id="host-document-item-delete" @click="remove(row as HostDocumentItem)" />
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('hostDocumentItems.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          @current-change="load()"
          @size-change="changePageSize"
          :total="total"
          background
          :layout="compactLayout ? 'total, prev, pager, next' : 'total, sizes, prev, pager, next, jumper'"
          :small="compactLayout"
          :pager-count="compactLayout ? 5 : 7"
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
        <el-alert :title="t('hostDocumentItems.clearMetadataHint')" type="info" :closable="false" />
        <el-form-item :label="t('hostDocumentItems.categoryLabel')">
          <el-select v-model="editCategoryId" clearable data-testid="host-document-item-edit-category" @clear="editCategoryId = null">
            <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('hostDocumentItems.thumbnailLabel')"><el-input v-model="editThumbnail" clearable data-testid="host-document-item-edit-thumbnail" /></el-form-item>
        <el-form-item :label="t('hostDocumentItems.sortLabel')"><el-input-number v-model="editSort" :precision="0" :min="-2147483648" :max="2147483647" data-testid="host-document-item-edit-sort" /></el-form-item>
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
                v-if="versionHistoryItem && versionHistoryItem.status !== 2 && !isCurrentVersion(versionHistoryItem, row)"
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
      <ElPagination v-model:current-page="versionHistoryPage" :total="versionHistoryTotal" :page-size="20"
        layout="prev, pager, next, total" @current-change="loadVersionHistory" />

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
  min-width: 0;
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
  min-width: 0;
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
  min-width: 0;
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
.host-document-items-view .document-module-split { min-width: 0; }
.host-document-items-view__version-input input { width: 130px; }
@media (max-width: 1100px) {
  .host-document-items-view .document-module-split { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto minmax(0, 1fr); }
  .host-document-items-view .document-module-split__aside { max-height: 150px; min-width: 0; }
}
</style>
