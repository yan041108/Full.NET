<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import type { MessageKey } from '@fullnet/admin-i18n';
import { ElButton, ElInput, ElOption, ElSelect } from 'element-plus';
import { listWorkflowConnectorCalls, reconcileWorkflowConnector, type WorkflowConnectorCall, type WorkflowConnectorResolution } from '../api/workflow-connectors';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ instanceId: string; revision: number; statusKey: string; canReconcile: boolean }>();
const emit = defineEmits<{ resolved: [] }>(); const { t } = useAdminI18n();
const statusLabel = (value: string) => t(`workflowDesigner.connector.status.${value}` as MessageKey);
const items = ref<WorkflowConnectorCall[]>([]); const page = ref(1); const total = ref(0); const loading = ref(false); const pending = ref(false);
const failed = ref(false); const selected = ref<WorkflowConnectorCall>(); const action = ref('accept_result'); const attempt = ref('');
const reason = ref(''); const evidence = ref(''); const receipt = ref<WorkflowConnectorResolution>(); const submitFailed = ref(false);
let generation = 0; let controller: AbortController | undefined; let intent = ''; let idempotencyKey = '';
const terminal = computed(() => ['completed', 'rejected', 'cancelled'].includes(props.statusKey));
const eligible = computed(() => selected.value && ['outcome_unknown', 'failed', 'rejected', 'cancelled'].includes(selected.value.statusKey));
const proofs = computed(() => selected.value?.attempts.filter(item => item.statusKey === 'received' && item.outcomeKey === 'succeeded') ?? []);
const canSubmit = computed(() => props.canReconcile && eligible.value && !pending.value && !loading.value && reason.value.trim().length > 0 &&
  reason.value.trim().length <= 512 && evidence.value.trim().length > 0 && evidence.value.trim().length <= 256 &&
  (action.value !== 'accept_result' || proofs.value.some(item => item.id === attempt.value)) &&
  (action.value !== 'confirm_no_effect' || terminal.value && proofs.value.length === 0) &&
  (action.value !== 'retry' || !terminal.value && !selected.value?.cancellationRequested));
async function load(): Promise<void> {
  controller?.abort(); controller = new AbortController(); const expected = ++generation; loading.value = true; failed.value = false;
  try { const result = await listWorkflowConnectorCalls(props.instanceId, page.value, controller.signal);
    if (expected === generation) { items.value = [...result.items]; total.value = result.total; selected.value = undefined; } }
  catch { if (expected === generation) { items.value = []; total.value = 0; failed.value = true; } }
  finally { if (expected === generation) loading.value = false; }
}
watch(() => [props.instanceId, props.revision], () => { page.value = 1; selected.value = undefined; receipt.value = undefined; pending.value = false; intent = ''; idempotencyKey = ''; void load(); }, { immediate: true });
onBeforeUnmount(() => { generation++; controller?.abort(); });
function select(call: WorkflowConnectorCall): void { selected.value = call; reason.value = ''; evidence.value = ''; attempt.value = ''; action.value = 'accept_result'; submitFailed.value = false; receipt.value = undefined; }
function changePage(delta: number): void { page.value += delta; void load(); }
async function submit(): Promise<void> {
  if (!canSubmit.value || !selected.value) return; const call = selected.value; const expected = generation;
  const body = { expectedInstanceRevision: props.revision, expectedRevision: call.revision, actionKey: action.value,
    attemptId: action.value === 'accept_result' ? attempt.value : null, reason: reason.value.trim(), evidenceReference: evidence.value.trim() };
  const next = JSON.stringify({ instanceId: props.instanceId, callId: call.id, ...body });
  if (next !== intent) { intent = next; idempotencyKey = crypto.randomUUID(); }
  pending.value = true; submitFailed.value = false;
  try {
    const result = await reconcileWorkflowConnector(props.instanceId, call.id, { ...body, idempotencyKey }, controller?.signal);
    if (expected !== generation) return; receipt.value = result; selected.value = undefined; emit('resolved');
  } catch {
    if (expected !== generation) return; submitFailed.value = true;
    // 结果不确定时保留原请求和幂等键；显式刷新后才创建基于新修订的操作。
  } finally { if (expected === generation) pending.value = false; }
}
</script>
<template>
  <section class="connector-panel" :aria-label="t('workflowDesigner.connector.operations')" :aria-busy="loading || pending">
    <div class="connector-heading"><h3>{{ t('workflowDesigner.connector.operations') }}</h3><ElButton :disabled="loading || pending" @click="load">{{ t('workflowDesigner.connector.refresh') }}</ElButton></div>
    <p v-if="failed" role="alert">{{ t('workflowDesigner.connector.loadFailed') }}</p>
    <p v-else-if="!loading && items.length === 0">{{ t('workflowDesigner.connector.empty') }}</p>
    <div v-for="call in items" :key="call.id" class="connector-call">
      <div><strong translate="no">{{ call.nodeKey }} · {{ call.connectorKey }}@{{ call.connectorVersion }}</strong><p>{{ statusLabel(call.statusKey) }}</p><p v-if="call.errorKey" translate="no">{{ call.errorKey }}</p>
        <p v-if="call.requiresReconciliation" role="status">{{ t('workflowDesigner.connector.reconciliationRequired') }}</p></div>
      <ElButton v-if="canReconcile && ['outcome_unknown', 'failed', 'rejected', 'cancelled'].includes(call.statusKey)" :disabled="loading || pending" @click="select(call)">{{ t('workflowDesigner.connector.reconcile') }}</ElButton>
    </div>
    <div v-if="total > 20" class="connector-pagination"><ElButton :disabled="page <= 1 || loading || pending" @click="changePage(-1)">{{ t('workflowDesigner.connector.previous') }}</ElButton><span>{{ page }} / {{ Math.ceil(total / 20) }}</span><ElButton :disabled="page * 20 >= total || loading || pending" @click="changePage(1)">{{ t('workflowDesigner.connector.next') }}</ElButton></div>
    <form v-if="selected && canReconcile" class="connector-resolution" @submit.prevent="submit">
      <p>{{ t('workflowDesigner.connector.reconcileHelp') }}</p>
      <ElSelect v-model="action" :disabled="pending" :aria-label="t('workflowDesigner.connector.action')">
        <ElOption value="accept_result" :disabled="proofs.length === 0" :label="t('workflowDesigner.connector.acceptResult')" />
        <ElOption value="retry" :disabled="terminal || selected.cancellationRequested" :label="t('workflowDesigner.connector.retry')" />
        <ElOption value="confirm_no_effect" :disabled="!terminal || proofs.length > 0" :label="t('workflowDesigner.connector.confirmNoEffect')" />
      </ElSelect>
      <ElSelect v-if="action === 'accept_result'" v-model="attempt" :disabled="pending" :aria-label="t('workflowDesigner.connector.attempt')"><ElOption v-for="proof in proofs" :key="proof.id" :value="proof.id" :label="`${proof.id} · ${proof.responseHash}`" /></ElSelect>
      <ElInput v-model="evidence" :disabled="pending" :maxlength="256" :aria-label="t('workflowDesigner.connector.evidence')" :placeholder="t('workflowDesigner.connector.evidence')" />
      <ElInput v-model="reason" type="textarea" :disabled="pending" :maxlength="512" :aria-label="t('workflowDesigner.connector.reason')" :placeholder="t('workflowDesigner.connector.reason')" />
      <p v-if="submitFailed" role="alert">{{ t('workflowDesigner.connector.submitFailed') }}</p>
      <div><ElButton native-type="submit" :disabled="!canSubmit" data-testid="connector-reconcile">{{ t('workflowDesigner.connector.reconcile') }}</ElButton><ElButton :disabled="pending" @click="selected = undefined">{{ t('common.cancel') }}</ElButton></div>
    </form>
    <p v-if="receipt" role="status">{{ t('workflowDesigner.connector.receipt') }} <span translate="no">{{ receipt.id }}</span></p>
  </section>
</template>
<style scoped>
.connector-panel, .connector-resolution { display: grid; gap: 16px; }
.connector-heading, .connector-call, .connector-pagination { display: flex; gap: 12px; align-items: center; justify-content: space-between; flex-wrap: wrap; }
.connector-call { border-block-end: 1px solid var(--el-border-color-light); padding-block: 12px; }
.connector-call p { margin-block: 6px; overflow-wrap: anywhere; }
.connector-resolution { border: 1px solid var(--el-border-color); padding: 16px; border-radius: 6px; }
</style>
