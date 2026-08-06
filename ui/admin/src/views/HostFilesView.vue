<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElMessageBox, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails, HostFile } from '@fullnet/client-contracts';
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
  deleteHostFile,
  downloadHostFileContent,
  listHostFiles,
  openHostFileBlob,
  uploadHostFile
} from '../api/host-files';

defineOptions({ name: 'HostFilesView' });

interface AppliedFilters {
  fileName: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostFile[]>([]);
const selectedFile = ref<File | null>(null);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ fileName: '' });
const canUpload = computed(() => session.can('files.files.upload'));
const canDownload = computed(() => session.can('files.files.download'));
const canDelete = computed(() => session.can('files.files.delete'));

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
  const keyword = appliedFilters.value.fileName.trim().toLowerCase();
  if (!keyword) {
    return items.value;
  }
  return items.value.filter(item => item.originalFileName.toLowerCase().includes(keyword));
});

const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'fileName',
    label: t('hostFiles.fileName'),
    placeholder: t('hostFiles.searchPlaceholder')
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const pageResult = await listHostFiles();
    items.value = pageResult.items;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { fileName: params.fileName ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { fileName: '' };
  resetPage();
}

function onFileSelected(event: Event): void {
  const input = event.target as HTMLInputElement;
  selectedFile.value = input.files?.[0] ?? null;
}

async function upload(): Promise<void> {
  if (changing.value || !selectedFile.value || !canUpload.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await uploadHostFile(selectedFile.value);
    selectedFile.value = null;
    ElMessage.success(t('hostFiles.uploadSuccess'));
    await load();
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

    <PermissionGate code="files.files.upload">
      <el-card shadow="never" class="art-form-card" aria-labelledby="upload-host-file-title">
        <div><h2 id="upload-host-file-title">{{ t('hostFiles.uploadTitle') }}</h2></div>
        <label>
          <span>{{ t('hostFiles.chooseFile') }}</span>
          <input type="file" data-testid="host-files-file-input" @change="onFileSelected" />
        </label>
        <el-button
          type="primary"
          data-testid="host-files-upload"
          :loading="changing"
          :disabled="!selectedFile"
          @click="upload"
        >
          {{ t('hostFiles.upload') }}
        </el-button>
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

            <el-table-column :label="t('users.columnActions')" width="180" fixed="right" align="center">
              <template #default="{ row }">
                <div class="art-crud-table-actions">
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
                </div>
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
            />
          </div>
        </div>
      </div>
    </el-card>
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
</style>
