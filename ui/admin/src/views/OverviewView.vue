<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { ElCol, ElRow, ElButton, ElTag } from 'element-plus';
import {
  DataLine,
  PieChart,
  TrendCharts,
  User
} from '@element-plus/icons-vue';
import {
  isFullNetProblemDetails,
  type CurrentUserResponse,
  type FullNetProblemDetails,
  FULLNET_SCALAR_UI_PATH,
  resolveFullNetApiUrl
} from '@fullnet/client-contracts';
import { getCurrentUser } from '../api/me';
import { getHostDashboardSummary } from '../api/platform-dashboard';
import { useAdminI18n } from '../i18n/adminI18n';
import { createTrafficLineOption } from '../framework/art-design/charts/fullNetChartTheme';
import ArtMetricCard from '../framework/art-design/components/ArtMetricCard.vue';
import type { HostDashboardBusinessEntry } from '@fullnet/client-contracts';

const router = useRouter();
const showApiDocumentation = import.meta.env.VITE_ENABLE_API_DOCUMENTATION === '1'
  || (import.meta.env.DEV && import.meta.env.VITE_ENABLE_API_DOCUMENTATION !== '0');

const FullNetChart = defineAsyncComponent(() =>
  import('../framework/art-design/charts/FullNetChart.vue')
);

const currentUser = ref<CurrentUserResponse>();
const summary = ref<Awaited<ReturnType<typeof getHostDashboardSummary>>>();
const problem = ref<FullNetProblemDetails>();
const loading = ref(false);
const { locale, t } = useAdminI18n();

onMounted(() => {
  void loadDashboard();
});

const metrics = computed(() => {
  const items = [];
  if (summary.value?.activeTenantCount != null) {
    items.push({
      label: t('overview.metric.activeTenants'),
      value: formatNumber(summary.value.activeTenantCount),
      icon: PieChart
    });
  }
  if (summary.value?.onlineSessionCount != null) {
    items.push({
      label: t('overview.metric.onlineUsers'),
      value: formatNumber(summary.value.onlineSessionCount),
      icon: User
    });
  }
  if (summary.value?.todayRequestCount != null) {
    items.push({
      label: t('overview.metric.todayRequests'),
      value: formatNumber(summary.value.todayRequestCount, { notation: 'compact' }),
      icon: TrendCharts
    });
  }
  if (summary.value?.todayErrorRate != null) {
    items.push({
      label: t('overview.metric.errorRate'),
      value: formatPercent(summary.value.todayErrorRate, false),
      icon: DataLine
    });
  }
  return items;
});

const activities = computed(() =>
  (summary.value?.recentActivities ?? []).map((item) => ({
    title: `${item.httpMethod} ${item.requestPath}`,
    meta: formatDateTime(item.occurredAtUtc),
    status: item.succeeded ? t('overview.status.success') : t('overview.status.failed')
  }))
);

const trafficPoints = computed(() => {
  const buckets = summary.value?.accessTrafficTrend?.buckets ?? [];
  if (buckets.length === 0) {
    return [];
  }

  return buckets.map(bucket => ({
    label: formatBucketLabel(bucket.bucketStartUtc),
    value: bucket.eventCount
  }));
});

const trafficOption = computed(() =>
  createTrafficLineOption(trafficPoints.value, t('overview.trafficAxis'))
);

const trafficSummary = computed(() => {
  if (trafficPoints.value.length === 0) {
    return '';
  }

  return t('overview.chartSummary', {
    peak: formatNumber(Math.max(...trafficPoints.value.map(point => point.value)), {
      notation: 'compact'
    })
  });
});

const businessEntries = computed(() => summary.value?.businessEntries ?? []);

const todoCountLabel = computed(() =>
  t('overview.todoCountLive', { count: businessEntries.value.length })
);

const chartThemeMode = computed<'light' | 'dark'>(() =>
  document.documentElement.dataset.artTheme === 'dark' ? 'dark' : 'light'
);

/** 开发代理模式下文档应指向 API 宿主，避免同源 `/scalar` 误落到 SPA 入口导致整页闪烁。 */
const apiDocumentationUrl = computed(() => {
  const explicitBase = (import.meta.env.VITE_API_BASE_URL ?? '').trim();
  if (explicitBase.length > 0) {
    return resolveFullNetApiUrl(explicitBase, FULLNET_SCALAR_UI_PATH);
  }

  const proxyTarget = import.meta.env.VITE_API_PROXY_TARGET;
  if (typeof proxyTarget === 'string' && proxyTarget.trim().length > 0) {
    return resolveFullNetApiUrl(proxyTarget.trim(), FULLNET_SCALAR_UI_PATH);
  }

  return FULLNET_SCALAR_UI_PATH;
});

const userStatus = computed(() => currentUser.value
  ? t('overview.connectedUser', { name: currentUser.value.displayName })
  : t('overview.verifySession'));

function formatNumber(
  value: number,
  options: Intl.NumberFormatOptions = {}
): string {
  return new Intl.NumberFormat(locale.value, options).format(value);
}

function formatPercent(value: number, signed = true): string {
  return new Intl.NumberFormat(locale.value, {
    style: 'percent',
    maximumFractionDigits: 2,
    signDisplay: signed ? 'exceptZero' : 'auto'
  }).format(value);
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function formatBucketLabel(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value));
}

function resolveBusinessEntryLabel(entry: HostDashboardBusinessEntry): string {
  switch (entry.entryKey) {
    case 'workflow.pending_todos':
      return t('overview.entry.workflowPendingTodos');
    case 'workflow.my_instances':
      return t('overview.entry.workflowMyInstances');
    default:
      return entry.entryKey;
  }
}

function openBusinessEntry(routePath: string): void {
  void router.push(routePath);
}

async function loadDashboard(): Promise<void> {
  if (loading.value) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    summary.value = await getHostDashboardSummary();
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      problem.value = error;
      return;
    }

    problem.value = {
      status: 500,
      code: 'client.unexpected_error',
      title: t('overview.clientFailure')
    };
  } finally {
    loading.value = false;
  }
}

async function loadCurrentUser(): Promise<void> {
  if (loading.value) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    currentUser.value = await getCurrentUser();
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      problem.value = error;
      return;
    }

    problem.value = {
      status: 500,
      code: 'client.unexpected_error',
      title: t('overview.clientFailure')
    };
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <section class="overview art-dashboard">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('overview.title') }}</h1>

    <div class="overview__toolbar">
      <a
        v-if="showApiDocumentation"
        class="overview__docs-link"
        :href="apiDocumentationUrl"
        target="_blank"
        rel="noopener noreferrer"
        data-testid="open-api-docs"
      >
        {{ t('overview.apiDocs') }}
      </a>
      <el-button
        class="overview__probe-btn"
        type="primary"
        :loading="loading"
        :aria-busy="loading"
        data-testid="load-current-user"
        @click="loadCurrentUser"
      >
        {{ loading ? t('overview.probing') : t('overview.probe') }}
      </el-button>
    </div>

    <el-row :gutter="20" data-testid="metric-grid" :aria-label="t('overview.metricsLabel')">
      <el-col
        v-for="metric in metrics"
        :key="metric.label"
        :sm="12"
        :md="6"
        :lg="6"
      >
        <ArtMetricCard
          :label="metric.label"
          :value="metric.value"
          :icon="metric.icon"
        />
      </el-col>
    </el-row>

    <section
      v-if="problem || currentUser"
      class="art-inline-result"
      :class="{ 'art-inline-result--error': problem }"
    >
      <span>{{ userStatus }}</span>
      <template v-if="problem">
        <strong data-testid="error-code" translate="no">{{ problem.code }}</strong>
        <code data-testid="trace-id" translate="no">{{ problem.traceId ?? t('overview.noTraceId') }}</code>
      </template>
    </section>

    <el-row :gutter="20">
      <el-col :span="24">
        <article class="art-card art-panel-card panel--chart">
          <header class="art-panel-card__header">
            <h2>{{ t('overview.trafficTitle') }}</h2>
            <span class="art-panel-card__meta">{{ t('overview.lastTwelveHours') }}</span>
          </header>
          <div data-testid="traffic-chart">
            <FullNetChart
              v-if="trafficPoints.length > 0"
              :option="trafficOption"
              :height="290"
              :ariaLabel="t('overview.chartLabel')"
              :summary="trafficSummary"
              :emptyLabel="t('overview.chartEmpty')"
              :locale="locale"
              :themeMode="chartThemeMode"
            />
            <p v-else class="overview__traffic-state" role="status">
              {{ t(summary?.accessTrafficTrend == null ? 'overview.trafficUnavailable' : 'overview.chartEmpty') }}
            </p>
          </div>
        </article>
      </el-col>

    </el-row>

    <el-row :gutter="20">
      <el-col :sm="24" :md="24" :lg="12">
        <article class="art-card art-panel-card">
          <header class="art-panel-card__header">
            <h2>{{ t('overview.activityTitle') }}</h2>
            <button type="button" class="overview__text-action">{{ t('overview.viewAll') }}</button>
          </header>
          <ol class="overview__activity-list">
            <li v-if="activities.length === 0">
              <span class="overview__activity-empty">{{ t('overview.emptyActivities') }}</span>
            </li>
            <li v-for="activity in activities" :key="`${activity.title}-${activity.meta}`">
              <div class="overview__activity-main">
                <strong>{{ activity.title }}</strong>
                <small>{{ activity.meta }}</small>
              </div>
              <el-tag size="small" effect="plain">{{ activity.status }}</el-tag>
            </li>
          </ol>
        </article>
      </el-col>

      <el-col :sm="24" :md="12" :lg="12">
        <article class="art-card art-panel-card">
          <header class="art-panel-card__header">
            <h2>{{ t('overview.todoTitle') }}</h2>
            <span class="art-panel-card__meta">{{ todoCountLabel }}</span>
          </header>
          <div class="overview__todo-stack">
            <p v-if="businessEntries.length === 0" class="overview__activity-empty">
              {{ t('overview.todoEmpty') }}
            </p>
            <button
              v-for="entry in businessEntries"
              :key="entry.entryKey"
              type="button"
              @click="openBusinessEntry(entry.routePath)"
            >
              <span>{{ resolveBusinessEntryLabel(entry) }}</span>
              <strong>{{ formatNumber(entry.count) }}</strong>
            </button>
          </div>
        </article>
      </el-col>
    </el-row>
  </section>
</template>

<style scoped>
.overview__toolbar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  margin-bottom: 8px;
}

.overview__docs-link {
  color: var(--art-theme-text);
  font-size: 13px;
  font-weight: 600;
  text-decoration: none;
}

.overview__docs-link:hover {
  text-decoration: underline;
}

.overview__probe-btn {
  --el-button-bg-color: var(--art-theme-text);
  --el-button-border-color: var(--art-theme-text);
  --el-button-hover-bg-color: #0a3d87;
  --el-button-hover-border-color: #0a3d87;
}

.panel--chart {
  padding-bottom: 8px;
}

.overview__traffic-state {
  display: grid;
  place-items: center;
  min-height: 180px;
  border: 1px dashed var(--fullnet-color-line);
  border-radius: var(--fullnet-radius-sm);
  color: var(--fullnet-color-ink-muted);
  font-size: 12px;
}

.overview__activity-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.overview__activity-list li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 58px;
  padding: 0 4px;
  border-bottom: 1px solid var(--art-gray-200);
}

.overview__activity-list li:last-child {
  border-bottom: 0;
}

.overview__activity-main {
  display: grid;
  gap: 4px;
  min-width: 0;
}

.overview__activity-main strong {
  overflow: hidden;
  color: var(--art-gray-800);
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.overview__activity-main small {
  color: var(--art-gray-600);
  font-size: 11px;
}

.overview__activity-empty {
  color: var(--art-gray-600);
  font-size: 13px;
}

.overview__text-action {
  border: 0;
  background: none;
  color: var(--art-theme-text);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}

.overview__todo-stack {
  display: grid;
  gap: 10px;
}

.overview__todo-stack button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 48px;
  padding: 0 14px;
  border: 1px solid var(--art-gray-200);
  border-radius: 8px;
  background: var(--art-default-box-color);
  color: var(--art-gray-800);
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
  transition: border-color 0.2s ease, transform 0.2s ease;
}

.overview__todo-stack button:hover {
  border-color: var(--art-theme-color);
  transform: translateX(2px);
}

.overview__todo-stack strong {
  color: var(--art-theme-text);
  font-size: 18px;
  font-weight: 600;
}
</style>
