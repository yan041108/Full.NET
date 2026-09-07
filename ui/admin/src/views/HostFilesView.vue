<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDialog,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTree
} from 'element-plus';
import type {
  BatchDeleteHostFilesResponse,
  BatchUploadHostFilesResponse,
  FullNetProblemDetails,
  HostFile,
  HostFileReferenceClaimResponse,
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
const previewDialogVisible = ref(false);
const previewUrl = ref<string | null>(null);
const previewTitle = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ fileName: '' });
const metadataDialogVisible = ref(false);
const referencesDrawerVisible = ref(false);
const references = ref<HostFileReferenceClaimResponse[]>([]);
const editingFile = ref<HostFile | null>(null);
const metadataForm = ref({ originalFileName: '', folderId: '' });
const newFolderName = ref('');
const canUpload = computed(() => session.can('files.files.upload'));
const canDownload = computed(() => session.can('files.files.download'));
const canDelete = computed(() => session.can('files.files.delete'));
const canUpdate = computed(() => session.can('files.files.update'));
const canReadReferences = computed(() => session.can('files.file_references.read'));
const canCreateFolder = computed(() => session.can('files.folders.create'));
const canUpdateFolder = computed(() => session.can('files.folders.update'));
const canDeleteFolder = computed(() => session.can('files.folders.delete'));
const canPreview = computed(() => session.can('files.files.read'));
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

onMounted(() => {
  void refreshAll();
});

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
  try {
    const tree = await listHostFolderTree();
    folderTree.value = [
      {
        id: ROOT_NODE_ID,
        label: t('hostFiles.rootFolder'),
        folder: null,
        children: mapFolderNodes(tree)
      }
    ];
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const folderId = selectedFolder.value
      ? selectedFolder.value.id
      : selectedFolder.value === null && folderTree.value.length > 0
        ? ''
        : undefined;
    const pageResult = await listHostFiles(page.value, pageSize.value, {
      folderId,
      fileNameContains: appliedFilters.value.fileName
    });
    items.value = pageResult.items;
    total.value = pageResult.total;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function onFolderSelected(node: TreeNode): void {
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
  const input = event.target as HTMLInputElement;
  selectedFiles.value = input.files ? Array.from(input.files) : [];
}

async function upload(): Promise<void> {
  if (changing.value || selectedFiles.value.length === 0 || !canUpload.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const folderId = selectedFolder.value?.id;
    if (selectedFiles.value.length === 1) {
      await uploadHostFile(selectedFiles.value[0], folderId);
      uploadResults.value = null;
      ElMessage.success(t('hostFiles.uploadSuccess'));
    } else {
      const result = await batchUploadHostFiles(selectedFiles.value, folderId);
      uploadResults.value = result;
      ElMessage.success(
        t('hostFiles.batchUploadSummary', {
          succeeded: result.succeededCount,
          total: result.results.length
        })
      );
    }
    selectedFiles.value = [];
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
    ElMessage.error(t('hostFiles.operationFailed'));
  } finally {
    changing.value = false;
  }
}

async function removeSelected(): Promise<void> {
  if (changing.value || !canDelete.value || selectedRows.value.length === 0) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('hostFiles.confirmBatchDelete', { count: selectedRows.value.length }),
      { type: 'warning' }
    );
  } catch {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const result = await batchDeleteHostFiles(selectedRows.value.map(row => row.id));
    ElMessage.success(
      t('hostFiles.batchDeleteSummary', {
        succeeded: result.succeededCount,
        total: result.results.length
      })
    );
    if (result.succeededCount < result.results.length) {
      const failed = result.results.filter(item => !item.succeeded);
      ElMessage.warning(
        failed.map(item => `${item.fileId}: ${item.errorCode ?? item.message ?? ''}`).join('\n')
      );
    }
    selectedRows.value = [];
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
    ElMessage.error(t('hostFiles.operationFailed'));
  } finally {
    changing.value = false;
  }
}

async function preview(file: HostFile): Promise<void> {
  if (changing.value || !canPreview.value || !isPreviewableHostFile(file.contentType)) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    if (previewUrl.value) {
      URL.revokeObjectURL(previewUrl.value);
      previewUrl.value = null;
    }
    const blob = await previewHostFileContent(file.id);
    previewUrl.value = URL.createObjectURL(blob);
    previewTitle.value = file.originalFileName;
    previewDialogVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error);
    ElMessage.error(t('hostFiles.previewFailed'));
  } finally {
    changing.value = false;
  }
}

function closePreviewDialog(): void {
  previewDialogVisible.value = false;
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value);
    previewUrl.value = null;
  }
}

function canPreviewFile(file: HostFile): boolean {
  return canPreview.value && isPreviewableHostFile(file.contentType);
}

async function createFolder(): Promise<void> {
  if (!canCreateFolder.value || !newFolderName.value.trim()) {
    return;
  }
  changing.value = true;
  try {
    await createHostFolder({
      parentId: selectedFolder.value?.id ?? null,
      name: newFolderName.value.trim()
    });
    newFolderName.value = '';
    ElMessage.success(t('hostFiles.folderCreateSuccess'));
    await refreshAll();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function renameFolder(node: TreeNode): Promise<void> {
  if (!canUpdateFolder.value || !node.folder) {
    return;
  }
  try {
    const { value } = await ElMessageBox.prompt(
      t('hostFiles.folderName'),
      t('hostFiles.renameFolder'),
      { inputValue: node.folder.name }
    );
    changing.value = true;
    await updateHostFolder(node.folder.id, {
      expectedRevision: node.folder.revision,
      name: value.trim(),
      displayOrder: node.folder.displayOrder
    });
    ElMessage.success(t('hostFiles.folderUpdateSuccess'));
    await refreshAll();
  } catch (error: unknown) {
    if (error !== 'cancel' && error !== 'close') {
      problem.value = toProblem(error, 'hostFiles.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

async function removeFolder(node: TreeNode): Promise<void> {
  if (!canDeleteFolder.value || !node.folder) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostFiles.confirmDeleteFolder', { name: node.folder.name }),
      t('hostFiles.deleteFolder'),
      { type: 'warning' }
    );
    changing.value = true;
    await deleteHostFolder(node.folder.id, { expectedRevision: node.folder.revision });
    if (selectedFolder.value?.id === node.folder.id) {
      selectedFolder.value = null;
    }
    ElMessage.success(t('hostFiles.folderDeleteSuccess'));
    await refreshAll();
  } catch (error: unknown) {
    if (error !== 'cancel' && error !== 'close') {
      problem.value = toProblem(error, 'hostFiles.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

function openMetadataDialog(item: HostFile): void {
  editingFile.value = item;
  metadataForm.value = {
    originalFileName: item.originalFileName,
    folderId: item.folderId ?? ''
  };
  metadataDialogVisible.value = true;
}

async function saveMetadata(): Promise<void> {
  if (!editingFile.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  try {
    await updateHostFileMetadata(editingFile.value.id, {
      expectedRevision: editingFile.value.revision,
      originalFileName: metadataForm.value.originalFileName.trim(),
      folderId: metadataForm.value.folderId ? metadataForm.value.folderId : null
    });
    metadataDialogVisible.value = false;
    ElMessage.success(t('hostFiles.metadataUpdateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openReferences(item: HostFile): Promise<void> {
  if (!canReadReferences.value) {
    return;
  }
  changing.value = true;
  try {
    const pageResult = await listHostFileReferences(item.id);
    references.value = pageResult.items;
    referencesDrawerVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(item: HostFile): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostFiles.confirmDelete', { name: item.originalFileName }),
      t('hostFiles.delete'),
      {
        type: 'warning',
        confirmButtonText: t('hostFiles.delete'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await deleteHostFile(item.id);
    ElMessage.success(t('hostFiles.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function download(item: HostFile): Promise<void> {
  if (changing.value || !canDownload.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const blob = await downloadHostFileContent(item.id);
    openHostFileBlob(blob);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
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
  <section class="host-files-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostFiles.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="host-files-layout">
      <el-card shadow="never" class="host-files-tree-card">
        <h2>{{ t('hostFiles.folderTreeTitle') }}</h2>
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
            <el-button type="primary" :loading="changing" @click="createFolder">
              {{ t('hostFiles.createFolder') }}
            </el-button>
          </div>
        </PermissionGate>
        <PermissionGate code="files.folders.update">
          <el-button
            v-if="selectedFolder"
            plain
            size="small"
            :disabled="changing"
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
            :disabled="changing"
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
                    :disabled="!hasSelectedRows || changing"
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
                        <el-button plain size="small" :disabled="changing" @click="openMetadataDialog(row as HostFile)">
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
                          :disabled="changing"
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

    <el-dialog v-model="metadataDialogVisible" :title="t('hostFiles.editMetadata')" width="480px">
      <el-form label-position="top">
        <el-form-item :label="t('hostFiles.fileName')">
          <el-input v-model="metadataForm.originalFileName" />
        </el-form-item>
        <el-form-item :label="t('hostFiles.folderId')">
          <el-input v-model="metadataForm.folderId" :placeholder="t('hostFiles.folderIdPlaceholder')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="metadataDialogVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="saveMetadata">{{ t('hostFiles.saveMetadata') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="previewDialogVisible"
      :title="previewTitle"
      width="70%"
      @closed="closePreviewDialog"
    >
      <iframe
        v-if="previewUrl"
        :src="previewUrl"
        class="host-files-preview-frame"
        title="preview"
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
