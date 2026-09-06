<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElTag
} from 'element-plus';
import type { ElasticsearchLogPipelineHealth, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
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

function clusterTagType(status: string): 'success' | 'warning' | 'danger' | 'info' {
  switch (status) {
    case 'green':
      return 'success';
    case 'yellow':
      return 'warning';
    case 'red':
      return 'danger';
    default:
      return 'info';
  }
}

function clusterStatusLabel(status: string): string {
  switch (status) {
    case 'green':
      return t('observabilityElasticsearchHealth.clusterStatus.green');
    case 'yellow':
      return t('observabilityElasticsearchHealth.clusterStatus.yellow');
    case 'red':
      return t('observabilityElasticsearchHealth.clusterStatus.red');
    case 'disabled':
      return t('observabilityElasticsearchHealth.clusterStatus.disabled');
    case 'misconfigured':
      return t('observabilityElasticsearchHealth.clusterStatus.misconfigured');
    case 'unreachable':
      return t('observabilityElasticsearchHealth.clusterStatus.unreachable');
    default:
      return status;
  }
}

async function loadHealth(): Promise<void> {
  loading.value = true;
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
    <ArtTableHeader :title="t('observabilityElasticsearchHealth.title')" />
    <p class="art-muted">{{ t('observabilityElasticsearchHealth.description') }}</p>

    <ElAlert
      v-if="health"
      type="info"
      :closable="false"
      :title="t('observabilityElasticsearchHealth.pipelineNoticeTitle')"
      :description="health.pipelineNotice"
      show-icon
      class="observability-elasticsearch-health-notice"
    />

    <ElCard v-if="canRead" v-loading="loading">
      <div class="observability-elasticsearch-health-actions">
        <ElButton :loading="loading" @click="loadHealth">
          {{ t('observabilityElasticsearchHealth.refresh') }}
        </ElButton>
      </div>

      <template v-if="health">
        <ElDescriptions :column="1" border>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.adapterKind')">
            {{ health.adapterKind }}
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.isEnabled')">
            <ElTag :type="health.isEnabled ? 'success' : 'info'">
              {{ health.isEnabled ? t('observabilityElasticsearchHealth.yes') : t('observabilityElasticsearchHealth.no') }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.isSinkRegistered')">
            <ElTag :type="health.isSinkRegistered ? 'success' : 'warning'">
              {{ health.isSinkRegistered ? t('observabilityElasticsearchHealth.yes') : t('observabilityElasticsearchHealth.no') }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.indexFormat')">
            {{ health.indexFormat }}
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.nodeEndpoints')">
            <span v-if="health.nodeEndpoints.length === 0">-</span>
            <ul v-else class="observability-elasticsearch-health-endpoints">
              <li v-for="endpoint in health.nodeEndpoints" :key="endpoint">{{ endpoint }}</li>
            </ul>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.otlpConfigured')">
            <ElTag :type="health.openTelemetryOtlpEndpointConfigured ? 'warning' : 'success'">
              {{ health.openTelemetryOtlpEndpointConfigured ? t('observabilityElasticsearchHealth.yes') : t('observabilityElasticsearchHealth.no') }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.clusterStatus')">
            <ElTag :type="clusterTagType(health.clusterStatus)">
              {{ clusterStatusLabel(health.clusterStatus) }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.clusterName')">
            {{ health.clusterName ?? '-' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.numberOfNodes')">
            {{ health.numberOfNodes ?? '-' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem :label="t('observabilityElasticsearchHealth.probeError')">
            {{ health.probeErrorMessage ?? '-' }}
          </ElDescriptionsItem>
        </ElDescriptions>
      </template>
    </ElCard>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title ?? t('common.requestFailed')"
      :description="problem.detail"
      show-icon
      closable
      @close="problem = undefined"
    />
  </div>
</template>

<style scoped>
.observability-elasticsearch-health-notice {
  margin-bottom: 16px;
}

.observability-elasticsearch-health-actions {
  margin-bottom: 16px;
}

.observability-elasticsearch-health-endpoints {
  margin: 0;
  padding-left: 20px;
}
</style>
