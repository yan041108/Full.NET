<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElTag } from 'element-plus';
import type {
  AuditingAccessLog,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingAccessLogsByCursor } from '../api/access-logs';

const { t } = useAdminI18n();
const items = ref<AuditingAccessLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const nextCursor = ref<string | null>(null);
const hasMore = ref(false);

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
    const page = cursor === undefined
      ? await listAuditingAccessLogsByCursor()
      : await listAuditingAccessLogsByCursor(cursor);
    items.value = append ? [...items.value, ...page.items] : page.items;
    nextCursor.value = page.nextCursor;
    hasMore.value = page.hasMore;
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
