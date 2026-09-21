<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { RefreshLeft, Search } from '@element-plus/icons-vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn
} from 'element-plus';
import type { FullNetProblemDetails, HostRecycleBinItemResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  listRecycleBinItems,
  purgeRecycleBinItem,
  restoreRecycleBinItem
} from '../api/document-recycle-bin';

defineOptions({ name: 'DocumentRecycleBinView' });

const { t } = useAdminI18n();
const items = ref<HostRecycleBinItemResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const searchKeyword = ref('');
const appliedKeyword = ref('');
const selectedRows = ref<HostRecycleBinItemResponse[]>([]);

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

const displayItems = computed(() => {
  const keyword = appliedKeyword.value.trim().toLowerCase();
  if (!keyword) {
    return items.value;
  }
  return items.value.filter(item => item.title.toLowerCase().includes(keyword));
});

const rowIndex = computed(() => (index: number) => (page.value - 1) * pageSize.value + index + 1);

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listRecycleBinItems(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    selectedRows.value = [];
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

function handleSearch() {
  appliedKeyword.value = searchKeyword.value;
  void syncTableLayout();
}

function handleSearchClear() {
  appliedKeyword.value = '';
  void syncTableLayout();
}

function onSelectionChange(rows: HostRecycleBinItemResponse[]) {
  selectedRows.value = rows;
}

async function restore(item: HostRecycleBinItemResponse) {
  changing.value = true;
  try {
    await restoreRecycleBinItem(item.id, { version: item.version });
    ElMessage.success(t('documentRecycleBin.restoreSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentRecycleBin.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function purge(item: HostRecycleBinItemResponse) {
  try {
    await ElMessageBox.confirm(
      t('documentRecycleBin.confirmPurge', { name: item.title }),
      t('documentRecycleBin.purge'),
      {
        type: 'warning',
        confirmButtonText: t('documentRecycleBin.purge'),
        cancelButtonText: t('users.cancel')
      }
    );
  } catch {
    return;
  }

  changing.value = true;
  try {
    await purgeRecycleBinItem(item.id);
    ElMessage.success(t('documentRecycleBin.purgeSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentRecycleBin.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function batchRestore() {
  if (selectedRows.value.length === 0) {
    return;
  }
  changing.value = true;
  try {
    for (const item of selectedRows.value) {
      await restoreRecycleBinItem(item.id, { version: item.version });
    }
    ElMessage.success(t('documentRecycleBin.restoreSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentRecycleBin.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function batchPurge() {
  if (selectedRows.value.length === 0) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('documentRecycleBin.confirmPurge', { name: `${selectedRows.value.length}` }),
      t('documentRecycleBin.batchPurge'),
      {
        type: 'warning',
        confirmButtonText: t('documentRecycleBin.purge'),
        cancelButtonText: t('users.cancel')
      }
    );
  } catch {
    return;
  }

  changing.value = true;
  try {
    for (const item of selectedRows.value) {
      await purgeRecycleBinItem(item.id);
    }
    ElMessage.success(t('documentRecycleBin.purgeSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentRecycleBin.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentRecycleBin.loadFailed' | 'documentRecycleBin.operationFailed' = 'documentRecycleBin.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

onMounted(load);
</script>

<template>
  <section class="document-recycle-bin-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentRecycleBin.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card class="document-module-toolbar-card" shadow="never">
      <div class="document-module-toolbar">
        <div class="document-module-toolbar__left">
          <PermissionGate code="document.host_recycle_bin.restore">
            <el-button
              type="success"
              :icon="RefreshLeft"
              :disabled="selectedRows.length === 0 || changing"
              data-testid="document-recycle-batch-restore"
              @click="batchRestore"
            >
              {{ t('documentRecycleBin.batchRestore') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="document.host_recycle_bin.purge">
            <el-button
              type="danger"
              plain
              :disabled="selectedRows.length === 0 || changing"
              data-testid="document-recycle-batch-purge"
              @click="batchPurge"
            >
              {{ t('documentRecycleBin.batchPurge') }}
            </el-button>
          </PermissionGate>
        </div>
        <div class="document-module-toolbar__right">
          <el-input
            v-model="searchKeyword"
            :placeholder="t('documentRecycleBin.searchPlaceholder')"
            clearable
            style="width: 280px"
            @keyup.enter="handleSearch"
            @clear="handleSearchClear"
          />
          <el-button :icon="Search" @click="handleSearch">{{ t('documentRecycleBin.search') }}</el-button>
        </div>
      </div>
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <el-alert type="warning" :closable="false" show-icon class="document-recycle-bin-view__hint">
          {{ t('documentRecycleBin.alertHint') }}
        </el-alert>

        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen"
          @refresh="load"
        />

        <div class="art-table" :class="{ 'is-empty': displayItems.length === 0 }">
          <el-table
            v-loading="loading"
            :data="displayItems"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            row-key="id"
            @selection-change="onSelectionChange"
          >
            <el-table-column type="selection" width="48" />
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>
            <el-table-column :label="t('documentRecycleBin.titleColumn')" min-width="240" prop="title" />
            <el-table-column :label="t('documentRecycleBin.deletedAt')" min-width="180" prop="deletedAtUtc" />
            <el-table-column :label="t('users.columnActions')" width="140" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.host_recycle_bin.restore">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-recycle-restore"
                      :title="t('documentRecycleBin.restore')"
                      :disabled="changing"
                      @click="restore(row as HostRecycleBinItemResponse)"
                    />
                  </PermissionGate>
                  <PermissionGate code="document.host_recycle_bin.purge">
                    <ArtTableActionButton
                      type="delete"
                      test-id="document-recycle-purge"
                      :title="t('documentRecycleBin.purge')"
                      :disabled="changing"
                      @click="purge(row as HostRecycleBinItemResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentRecycleBin.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>
  </section>
</template>

<style scoped>
.document-recycle-bin-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-recycle-bin-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-recycle-bin-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-recycle-bin-view__hint {
  margin-bottom: 12px;
}
</style>
