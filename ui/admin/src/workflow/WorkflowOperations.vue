<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { ElButton } from 'element-plus';
import { useRouter } from 'vue-router';
import type { WorkflowOperationsStepsResponse, WorkflowOperationsBacklogResponse } from '@fullnet/client-contracts';
import { listWorkflowOperationsSteps, listWorkflowOperationsBacklog } from '../api/workflow-observability';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { formatAdminDateTime } from './workflowAdminFormat';

const session = useSessionStore(), router = useRouter(), { t, locale } = useAdminI18n();
const open = ref(false), tab = ref<'steps' | 'backlog'>('steps'), page = ref(1), loading = ref(false), failed = ref(false);
const minimum = ref(0), overdue = ref(false), steps = ref<WorkflowOperationsStepsResponse>(), backlog = ref<WorkflowOperationsBacklogResponse>();
let epoch = 0; let controller: AbortController | undefined;
const allowed = computed(() => !session.switching && session.can('workflow.operations.read') && session.can('workflow.instances.read'));
const total = computed(() => tab.value === 'steps' ? steps.value?.total ?? 0 : backlog.value?.total ?? 0);
const observed = computed(() => tab.value === 'steps' ? steps.value?.observedAtUtc : backlog.value?.observedAtUtc);
function clear(): void { epoch++; controller?.abort(); controller = undefined; loading.value = false; failed.value = false; steps.value = undefined; backlog.value = undefined; }
watch(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope,
  session.currentUser?.profileVersion, allowed.value]), () => { clear(); open.value = false; page.value = 1; }, { flush: 'sync' });
watch([tab, minimum, overdue], () => { clear(); page.value = 1; }, { flush: 'sync' });
onBeforeUnmount(clear);
onDeactivated(() => { open.value = false; clear(); });
async function load(): Promise<void> {
  if (!allowed.value || loading.value || !Number.isInteger(minimum.value) || minimum.value < 0 || minimum.value > 31536000) return;
  clear(); open.value = true; loading.value = true; const current = epoch; const cancellation = new AbortController(); controller = cancellation;
  try {
    const result = tab.value === 'steps' ? await listWorkflowOperationsSteps({ page: page.value, pageSize: 20, minimumDwellSeconds: minimum.value, onlyOverdue: overdue.value }, cancellation.signal)
      : await listWorkflowOperationsBacklog({ page: page.value, pageSize: 20 }, cancellation.signal);
    if (current !== epoch || cancellation.signal.aborted || !allowed.value) return;
    if (tab.value === 'steps') steps.value = result as WorkflowOperationsStepsResponse; else backlog.value = result as WorkflowOperationsBacklogResponse;
  } catch { if (current === epoch && !cancellation.signal.aborted) failed.value = true; }
  finally { if (current === epoch) loading.value = false; }
}
function inspect(id: string): void { if (allowed.value) void router.push({ path: '/workflow/instances', query: { resourceId: id } }); }
function recovery(): void { if (session.can('workflow.recovery_tasks.read') && !session.switching) void router.push('/workflow/recovery-tasks'); }
function move(direction: number): void { page.value += direction; void load(); }
const date = (value: string | null | undefined) => value ? formatAdminDateTime(locale.value, value) : '—';
</script>
<template>
  <section v-if="allowed" class="workflow-operations">
    <el-button data-testid="workflow-operations-open" @click="load">{{ t('workflowR5.operations') }}</el-button>
    <div v-if="open" data-testid="workflow-operations-panel">
      <p>{{ t('workflowR5.operationsHelp') }}</p>
      <div class="workflow-operations__controls">
        <label>{{ t('workflowR5.kind') }}<select v-model="tab"><option value="steps">{{ t('workflowR5.steps') }}</option><option value="backlog">{{ t('workflowR5.backlog') }}</option></select></label>
        <label v-if="tab === 'steps'">{{ t('workflowR5.minimum') }}<input v-model.number="minimum" data-testid="workflow-operations-minimum" type="number" min="0" max="31536000"></label>
        <label v-if="tab === 'steps'"><input v-model="overdue" type="checkbox">{{ t('workflowR5.overdueOnly') }}</label>
        <el-button data-testid="workflow-operations-refresh" :disabled="loading" @click="load">{{ t('workflowR5.refresh') }}</el-button>
        <el-button v-if="session.can('workflow.recovery_tasks.read')" data-testid="workflow-operations-recovery" @click="recovery">{{ t('workflowR5.recovery') }}</el-button>
        <el-button @click="open = false; clear()">{{ t('workflowR5.close') }}</el-button>
      </div>
      <p v-if="loading" role="status">{{ t('workflowR5.loading') }}</p><p v-if="failed" role="alert">{{ t('workflowR5.failed') }}</p>
      <template v-if="observed">
        <p>{{ t('workflowR5.observed') }}: {{ date(observed) }} · {{ t('workflowR5.total', { count: total }) }}</p>
        <div class="workflow-operations__table">
          <table v-if="steps"><thead><tr><th>{{ t('workflowR5.node') }}</th><th>{{ t('workflowR5.dwell') }}</th><th>{{ t('workflowR5.sla') }}</th><th>{{ t('workflowR5.inspect') }}</th></tr></thead>
            <tbody><tr v-for="item in steps.items" :key="item.id"><td><code translate="no">{{ item.definitionKey }} / {{ item.nodeKey }} · {{ item.nodeTypeKey }}</code><p>{{ item.instanceStatusKey }} / {{ item.statusKey }}</p></td>
              <td>{{ item.dwellSeconds }}s</td><td>{{ date(item.dueAtUtc) }}<p>{{ item.isOverdue ? t('workflowR5.overdue') : item.pausedAtUtc ? t('workflowR5.paused') : item.slaStatusKey ?? '—' }}</p></td>
              <td><el-button data-testid="workflow-operations-inspect" @click="inspect(item.instanceId)">{{ t('workflowR5.inspect') }}</el-button></td></tr></tbody></table>
          <table v-if="backlog"><thead><tr><th>{{ t('workflowR5.node') }}</th><th>{{ t('workflowR5.schedule') }}</th><th>{{ t('workflowR5.inspect') }}</th></tr></thead>
            <tbody><tr v-for="item in backlog.items" :key="item.id"><td><code translate="no">{{ item.definitionKey }} / {{ item.nodeKey }}</code><p>{{ item.instanceStatusKey }} / {{ item.statusKey }}</p></td>
              <td>{{ date(item.dueAtUtc) }}<p>{{ t('workflowR5.ready') }}: {{ item.isReady ? t('workflowR5.yes') : t('workflowR5.no') }} · {{ t('workflowR5.attempts') }}: {{ item.attemptCount }}</p><p>{{ t('workflowR5.lease') }}: {{ date(item.leaseExpiresAtUtc) }}</p></td>
              <td><el-button @click="inspect(item.instanceId)">{{ t('workflowR5.inspect') }}</el-button></td></tr></tbody></table>
        </div>
        <div class="workflow-operations__controls"><el-button :disabled="loading || page <= 1" @click="move(-1)">{{ t('workflowR5.previous') }}</el-button>
          <span>{{ page }}</span><el-button :disabled="loading || page * 20 >= total" @click="move(1)">{{ t('workflowR5.next') }}</el-button></div>
      </template>
    </div>
  </section>
</template>
<style scoped>
.workflow-operations { margin-block: 1rem; min-width: 0; } .workflow-operations__controls { display: flex; flex-wrap: wrap; gap: .75rem; align-items: center; margin-block: 1rem; }
label { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; } input[type=number] { width: 8rem; } .workflow-operations__table { overflow: auto; }
table { border-collapse: collapse; width: 100%; text-align: start; } th, td { border: 1px solid var(--el-border-color); padding: .6rem; vertical-align: top; } code { overflow-wrap: anywhere; }
</style>
