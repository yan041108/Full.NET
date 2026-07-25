<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref } from 'vue';
import { ElCol, ElRow, ElButton, ElProgress, ElTag } from 'element-plus';
import {
  DataLine,
  PieChart,
  TrendCharts,
  User
} from '@element-plus/icons-vue';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  FULLNET_SCALAR_UI_PATH,
  resolveFullNetApiUrl
} from '@fullnet/client-contracts';
import { request } from '../api/http';
import { getHostDashboardSummary } from '../api/platform-dashboard';
import { useAdminI18n } from '../i18n/adminI18n';
import { createTrafficLineOption } from '../framework/art-design/charts/fullNetChartTheme';
import ArtMetricCard from '../framework/art-design/components/ArtMetricCard.vue';

const FullNetChart = defineAsyncComponent(() =>
  import('../framework/art-design/charts/FullNetChart.vue')
);

interface CurrentUser {
  id: string;
  displayName: string;
}

const currentUser = ref<CurrentUser>();
const summary = ref<Awaited<ReturnType<typeof getHostDashboardSummary>>>();
const problem = ref<FullNetProblemDetails>();
const loading = ref(false);
const { locale, t } = useAdminI18n();

onMounted(() => {
  void loadDashboard();
});

const metrics = computed(() => [
  {
    label: t('overview.metric.activeTenants'),
    value: formatNumber(summary.value?.activeTenantCount ?? 0),
    icon: PieChart
  },
  {
    label: t('overview.metric.onlineUsers'),
    value: formatNumber(summary.value?.onlineSessionCount ?? 0),
    icon: User
  },
  {
    label: t('overview.metric.todayRequests'),
    value: formatNumber(summary.value?.todayRequestCount ?? 0, { notation: 'compact' }),
    icon: TrendCharts
  },
  {
    label: t('overview.metric.errorRate'),
    value: formatPercent(summary.value?.todayErrorRate ?? 0, false),
    icon: DataLine
  }
]);

const activities = computed(() =>
  (summary.value?.recentActivities ?? []).map((item, index) => ({
    title: `${item.httpMethod} ${item.requestPath}`,
    meta: formatDateTime(item.occurredAtUtc),
    status: item.succeeded ? t('overview.status.success') : t('overview.status.failed')
  }))
);

const trafficPoints = computed(() => [
  { label: '00:00', value: 42000 },
  { label: '03:00', value: 88000 },
  { label: '06:00', value: 52000 },
  { label: '09:00', value: 118000 },
  { label: '12:00', value: 92000 }
]);

const trafficOption = computed(() =>
  createTrafficLineOption(trafficPoints.value, t('overview.trafficAxis'))
);

const trafficSummary = computed(() =>
  t('overview.chartSummary', {
    peak: formatNumber(Math.max(...trafficPoints.value.map(point => point.value)), {
      notation: 'compact'
    })
  })
);

const chartThemeMode = computed<'light' | 'dark'>(() =>
  document.documentElement.dataset.artTheme === 'dark' ? 'dark' : 'light'
);

const apiDocumentationUrl = computed(() =>
  resolveFullNetApiUrl(import.meta.env.VITE_API_BASE_URL ?? '', FULLNET_SCALAR_UI_PATH)
);

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
    currentUser.value = await request<CurrentUser>('/api/v1/me');
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
      <span class="overview__live"><i aria-hidden="true" />{{ t('overview.live') }}</span>
      <a
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
      <el-col :sm="24" :md="12" :lg="14">
        <article class="art-card art-panel-card panel--chart">
          <header class="art-panel-card__header">
            <h2>{{ t('overview.trafficTitle') }}</h2>
            <span class="art-panel-card__meta">{{ t('overview.lastTwelveHours') }}</span>
          </header>
          <FullNetChart
            data-testid="traffic-chart"
            :option="trafficOption"
            :height="290"
            :ariaLabel="t('overview.chartLabel')"
            :summary="trafficSummary"
            :emptyLabel="t('overview.chartEmpty')"
            :locale="locale"
            :themeMode="chartThemeMode"
          />
        </article>
      </el-col>

      <el-col :sm="24" :md="12" :lg="10">
        <article class="art-card art-panel-card">
          <header class="art-panel-card__header">
            <h2>{{ t('overview.pulseTitle') }}</h2>
            <span class="overview__status-dot" role="img" :aria-label="t('overview.serviceHealthy')" />
          </header>
          <div class="overview__pulse-score">
            <strong>98.6</strong>
            <span>{{ t('overview.healthScore') }}</span>
          </div>
          <div class="overview__pulse-list">
            <div>
              <span>{{ t('overview.apiAvailability') }}</span>
              <el-progress :percentage="99" :show-text="false" :stroke-width="6" :aria-label="t('overview.apiAvailability')" />
            </div>
            <div>
              <span>{{ t('overview.cacheHitRate') }}</span>
              <el-progress :percentage="94" :show-text="false" :stroke-width="6" :aria-label="t('overview.cacheHitRate')" />
            </div>
            <div>
              <span>{{ t('overview.jobPunctuality') }}</span>
              <el-progress :percentage="97" :show-text="false" :stroke-width="6" :aria-label="t('overview.jobPunctuality')" />
            </div>
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
            <li v-for="activity in activities" :key="activity.title">
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
            <span class="art-panel-card__meta">{{ t('overview.todoCount') }}</span>
          </header>
          <div class="overview__todo-stack">
            <button type="button">
              <span>{{ t('overview.todo.permissionReview') }}</span>
              <strong>08</strong>
            </button>
            <button type="button">
              <span>{{ t('overview.todo.tenantApproval') }}</span>
              <strong>03</strong>
            </button>
            <button type="button">
              <span>{{ t('overview.todo.alertAcknowledgement') }}</span>
              <strong>01</strong>
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

.overview__live {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--fullnet-color-success);
  font-size: 12px;
  font-weight: 600;
}

.overview__live i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--art-theme-color);
  box-shadow: 0 0 0 4px rgb(64 158 255 / 12%);
}

.overview__status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--fullnet-color-success);
  box-shadow: 0 0 0 4px rgb(39 136 94 / 12%);
}

.panel--chart {
  padding-bottom: 8px;
}

.overview__pulse-score {
  display: flex;
  align-items: baseline;
  gap: 10px;
  margin-bottom: 8px;
}

.overview__pulse-score strong {
  color: var(--art-gray-800);
  font-size: 42px;
  font-weight: 600;
  line-height: 1;
}

.overview__pulse-score span {
  color: var(--art-gray-600);
  font-size: 12px;
}

.overview__pulse-list {
  display: grid;
  gap: 16px;
}

.overview__pulse-list > div > span {
  display: block;
  margin-bottom: 8px;
  color: var(--art-gray-600);
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
