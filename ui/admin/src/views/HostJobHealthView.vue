<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElCard, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { FullNetProblemDetails, HostJobHealth } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getHostJobHealth } from '../api/host-job-health';

defineOptions({ name: 'HostJobHealthView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const health = ref<HostJobHealth | null>(null);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const canRead = computed(() => session.can('jobs.health.read'));

function formatUtc(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

async function load(): Promise<void> {
  if (!canRead.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    health.value = await getHostJobHealth();
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('hostJobHealth.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="host-job-health-view art-page-stack" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('hostJobHealth.eyebrow') }}</p>
      <h1>{{ t('hostJobHealth.title') }}</h1>
      <p>{{ t('hostJobHealth.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard v-loading="loading" class="art-card">
      <h2>{{ t('hostJobHealth.backlogTitle') }}</h2>
      <p v-if="health">
        {{ t('hostJobHealth.pendingCount', { count: health.backlog.pendingCount }) }}
      </p>
    </ElCard>

    <ElCard v-if="health" class="art-card">
      <h2>{{ t('hostJobHealth.handlersTitle') }}</h2>
      <ul>
        <li v-for="handler in health.registeredHandlers" :key="handler" translate="no">
          {{ handler }}
        </li>
      </ul>
    </ElCard>

    <ElCard v-if="health" class="art-card">
      <h2>{{ t('hostJobHealth.workersTitle') }}</h2>
      <ElTable :data="health.workers" stripe border style="width: 100%">
        <ElTableColumn prop="hostProfile" :label="t('hostJobHealth.columnHostProfile')" />
        <ElTableColumn :label="t('hostJobHealth.columnLastHeartbeat')">
          <template #default="{ row }">{{ formatUtc(row.lastHeartbeatAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn :label="t('hostJobHealth.columnStale')" width="100" align="center">
          <template #default="{ row }">
            <ElTag :type="row.isStale ? 'danger' : 'success'" effect="light">
              {{ row.isStale ? t('hostJobHealth.staleYes') : t('hostJobHealth.staleNo') }}
            </ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>
  </section>
</template>
