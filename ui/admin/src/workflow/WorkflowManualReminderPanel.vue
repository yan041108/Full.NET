<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElMessage } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { listWorkflowReminderTargets, requestWorkflowReminder, type WorkflowReminderRequest, type WorkflowReminderTargets, type WorkflowReminderTarget } from '../api/workflow-reminders';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ instanceId: string; revision: number; statusKey: string }>();
const session = useSessionStore();
const { t } = useAdminI18n();
const targets = ref<WorkflowReminderTargets>();
const busy = ref(false);
const failed = ref(false);
let epoch = 0;
let controller: AbortController | undefined;
let pending: WorkflowReminderRequest | undefined;
const context = computed(() => JSON.stringify([props.instanceId, props.revision, props.statusKey, session.currentUser?.id,
  session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope, session.switching, session.can('workflow.instances.remind')]));
watch(context, () => { epoch++; controller?.abort(); targets.value = undefined; pending = undefined; busy.value = false; failed.value = false; void load(); }, { immediate: true, flush: 'sync' });
onBeforeUnmount(() => { epoch++; controller?.abort(); });

async function load(): Promise<void> {
  if (!session.can('workflow.instances.remind') || session.switching || props.statusKey !== 'active') return;
  const current = epoch;
  controller?.abort(); controller = new AbortController();
  try {
    const result = await listWorkflowReminderTargets(props.instanceId, controller.signal);
    if (current === epoch) { targets.value = result; failed.value = false; }
  } catch (error: unknown) {
    if (current !== epoch) return;
    if (isFullNetProblemDetails(error) && [403, 404].includes(error.status)) targets.value = undefined;
    else failed.value = true;
  }
}

async function remind(target: WorkflowReminderTarget): Promise<void> {
  if (busy.value || !target.canRemind || !targets.value || !session.can('workflow.instances.remind') || session.switching) return;
  const current = epoch; const id = props.instanceId;
  if (pending && pending.stepId !== target.stepId) return;
  pending ??= { stepId: target.stepId, expectedInstanceRevision: targets.value.instanceRevision,
    expectedStepRevision: target.stepRevision, idempotencyKey: `remind-${crypto.randomUUID()}` };
  busy.value = true;
  try {
    const receipt = await requestWorkflowReminder(id, pending);
    if (current !== epoch) return;
    if (targets.value) targets.value = { ...targets.value, items: targets.value.items.map(item => item.stepId === receipt.stepId
      ? { ...item, manualCount: receipt.manualSequence, lastRequestedAtUtc: receipt.acceptedAtUtc, canRemind: false } : item) };
    pending = undefined; ElMessage.success(t('workflowInstances.remindAccepted')); await load();
  } catch (error: unknown) {
    if (current !== epoch) return;
    ElMessage.error(t('workflowInstances.remindFailed'));
    if (isFullNetProblemDetails(error) && error.status < 500) {
      pending = undefined;
      if ([403, 404].includes(error.status)) targets.value = undefined;
      else await load();
    }
    // 网络结果未知时保留首次请求键，不允许切换其他节点制造重复通知。
  } finally { if (current === epoch) busy.value = false; }
}
</script>

<template>
  <section v-if="targets?.items.length || failed" class="workflow-reminders" :aria-label="t('workflowInstances.remind')">
    <div class="workflow-reminders__heading"><strong>{{ t('workflowInstances.remind') }}</strong><span>{{ t('workflowInstances.remindHint') }}</span></div>
    <el-button v-if="failed" @click="load">{{ t('workflowInstances.remindRetry') }}</el-button>
    <div v-for="target in targets?.items" :key="target.stepId" class="workflow-reminders__row">
      <code translate="no">{{ target.nodeKey }}</code>
      <span>{{ t('workflowInstances.remindCount', { count: target.manualCount }) }}</span>
      <el-button :disabled="busy || !target.canRemind || !!pending && pending.stepId !== target.stepId" :loading="busy && pending?.stepId === target.stepId"
        data-testid="workflow-manual-remind" @click="remind(target)">{{ t('workflowInstances.remind') }}</el-button>
    </div>
  </section>
</template>

<style scoped>
.workflow-reminders { padding: 1rem; border: 1px solid var(--el-border-color-lighter); border-radius: 8px; background: var(--el-bg-color); }
.workflow-reminders__heading { display: flex; flex-wrap: wrap; gap: .75rem; align-items: baseline; }
.workflow-reminders__heading span { color: var(--el-text-color-secondary); font-size: .875rem; }
.workflow-reminders__row { display: flex; flex-wrap: wrap; align-items: center; gap: 1rem; padding-top: .75rem; }
.workflow-reminders__row code { flex: 1; overflow-wrap: anywhere; }
</style>
