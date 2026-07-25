<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElCard, ElTag } from 'element-plus';
import type {
  AuditingOperationLog,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAuditingOperationLogs } from '../api/operation-logs';

const { t } = useAdminI18n();
const items = ref<AuditingOperationLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listAuditingOperationLogs();
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
        code: 'client.auditing_operation_log_failed',
        title: t('operationLogs.loadFailed')
      };
}
</script>

<template>
  <section class="operation-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('operationLogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('operationLogs.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('operationLogs.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ item.actionKey }}</strong>
          <small>{{ t('operationLogs.statusCode') }}: {{ item.statusCode }}</small>
          <small>{{ t('operationLogs.durationMs') }}: {{ item.durationMs }}</small>
          <small>{{ t('operationLogs.occurredAt') }}: {{ item.occurredAtUtc }}</small>
        </div>
        <el-tag effect="plain">
          {{ item.succeeded ? t('operationLogs.succeeded') : t('operationLogs.failed') }}
        </el-tag>
      </article>
    </el-card>
  </section>
</template>
