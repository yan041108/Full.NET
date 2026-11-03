<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { ElAlert, ElButton, ElDialog, ElInput, ElMessageBox, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type AiAgentApprovalResponse, type AiAgentRunResponse, type AiModelConfigListItem,
  type AiPersonalScheduleCompletionStatus, type CreateAiAgentRunRequest } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalScheduleCompletionText } from '../i18n/personal-schedule-completion';
import { createAiAgentRun, getAiAgentRun, resumeAiAgentRun } from '../api/ai-agent-runs';
import { createAgentApproval, getAgentApproval } from '../api/ai-agent-approvals';
import { getScheduleCompletionProposal, getScheduleCompletionStatus, reconcileScheduleCompletion } from '../api/ai-personal-schedule-completion';
import { listAiModelConfigs } from '../api/ai-model-configs';

const props = defineProps<{ scheduleId: string; expectedVersion: number; originalRunId?: string }>();
const emit = defineEmits<{ close: []; changed: []; accepted: [runId: string] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof personalScheduleCompletionText>[1]) => personalScheduleCompletionText(locale.value, key);
const allowed = computed(() => !session.switching && ['calendar.personal_schedules.read', 'calendar.personal_schedules.set_status',
  'calendar.personal_schedules.assistant_complete', 'ai.agent_runs.create', 'ai.agent_runs.read', 'ai.agent_runs.resume',
  'ai.agent_approvals.read', 'ai.agent_approvals.request', 'ai.models.read'].every(code => session.can(code)));
const models = ref<AiModelConfigListItem[]>([]), modelId = ref(''), run = ref<AiAgentRunResponse>(), approval = ref<AiAgentApprovalResponse>();
const approvalId = ref(''), result = ref<AiPersonalScheduleCompletionStatus>(), busy = ref(false), failed = ref(false);
const originalRunId = ref(props.originalRunId ?? '');
let epoch = 0, controller = new AbortController(), pending: CreateAiAgentRunRequest | undefined, confirming = false;
function clear() { ++epoch; controller.abort(); controller = new AbortController(); if (confirming) ElMessageBox.close(); confirming = false;
  models.value = []; modelId.value = ''; run.value = undefined; originalRunId.value = ''; approval.value = undefined; approvalId.value = ''; result.value = undefined; pending = undefined; busy.value = false; }
function close() { clear(); emit('close'); }
watch(() => [props.scheduleId, props.expectedVersion, session.currentUser, session.currentContextName, session.switching, allowed.value], close, { deep: true, flush: 'sync' });
onBeforeUnmount(clear);
async function act(action: (current: number, signal: AbortSignal) => Promise<void>) {
  if (busy.value || !allowed.value) return; const current = epoch; busy.value = true; failed.value = false;
  try { await action(current, controller.signal); }
  catch (error) { if (current === epoch && error !== 'cancel' && error !== 'close') { failed.value = true; result.value = undefined; approval.value = undefined; } }
  finally { if (current === epoch) busy.value = false; }
}
async function refresh(current: number, signal: AbortSignal) {
  const id = originalRunId.value; if (!id) return;
  const value = await getAiAgentRun(id, signal); if (current !== epoch) return; run.value = value;
  if (approvalId.value) { const item = await getAgentApproval(approvalId.value, signal); if (current !== epoch) return; approval.value = item; }
  if (value.statusKey === 'completed' || value.statusKey === 'reconciliation_required') {
    try { const receipt = await getScheduleCompletionStatus(id, signal); if (current !== epoch) return; result.value = receipt;
      if (receipt.state === 'completed') emit('changed'); }
    catch (error) { if (!isFullNetProblemDetails(error) || error.status !== 404 || value.statusKey === 'completed') throw error; }
  }
}
async function create(current: number, signal: AbortSignal) {
  if (originalRunId.value || !modelId.value) return;
  pending ??= { clientRequestId: crypto.randomUUID(), definitionKey: 'fullnet-personal-schedule-complete-v1', modelConfigId: modelId.value,
    prompt: '', inputTokenLimit: 1, outputTokenLimit: 1, personalScheduleTarget: { id: props.scheduleId, version: props.expectedVersion } };
  const saved = await createAiAgentRun(pending, signal); if (current !== epoch) return;
  originalRunId.value = saved.runId; pending = undefined; emit('accepted', saved.runId); await refresh(current, signal);
}
async function request(current: number, signal: AbortSignal) {
  if (!run.value || approvalId.value || run.value.statusKey !== 'awaiting_approval') return;
  const proposal = await getScheduleCompletionProposal(run.value.id, signal); if (current !== epoch) return;
  if (proposal.scheduleId !== props.scheduleId || proposal.expectedVersion !== props.expectedVersion) throw new Error('client.invalid_schedule_completion_proposal');
  confirming = true; try { await ElMessageBox.confirm(text('confirm'), text('title'), { type: 'warning' }); } finally { confirming = false; }
  if (current !== epoch || signal.aborted || !allowed.value) return;
  const item = await createAgentApproval({ runId: proposal.runId, operationId: proposal.operationId, toolName: proposal.toolName,
    toolVersion: proposal.toolVersion, argumentsJson: proposal.argumentsJson }, signal);
  if (current === epoch) { approvalId.value = item.approvalId; await refresh(current, signal); }
}
async function resume(current: number, signal: AbortSignal) {
  if (!run.value || approval.value?.decisionKey !== 'approved' || approval.value.consumedAtUtc || run.value.statusKey !== 'awaiting_approval') return;
  await resumeAiAgentRun(run.value.id, signal); if (current === epoch) await refresh(current, signal);
}
async function reconcile(current: number, signal: AbortSignal) {
  if (!run.value || result.value?.state !== 'unknown') return;
  const receipt = await reconcileScheduleCompletion(run.value.id, signal); if (current !== epoch) return;
  result.value = receipt; if (receipt.state === 'completed') emit('changed');
}
const runLabel = computed(() => {
  const key = run.value?.statusKey; return key === 'queued' || key === 'running' || key === 'awaiting_approval' ? text(key)
    : key === 'completed' ? text('runAccepted') : key === 'reconciliation_required' ? text('unknown') : key ? text('failed') : '';
});
const resultLabel = computed(() => { const state = result.value?.state; return state === 'accepted' || state === 'unknown' || state === 'completed' || state === 'rejected' ? text(state) : ''; });
const reasonLabel = computed(() => { const reason = result.value?.reason; return reason === 'authorization_unavailable' || reason === 'intent_expired' || reason === 'target_changed' ? text(reason) : ''; });
onMounted(() => act(async (current, signal) => { const value = await listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true }, signal);
  if (current === epoch) { models.value = value.items; if (originalRunId.value) await refresh(current, signal); } }));
</script>
<template>
  <section v-if="allowed" class="schedule-completion-layer">
    <ElDialog :model-value="true" :title="text('title')" width="min(760px, calc(100vw - 32px))" data-testid="schedule-completion-dialog" @close="close">
      <div class="schedule-completion">
        <p>{{ text('hint') }}</p><p>{{ text('target') }}: <code>{{ scheduleId }}</code> · {{ text('version') }}: {{ expectedVersion }}</p>
        <ElAlert v-if="failed" :title="text('unavailable')" type="error" :closable="false" role="alert" />
        <label for="schedule-completion-model">{{ text('model') }}</label>
        <ElSelect id="schedule-completion-model" v-model="modelId" :aria-label="text('model')" :disabled="busy || !!originalRunId || !!pending" data-testid="schedule-completion-model">
          <ElOption v-for="model in models" :key="model.id" :value="model.id" :label="model.name" />
        </ElSelect>
        <div class="schedule-completion__actions">
          <ElButton v-if="!originalRunId" :disabled="busy || !modelId" data-testid="schedule-completion-create" @click="act(create)">{{ text('create') }}</ElButton>
          <ElButton v-if="originalRunId" :disabled="busy" data-testid="schedule-completion-refresh" @click="act(refresh)">{{ text('refresh') }}</ElButton>
          <ElButton v-if="run?.statusKey === 'awaiting_approval' && !approvalId" :disabled="busy" data-testid="schedule-completion-request" @click="act(request)">{{ text('request') }}</ElButton>
          <ElButton v-if="run?.statusKey === 'awaiting_approval' && approval?.decisionKey === 'approved' && !approval.consumedAtUtc" :disabled="busy" data-testid="schedule-completion-resume" @click="act(resume)">{{ text('resume') }}</ElButton>
          <ElButton v-if="result?.state === 'unknown'" :disabled="busy" data-testid="schedule-completion-reconcile" @click="act(reconcile)">{{ text('reconcile') }}</ElButton>
        </div>
        <p v-if="run" role="status" data-testid="schedule-completion-run-state">{{ runLabel }}</p>
        <p v-if="originalRunId">{{ text('run') }}: <code>{{ originalRunId }}</code></p>
        <label v-if="approvalId" for="schedule-completion-approval">{{ text('approval') }}</label>
        <ElInput v-if="approvalId" id="schedule-completion-approval" :model-value="approvalId" readonly data-testid="schedule-completion-approval" />
        <RouterLink v-if="approvalId && session.can('ai.agent_runs.read')" to="/ai/agent-runs">{{ text('review') }}</RouterLink>
        <div v-if="result" role="status" data-testid="schedule-completion-result">
          <p>{{ resultLabel }}</p><p>{{ text('operation') }}: <code>{{ result.operationId }}</code></p>
          <p v-if="reasonLabel">{{ text('reason') }}: {{ reasonLabel }}</p>
          <p v-if="result.state === 'completed'">{{ text('actualVersion') }}: {{ result.actualVersion }} · {{ result.completedAtUtc }}</p>
        </div>
        <p>{{ text('closeHint') }}</p>
      </div>
      <template #footer><ElButton @click="close">{{ text('close') }}</ElButton></template>
    </ElDialog>
  </section>
</template>
<style scoped>
.schedule-completion { display: grid; gap: .8rem; min-width: 0; overflow-wrap: anywhere; }
.schedule-completion p { margin: 0; }.schedule-completion :deep(.el-select) { width: 100%; }
.schedule-completion__actions { display: flex; flex-wrap: wrap; gap: .6rem; }.schedule-completion__actions :deep(.el-button) { margin-left: 0; max-width: 100%; white-space: normal; height: auto; min-height: 32px; }
</style>
