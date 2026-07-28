<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElTag } from 'element-plus';
import type {
  AuditingAccessLog,
  AuditingAccessLogQuery,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  applyAuditingAccessLogContainsDefaults,
  isFullNetProblemDetails
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingAccessLogsByCursor } from '../api/access-logs';

const { t } = useAdminI18n();
const items = ref<AuditingAccessLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const nextCursor = ref<string | null>(null);
const hasMore = ref(false);
const pathContains = ref('');
const fromUtcInput = ref('');
const toUtcInput = ref('');
const activeQuery = ref<AuditingAccessLogQuery>({});
const containsDefaultRangeApplied = ref(false);

onMounted(load);

async function load(): Promise<void> {
  items.value = [];
  nextCursor.value = null;
  hasMore.value = false;
  await loadBatch();
}

async function loadMore(): Promise<void> {
  if (!hasMore.value || nextCursor.value === null) {
    return;
  }
  await loadBatch(nextCursor.value, true);
}

async function search(): Promise<void> {
  activeQuery.value = buildQuery();
  await load();
}

function handlePathContainsInput(): void {
  if (!pathContains.value.trim()) {
    if (containsDefaultRangeApplied.value) {
      fromUtcInput.value = '';
      toUtcInput.value = '';
    }
    containsDefaultRangeApplied.value = false;
    return;
  }

  const hadNoTimeRange = !fromUtcInput.value && !toUtcInput.value;
  const query = applyAuditingAccessLogContainsDefaults({
    pathContains: pathContains.value,
    fromUtc: toUtcIso(fromUtcInput.value),
    toUtc: toUtcIso(toUtcInput.value)
  });
  applyVisibleDefaults(query);
  if (hadNoTimeRange && query.fromUtc && query.toUtc) {
    containsDefaultRangeApplied.value = true;
  }
}

function markTimeRangeEdited(): void {
  containsDefaultRangeApplied.value = false;
}

async function loadBatch(
  cursor?: string | null,
  append = false
): Promise<void> {
  if (loading.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    const options = {
      ...activeQuery.value,
      ...(cursor ? { cursor } : {})
    };
    const page = Object.keys(options).length === 0
      ? await listAuditingAccessLogsByCursor()
      : await listAuditingAccessLogsByCursor(options);
    items.value = append ? [...items.value, ...page.items] : page.items;
    nextCursor.value = page.nextCursor;
    hasMore.value = page.hasMore;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function buildQuery(): AuditingAccessLogQuery {
  const query = applyAuditingAccessLogContainsDefaults({
    pathContains: pathContains.value,
    fromUtc: toUtcIso(fromUtcInput.value),
    toUtc: toUtcIso(toUtcInput.value)
  });
  applyVisibleDefaults(query);
  return query;
}

function applyVisibleDefaults(query: AuditingAccessLogQuery): void {
  if (query.fromUtc && !fromUtcInput.value) {
    fromUtcInput.value = toDateTimeLocal(query.fromUtc);
  }
  if (query.toUtc && !toUtcInput.value) {
    toUtcInput.value = toDateTimeLocal(query.toUtc);
  }
}

function toUtcIso(value: string): string | undefined {
  if (!value) {
    return undefined;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

function toDateTimeLocal(value: string): string {
  const parsed = new Date(value);
  const local = new Date(
    parsed.getTime() - parsed.getTimezoneOffset() * 60_000
  );
  return local.toISOString().slice(0, 16);
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.auditing_access_log_failed',
        title: t('accessLogs.loadFailed')
      };
}
</script>

<template>
  <section class="access-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('accessLogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <form class="art-filter-row" @submit.prevent="search">
      <label>
        <span>{{ t('accessLogs.pathContains') }}</span>
        <input
          v-model="pathContains"
          data-testid="access-logs-path-contains"
          type="search"
          autocomplete="off"
          @input="handlePathContainsInput"
        />
      </label>
      <label>
        <span>{{ t('accessLogs.fromUtc') }}</span>
        <input
          v-model="fromUtcInput"
          data-testid="access-logs-from-utc"
          type="datetime-local"
          @input="markTimeRangeEdited"
        />
      </label>
      <label>
        <span>{{ t('accessLogs.toUtc') }}</span>
        <input
          v-model="toUtcInput"
          data-testid="access-logs-to-utc"
          type="datetime-local"
          @input="markTimeRangeEdited"
        />
      </label>
      <el-button
        data-testid="access-logs-search"
        native-type="button"
        :loading="loading"
        @click="search"
      >
        {{ t('accessLogs.search') }}
      </el-button>
    </form>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('accessLogs.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('accessLogs.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ item.httpMethod }} {{ item.requestPath }}</strong>
          <small>{{ t('accessLogs.statusCode') }}: {{ item.statusCode }}</small>
          <small>{{ t('accessLogs.durationMs') }}: {{ item.durationMs }}</small>
          <small>{{ t('accessLogs.occurredAt') }}: {{ item.occurredAtUtc }}</small>
        </div>
        <el-tag effect="plain">
          {{ item.isAuthenticated ? t('accessLogs.authenticated') : t('accessLogs.anonymous') }}
        </el-tag>
      </article>
      <div v-if="hasMore" class="art-table-card__footer">
        <el-button
          data-testid="access-logs-load-more"
          :loading="loading"
          @click="loadMore"
        >
          {{ t('accessLogs.loadMore') }}
        </el-button>
      </div>
    </el-card>
  </section>
</template>
