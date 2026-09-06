<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElAlert,
  ElCard,
  ElCol,
  ElPagination,
  ElRow,
  ElStatistic,
  ElTable,
  ElTableColumn,
  ElTabPane,
  ElTabs
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostDocumentAccessLogResponse,
  HostDocumentStatisticsResponse
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentAccessLogs } from '../api/document-access-logs';
import { getDocumentStatistics } from '../api/document-statistics';

defineOptions({ name: 'DocumentStatisticsView' });

const { t } = useAdminI18n();
const session = useSessionStore();
const activeTab = ref<'statistics' | 'accessLogs'>('statistics');
const statisticsLoading = ref(false);
const accessLogsLoading = ref(false);
const statisticsProblem = ref<FullNetProblemDetails>();
const accessLogsProblem = ref<FullNetProblemDetails>();
const statistics = ref<HostDocumentStatisticsResponse | null>(null);
const accessLogs = ref<HostDocumentAccessLogResponse[]>([]);
const accessLogsPage = ref(1);
const accessLogsPageSize = ref(20);
const accessLogsTotal = ref(0);

const canReadAccessLogs = computed(() => session.can('document.host_access_logs.read'));

/** 统计面板优先展示服务端已格式化好的总大小文案，避免前端再复制一套单位换算规则。 */
function formatTotalSize(): string {
  return statistics.value?.summary.totalSizeInfo ?? '';
}

function accessTypeLabel(accessTypeKey: string): string {
  const key = `documentStatistics.accessLogs.accessType.${accessTypeKey}` as const;
  const translated = t(key);
  return translated === key ? accessTypeKey : translated;
}

function sourceLabel(sourceKey: string): string {
  const key = `documentStatistics.accessLogs.source.${sourceKey}` as const;
  const translated = t(key);
  return translated === key ? sourceKey : translated;
}

/** 页面只有一份汇总快照，失败时清空旧数据，避免用户把历史统计误认为最新结果。 */
async function loadStatistics() {
  statisticsLoading.value = true;
  statisticsProblem.value = undefined;
  try {
    statistics.value = await getDocumentStatistics();
  } catch (error) {
    statistics.value = null;
    statisticsProblem.value = toProblem(error, 'documentStatistics.loadFailed');
  } finally {
    statisticsLoading.value = false;
  }
}

/** 访问日志按服务端分页拉取，避免把全量日志一次性加载到浏览器。 */
async function loadAccessLogs() {
  if (!canReadAccessLogs.value) {
    return;
  }

  accessLogsLoading.value = true;
  accessLogsProblem.value = undefined;
  try {
    const result = await listDocumentAccessLogs(accessLogsPage.value, accessLogsPageSize.value);
    accessLogs.value = result.items;
    accessLogsPage.value = result.page;
    accessLogsPageSize.value = result.pageSize;
    accessLogsTotal.value = result.total;
  } catch (error) {
    accessLogs.value = [];
    accessLogsProblem.value = toProblem(error, 'documentStatistics.accessLogs.loadFailed');
  } finally {
    accessLogsLoading.value = false;
  }
}

/** 统一把未知异常收敛成当前页可展示的错误结构，保证空态和告警文案来源一致。 */
function toProblem(error: unknown, fallbackCode: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackCode), status: 500, code: fallbackCode };
}

watch(activeTab, (tab) => {
  if (tab === 'accessLogs' && canReadAccessLogs.value && accessLogs.value.length === 0 && !accessLogsLoading.value) {
    void loadAccessLogs();
  }
});

onMounted(() => {
  void loadStatistics();
});
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentStatistics.title') }}</h1>

    <el-tabs v-model="activeTab" data-testid="document-statistics-tabs">
      <el-tab-pane :label="t('documentStatistics.tabs.statistics')" name="statistics" />
      <el-tab-pane
        v-if="canReadAccessLogs"
        :label="t('documentStatistics.tabs.accessLogs')"
        name="accessLogs"
      />
    </el-tabs>

    <template v-if="activeTab === 'statistics'">
      <el-alert
        v-if="statisticsProblem"
        type="error"
        :title="statisticsProblem.title"
        :description="statisticsProblem.detail ?? statisticsProblem.code"
        show-icon
        class="art-page-alert"
      />

      <el-card v-loading="statisticsLoading" shadow="never" data-testid="document-statistics-panel">
        <template v-if="statistics">
          <el-row :gutter="16" class="document-statistics__summary">
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.totalItems')" :value="statistics.summary.totalItems" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.totalVersions')" :value="statistics.summary.totalVersions" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic
                :title="t('documentStatistics.totalSize')"
                :value="statistics.summary.totalSizeKb"
                :formatter="formatTotalSize"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.shareCount')" :value="statistics.shareCount" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.recycleBinCount')" :value="statistics.recycleBinCount" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.todayCreated')" :value="statistics.todayCreatedCount" />
            </el-col>
          </el-row>

          <h2 class="document-statistics__section-title">{{ t('documentStatistics.byTypeTitle') }}</h2>
          <el-table
            v-if="statistics.byType.length"
            :data="statistics.byType"
            size="small"
            data-testid="document-statistics-by-type"
          >
            <el-table-column prop="extension" :label="t('documentStatistics.byType.extension')" />
            <el-table-column prop="count" :label="t('documentStatistics.byType.count')" />
            <el-table-column prop="totalSizeKb" :label="t('documentStatistics.byType.totalSizeKb')" />
          </el-table>
          <p v-else data-testid="document-statistics-by-type-empty">{{ t('documentStatistics.byType.empty') }}</p>
        </template>
        <p v-else-if="!statisticsLoading">{{ t('documentStatistics.empty') }}</p>
      </el-card>
    </template>

    <template v-else-if="activeTab === 'accessLogs' && canReadAccessLogs">
      <el-alert
        v-if="accessLogsProblem"
        type="error"
        :title="accessLogsProblem.title"
        :description="accessLogsProblem.detail ?? accessLogsProblem.code"
        show-icon
        class="art-page-alert"
      />

      <el-card v-loading="accessLogsLoading" shadow="never" data-testid="document-access-logs-panel">
        <el-table v-if="accessLogs.length" :data="accessLogs" size="small" data-testid="document-access-logs-table">
          <el-table-column prop="documentTitle" :label="t('documentStatistics.accessLogs.documentTitle')" />
          <el-table-column :label="t('documentStatistics.accessLogs.accessType')">
            <template #default="{ row }">
              <span translate="no">{{ accessTypeLabel(row.accessTypeKey) }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('documentStatistics.accessLogs.source')">
            <template #default="{ row }">
              <span translate="no">{{ sourceLabel(row.sourceKey) }}</span>
            </template>
          </el-table-column>
          <el-table-column prop="occurredAtUtc" :label="t('documentStatistics.accessLogs.occurredAt')" />
        </el-table>
        <p v-else-if="!accessLogsLoading" data-testid="document-access-logs-empty">
          {{ t('documentStatistics.accessLogs.empty') }}
        </p>

        <div v-if="accessLogsTotal > 0" class="art-table__pagination center custom-pagination">
          <el-pagination
            v-model:current-page="accessLogsPage"
            v-model:page-size="accessLogsPageSize"
            layout="total, prev, pager, next"
            :total="accessLogsTotal"
            @current-change="loadAccessLogs"
            @size-change="loadAccessLogs"
          />
        </div>
      </el-card>
    </template>
  </section>
</template>

<style scoped>
.document-statistics__summary {
  margin-bottom: 24px;
}

.document-statistics__section-title {
  margin: 0 0 12px;
  font-size: 16px;
  font-weight: 600;
}
</style>
