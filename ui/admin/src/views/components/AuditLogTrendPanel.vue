<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, watch } from 'vue';
import { ElCard } from 'element-plus';
import type { EChartsCoreOption } from 'echarts/core';
import type { AuditingLogTrend } from '@fullnet/client-contracts';
import { useAdminI18n } from '../../i18n/adminI18n';
import {
  createDefaultTrendQuery,
  queryAuditLogTrend,
  type AuditLogTrendKind
} from '../../api/auditing-analytics';

const FullNetChart = defineAsyncComponent(() =>
  import('../../framework/art-design/charts/FullNetChart.vue')
);

const props = defineProps<{
  kind: AuditLogTrendKind;
}>();

const { locale, t } = useAdminI18n();
const loading = ref(false);
const trend = ref<AuditingLogTrend>();
const problem = ref<string>();

const chartThemeMode = computed<'light' | 'dark'>(() =>
  document.documentElement.dataset.artTheme === 'dark' ? 'dark' : 'light'
);

const chartOption = computed<EChartsCoreOption>(() => {
  const buckets = trend.value?.buckets ?? [];
  return {
    grid: { left: 12, right: 12, top: 24, bottom: 8, containLabel: true },
    legend: {
      top: 0,
      data: [t('auditAnalytics.trendEvents'), t('auditAnalytics.trendErrors')]
    },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: buckets.map(bucket => formatBucketLabel(bucket.bucketStartUtc))
    },
    yAxis: {
      type: 'value',
      name: t('auditAnalytics.trendAxis')
    },
    series: [
      {
        name: t('auditAnalytics.trendEvents'),
        type: 'line',
        smooth: true,
        data: buckets.map(bucket => bucket.eventCount)
      },
      {
        name: t('auditAnalytics.trendErrors'),
        type: 'line',
        smooth: true,
        data: buckets.map(bucket => bucket.errorCount)
      }
    ]
  };
});

const summaryText = computed(() => {
  if (!trend.value) {
    return '';
  }
  return t('auditAnalytics.trendSummary', {
    total: formatNumber(trend.value.totalCount),
    bucketMinutes: trend.value.bucketSizeMinutes
  });
});

watch(() => props.kind, () => {
  void loadTrend();
});

onMounted(() => {
  void loadTrend();
});

async function loadTrend(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    trend.value = await queryAuditLogTrend(props.kind, createDefaultTrendQuery());
  } catch {
    problem.value = t('auditAnalytics.trendLoadFailed');
  } finally {
    loading.value = false;
  }
}

function formatBucketLabel(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value));
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat(locale.value).format(value);
}

defineExpose({ reload: loadTrend });
</script>

<template>
  <el-card shadow="never" class="audit-log-trend-panel" :aria-busy="loading">
    <div class="audit-log-trend-panel__header">
      <div>
        <h2 class="audit-log-trend-panel__title">{{ t('auditAnalytics.trendTitle') }}</h2>
        <p v-if="summaryText" class="audit-log-trend-panel__summary">{{ summaryText }}</p>
      </div>
    </div>

    <div v-if="problem" class="art-inline-alert" role="alert">{{ problem }}</div>

    <div v-loading="loading" class="audit-log-trend-panel__chart">
      <FullNetChart
        v-if="trend && trend.buckets.length > 0"
        :option="chartOption"
        :theme-mode="chartThemeMode"
        :ariaLabel="t('auditAnalytics.trendTitle')"
        :empty-label="t('auditAnalytics.trendEmpty')"
      />
      <p v-else-if="!loading" class="audit-log-trend-panel__empty">
        {{ t('auditAnalytics.trendEmpty') }}
      </p>
    </div>
  </el-card>
</template>

<style scoped>
.audit-log-trend-panel {
  margin-bottom: 16px;
}

.audit-log-trend-panel__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 12px;
}

.audit-log-trend-panel__title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.audit-log-trend-panel__summary {
  margin: 4px 0 0;
  color: var(--art-text-secondary, #667085);
  font-size: 13px;
}

.audit-log-trend-panel__chart {
  min-height: 240px;
}

.audit-log-trend-panel__empty {
  margin: 0;
  padding: 24px 0;
  text-align: center;
  color: var(--art-text-secondary, #667085);
}
</style>
