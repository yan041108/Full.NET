<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox, ElPagination, ElTable, ElTableColumn } from 'element-plus';
// 为避免 barrel 层重复标识符冲突，此处用新版 Response 类型别名旧名
import type { FullNetProblemDetails, HostDocumentItemResponse as HostDocumentItem } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createDocumentItem,
  deleteDocumentItem,
  downloadDocumentContent,
  listDocumentItems,
  openDocumentBlob,
  restoreDocumentItem,
  updateDocumentItem,
  uploadDocumentVersion
} from '../api/document-items';

defineOptions({ name: 'HostDocumentItemsView' });

interface AppliedFilters {
  title: string;
}

interface DeletedDocumentEntry {
  item: HostDocumentItem;
  restoreVersion: number;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentItem[]>([]);
const title = ref('');
const description = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const versionFile = ref<File | null>(null);
const versionTargetId = ref<string>();
const editingId = ref<string>();
const recentlyDeleted = ref<DeletedDocumentEntry[]>([]);
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '' });
const canCreate = computed(() => session.can('document.host_documents.create'));
const canUpdate = computed(() => session.can('document.host_documents.update'));
const canAddVersion = computed(() => session.can('document.host_documents.add_version'));
const canDelete = computed(() => session.can('document.host_documents.delete'));
const canRestore = computed(() => session.can('document.host_documents.restore'));
const canDownload = computed(() => session.can('document.host_documents.download'));
const editingItem = computed(() => items.value.find(entry => entry.id === editingId.value));

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

const filteredItems = computed(() => {
  const keyword = appliedFilters.value.title.trim().toLowerCase();
  if (!keyword) {
    return items.value;
  }
  return items.value.filter(item => item.title.toLowerCase().includes(keyword));
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

watchLoading(loading);

onMounted(() => {
  void load();
});

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
    const pageResult = await listDocumentItems();
    items.value = pageResult.items;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { title: params.title ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { title: '' };
  resetPage();
}

async function create(): Promise<void> {
  if (changing.value || !canCreate.value || !title.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentItem(title.value.trim(), description.value.trim() || null);
    title.value = '';
    description.value = '';
    ElMessage.success(t('hostDocumentItems.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
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
  title.value = item.title;
  description.value = item.description ?? '';
}

function cancelEdit(): void {
  editingId.value = undefined;
  title.value = '';
  description.value = '';
}

async function saveEdit(item: HostDocumentItem): Promise<void> {
  if (changing.value || !canUpdate.value || editingId.value !== item.id) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateDocumentItem(
      item.id,
      title.value.trim(),
      description.value.trim() || null,
      item.version
    );
    editingId.value = undefined;
    title.value = '';
    description.value = '';
    ElMessage.success(t('hostDocumentItems.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
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

function findDeletedEntry(item: HostDocumentItem): DeletedDocumentEntry | undefined {
  return recentlyDeleted.value.find(entry => entry.item.id === item.id);
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostDocumentItems.loadFailed' | 'hostDocumentItems.operationFailed' = 'hostDocumentItems.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_item_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-document-items-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostDocumentItems.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canCreate && !editingId" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-document-item-title">
        <div><h2 id="create-document-item-title">{{ t('hostDocumentItems.createTitle') }}</h2></div>
        <label>
          <span>{{ t('hostDocumentItems.titleLabel') }}</span>
          <el-input v-model="title" data-testid="host-document-item-title" :placeholder="t('hostDocumentItems.titlePlaceholder')" />
        </label>
        <label>
          <span>{{ t('hostDocumentItems.descriptionLabel') }}</span>
          <el-input v-model="description" data-testid="host-document-item-description" :placeholder="t('hostDocumentItems.descriptionPlaceholder')" />
        </label>
        <PermissionGate code="document.host_documents.create">
          <el-button type="primary" data-testid="host-document-item-create" :loading="changing" @click="create">
            {{ t('hostDocumentItems.create') }}
          </el-button>
        </PermissionGate>
      </div>
    </el-card>

    <el-card v-if="editingId && canUpdate" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="edit-document-item-title">
        <div><h2 id="edit-document-item-title">{{ t('hostDocumentItems.editTitle') }}</h2></div>
        <label>
          <span>{{ t('hostDocumentItems.titleLabel') }}</span>
          <el-input v-model="title" data-testid="host-document-item-edit-title" />
        </label>
        <label>
          <span>{{ t('hostDocumentItems.descriptionLabel') }}</span>
          <el-input v-model="description" data-testid="host-document-item-edit-description" />
        </label>
        <PermissionGate code="document.host_documents.update">
          <el-button type="primary" data-testid="host-document-item-save" :loading="changing" :disabled="!editingItem" @click="editingItem && saveEdit(editingItem)">
            {{ t('hostDocumentItems.save') }}
          </el-button>
        </PermissionGate>
        <el-button plain :disabled="changing" @click="cancelEdit">{{ t('hostDocumentItems.cancel') }}</el-button>
      </div>
    </el-card>

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
        />

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
          >
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

            <el-table-column :label="t('users.columnActions')" width="320" fixed="right" align="center">
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate v-if="row.currentVersion" code="document.host_documents.download">
                    <el-button plain size="small" data-testid="host-document-item-download" :disabled="changing" @click="downloadFile(row.id)">
                      {{ t('hostDocumentItems.download') }}
                    </el-button>
                  </PermissionGate>
                  <PermissionGate code="document.host_documents.update">
              <el-button plain size="small" data-testid="host-document-item-edit" @click="startEdit(row as HostDocumentItem)">
                      {{ t('hostDocumentItems.edit') }}
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

        <div class="art-table__pagination center custom-pagination">
          <el-pagination
            v-model:current-page="deletedPage"
            v-model:page-size="deletedPageSize"
            :total="deletedTotal"
            background
            layout="total, sizes, prev, pager, next, jumper"
            :page-sizes="[10, 20, 50, 100]"
          />
        </div>
      </div>
    </el-card>
  </section>
</template>

<style scoped>
.host-document-items-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
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

.host-document-items-view__version-input {
  display: inline-flex;
  align-items: center;
}
</style>
