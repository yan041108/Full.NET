<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElMessage,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  ServerInstanceCatalogEntry,
  ServerRuntimeMetric,
  ServerRuntimeSnapshot
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getObservabilityServerRuntime,
  listObservabilityServerInstances
} from '../api/observability-server-monitor';

defineOptions({ name: 'ObservabilityServerMonitorView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const instances = ref<ServerInstanceCatalogEntry[]>([]);
const selectedKey = ref<string>();
const runtime = ref<ServerRuntimeSnapshot>();
const loading = ref(false);
const runtimeLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const canRead = computed(() => session.can('observability.server.read'));
const selectedInstance = computed(() =>
  instances.value.find(entry => entry.instanceKey === selectedKey.value));

/** 将未知异常统一折叠为可渲染的 ProblemDetails。 */
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        type: 'about:blank',
        title: t('observabilityServerMonitor.loadFailed'),
        status: 500,
        code: 'client.unexpected_error'
      };
}

/** 按当前活动语言格式化 UTC 时间。 */
function formatUtc(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'medium'
  }).format(new Date(value));
}

/** 将字节数格式化为运维友好的单位。 */
function formatBytes(value: number): string {
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KiB`;
  }
  return `${(value / 1024 / 1024).toFixed(1)} MiB`;
}

/** 渲染指标值，并在不可用时展示原因而非伪造数值。 */
function formatMetric(metric: ServerRuntimeMetric): string {
  if (metric.availability !== 'available') {
    return metric.unavailableReason
      ?? translateRuntimeMessage(t, `observabilityServerMonitor.availability.${metric.availability}`);
  }
  if (metric.unit === 'bytes' && metric.longValue != null) {
    return formatBytes(metric.longValue);
  }
  if (metric.unit === 'percent' && metric.doubleValue != null) {
    return `${metric.doubleValue.toFixed(2)}%`;
  }
  if (metric.longValue != null) {
    return String(metric.longValue);
  }
  if (metric.doubleValue != null) {
    return String(metric.doubleValue);
  }
  return t('observabilityServerMonitor.metricUnavailable');
}

/** 加载实例目录并默认选中当前实例。 */
async function loadInstances(): Promise<void> {
  if (!canRead.value) {
    problem.value = {
      type: 'about:blank',
      title: t('observabilityServerMonitor.forbidden'),
      status: 403,
      code: 'client.forbidden'
    };
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    instances.value = await listObservabilityServerInstances();
    const current = instances.value.find(entry => entry.isCurrent);
    selectedKey.value = current?.instanceKey ?? instances.value[0]?.instanceKey;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

/** 读取选中实例的运行时快照。 */
async function loadRuntime(instanceKey?: string): Promise<void> {
  if (!instanceKey || !canRead.value) {
    runtime.value = undefined;
    return;
  }

  const instance = instances.value.find(entry => entry.instanceKey === instanceKey);
  if (!instance || instance.runtimeQueryability !== 'local') {
    runtime.value = undefined;
    problem.value = {
      type: 'about:blank',
      title: t('observabilityServerMonitor.catalogOnly'),
      status: 404,
      code: 'observability.server_instances.not_found'
    };
    return;
  }

  runtimeLoading.value = true;
  problem.value = undefined;
  try {
    runtime.value = await getObservabilityServerRuntime(instanceKey);
  } catch (error: unknown) {
    runtime.value = undefined;
    problem.value = toProblem(error);
  } finally {
    runtimeLoading.value = false;
  }
}

watch(selectedKey, key => {
  void loadRuntime(key);
});

onMounted(() => {
  void loadInstances();
});
</script>

<template>
  <section class="observability-server-monitor art-page-stack" :aria-busy="loading || runtimeLoading">
    <header>
      <p class="art-eyebrow">{{ t('observabilityServerMonitor.eyebrow') }}</p>
      <h1>{{ t('observabilityServerMonitor.title') }}</h1>
      <p>{{ t('observabilityServerMonitor.description') }}</p>
    </header>

    <p v-if="problem" class="art-problem">{{ problem.title }}</p>

    <ElCard class="art-card observability-server-monitor__card">
      <div class="observability-server-monitor__toolbar">
        <ElButton type="primary" :loading="loading" @click="loadInstances">
          {{ t('observabilityServerMonitor.refresh') }}
        </ElButton>
      </div>

      <ElTable
        :data="instances"
        highlight-current-row
        row-key="instanceKey"
        @current-change="row => { selectedKey = row?.instanceKey; }"
      >
        <ElTableColumn prop="displayName" :label="t('observabilityServerMonitor.displayName')" min-width="180" />
        <ElTableColumn prop="instanceKey" :label="t('observabilityServerMonitor.instanceKey')" min-width="160" />
        <ElTableColumn prop="hostRole" :label="t('observabilityServerMonitor.hostRole')" width="120" />
        <ElTableColumn :label="t('observabilityServerMonitor.current')" width="100">
          <template #default="{ row }">
            <ElTag v-if="row.isCurrent" type="success">{{ t('observabilityServerMonitor.yes') }}</ElTag>
            <span v-else>{{ t('observabilityServerMonitor.no') }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('observabilityServerMonitor.queryability')" min-width="140">
          <template #default="{ row }">
            {{ translateRuntimeMessage(t, `observabilityServerMonitor.queryability.${row.runtimeQueryability}`) }}
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElCard v-if="runtime" class="art-card observability-server-monitor__runtime">
      <template #header>
        <div class="observability-server-monitor__runtime-header">
          <h2>{{ runtime.displayName }}</h2>
          <span>{{ t('observabilityServerMonitor.capturedAt', { value: formatUtc(runtime.capturedAtUtc) }) }}</span>
        </div>
      </template>

      <ElDescriptions :column="2" border>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.framework')">
          {{ runtime.frameworkDescription }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.applicationVersion')">
          {{ runtime.applicationVersion }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.operatingSystem')">
          {{ runtime.operatingSystemDescription }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.architecture')">
          {{ runtime.processArchitecture }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.machineName')">
          {{ runtime.machineName }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.uptime')">
          {{ runtime.uptimeSeconds }} s
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.processStartedAt')">
          {{ formatUtc(runtime.processStartedAtUtc) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityServerMonitor.processId')">
          {{ runtime.processId }}
        </ElDescriptionsItem>
      </ElDescriptions>

      <ElTable :data="runtime.metrics" class="observability-server-monitor__metrics">
        <ElTableColumn prop="label" :label="t('observabilityServerMonitor.metric')" min-width="160" />
        <!-- @vue-generic {ServerRuntimeMetric} -->
          <ElTableColumn :label="t('observabilityServerMonitor.metricValue')" min-width="160">
          <template #default="{ row }">
            {{ formatMetric(row) }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('observabilityServerMonitor.metricAvailability')" min-width="140">
          <template #default="{ row }">
            {{ translateRuntimeMessage(t, `observabilityServerMonitor.availability.${row.availability}`) }}
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElCard
      v-else-if="selectedInstance && selectedInstance.runtimeQueryability !== 'local'"
      class="art-card"
    >
      <p>{{ t('observabilityServerMonitor.catalogOnly') }}</p>
    </ElCard>
  </section>
</template>

<style scoped>
.observability-server-monitor__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 1rem;
}

.observability-server-monitor__runtime-header {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: baseline;
  justify-content: space-between;
}

.observability-server-monitor__runtime-header h2 {
  margin: 0;
}

.observability-server-monitor__metrics {
  margin-top: 1.25rem;
}
</style>
