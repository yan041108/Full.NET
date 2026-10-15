<script setup lang="ts">
import { computed, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { useBlobPreview } from '../composables/useBlobPreview';
import { isPassiveContentType } from '../utils/openContentBlob';
import {
  ElButton,
  ElCard,
  ElDialog,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTree
} from 'element-plus';
import type {
  BatchUploadHostFilesResponse,
  FullNetProblemDetails,
  HostFile,
  HostFileReferenceClaimResponse,
  HostFolderResponse,
  HostFolderTreeNode
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  batchDeleteHostFiles,
  batchUploadHostFiles,
  createHostFolder,
  deleteHostFile,
  deleteHostFolder,
  downloadHostFileContent,
  listHostFileReferences,
  listHostFiles,
  listHostFolderTree,
  openHostFileBlob,
  previewHostFileContent,
  updateHostFileMetadata,
  updateHostFolder,
  uploadHostFile
} from '../api/host-files';
import { isPreviewableHostFile } from '@fullnet/client-contracts';

defineOptions({ name: 'HostFilesView' });

interface AppliedFilters {
  fileName: string;
}

interface TreeNode {
  id: string;
  label: string;
  folder: HostFolderTreeNode | null;
  children?: TreeNode[];
}

const ROOT_NODE_ID = '__root__';
const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostFile[]>([]);
const folderTree = ref<TreeNode[]>([]);
const selectedFolder = ref<HostFolderTreeNode | null>(null);
const selectedFiles = ref<File[]>([]);
const uploadResults = ref<BatchUploadHostFilesResponse | null>(null);
const selectedRows = ref<HostFile[]>([]);
type DeleteOutcome = 'processing' | 'succeeded' | 'failed' | 'unconfirmed';
const deleteReceipt = ref<{ succeeded: number; total: number; items: { fileId: string; name: string; outcome: DeleteOutcome; errorCode: string | null }[] }>();
const unconfirmedFileIds = ref(new Set<string>());
const fileUnconfirmed = (id: string) => unconfirmedFileIds.value.has(id.toLowerCase());
const unconfirmedFolderIds = ref(new Set<string>());
const folderUnconfirmed = (id: string) => unconfirmedFolderIds.value.has(id.toLowerCase());
const folderCreationUnconfirmed = ref(false);
const previewDialogVisible = ref(false);
const filePreview = useBlobPreview();
const previewUrl = filePreview.url;
const previewTitle = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ fileName: '' });
const metadataDialogVisible = ref(false);
let metadataSequence = 0;
const metadataIntent = ref<{ token: number; close: () => void; update: (open: boolean) => void; save: () => void }>();
const referencesDrawerVisible = ref(false);
const references = ref<HostFileReferenceClaimResponse[]>([]);
const editingFile = ref<HostFile | null>(null);
const metadataForm = ref({ originalFileName: '', folderId: '' });
const newFolderName = ref('');
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const allowed = (permission: string) => viewActive.value && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null && session.can('files.files.read') && session.can(permission);
const canRead = computed(() => allowed('files.files.read'));
const canPreview = computed(() => allowed('files.files.read'));
const hasSelectedRows = computed(() => selectedRows.value.length > 0);

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

const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const pagedItems = computed(() => items.value);

function resetPage(): void {
  page.value = 1;
}

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'fileName',
    label: t('hostFiles.fileName'),
    placeholder: t('hostFiles.searchPlaceholder')
  }
]);

watchLoading(loading);

let confirmationSequence = 0;
const confirmation = ref<{ token: number; title: string; message: string; input: boolean; confirm: () => void; dismiss: () => void; update: (open: boolean) => void }>();
const confirmationInput = ref(''); let settleConfirmation: ((result: { confirmed: boolean; value: string }) => void) | undefined;
function finishConfirmation(confirmed: boolean, token = confirmation.value?.token): void {
  if (!confirmation.value || token !== confirmation.value.token) return;
  if (confirmed && confirmation.value?.input && !confirmationInput.value.trim()) return;
  const settle = settleConfirmation; const value = confirmationInput.value.trim(); settleConfirmation = undefined;
  confirmation.value = undefined; confirmationInput.value = ''; settle?.({ confirmed, value });
}
function askConfirmation(title: string, message: string, value?: string): Promise<{ confirmed: boolean; value: string }> {
  finishConfirmation(false); confirmationInput.value = value ?? '';
  return new Promise(resolve => {
    const token = ++confirmationSequence; settleConfirmation = resolve; const dismiss = () => finishConfirmation(false, token);
    confirmation.value = { token, title, message, input: value !== undefined, confirm: () => finishConfirmation(true, token), dismiss, update: open => { if (!open) dismiss(); } };
  });
}
// 文件内容、上传回执、编辑和确认由当前 Host 会话拥有；失效只取消客户端接入，不承诺回滚写入。
const scope = useAuthorizedViewScope(session, () => {
  finishConfirmation(false); deleteReceipt.value = undefined; unconfirmedFileIds.value = new Set(); unconfirmedFolderIds.value = new Set(); folderCreationUnconfirmed.value = false; items.value = []; folderTree.value = []; selectedFolder.value = null;
  selectedFiles.value = []; selectedRows.value = []; uploadResults.value = null; page.value = 1; pageSize.value = 20; total.value = 0;
  previewDialogVisible.value = false; filePreview.clear(); previewTitle.value = ''; metadataDialogVisible.value = false; editingFile.value = null;
  metadataForm.value = { originalFileName: '', folderId: '' }; referencesDrawerVisible.value = false; references.value = [];
  searchForm.value = {}; appliedFilters.value = { fileName: '' }; newFolderName.value = ''; problem.value = undefined; loading.value = false; changing.value = false;
}, refreshAll);
type ViewRequest = ReturnType<typeof scope.begin>;
let listRequest: ViewRequest; let foldersRequest: ViewRequest; let actionRequest: ViewRequest;
let metadataRequest: ViewRequest; let previewRequest: ViewRequest; let referencesRequest: ViewRequest;
function begin(permission: string): ViewRequest { return allowed(permission) ? scope.begin(permission) : undefined; }
function beginAction(permission: string): ViewRequest {
  if (changing.value) return; const request = begin(permission); if (!request) return;
  actionRequest = request; changing.value = true; problem.value = undefined; return request;
}
function finishAction(request: NonNullable<ViewRequest>): void { if (request === actionRequest && request.current()) changing.value = false; request.finish(); }
function cancelAction(request: ViewRequest): void { request?.cancel(); if (request && request === actionRequest) { changing.value = false; actionRequest = undefined; } }
const findCurrentFile = (file: HostFile) => items.value.find(item => item.id.toLowerCase() === file.id.toLowerCase() && item.revision === file.revision);
const currentFile = (file: HostFile) => !!findCurrentFile(file);
function removeConfirmedFiles(ids: string[]): void {
  const deleted = new Set(ids.map(id => id.toLowerCase()));
  const previous = items.value.length;
  items.value = items.value.filter(item => !deleted.has(item.id.toLowerCase()));
  total.value = Math.max(0, total.value - (previous - items.value.length));
  selectedRows.value = selectedRows.value.filter(item => !deleted.has(item.id.toLowerCase()));
}
function markUnconfirmed(ids: string[]): void {
  listRequest?.cancel(); loading.value = false;
  unconfirmedFileIds.value = new Set([...unconfirmedFileIds.value, ...ids.map(id => id.toLowerCase())]);
}
function findFolder(id: string, nodes = folderTree.value): HostFolderTreeNode | undefined {
  for (const node of nodes) { if (node.folder?.id.toLowerCase() === id.toLowerCase()) return node.folder; const nested = findFolder(id, node.children ?? []); if (nested) return nested; }
  return undefined;
}
const currentFolder = (folder: HostFolderTreeNode) => findFolder(folder.id)?.revision === folder.revision;
function markFolderUnconfirmed(id?: string): void {
  foldersRequest?.cancel();
  // 创建结果未知时没有可信的新标识，单独等待目录树重读，避免重复创建。
  if (id) unconfirmedFolderIds.value = new Set([...unconfirmedFolderIds.value, id.toLowerCase()]);
  else folderCreationUnconfirmed.value = true;
}
function applyConfirmedFolder(result: HostFolderResponse, deleted = false): void {
  // 先维护确认结果与本地选择，补读失败仍可恢复操作；更高观察修订保持权威。
  const existing = findFolder(result.id);
  if (existing && existing.revision > result.revision) return;
  const same = (id: string | undefined) => id?.toLowerCase() === result.id.toLowerCase();
  function update(nodes: TreeNode[]): TreeNode[] {
    return nodes.flatMap(node => {
      if (same(node.folder?.id) && deleted) return [];
      const children = update(node.children ?? []);
      const folder = same(node.folder?.id) ? { ...result, children: children.flatMap(child => child.folder ? [child.folder] : []) } : node.folder;
      return [{ ...node, label: same(node.folder?.id) ? result.name : node.label, folder: folder ? { ...folder, children: children.flatMap(child => child.folder ? [child.folder] : []) } : null, children }];
    });
  }
  let nodes = update(folderTree.value);
  if (!deleted && !existing) {
    if (!nodes.length && result.parentId === null) nodes = [{ id: ROOT_NODE_ID, label: t('hostFiles.rootFolder'), folder: null, children: [] }];
    function insert(entries: TreeNode[]): void {
      for (const node of entries) {
        if (result.parentId === null ? node.id === ROOT_NODE_ID : node.folder?.id.toLowerCase() === result.parentId.toLowerCase()) {
          node.children = [...(node.children ?? []), { id: result.id, label: result.name, folder: { ...result, children: [] }, children: [] }];
          node.children.sort((a, b) => (a.folder!.displayOrder - b.folder!.displayOrder) || (a.label < b.label ? -1 : a.label > b.label ? 1 : a.id.toLowerCase().localeCompare(b.id.toLowerCase())));
          return;
        }
        insert(node.children ?? []);
      }
    }
    insert(nodes); nodes = update(nodes);
  }
  folderTree.value = nodes;
  if (selectedFolder.value) {
    selectedFolder.value = findFolder(selectedFolder.value.id) ?? null;
    if (!selectedFolder.value) { listRequest?.cancel(); loading.value = false; items.value = []; selectedRows.value = []; total.value = 0; page.value = 1; }
  }
}
function applyConfirmedMetadata(result: HostFile): void {
  const current = items.value.find(item => item.id.toLowerCase() === result.id.toLowerCase());
  if (!current || current.revision > result.revision) return;
  if (result.folderId?.toLowerCase() !== selectedFolder.value?.id.toLowerCase()) { removeConfirmedFiles([result.id]); return; }
  // 文件名 LIKE 的通配及排序规则由数据库决定，本地只更新确认内容，不伪造筛选结果。
  items.value = items.value.map(item => item.id.toLowerCase() === result.id.toLowerCase() ? result : item);
  selectedRows.value = selectedRows.value.map(item => item.id.toLowerCase() === result.id.toLowerCase() ? result : item);
}
watch(metadataDialogVisible, open => { if (!open) {
  if (metadataRequest?.current() && editingFile.value) markUnconfirmed([editingFile.value.id]);
  cancelAction(metadataRequest); metadataRequest = undefined; metadataIntent.value = undefined; editingFile.value = null; metadataForm.value = { originalFileName: '', folderId: '' };
} }, { flush: 'sync' });
watch(items, () => { if (editingFile.value && !currentFile(editingFile.value)) metadataDialogVisible.value = false; }, { flush: 'sync', deep: true });
watch(previewDialogVisible, open => { if (!open) { cancelAction(previewRequest); filePreview.clear(); previewTitle.value = ''; } }, { flush: 'sync' });
watch(referencesDrawerVisible, open => { if (!open) { cancelAction(referencesRequest); references.value = []; } }, { flush: 'sync' });

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function mapFolderNodes(nodes: HostFolderTreeNode[]): TreeNode[] {
  return nodes.map(node => ({
    id: node.id,
    label: node.name,
    folder: node,
    children: mapFolderNodes(node.children)
  }));
}

async function refreshAll(): Promise<void> {
  await Promise.all([loadFolders(), load()]);
}

async function loadFolders(): Promise<void> {
  foldersRequest?.cancel(); const request = begin('files.files.read'); if (!request) return; foldersRequest = request;
  try {
    const tree = await listHostFolderTree(request.signal); if (!request.current()) return;
    folderTree.value = [{ id: ROOT_NODE_ID, label: t('hostFiles.rootFolder'), folder: null, children: mapFolderNodes(tree) }];
    unconfirmedFolderIds.value = new Set(); folderCreationUnconfirmed.value = false;
    if (selectedFolder.value) { const id = selectedFolder.value.id; selectedFolder.value = findFolder(id) ?? null; if (!selectedFolder.value) void load(); }
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { request.finish(); }
}

async function load(): Promise<void> {
  listRequest?.cancel(); const request = begin('files.files.read'); if (!request) return; listRequest = request;
  loading.value = true; problem.value = undefined;
  try {
    // 根目录身份不依赖目录树和文件页哪一个先完成。
    const result = await listHostFiles(page.value, pageSize.value, { folderId: selectedFolder.value?.id ?? '', fileNameContains: appliedFilters.value.fileName }, request.signal);
    if (!request.current()) return;
    // 新目录可能使在途编辑失效；先清理旧限制，保留同步失效回调新增的未知提交标记。
    unconfirmedFileIds.value = new Set(); items.value = result.items; total.value = result.total;
    await nextTick(() => { if (request.current()) updateTableHeight(); });
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
}

function onFolderSelected(node: TreeNode): void {
  if (!allowed('files.files.read') || changing.value || node.folder && !currentFolder(node.folder)) return;
  selectedFolder.value = node.id === ROOT_NODE_ID ? null : node.folder;
  resetPage();
  void load();
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { fileName: params.fileName ?? '' };
  resetPage();
  void load();
}

function resetSearch(): void {
  appliedFilters.value = { fileName: '' };
  resetPage();
  void load();
}

function onFileSelected(event: Event): void {
  if (!allowed('files.files.upload') || changing.value) return;
  const input = event.target as HTMLInputElement;
  selectedFiles.value = input.files ? Array.from(input.files) : [];
}

async function upload(): Promise<void> {
  if (!selectedFiles.value.length) return; const request = beginAction('files.files.upload'); if (!request) return;
  const files = [...selectedFiles.value]; const folderId = selectedFolder.value?.id;
  try {
    if (files.length === 1) {
      await uploadHostFile(files[0]!, folderId, request.signal); if (!request.current()) return;
      uploadResults.value = null; ElMessage.success(t('hostFiles.uploadSuccess'));
    } else {
      const result = await batchUploadHostFiles(files, folderId, request.signal); if (!request.current()) return;
      uploadResults.value = result; ElMessage.success(t('hostFiles.batchUploadSummary', { succeeded: result.succeededCount, total: result.results.length }));
    }
    selectedFiles.value = []; if (request.current()) await load();
  } catch (error) { if (request.current()) problem.value = toProblem(error, 'hostFiles.operationFailed'); }
  finally { finishAction(request); }
}

async function removeSelected(): Promise<void> {
  if (!selectedRows.value.length || !selectedRows.value.every(row => currentFile(row) && !fileUnconfirmed(row.id))) return;
  const targets = [...new Map(selectedRows.value.map(row => { const file = findCurrentFile(row)!; return [file.id.toLowerCase(), { ...file }] as const; })).values()];
  const request = beginAction('files.files.delete'); if (!request) return; let dispatched = false;
  try {
    const answer = await askConfirmation(t('hostFiles.delete'), t('hostFiles.confirmBatchDelete', { count: targets.length }));
    if (!answer.confirmed || !request.current() || !targets.every(currentFile)) return;
    listRequest?.cancel(); loading.value = false; dispatched = true;
    deleteReceipt.value = { succeeded: 0, total: targets.length, items: targets.map(file => ({ fileId: file.id, name: file.originalFileName, outcome: 'processing', errorCode: null })) };
    const result = await batchDeleteHostFiles(targets.map(file => file.id), request.signal); if (!request.current()) return;
    // 回执按目标重排，不把未知服务端 message 当作用户可展示文本。
    const outcomes = new Map(result.results.map(item => [item.fileId.toLowerCase(), item]));
    if (result.results.length !== targets.length || outcomes.size !== targets.length || targets.some(file => !outcomes.has(file.id.toLowerCase()))
      || result.results.filter(item => item.succeeded).length !== result.succeededCount) throw new Error('client.invalid_host_file_delete_batch');
    deleteReceipt.value = { succeeded: result.succeededCount, total: targets.length, items: targets.map(file => { const item = outcomes.get(file.id.toLowerCase())!;
      return { fileId: file.id, name: file.originalFileName, outcome: item.succeeded ? 'succeeded' : 'failed', errorCode: item.errorCode }; }) };
    listRequest?.cancel(); loading.value = false; removeConfirmedFiles(result.results.filter(item => item.succeeded).map(item => item.fileId));
    const summary = t('hostFiles.batchDeleteSummary', { succeeded: result.succeededCount, total: targets.length });
    if (result.succeededCount === targets.length) ElMessage.success(summary); else ElMessage.warning(summary);
    selectedRows.value = []; await load();
  } catch (error) {
    if (request.current()) {
      if (dispatched) {
        markUnconfirmed(targets.map(file => file.id));
        deleteReceipt.value = { succeeded: 0, total: targets.length, items: targets.map(file => ({ fileId: file.id, name: file.originalFileName, outcome: 'unconfirmed', errorCode: null })) };
      }
      problem.value = toProblem(error, 'hostFiles.operationFailed');
    }
  } finally { finishAction(request); }
}

async function preview(file: HostFile): Promise<void> {
  if (!currentFile(file) || !canPreviewFile(file)) return; const request = beginAction('files.files.read'); if (!request) return; previewRequest = request;
  previewTitle.value = file.originalFileName; previewDialogVisible.value = true;
  try { const loaded = await filePreview.load(() => previewHostFileContent(file.id, request.signal)); if (!request.current() || !loaded) return; }
  catch (error) { if (request.current()) { problem.value = toProblem(error); closePreviewDialog(); } }
  finally { finishAction(request); }
}
function closePreviewDialog(): void { previewDialogVisible.value = false; }

function canPreviewFile(file: HostFile): boolean {
  return canPreview.value && isPreviewableHostFile(file.contentType) && isPassiveContentType(file.contentType);
}

async function createFolder(): Promise<void> {
  const draft = newFolderName.value, name = draft.trim();
  if (!name || folderCreationUnconfirmed.value || selectedFolder.value && (!currentFolder(selectedFolder.value) || folderUnconfirmed(selectedFolder.value.id))) return;
  const request = beginAction('files.folders.create'); if (!request) return;
  const parentId = selectedFolder.value?.id ?? null;
  foldersRequest?.cancel();
  try {
    const result = await createHostFolder({ parentId, name }, request.signal); if (!request.current()) return;
    if (result.parentId?.toLowerCase() !== parentId?.toLowerCase()) throw new Error('client.invalid_host_folder_parent');
    foldersRequest?.cancel(); applyConfirmedFolder(result);
    if (newFolderName.value === draft) newFolderName.value = '';
    ElMessage.success(t('hostFiles.folderCreateSuccess')); await refreshAll();
  } catch (error) { if (request.current()) { markFolderUnconfirmed(); problem.value = toProblem(error, 'hostFiles.operationFailed'); } }
  finally { finishAction(request); }
}
async function renameFolder(node: TreeNode): Promise<void> { await changeFolder(node, 'rename'); }
async function removeFolder(node: TreeNode): Promise<void> { await changeFolder(node, 'delete'); }
async function changeFolder(node: TreeNode, kind: 'rename' | 'delete'): Promise<void> {
  if (!node.folder || !currentFolder(node.folder) || folderUnconfirmed(node.folder.id)) return;
  const folder = { ...findFolder(node.folder.id)! };
  const request = beginAction(kind === 'rename' ? 'files.folders.update' : 'files.folders.delete'); if (!request) return;
  let dispatched = false;
  try {
    const answer = await askConfirmation(t(kind === 'rename' ? 'hostFiles.renameFolder' : 'hostFiles.deleteFolder'),
      kind === 'rename' ? t('hostFiles.folderName') : t('hostFiles.confirmDeleteFolder', { name: folder.name }), kind === 'rename' ? folder.name : undefined);
    if (!answer.confirmed || !request.current() || !currentFolder(folder)) return;
    foldersRequest?.cancel(); dispatched = true;
    const result = kind === 'rename'
      ? await updateHostFolder(folder.id, { expectedRevision: folder.revision, name: answer.value, displayOrder: folder.displayOrder }, request.signal)
      : await deleteHostFolder(folder.id, { expectedRevision: folder.revision }, request.signal);
    if (!request.current()) return;
    if (result.id.toLowerCase() !== folder.id.toLowerCase() || (kind === 'rename' ? result.revision <= folder.revision : result.revision !== folder.revision)) throw new Error('client.invalid_host_folder_mutation');
    foldersRequest?.cancel(); applyConfirmedFolder(result, kind === 'delete');
    ElMessage.success(t(kind === 'rename' ? 'hostFiles.folderUpdateSuccess' : 'hostFiles.folderDeleteSuccess')); await refreshAll();
  } catch (error) { if (request.current()) { if (dispatched) markFolderUnconfirmed(folder.id); problem.value = toProblem(error, 'hostFiles.operationFailed'); } }
  finally { finishAction(request); }
}

function openMetadataDialog(item: HostFile): void {
  const current = findCurrentFile(item);
  if (!allowed('files.files.update') || changing.value || !current || fileUnconfirmed(current.id)) return;
  editingFile.value = { ...current }; metadataForm.value = { originalFileName: current.originalFileName, folderId: current.folderId ?? '' };
  const token = ++metadataSequence, close = () => { if (metadataIntent.value?.token === token) metadataDialogVisible.value = false; };
  metadataIntent.value = { token, close, update: open => { if (!open) close(); }, save: () => { if (metadataIntent.value?.token === token) void saveMetadata(); } };
  metadataDialogVisible.value = true;
}
async function saveMetadata(): Promise<void> {
  const editing = editingFile.value; if (!metadataDialogVisible.value || !editing || !currentFile(editing) || fileUnconfirmed(editing.id) || !metadataForm.value.originalFileName.trim()) return;
  const file = { ...editing };
  const request = beginAction('files.files.update'); if (!request) return; metadataRequest = request;
  const body = { expectedRevision: file.revision, originalFileName: metadataForm.value.originalFileName.trim(), folderId: metadataForm.value.folderId || null };
  listRequest?.cancel(); loading.value = false;
  try {
    const result = await updateHostFileMetadata(file.id, body, request.signal); if (!request.current()) return;
    if (result.id.toLowerCase() !== file.id.toLowerCase() || result.revision <= file.revision) throw new Error('client.invalid_host_file_mutation');
    metadataRequest = undefined; listRequest?.cancel(); loading.value = false;
    applyConfirmedMetadata(result); metadataDialogVisible.value = false;
    ElMessage.success(t('hostFiles.metadataUpdateSuccess')); await load();
  } catch (error) { if (request.current()) {
    metadataRequest = undefined; markUnconfirmed([file.id]); metadataDialogVisible.value = false; problem.value = toProblem(error, 'hostFiles.operationFailed');
  } } finally { if (metadataRequest === request) metadataRequest = undefined; finishAction(request); }
}

async function openReferences(item: HostFile): Promise<void> {
  if (!currentFile(item)) return; const request = beginAction('files.file_references.read'); if (!request) return; referencesRequest = request;
  references.value = []; referencesDrawerVisible.value = true;
  try { const result = await listHostFileReferences(item.id, 1, 20, request.signal); if (request.current()) references.value = result.items; }
  catch (error) { if (request.current()) problem.value = toProblem(error, 'hostFiles.operationFailed'); }
  finally { finishAction(request); }
}
async function remove(item: HostFile): Promise<void> {
  const current = findCurrentFile(item); if (!current || fileUnconfirmed(current.id)) return;
  const target = { ...current }; const request = beginAction('files.files.delete'); if (!request) return; let dispatched = false;
  try {
    const answer = await askConfirmation(t('hostFiles.delete'), t('hostFiles.confirmDelete', { name: target.originalFileName }));
    if (!answer.confirmed || !request.current() || !currentFile(target)) return;
    listRequest?.cancel(); loading.value = false; dispatched = true;
    const deleted = await deleteHostFile(target.id, request.signal); if (!request.current()) return;
    if (deleted.id.toLowerCase() !== target.id.toLowerCase()) throw new Error('client.invalid_host_file_identity');
    listRequest?.cancel(); loading.value = false; removeConfirmedFiles([target.id]);
    ElMessage.success(t('hostFiles.deleteSuccess')); await load();
  } catch (error) { if (request.current()) {
    if (dispatched) markUnconfirmed([target.id]);
    problem.value = { ...toProblem(error, 'hostFiles.operationFailed'), detail: t('hostFiles.deleteReceiptHint') };
  } } finally { finishAction(request); }
}

async function download(item: HostFile): Promise<void> {
  if (!currentFile(item)) return; const request = beginAction('files.files.download'); if (!request) return;
  try { const blob = await downloadHostFileContent(item.id, request.signal); if (request.current()) openHostFileBlob(blob, item.originalFileName); }
  catch (error) { if (request.current()) problem.value = toProblem(error, 'hostFiles.operationFailed'); }
  finally { finishAction(request); }
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostFiles.loadFailed' | 'hostFiles.operationFailed' = 'hostFiles.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_file_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section v-if="canRead" class="host-files-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostFiles.title') }}</h1>
    <p v-if="unconfirmedFileIds.size || unconfirmedFolderIds.size || folderCreationUnconfirmed" role="status">{{ t('hostFiles.mutationRefreshHint') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="host-files-layout">
      <el-card shadow="never" class="host-files-tree-card">
        <h2>{{ t('hostFiles.folderTreeTitle') }}</h2>
        <el-button plain size="small" data-testid="host-files-folder-refresh" :disabled="changing" @click="loadFolders">{{ t('common.refresh') }}</el-button>
        <el-tree
          :data="folderTree"
          node-key="id"
          default-expand-all
          highlight-current
          @node-click="onFolderSelected"
        />
        <PermissionGate code="files.folders.create">
          <div class="host-files-folder-create">
            <el-input v-model="newFolderName" :placeholder="t('hostFiles.newFolderPlaceholder')" />
            <el-button type="primary" :loading="changing" :disabled="folderCreationUnconfirmed || !!selectedFolder && folderUnconfirmed(selectedFolder.id)" @click="createFolder">
              {{ t('hostFiles.createFolder') }}
            </el-button>
          </div>
        </PermissionGate>
        <PermissionGate code="files.folders.update">
          <el-button
            v-if="selectedFolder"
            plain
            size="small"
            :disabled="changing || folderUnconfirmed(selectedFolder.id)"
            @click="renameFolder({ id: selectedFolder.id, label: selectedFolder.name, folder: selectedFolder })"
          >
            {{ t('hostFiles.renameFolder') }}
          </el-button>
        </PermissionGate>
        <PermissionGate code="files.folders.delete">
          <el-button
            v-if="selectedFolder"
            type="danger"
            plain
            size="small"
            :disabled="changing || folderUnconfirmed(selectedFolder.id)"
            @click="removeFolder({ id: selectedFolder.id, label: selectedFolder.name, folder: selectedFolder })"
          >
            {{ t('hostFiles.deleteFolder') }}
          </el-button>
        </PermissionGate>
      </el-card>

      <div class="host-files-main">
        <PermissionGate code="files.files.upload">
          <el-card shadow="never" class="art-form-card" aria-labelledby="upload-host-file-title">
            <div><h2 id="upload-host-file-title">{{ t('hostFiles.uploadTitle') }}</h2></div>
            <label>
              <span>{{ t('hostFiles.chooseFile') }}</span>
              <input type="file" multiple data-testid="host-files-file-input" @change="onFileSelected" />
            </label>
            <el-button
              type="primary"
              data-testid="host-files-upload"
              :loading="changing"
              :disabled="selectedFiles.length === 0"
              @click="upload"
            >
              {{ t('hostFiles.upload') }}
            </el-button>
            <el-table
              v-if="uploadResults"
              :data="uploadResults.results"
              size="small"
              class="host-files-upload-results"
            >
              <el-table-column prop="originalFileName" :label="t('hostFiles.fileName')" />
              <el-table-column :label="t('hostFiles.uploadResult')" width="120">
                <template #default="{ row }">
                  {{ row.succeeded ? t('hostFiles.uploadSucceeded') : t('hostFiles.uploadFailed') }}
                </template>
              </el-table-column>
              <el-table-column prop="errorCode" :label="t('hostFiles.errorCode')" />
            </el-table>
          </el-card>
        </PermissionGate>

        <el-card v-if="deleteReceipt" shadow="never" data-testid="host-files-delete-receipt">
          <h2>{{ t('hostFiles.deleteReceiptTitle') }}</h2>
          <p>{{ t('hostFiles.batchDeleteSummary', { succeeded: deleteReceipt.succeeded, total: deleteReceipt.total }) }}</p>
          <p>{{ t('hostFiles.deleteReceiptHint') }}</p>
          <el-table :data="deleteReceipt.items" :max-height="200" size="small">
            <el-table-column prop="name" :label="t('hostFiles.fileName')" />
            <el-table-column :label="t('hostFiles.deleteResult')"><template #default="{ row }">
              {{ t(row.outcome === 'succeeded' ? 'hostFiles.deleteSucceeded' : row.outcome === 'failed' ? 'hostFiles.deleteFailed' : row.outcome === 'processing' ? 'hostFiles.deleteProcessing' : 'hostFiles.deleteUnconfirmed') }}
            </template></el-table-column>
            <el-table-column prop="errorCode" :label="t('hostFiles.errorCode')" />
          </el-table>
        </el-card>

        <ArtSearchBar
          v-model="searchForm"
          :items="searchItems"
          :default-visible-count="1"
          :show-expand="false"
          :search-label="t('hostFiles.query')"
          :reset-label="t('hostFiles.reset')"
          @search="handleSearch"
          @reset="resetSearch"
        />

        <el-card class="art-table-card" shadow="never">
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
                <PermissionGate code="files.files.delete">
                  <el-button
                    type="danger"
                    plain
                    :disabled="!hasSelectedRows || changing || selectedRows.some(row => fileUnconfirmed(row.id))"
                    data-testid="host-files-batch-delete"
                    @click="removeSelected"
                  >
                    {{ t('hostFiles.batchDelete') }}
                  </el-button>
                </PermissionGate>
              </template>
            </ArtTableHeader>

            <div class="art-table" :class="{ 'is-empty': pagedItems.length === 0 }">
              <el-table
                v-loading="loading"
                :data="pagedItems"
                :height="tableHeight"
                :size="tableSize"
                :stripe="tableZebra"
                :border="tableBorder"
                :header-cell-style="tableHeaderCellStyle"
                class="art-crud-data-table"
                :class="{ 'art-table--header-bg': tableHeaderBackground }"
                @selection-change="rows => { selectedRows = rows as HostFile[]; }"
              >
                <el-table-column type="selection" width="48" />
                <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                  <template #default="{ $index }">{{ rowIndex($index) }}</template>
                </el-table-column>

                <el-table-column :label="t('hostFiles.fileName')" min-width="200">
                  <template #default="{ row }">
                    <div>
                      <div translate="no">{{ row.originalFileName }}</div>
                      <code translate="no">{{ row.contentType }}</code>
                    </div>
                  </template>
                </el-table-column>

                <el-table-column :label="t('hostFiles.sizeBytes')" width="120" align="center" prop="sizeBytes" />
                <el-table-column :label="t('hostFiles.createdAt')" min-width="180" prop="createdAtUtc" />

                <el-table-column :label="t('users.columnActions')" width="260" fixed="right" align="center">
                  <template #default="{ row }">
                    <ArtTableActionGroup>
                      <PermissionGate code="files.files.update">
                        <el-button plain size="small" :disabled="changing || fileUnconfirmed(row.id)" @click="openMetadataDialog(row as HostFile)">
                          {{ t('hostFiles.editMetadata') }}
                        </el-button>
                      </PermissionGate>
                      <PermissionGate code="files.file_references.read">
                        <el-button plain size="small" :disabled="changing" @click="openReferences(row as HostFile)">
                          {{ t('hostFiles.references') }}
                        </el-button>
                      </PermissionGate>
                      <PermissionGate code="files.files.read">
                        <el-button
                          v-if="canPreviewFile(row as HostFile)"
                          plain
                          size="small"
                          :disabled="changing"
                          @click="preview(row as HostFile)"
                        >
                          {{ t('hostFiles.preview') }}
                        </el-button>
                      </PermissionGate>
                      <PermissionGate code="files.files.download">
                        <el-button
                          plain
                          size="small"
                          data-testid="host-files-download"
                          :disabled="changing"
                          @click="download(row as HostFile)"
                        >
                          {{ t('hostFiles.download') }}
                        </el-button>
                      </PermissionGate>
                      <PermissionGate code="files.files.delete">
                        <el-button
                          type="danger"
                          plain
                          size="small"
                          data-testid="host-files-delete"
                          :disabled="changing || fileUnconfirmed(row.id)"
                          @click="remove(row as HostFile)"
                        >
                          {{ t('hostFiles.delete') }}
                        </el-button>
                      </PermissionGate>
                    </ArtTableActionGroup>
                  </template>
                </el-table-column>

                <template #empty>{{ t('hostFiles.emptyDirectory') }}</template>
              </el-table>

              <div class="art-table__pagination center custom-pagination">
                <el-pagination
                  v-model:current-page="page"
                  v-model:page-size="pageSize"
                  :total="total"
                  background
                  layout="total, sizes, prev, pager, next, jumper"
                  :page-sizes="[10, 20, 50, 100]"
                  @current-change="load"
                  @size-change="load"
                />
              </div>
            </div>
          </div>
        </el-card>
      </div>
    </div>

    <el-dialog v-if="confirmation" :key="confirmation.token" :model-value="true" :title="confirmation.title" width="460px" :close-on-click-modal="false"
      v-on="{ 'update:modelValue': confirmation.update }">
      <p data-testid="host-files-confirm-message">{{ confirmation?.message }}</p>
      <el-form v-if="confirmation?.input"><el-form-item :label="t('hostFiles.folderName')" required>
        <el-input v-model="confirmationInput" data-testid="host-files-confirm-input" />
      </el-form-item></el-form>
      <template #footer>
        <el-button data-testid="host-files-confirm-dismiss" v-on="{ click: confirmation.dismiss }">{{ t('common.cancel') }}</el-button>
        <el-button type="warning" data-testid="host-files-confirm-accept" :disabled="confirmation?.input && !confirmationInput.trim()" v-on="{ click: confirmation.confirm }">{{ t('common.confirm') }}</el-button>
      </template>
    </el-dialog>
    <el-dialog v-if="metadataIntent" :key="metadataIntent.token" :model-value="metadataDialogVisible" :title="t('hostFiles.editMetadata')" width="480px"
      v-on="{ 'update:modelValue': metadataIntent.update }">
      <el-form label-position="top">
        <el-form-item :label="t('hostFiles.fileName')" required>
          <el-input v-model="metadataForm.originalFileName" />
        </el-form-item>
        <el-form-item :label="t('hostFiles.folderId')">
          <el-input v-model="metadataForm.folderId" :placeholder="t('hostFiles.folderIdPlaceholder')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button data-testid="host-files-metadata-back" v-on="{ click: metadataIntent.close }">{{ t('status.back') }}</el-button>
        <el-button data-testid="host-files-metadata-save" type="primary" :loading="changing" v-on="{ click: metadataIntent.save }">{{ t('hostFiles.saveMetadata') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="previewDialogVisible"
      :title="previewTitle"
      width="70%"
    >
      <iframe
        v-if="previewUrl"
        :src="previewUrl"
        class="host-files-preview-frame"
        title="preview"
        sandbox=""
        referrerpolicy="no-referrer"
      />
    </el-dialog>

    <el-drawer v-model="referencesDrawerVisible" :title="t('hostFiles.references')" size="40%">
      <el-table :data="references" size="small">
        <el-table-column prop="consumerModule" :label="t('hostFiles.referenceModule')" />
        <el-table-column prop="state" :label="t('hostFiles.referenceState')" />
        <el-table-column prop="consumerReferenceId" :label="t('hostFiles.referenceId')" />
      </el-table>
    </el-drawer>
  </section>
</template>

<style scoped>
.host-files-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.host-files-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.host-files-layout {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 16px;
  min-height: 0;
  flex: 1;
}

.host-files-tree-card,
.host-files-main {
  min-height: 0;
}

.host-files-folder-create {
  display: grid;
  gap: 8px;
  margin-top: 12px;
}

.host-files-upload-results {
  margin-top: 12px;
}

.host-files-preview-frame {
  width: 100%;
  min-height: 480px;
  border: 0;
}
</style>
