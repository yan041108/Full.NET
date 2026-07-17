<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElButton, ElProgress, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { request } from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';

interface CurrentUser {
  id: string;
  displayName: string;
}

const currentUser = ref<CurrentUser>();
const problem = ref<FullNetProblemDetails>();
const loading = ref(false);
const { locale, t } = useAdminI18n();

const metrics = computed(() => [
  { label: t('overview.metric.activeTenants'), value: formatNumber(128), delta: formatPercent(.124), tone: 'jade' },
  { label: t('overview.metric.onlineUsers'), value: formatNumber(2406), delta: formatPercent(.081), tone: 'cyan' },
  { label: t('overview.metric.todayRequests'), value: formatNumber(86200, { notation: 'compact' }), delta: formatPercent(.187), tone: 'amber' },
  { label: t('overview.metric.errorRate'), value: formatPercent(.0008, false), delta: formatPercent(-.0003), tone: 'rose' }
]);

const activities = computed(() => [
  { title: t('overview.activity.tenantInitialized'), meta: t('overview.activity.twoMinutes'), status: t('overview.status.success') },
  { title: t('overview.activity.policySynced'), meta: t('overview.activity.eighteenMinutes'), status: t('overview.status.synced') },
  { title: t('overview.activity.sqlServerVerified'), meta: t('overview.activity.fortyOneMinutes'), status: t('overview.status.passed') },
  { title: t('overview.activity.mysqlInspection'), meta: t('overview.activity.oneHour'), status: t('overview.status.running') }
]);

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
  <section class="overview">
    <section class="overview__heading">
      <div>
        <p class="eyebrow">{{ t('overview.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('overview.title') }}</h1>
        <p class="overview__summary">{{ t('overview.summary') }}</p>
      </div>
      <div class="overview__actions">
        <span class="live-badge"><i aria-hidden="true" />{{ t('overview.live') }}</span>
        <el-button class="probe-button" :loading="loading" :aria-busy="loading" data-testid="load-current-user" @click="loadCurrentUser">
          {{ loading ? t('overview.probing') : t('overview.probe') }}
        </el-button>
      </div>
    </section>

    <section class="metric-grid" data-testid="metric-grid" :aria-label="t('overview.metricsLabel')">
      <article v-for="metric in metrics" :key="metric.label" class="metric-card" :data-tone="metric.tone">
        <span class="metric-card__label">{{ metric.label }}</span>
        <strong>{{ metric.value }}</strong>
        <span class="metric-card__delta">{{ metric.delta }} <small>{{ t('overview.comparedYesterday') }}</small></span>
      </article>
    </section>

    <section v-if="problem || currentUser" class="contract-result" :class="{ 'contract-result--error': problem }">
      <span>{{ userStatus }}</span>
      <template v-if="problem">
        <strong data-testid="error-code" translate="no">{{ problem.code }}</strong>
        <code data-testid="trace-id" translate="no">{{ problem.traceId ?? t('overview.noTraceId') }}</code>
      </template>
    </section>

    <section class="workspace-grid">
      <article class="panel panel--wide">
        <header class="panel__header">
          <div>
            <span class="panel__index">01</span>
            <h2>{{ t('overview.trafficTitle') }}</h2>
          </div>
          <span class="panel__meta">{{ t('overview.lastTwelveHours') }}</span>
        </header>
        <div class="signal-chart" role="img" :aria-label="t('overview.chartLabel')">
          <div class="signal-chart__scale"><span>120k</span><span>80k</span><span>40k</span><span>0</span></div>
          <svg viewBox="0 0 760 250" preserveAspectRatio="none" aria-hidden="true">
            <defs>
              <linearGradient id="vue-signal-fill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stop-color="#0b8f87" stop-opacity="0.34" />
                <stop offset="1" stop-color="#0b8f87" stop-opacity="0.01" />
              </linearGradient>
            </defs>
            <g class="signal-chart__grid">
              <line x1="0" y1="40" x2="760" y2="40" />
              <line x1="0" y1="105" x2="760" y2="105" />
              <line x1="0" y1="170" x2="760" y2="170" />
              <line x1="0" y1="235" x2="760" y2="235" />
            </g>
            <path class="signal-chart__area" d="M0,218 C60,212 78,168 130,176 C176,184 188,78 244,94 C294,108 304,158 360,142 C422,123 442,48 498,69 C550,88 566,151 622,136 C680,121 710,82 760,102 L760,250 L0,250 Z" />
            <path class="signal-chart__line" d="M0,218 C60,212 78,168 130,176 C176,184 188,78 244,94 C294,108 304,158 360,142 C422,123 442,48 498,69 C550,88 566,151 622,136 C680,121 710,82 760,102" />
            <circle cx="244" cy="94" r="5" />
            <circle cx="498" cy="69" r="5" />
          </svg>
          <div class="signal-chart__axis"><span>00:00</span><span>03:00</span><span>06:00</span><span>09:00</span><span>12:00</span></div>
        </div>
      </article>

      <article class="panel pulse-panel">
        <header class="panel__header">
          <div>
            <span class="panel__index">02</span>
            <h2>{{ t('overview.pulseTitle') }}</h2>
          </div>
          <span class="status-dot" :aria-label="t('overview.serviceHealthy')" />
        </header>
        <div class="pulse-score">
          <strong>98.6</strong>
          <span>{{ t('overview.healthScore') }}</span>
        </div>
        <div class="pulse-list">
          <div><span>{{ t('overview.apiAvailability') }}</span><el-progress :percentage="99" :show-text="false" :stroke-width="5" /></div>
          <div><span>{{ t('overview.cacheHitRate') }}</span><el-progress :percentage="94" :show-text="false" :stroke-width="5" /></div>
          <div><span>{{ t('overview.jobPunctuality') }}</span><el-progress :percentage="97" :show-text="false" :stroke-width="5" /></div>
        </div>
      </article>

      <article class="panel activity-panel">
        <header class="panel__header">
          <div>
            <span class="panel__index">03</span>
            <h2>{{ t('overview.activityTitle') }}</h2>
          </div>
          <button type="button" class="text-action">{{ t('overview.viewAll') }}</button>
        </header>
        <ol class="activity-list">
          <li v-for="(activity, index) in activities" :key="activity.title">
            <span class="activity-list__number">{{ String(index + 1).padStart(2, '0') }}</span>
            <div><strong>{{ activity.title }}</strong><small>{{ activity.meta }}</small></div>
            <el-tag size="small" effect="plain">{{ activity.status }}</el-tag>
          </li>
        </ol>
      </article>

      <article class="panel todo-panel">
        <header class="panel__header">
          <div>
            <span class="panel__index">04</span>
            <h2>{{ t('overview.todoTitle') }}</h2>
          </div>
          <span class="panel__meta">{{ t('overview.todoCount') }}</span>
        </header>
        <div class="todo-stack">
          <button type="button"><span>{{ t('overview.todo.permissionReview') }}</span><strong>08</strong></button>
          <button type="button"><span>{{ t('overview.todo.tenantApproval') }}</span><strong>03</strong></button>
          <button type="button"><span>{{ t('overview.todo.alertAcknowledgement') }}</span><strong>01</strong></button>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.overview { display: grid; gap: 22px; }
.overview__heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px 4px; }
.eyebrow { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 11px; font-weight: 700; letter-spacing: .18em; }
h1 { margin: 0; color: var(--fullnet-color-ink); font-family: var(--fullnet-font-display); font-size: clamp(28px, 3.4vw, 44px); font-weight: 520; letter-spacing: -.045em; }
.overview__summary { margin: 10px 0 0; color: var(--fullnet-color-ink-muted); font-size: 14px; }
.overview__actions { display: flex; align-items: center; gap: 12px; }
.live-badge { display: inline-flex; align-items: center; gap: 8px; color: var(--fullnet-color-success); font-size: 12px; font-weight: 700; }
.live-badge i, .status-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--fullnet-color-accent-bright); box-shadow: 0 0 0 5px rgb(66 185 166 / 13%); }
.probe-button { --el-button-bg-color: var(--fullnet-color-ink); --el-button-border-color: var(--fullnet-color-ink); --el-button-text-color: #fff; --el-button-hover-bg-color: var(--fullnet-color-accent); --el-button-hover-border-color: var(--fullnet-color-accent); height: 40px; padding-inline: 18px; border-radius: var(--fullnet-radius-sm); }
.metric-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
.metric-card { position: relative; overflow: hidden; min-height: 128px; padding: 20px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: 0 12px 36px rgb(23 33 43 / 4%); }
.metric-card::after { position: absolute; right: -18px; bottom: -34px; width: 92px; height: 92px; border: 14px solid color-mix(in srgb, var(--card-color) 13%, transparent); border-radius: 50%; content: ""; }
.metric-card[data-tone="jade"] { --card-color: #0b8f87; }
.metric-card[data-tone="cyan"] { --card-color: #287f9f; }
.metric-card[data-tone="amber"] { --card-color: #d99b35; }
.metric-card[data-tone="rose"] { --card-color: #c94a4a; }
.metric-card__label { display: block; color: var(--fullnet-color-ink-muted); font-size: 12px; }
.metric-card strong { display: block; margin: 13px 0 7px; color: var(--fullnet-color-ink); font-family: var(--fullnet-font-display); font-size: 31px; font-weight: 560; letter-spacing: -.035em; }
.metric-card__delta { color: var(--card-color); font-family: var(--fullnet-font-display); font-size: 12px; font-weight: 700; }
.metric-card__delta small { color: var(--fullnet-color-ink-muted); font-family: var(--fullnet-font-sans); font-weight: 400; }
.contract-result { display: flex; align-items: center; gap: 14px; min-height: 44px; padding: 10px 14px; border-left: 3px solid var(--fullnet-color-success); background: rgb(39 136 94 / 8%); color: var(--fullnet-color-ink); font-size: 12px; }
.contract-result--error { border-color: var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.contract-result strong { margin-left: auto; color: var(--fullnet-color-danger); }
.contract-result code { color: var(--fullnet-color-ink-muted); }
.workspace-grid { display: grid; grid-template-columns: minmax(0, 1.85fr) minmax(280px, .8fr); gap: 16px; }
.panel { min-width: 0; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.panel__header { display: flex; align-items: center; justify-content: space-between; min-height: 64px; padding: 0 20px; border-bottom: 1px solid var(--fullnet-color-line); }
.panel__header > div { display: flex; align-items: center; gap: 11px; }
.panel__header h2 { margin: 0; color: var(--fullnet-color-ink); font-size: 15px; font-weight: 700; }
.panel__index { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .12em; }
.panel__meta { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.signal-chart { position: relative; height: 290px; padding: 26px 24px 18px 58px; }
.signal-chart svg { width: 100%; height: 220px; overflow: visible; }
.signal-chart__grid line { stroke: #e7ebe7; stroke-width: 1; }
.signal-chart__area { fill: url(#vue-signal-fill); }
.signal-chart__line { fill: none; stroke: var(--fullnet-color-accent); stroke-width: 3; vector-effect: non-scaling-stroke; }
.signal-chart circle { fill: var(--fullnet-color-panel); stroke: var(--fullnet-color-accent); stroke-width: 3; }
.signal-chart__scale { position: absolute; top: 24px; bottom: 42px; left: 16px; display: flex; flex-direction: column; justify-content: space-between; color: #84908e; font-family: var(--fullnet-font-display); font-size: 9px; }
.signal-chart__axis { display: flex; justify-content: space-between; color: #84908e; font-family: var(--fullnet-font-display); font-size: 9px; }
.pulse-panel { background: var(--fullnet-color-sidebar); color: #fff; }
.pulse-panel .panel__header { border-color: rgb(255 255 255 / 10%); }
.pulse-panel .panel__header h2 { color: #fff; }
.pulse-score { display: flex; align-items: baseline; gap: 10px; padding: 27px 22px 12px; }
.pulse-score strong { font-family: var(--fullnet-font-display); font-size: 48px; font-weight: 450; letter-spacing: -.06em; }
.pulse-score span { color: #9eabae; font-size: 11px; }
.pulse-list { display: grid; gap: 19px; padding: 12px 22px 28px; }
.pulse-list > div > span { display: block; margin-bottom: 8px; color: #b8c2c3; font-size: 11px; }
.pulse-list :deep(.el-progress-bar__outer) { background: rgb(255 255 255 / 10%); }
.pulse-list :deep(.el-progress-bar__inner) { background: var(--fullnet-color-accent-bright); }
.activity-list { margin: 0; padding: 4px 20px 13px; list-style: none; }
.activity-list li { display: grid; grid-template-columns: 34px minmax(0, 1fr) auto; align-items: center; gap: 12px; min-height: 65px; border-bottom: 1px solid #edf0ed; }
.activity-list li:last-child { border-bottom: 0; }
.activity-list__number { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; }
.activity-list strong, .activity-list small { display: block; }
.activity-list strong { overflow: hidden; color: var(--fullnet-color-ink); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.activity-list small { margin-top: 5px; color: var(--fullnet-color-ink-muted); font-size: 10px; }
.activity-list :deep(.el-tag) { --el-tag-bg-color: transparent; --el-tag-border-color: var(--fullnet-color-line); --el-tag-text-color: var(--fullnet-color-ink-muted); border-radius: 3px; }
.text-action { border: 0; background: none; color: var(--fullnet-color-accent); font: inherit; font-size: 11px; cursor: pointer; }
.todo-stack { display: grid; gap: 9px; padding: 16px; }
.todo-stack button { display: flex; align-items: center; justify-content: space-between; min-height: 52px; padding: 0 15px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-sm); background: #fafbf7; color: var(--fullnet-color-ink); font-family: var(--fullnet-font-sans); cursor: pointer; transition: border-color var(--fullnet-motion-fast), transform var(--fullnet-motion-fast); }
.todo-stack button:hover { border-color: var(--fullnet-color-accent); transform: translateX(3px); }
.todo-stack strong { color: var(--fullnet-color-signal); font-family: var(--fullnet-font-display); font-size: 19px; }
@media (max-width: 1050px) { .metric-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .workspace-grid { grid-template-columns: 1fr; } }
@media (max-width: 720px) { .overview__heading { align-items: flex-start; flex-direction: column; } .overview__actions { width: 100%; justify-content: space-between; } .metric-grid { grid-template-columns: 1fr; } .signal-chart { height: 250px; padding-left: 48px; } .signal-chart svg { height: 180px; } }
</style>
