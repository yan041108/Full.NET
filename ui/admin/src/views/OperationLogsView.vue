<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { AuditingOperationLog, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import AuditLogDetailDrawer, {
  type AuditLogDetailRecord
} from './components/AuditLogDetailDrawer.vue';
import AuditLogExportDialog from './components/AuditLogExportDialog.vue';
import AuditLogTrendPanel from './components/AuditLogTrendPanel.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingOperationLogs, type AuditingOperationLogFilters } from '../api/operation-logs';
import { resolveAuditLogSearchTimeRange } from './auditLogSearchTimeRange';

defineOptions({ name: 'OperationLogsView' });

const { t } = useAdminI18n();
const items = ref<AuditingOperationLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const detailOpen = ref(false);
const selectedRecord = ref<AuditLogDetailRecord | null>(null);
const exportOpen = ref(false);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const searchForm = ref<Record<string, string | undefined>>({});
const activeFilters = ref<AuditingOperationLogFilters>({});
let loadController: AbortController | undefined;

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'pathContains', label: t('accessLogs.pathContains'), placeholder: t('accessLogs.pathContains') },
  {
    key: 'httpMethod', label: t('accessLogs.httpMethod'), type: 'select',
    options: ['POST', 'PUT', 'PATCH', 'DELETE'].map(value => ({ label: value, value }))
  },
  {
    key: 'succeeded', label: t('users.status'), type: 'select',
    options: [
      { label: t('operationLogs.succeeded'), value: 'true' },
      { label: t('operationLogs.failed'), value: 'false' }
    ]
  },
  { key: 'fromUtc', label: t('accessLogs.fromUtc'), placeholder: '2026-09-29T00:00:00Z' },
  { key: 'toUtc', label: t('accessLogs.toUtc'), placeholder: '2026-09-29T23:59:59Z' }
]);

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
watch([page, pageSize], () => void load(), { flush: 'post' });

onMounted(() => {
  void load();
});
onBeforeUnmount(() => {
  loadController?.abort();
  loadController = undefined;
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function load(): Promise<void> {
  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listAuditingOperationLogs(
      page.value, pageSize.value, controller.signal, activeFilters.value);
    if (loadController !== controller) return;
    items.value = result.items;
    total.value = result.total;
  } catch (error: unknown) {
    if (loadController === controller && !controller.signal.aborted) {
      problem.value = toProblem(error);
    }
  } finally {
    if (loadController === controller) {
      loading.value = false;
      void syncTableLayout();
    }
  }
}

function setPage(value: number): void {
  page.value = value;
}

function setPageSize(value: number): void {
  page.value = 1;
  pageSize.value = value;
}

function applySearch(form: Record<string, string | undefined>): void {
  const pathContains = form.pathContains?.trim();
  const timeRange = resolveAuditLogSearchTimeRange(form, Boolean(pathContains));
  if (!timeRange.valid) {
    problem.value = {
      status: 400,
      code: 'client.auditing_operation_log_time_range_invalid',
      title: t('operationLogs.invalidTimeRange')
    };
    return;
  }
  if (timeRange.defaulted) {
    searchForm.value.fromUtc = timeRange.fromUtc;
    searchForm.value.toUtc = timeRange.toUtc;
  }
  activeFilters.value = {
    ...(pathContains ? { pathContains } : {}),
    ...(form.httpMethod ? { httpMethod: form.httpMethod } : {}),
    ...(form.succeeded === 'true' || form.succeeded === 'false'
      ? { succeeded: form.succeeded === 'true' } : {}),
    ...(timeRange.fromUtc ? { fromUtc: timeRange.fromUtc } : {}),
    ...(timeRange.toUtc ? { toUtc: timeRange.toUtc } : {})
  };
  if (page.value === 1) void load();
  else page.value = 1;
}

function resetSearch(): void {
  activeFilters.value = {};
  if (page.value === 1) void load();
  else page.value = 1;
}

function openDetail(row: AuditingOperationLog): void {
  selectedRecord.value = {
    id: row.id,
    occurredAtUtc: row.occurredAtUtc,
    traceId: row.traceId,
    supportsRestrictedDetails: true,
    title: row.actionKey,
    subtitle: `${row.httpMethod} ${row.requestPath}`,
    fields: [
      { label: t('operationLogs.actionKey'), value: row.actionKey },
      { label: t('accessLogs.httpMethod'), value: row.httpMethod },
      { label: t('accessLogs.requestPath'), value: row.requestPath },
      { label: t('operationLogs.statusCode'), value: row.statusCode },
      { label: t('operationLogs.durationMs'), value: row.durationMs },
      { label: t('operationLogs.occurredAt'), value: row.occurredAtUtc },
      { label: t('users.status'), value: t(row.succeeded ? 'operationLogs.succeeded' : 'operationLogs.failed') },
      { label: t('auditAnalytics.userId'), value: row.userId },
      { label: t('auditAnalytics.tenantId'), value: row.tenantId },
      { label: t('auditAnalytics.clientIpFingerprint'), value: row.clientIpFingerprint },
      { label: t('auditAnalytics.permissionCode'), value: row.permissionCode },
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
        code: 'client.auditing_operation_log_failed',
        title: t('operationLogs.loadFailed')
      };
}
</script>

<template>
  <section class="operation-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('operationLogs.title') }}</h1>

    <AuditLogTrendPanel kind="operation" />

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="5"
      :search-label="t('accessLogs.query')"
      :reset-label="t('accessLogs.reset')"
      :show-expand="false"
      @search="applySearch"
      @reset="resetSearch"
    />

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
            <PermissionGate code="auditing.operations.export">
              <el-button data-testid="operation-logs-action-export" @click="exportOpen = true">
                {{ t('auditExport.action') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

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
            @row-click="openDetail"
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

            <!-- @vue-generic {AuditingOperationLog} -->
          <el-table-column :label="t('auditAnalytics.viewDetail')" width="120" align="center">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row)">
                  {{ t('auditAnalytics.viewDetail') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('operationLogs.emptyDirectory') }}</template>
          </el-table>
        </div>
        <el-pagination
          class="art-table-pagination"
          :current-page="page"
          :page-size="pageSize"
          :total="total"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
          @current-change="setPage"
          @size-change="setPageSize"
        />
      </div>
    </el-card>

    <AuditLogDetailDrawer v-model="detailOpen" :record="selectedRecord" />
    <AuditLogExportDialog v-model:open="exportOpen" kind="operation" />
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
