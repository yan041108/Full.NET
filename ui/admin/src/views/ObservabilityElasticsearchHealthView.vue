<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem
} from 'element-plus';
import type { ElasticsearchLogPipelineHealth, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getElasticsearchLogPipelineHealth } from '../api/observability-elasticsearch-health';

defineOptions({ name: 'ObservabilityElasticsearchHealthView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const health = ref<ElasticsearchLogPipelineHealth | null>(null);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const canRead = computed(() => session.can('observability.elasticsearch.read'));
async function loadHealth(): Promise<void> {
  loading.value = true;
  health.value = null;
  problem.value = undefined;
  try {
    health.value = await getElasticsearchLogPipelineHealth();
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('observabilityElasticsearchHealth.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  if (canRead.value) {
    void loadHealth();
  }
});
</script>

<template>
  <div class="observability-elasticsearch-health-view art-page-stack">
    <h1>{{ t('observabilityElasticsearchHealth.title') }}</h1>
    <p class="art-muted">{{ t('observabilityElasticsearchHealth.description') }}</p>
    <ElAlert v-if="problem" type="error" :closable="false" :title="problem.title" />
    <ElCard v-if="canRead">
      <ElButton :loading="loading" @click="loadHealth">{{ t('observabilityElasticsearchHealth.refresh') }}</ElButton>
      <ElDescriptions v-if="health" :column="1" border>
        <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.deliveryStatus')">
          {{ t(`observabilityElasticsearchHealth.deliveryStatus.${health.deliveryStatus}`) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.confirmationBoundary')">
          {{ t(`observabilityElasticsearchHealth.confirmationBoundary.${health.deliveryConfirmationBoundary}`) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.queryEnabled')">
          {{ health.elasticsearchQueryEnabled ? t('observabilityElasticsearchHealth.yes') : t('observabilityElasticsearchHealth.no') }}
        </ElDescriptionsItem>
        <ElDescriptionsItem v-if="health.elasticsearchQueryEnabled" :label="t('observabilityElasticsearchHealth.queryEndpoint')">
          {{ health.elasticsearchEndpoint }}
        </ElDescriptionsItem>
        <ElDescriptionsItem v-if="health.elasticsearchQueryEnabled" :label="t('observabilityElasticsearchHealth.queryIndex')">
          {{ health.elasticsearchIndexPattern }}
        </ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>
  </div>
</template>
