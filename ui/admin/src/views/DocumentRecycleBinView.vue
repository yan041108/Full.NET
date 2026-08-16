<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
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
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
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

watchLoading(loading);

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
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
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
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentRecycleBin.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
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
          layout="refresh,size,fullscreen"
          @refresh="load"
        />

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            v-loading="loading"
            :data="items"
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

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next"
              :page-sizes="[10, 20, 50]"
              @current-change="load"
              @size-change="load"
            />
          </div>
        </div>
      </div>
    </el-card>
  </section>
</template>
