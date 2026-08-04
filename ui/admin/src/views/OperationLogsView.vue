<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElCard, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { AuditingOperationLog, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingOperationLogs } from '../api/operation-logs';

defineOptions({ name: 'OperationLogsView' });

const { t } = useAdminI18n();
const items = ref<AuditingOperationLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

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

const filteredItems = computed(() => items.value);
const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

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
    const result = await listAuditingOperationLogs();
    items.value = result.items;
    resetPage();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.auditing_operation_log_failed',
        title: t('operationLogs.loadFailed')
      };
}
</script>

<template>
  <section class="operation-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('operationLogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never" class="art-table-card">
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

            <el-table-column :label="t('operationLogs.actionKey')" min-width="200" prop="actionKey" />

            <el-table-column :label="t('operationLogs.statusCode')" width="100" align="center" prop="statusCode" />

            <el-table-column :label="t('operationLogs.durationMs')" width="120" align="center" prop="durationMs" />

            <el-table-column :label="t('operationLogs.occurredAt')" min-width="180" prop="occurredAtUtc" />

            <el-table-column :label="t('users.status')" width="100" align="center">
              <template #default="{ row }">
                <el-tag :type="row.succeeded ? 'success' : 'danger'" effect="plain">
                  {{ t(row.succeeded ? 'operationLogs.succeeded' : 'operationLogs.failed') }}
                </el-tag>
              </template>
            </el-table-column>

            <template #empty>{{ t('operationLogs.emptyDirectory') }}</template>
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
.operation-logs-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.operation-logs-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
