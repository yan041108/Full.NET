<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElCard, ElTag } from 'element-plus';
import type {
  AuditingExceptionLog,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingExceptionLogs } from '../api/exception-logs';

const { t } = useAdminI18n();
const items = ref<AuditingExceptionLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listAuditingExceptionLogs();
    items.value = page.items;
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
        code: 'client.auditing_exception_log_failed',
        title: t('exceptionLogs.loadFailed')
      };
}
</script>

<template>
  <section class="exception-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('exceptionLogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('exceptionLogs.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('exceptionLogs.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ item.exceptionType }}</strong>
          <small>{{ t('exceptionLogs.message') }}: {{ item.message }}</small>
          <small v-if="item.requestPath">
            {{ t('exceptionLogs.requestPath') }}: {{ item.requestPath }}
          </small>
          <small>{{ t('exceptionLogs.occurredAt') }}: {{ item.occurredAtUtc }}</small>
        </div>
        <el-tag effect="plain">{{ item.httpMethod ?? '—' }}</el-tag>
      </article>
    </el-card>
  </section>
</template>
