<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElInput, ElMessageBox } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type AiAgentWorkflowProposalResponse, type AiAgentApprovalResponse } from '@fullnet/client-contracts';
import * as api from '../api/ai-agent-approvals';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { aiWorkflowText } from '../i18n/ai-workflow';
const props = defineProps<{ runId?: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof aiWorkflowText>[1]) => aiWorkflowText(locale.value, key);
const readable = computed(() => !session.switching && session.can('ai.agent_approvals.read'));
const requestable = computed(() => readable.value && session.can('ai.agent_runs.read') && session.can('ai.agent_approvals.request'));
const proposal = ref<AiAgentWorkflowProposalResponse>(), approval = ref<AiAgentApprovalResponse>(), approvalId = ref('');
const busy = ref(false), failure = ref(''), requested = ref(false), refreshRequired = ref(false);
let epoch = 0, controller = new AbortController(), confirming = false;
function reset() {
  epoch++; controller.abort(); controller = new AbortController(); if (confirming) ElMessageBox.close(); confirming = false;
  proposal.value = undefined; approval.value = undefined; approvalId.value = ''; busy.value = false; failure.value = ''; requested.value = false; refreshRequired.value = false;
}
watch(() => [session.currentUser, session.currentContextName, session.switching, props.runId], reset, { deep: true, flush: 'sync' });
onBeforeUnmount(reset);
async function act(action: (current: number, signal: AbortSignal) => Promise<void>) {
  if (busy.value || !readable.value) return;
  const current = epoch; busy.value = true; failure.value = '';
  try { await action(current, controller.signal); }
  catch (error) { if (current === epoch && error !== 'cancel' && error !== 'close') failure.value = isFullNetProblemDetails(error) ? `${text('unavailable')} (${error.code})` : text('unavailable'); }
  finally { if (current === epoch) busy.value = false; }
}
async function readProposal(current: number, signal: AbortSignal) {
  if (!props.runId || !requestable.value) return;
  proposal.value = undefined;
  const result = await api.getAgentWorkflowProposal(props.runId, signal);
  if (current === epoch) proposal.value = result;
}
async function request(current: number, signal: AbortSignal) {
  const item = proposal.value; if (!item || !requestable.value) return;
  confirming = true;
  try { await ElMessageBox.confirm(text('confirmRequest'), text('proposal'), { type: 'warning' }); }
  finally { confirming = false; }
  if (current !== epoch || signal.aborted || !requestable.value) return;
  const result = await api.createAgentApproval({ runId: item.runId, operationId: item.operationId, toolName: item.toolName,
    toolVersion: item.toolVersion, argumentsJson: item.argumentsJson }, signal);
  if (current === epoch) { approvalId.value = result.approvalId; requested.value = true; await lookup(current, signal); }
}
async function lookup(current: number, signal: AbortSignal) {
  if (!isGuid(approvalId.value)) return;
  approval.value = undefined;
  const result = await api.getAgentApproval(approvalId.value, signal);
  if (current === epoch) { approval.value = result; refreshRequired.value = false; }
}
async function decide(approve: boolean, current: number, signal: AbortSignal) {
  const item = approval.value;
  if (!item || refreshRequired.value || !session.can('ai.agent_approvals.decide')) return;
  confirming = true;
  try { await ElMessageBox.confirm(text(approve ? 'confirmApprove' : 'confirmReject'), text('approval'), { type: 'warning' }); }
  finally { confirming = false; }
  if (current !== epoch || signal.aborted || !session.can('ai.agent_approvals.decide')) return;
  try {
    const result = await api.decideAgentApproval(item.id, { approve, expectedVersion: item.version }, signal);
    if (current === epoch) approval.value = result;
  } catch (error) { if (current === epoch) refreshRequired.value = true; throw error; }
}
const pending = computed(() => approval.value?.decisionKey === 'pending' && !approval.value.consumedAtUtc && Date.parse(approval.value.expiresAtUtc) > Date.now());
const decision = computed(() => {
  const key = approval.value?.decisionKey;
  return key === 'pending' || key === 'approved' || key === 'rejected' ? text(key) : key ?? '';
});
watch(approvalId, () => { approval.value = undefined; refreshRequired.value = false; });
</script>
<template>
  <ElCard v-if="readable" shadow="never" class="ai-workflow-approval" data-testid="ai-workflow-approval">
    <template #header>{{ text('approval') }}</template>
    <ElAlert v-if="failure" :title="failure" type="error" :closable="false" role="alert" />
    <ElAlert v-if="requested" :title="text('requested')" type="success" :closable="false" />
    <ElAlert v-if="refreshRequired" :title="text('decisionUnknown')" type="warning" :closable="false" />
    <div v-if="runId && requestable">
      <ElButton :disabled="busy" data-testid="ai-workflow-read-proposal" @click="act(readProposal)">{{ text('readProposal') }}</ElButton>
      <ElDescriptions v-if="proposal" :column="1" border>
        <ElDescriptionsItem :label="text('current')">{{ proposal.currentTitle }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('proposed')">{{ proposal.proposedTitle }}</ElDescriptionsItem>
      </ElDescriptions>
      <ElButton v-if="proposal" :disabled="busy || requested" data-testid="ai-workflow-request-approval" @click="act(request)">{{ text('request') }}</ElButton>
    </div>
    <div class="ai-workflow-approval__lookup">
      <ElInput v-model="approvalId" :aria-label="text('approvalId')" :placeholder="text('approvalId')" :disabled="busy" />
      <ElButton :disabled="busy || !isGuid(approvalId)" data-testid="ai-workflow-lookup-approval" @click="act(lookup)">{{ text('lookup') }}</ElButton>
    </div>
    <ElDescriptions v-if="approval" :column="1" border>
      <ElDescriptionsItem :label="text('decision')">{{ decision }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="text('action')">{{ approval.actionSummary }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="text('approvalTarget')">{{ approval.targetSummary }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="text('approvalChange')">{{ approval.changeSummary }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="text('scope')">{{ approval.scopeSummary }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="text('expires')">{{ new Date(approval.expiresAtUtc).toLocaleString(locale) }}</ElDescriptionsItem>
    </ElDescriptions>
    <p v-if="approval">{{ text(approval.consumedAtUtc ? 'consumed' : 'notConsumed') }}</p>
    <div v-if="pending && session.can('ai.agent_approvals.decide')" class="ai-workflow-approval__decide">
      <ElButton type="success" :disabled="busy || refreshRequired" data-testid="ai-workflow-approve" @click="act((current, signal) => decide(true, current, signal))">{{ text('approve') }}</ElButton>
      <ElButton type="danger" :disabled="busy || refreshRequired" @click="act((current, signal) => decide(false, current, signal))">{{ text('reject') }}</ElButton>
    </div>
  </ElCard>
</template>
<style scoped>
.ai-workflow-approval { overflow-wrap: anywhere; min-width: 0; }
.ai-workflow-approval__lookup { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
.ai-workflow-approval__lookup .el-input { flex: 1 1 280px; }
.ai-workflow-approval__decide { display: flex; flex-wrap: wrap; gap: 12px; }
/* 独立批准者需要清晰辨认两种决定；局部收紧配色，不改变共享主题。 */
.ai-workflow-approval__decide :deep(.el-button--success) {
  --el-button-bg-color: #166534;
  --el-button-border-color: #166534;
  --el-button-hover-bg-color: #14532d;
  --el-button-hover-border-color: #14532d;
  --el-button-active-bg-color: #14532d;
  --el-button-active-border-color: #14532d;
}
.ai-workflow-approval__decide :deep(.el-button--danger) {
  --el-button-bg-color: #b91c1c;
  --el-button-border-color: #b91c1c;
  --el-button-hover-bg-color: #991b1b;
  --el-button-hover-border-color: #991b1b;
  --el-button-active-bg-color: #991b1b;
  --el-button-active-border-color: #991b1b;
}
</style>
