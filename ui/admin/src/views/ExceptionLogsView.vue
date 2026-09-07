<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { AuditingExceptionLog, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import AuditLogDetailDrawer, {
  type AuditLogDetailRecord
} from './components/AuditLogDetailDrawer.vue';
import AuditLogExportDialog from './components/AuditLogExportDialog.vue';
import AuditLogTrendPanel from './components/AuditLogTrendPanel.vue';
import PermissionGate from '../components/PermissionGate.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingExceptionLogs } from '../api/exception-logs';

defineOptions({ name: 'ExceptionLogsView' });

const { t } = useAdminI18n();
const items = ref<AuditingExceptionLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const detailOpen = ref(false);
const selectedRecord = ref<AuditLogDetailRecord | null>(null);
const exportOpen = ref(false);

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
    const result = await listAuditingExceptionLogs();
    items.value = result.items;
    resetPage();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function openDetail(row: AuditingExceptionLog): void {
  selectedRecord.value = {
    id: row.id,
    occurredAtUtc: row.occurredAtUtc,
    traceId: row.traceId,
    title: row.exceptionType,
    subtitle: row.message,
    fields: [
      { label: t('exceptionLogs.exceptionType'), value: row.exceptionType },
      { label: t('exceptionLogs.message'), value: row.message },
      { label: t('accessLogs.httpMethod'), value: row.httpMethod },
      { label: t('exceptionLogs.requestPath'), value: row.requestPath },
      { label: t('exceptionLogs.occurredAt'), value: row.occurredAtUtc },
      { label: 'TraceId', value: row.traceId }
    ]
  };
  detailOpen.value = true;
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.auditing_exception_log_failed',
        title: t('exceptionLogs.loadFailed')
      };
}
</script>

<template>
  <section class="exception-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('exceptionLogs.title') }}</h1>

    <AuditLogTrendPanel kind="exception" />

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
        >
          <template #left>
            <PermissionGate code="auditing.exceptions.export">
              <el-button data-testid="exception-logs-action-export" @click="exportOpen = true">
                {{ t('auditExport.action') }}
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
            @row-click="openDetail"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('exceptionLogs.exceptionType')" min-width="180" prop="exceptionType" />

            <el-table-column :label="t('exceptionLogs.message')" min-width="240" show-overflow-tooltip prop="message" />

            <el-table-column :label="t('exceptionLogs.requestPath')" min-width="180" show-overflow-tooltip>
              <template #default="{ row }">{{ row.requestPath ?? '—' }}</template>
            </el-table-column>

            <el-table-column :label="t('exceptionLogs.occurredAt')" min-width="180" prop="occurredAtUtc" />

            <el-table-column :label="t('accessLogs.httpMethod')" width="100" align="center">
              <template #default="{ row }">
                <el-tag effect="plain">{{ row.httpMethod ?? '—' }}</el-tag>
              </template>
            </el-table-column>

            <!-- @vue-generic {AuditingExceptionLog} -->
          <el-table-column :label="t('auditAnalytics.viewDetail')" width="120" align="center">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row)">
                  {{ t('auditAnalytics.viewDetail') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('exceptionLogs.emptyDirectory') }}</template>
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

    <AuditLogDetailDrawer v-model="detailOpen" :record="selectedRecord" />
    <AuditLogExportDialog v-model:open="exportOpen" kind="exception" />
  </section>
</template>

<style scoped>
.exception-logs-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.exception-logs-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
