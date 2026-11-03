<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';
import { ElButton, ElCard, ElForm, ElFormItem, ElInput, ElInputNumber, ElSelect, ElOption, ElDatePicker, ElDescriptions, ElDescriptionsItem, ElTag } from 'element-plus';
import { isWorkflowCollaborationId, readWorkflowCollaborationOffer, readWorkflowCollaborationAcceptance, type WorkflowCollaborationGrant } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showError, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { getWorkflowCollaborationGrant, proposeWorkflowCollaborationGrant, acceptWorkflowCollaborationGrant, revokeWorkflowCollaborationGrant } from '../api/workflow-collaboration-grants';
const session = useSessionStore(); const { t } = useAdminI18n();
const lookup = ref(''); const selected = ref<WorkflowCollaborationGrant>(); const receipt = ref<WorkflowCollaborationGrant>();
const busy = ref(false); const mustRefresh = ref(false); const targetScope = ref('tenant');
const proposal = reactive({ targetTenantId: '', sourceDefinitionId: '', sourceFormVersionId: '', targetWorkflowKey: '', inputFields: '', outputFields: '', maximum: 64, expires: '' });
const acceptance = reactive({ version: '', starter: '', users: '', maximum: 64 });
const inputs = reactive<Record<string, string>>({}); const outputs = reactive<Record<string, string>>({}); const reason = ref('');
const proposalFields = ['sourceDefinitionId', 'sourceFormVersionId', 'targetWorkflowKey'] as const;
const scope = computed(() => session.currentUser?.tenantId ? `tenant:${session.currentUser.tenantId.replaceAll('-', '').toLowerCase()}` : 'host');
const readable = computed(() => !session.switching && session.can('workflow.collaboration_grants.read'));
const target = computed(() => selected.value?.targetTenantScopeKey === scope.value);
const participant = computed(() => selected.value && [selected.value.sourceTenantScopeKey, selected.value.targetTenantScopeKey].includes(scope.value));
const canAccept = computed(() => readable.value && target.value && selected.value?.statusKey === 'proposed' && !mustRefresh.value && session.can('workflow.collaboration_grants.accept'));
const canRevoke = computed(() => readable.value && participant.value && selected.value?.statusKey !== 'revoked' && !mustRefresh.value && session.can('workflow.collaboration_grants.revoke'));
let generation = 0; let disposed = false; let controller = new AbortController(); const attempts = new Map<string, string>();
const list = (value: string) => value.trim() ? value.split(/[,，\n]/u).map(item => item.trim()) : [];
function retryKey(operation: string, payload: unknown): string {
  const fingerprint = JSON.stringify([operation, payload]); let value = attempts.get(fingerprint);
  if (!value) { value = crypto.randomUUID(); attempts.set(fingerprint, value); } return value;
}
function clearMappings(): void { for (const k of Object.keys(inputs)) delete inputs[k]; for (const k of Object.keys(outputs)) delete outputs[k]; }
function apply(value: WorkflowCollaborationGrant): void {
  if (![value.sourceTenantScopeKey, value.targetTenantScopeKey].includes(scope.value)) throw new TypeError('foreign grant');
  selected.value = value; mustRefresh.value = false; clearMappings();
  for (const k of value.offer.inputFieldKeys) inputs[k] = k;
  for (const k of value.offer.outputFieldKeys) outputs[k] = k;
  acceptance.version = ''; acceptance.starter = ''; acceptance.users = ''; acceptance.maximum = value.offer.maximumTargetActivations;
  reason.value = '';
}
function failure(error: unknown): void {
  if (error instanceof TypeError) showWarning(t('workflowCollaboration.invalid'));
  else if (typeof error === 'object' && error !== null && 'status' in error) showProblem(error, t('workflowCollaboration.failed'));
  else showError(t('workflowCollaboration.failed'));
}
async function load(): Promise<void> {
  if (!readable.value || busy.value) return; const current = ++generation; selected.value = undefined; receipt.value = undefined; busy.value = true;
  try { if (!isWorkflowCollaborationId(lookup.value)) throw new TypeError('id'); const value = await getWorkflowCollaborationGrant(lookup.value, controller.signal); if (current === generation && !disposed) apply(value); }
  catch (error) { if (current === generation && !disposed) failure(error); }
  finally { if (current === generation) busy.value = false; }
}
async function mutate(operation: 'propose' | 'accept' | 'revoke'): Promise<void> {
  if (busy.value || !readable.value || operation === 'propose' && !session.can('workflow.collaboration_grants.propose') ||
    operation === 'accept' && !canAccept.value || operation === 'revoke' && !canRevoke.value) return;
  const current = ++generation; busy.value = true;
  try {
    let result: WorkflowCollaborationGrant;
    if (operation === 'propose') {
      const offer = readWorkflowCollaborationOffer({ schemaVersion: 1, sourceDefinitionId: proposal.sourceDefinitionId, sourceFormVersionId: proposal.sourceFormVersionId,
        targetWorkflowKey: proposal.targetWorkflowKey, inputFieldKeys: list(proposal.inputFields), outputFieldKeys: list(proposal.outputFields), maximumTargetActivations: proposal.maximum, expiresAtUtc: proposal.expires });
      const tenant = targetScope.value === 'host' ? null : proposal.targetTenantId;
      if (Date.parse(offer.expiresAtUtc) <= Date.now() || tenant !== null && !isWorkflowCollaborationId(tenant) ||
        (tenant === null ? 'host' : `tenant:${tenant.replaceAll('-', '').toLowerCase()}`) === scope.value) throw new TypeError('target');
      result = await proposeWorkflowCollaborationGrant(tenant, retryKey(operation, [tenant, offer]), offer, controller.signal);
    } else {
      const grant = selected.value!;
      if (operation === 'accept') {
        if (Date.parse(grant.expiresAtUtc) <= Date.now()) throw new TypeError('expired');
        const policy = readWorkflowCollaborationAcceptance({ schemaVersion: 1, targetDefinitionVersionId: acceptance.version, starterUserId: acceptance.starter,
          allowedUserIds: list(acceptance.users), maximumActivities: acceptance.maximum, inputFields: Object.fromEntries(Object.entries(inputs).filter(([, local]) => local.trim())),
          outputFields: Object.fromEntries(Object.entries(outputs).filter(([, local]) => local.trim()).map(([source, local]) => [local, source])) }, grant.offer);
        const destinations = Object.values(outputs).filter(local => local.trim());
        if (new Set(destinations).size !== destinations.length) throw new TypeError('mapping');
        result = await acceptWorkflowCollaborationGrant(grant.id, retryKey(operation, [grant.id, policy]), policy, grant.offer, controller.signal);
      } else result = await revokeWorkflowCollaborationGrant(grant.id, grant.revision, retryKey(operation, [grant.id, grant.revision, reason.value.trim()]), reason.value, controller.signal);
    }
    if (current !== generation || disposed) return;
    receipt.value = result; lookup.value = result.id; mustRefresh.value = true;
    // 幂等回执可能来自撤销前；只把重新读取的权威状态用于下一次CAS。
    const refreshed = await getWorkflowCollaborationGrant(result.id, controller.signal);
    if (current !== generation || disposed) return; apply(refreshed); showSuccess(t('workflowCollaboration.done'));
  } catch (error) {
    if (current === generation && !disposed) {
      if (typeof error === 'object' && error !== null && 'status' in error) {
        if (error.status === 409) mustRefresh.value = true;
        // 服务端已拒绝当前读取/操作权时，不能继续展示或使用旧政策。
        if ([401, 403, 404].includes(Number(error.status))) { selected.value = undefined; receipt.value = undefined; clearMappings(); reason.value = ''; mustRefresh.value = true; }
      }
      failure(error);
    }
  }
  finally { if (current === generation) busy.value = false; }
}
function reset(): void {
  generation++; controller.abort(); controller = new AbortController(); selected.value = undefined; receipt.value = undefined; lookup.value = ''; reason.value = ''; clearMappings(); attempts.clear();
  Object.assign(proposal, { targetTenantId: '', sourceDefinitionId: '', sourceFormVersionId: '', targetWorkflowKey: '', inputFields: '', outputFields: '', maximum: 64, expires: '' });
  Object.assign(acceptance, { version: '', starter: '', users: '', maximum: 64 }); busy.value = false; mustRefresh.value = false;
}
watch(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope, session.currentUser?.permissions, session.switching]), reset, { flush: 'sync' });
onBeforeUnmount(() => { disposed = true; reset(); });
</script>
<template>
  <section class="art-page-stack" data-testid="collaboration-page">
    <h1>{{ t('navigation.workflowCollaboration.title') }}</h1><p>{{ t('workflowCollaboration.help') }}</p>
    <ElCard v-if="readable">
      <ElForm label-position="top" @submit.prevent="load">
        <ElFormItem :label="t('workflowCollaboration.lookup')" required><ElInput v-model="lookup" :aria-label="t('workflowCollaboration.lookup')" data-testid="grant-id" :disabled="busy" /></ElFormItem>
        <ElButton native-type="submit" :loading="busy" data-testid="grant-load">{{ t('workflowCollaboration.load') }}</ElButton>
      </ElForm>
      <p v-if="!selected">{{ t('workflowCollaboration.empty') }}</p>
      <template v-if="selected">
        <ElDescriptions :column="1" border data-testid="grant-detail">
          <ElDescriptionsItem :label="t('workflowCollaboration.lookup')">{{ selected.id }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.status')"><ElTag>{{ t(`workflowCollaboration.${selected.statusKey}`) }}</ElTag></ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.revision')">{{ selected.revision }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.source')">{{ selected.sourceTenantScopeKey }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.target')">{{ selected.targetTenantScopeKey }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.targetWorkflowKey')">{{ selected.offer.targetWorkflowKey }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.inputFields')">{{ selected.offer.inputFieldKeys.join(', ') }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.outputFields')">{{ selected.offer.outputFieldKeys.join(', ') }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.maximum')">{{ selected.offer.maximumTargetActivations }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.expires')">{{ selected.expiresAtUtc }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('workflowCollaboration.hash')">{{ selected.policyHash }}</ElDescriptionsItem>
          <template v-if="selected.acceptance">
            <ElDescriptionsItem :label="t('workflowCollaboration.targetVersion')">{{ selected.acceptance.targetDefinitionVersionId }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('workflowCollaboration.starter')">{{ selected.acceptance.starterUserId }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('workflowCollaboration.allowedUsers')">{{ selected.acceptance.allowedUserIds.join(', ') }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('workflowCollaboration.maximum')">{{ selected.acceptance.maximumActivities }}</ElDescriptionsItem>
            <ElDescriptionsItem :label="t('workflowCollaboration.inputMap')"><div v-for="(local, source) in selected.acceptance.inputFields" :key="source">{{ source }} → {{ local }}</div></ElDescriptionsItem>
            <ElDescriptionsItem :label="t('workflowCollaboration.outputMap')"><div v-for="(source, local) in selected.acceptance.outputFields" :key="local">{{ local }} → {{ source }}</div></ElDescriptionsItem>
          </template>
        </ElDescriptions>
        <p v-if="mustRefresh" role="status">{{ t('workflowCollaboration.refresh') }}</p>
        <ElForm v-if="canAccept" label-position="top" @submit.prevent="mutate('accept')" data-testid="grant-accept-form">
          <ElFormItem :label="t('workflowCollaboration.targetVersion')" required><ElInput v-model="acceptance.version" :aria-label="t('workflowCollaboration.targetVersion')" data-testid="grant-version" :disabled="busy" /></ElFormItem>
          <ElFormItem :label="t('workflowCollaboration.starter')" required><ElInput v-model="acceptance.starter" :aria-label="t('workflowCollaboration.starter')" data-testid="grant-starter" :disabled="busy" /></ElFormItem>
          <ElFormItem :label="t('workflowCollaboration.allowedUsers')" required><ElInput v-model="acceptance.users" :aria-label="t('workflowCollaboration.allowedUsers')" data-testid="grant-members" :disabled="busy" /></ElFormItem>
          <ElFormItem :label="t('workflowCollaboration.maximum')" required><ElInputNumber v-model="acceptance.maximum" :aria-label="t('workflowCollaboration.maximum')" :min="1" :max="selected.offer.maximumTargetActivations" :disabled="busy" /></ElFormItem>
          <ElFormItem v-for="field in selected.offer.inputFieldKeys" :key="`in-${field}`" :label="`${t('workflowCollaboration.inputMap')}: ${field}`"><ElInput v-model="inputs[field]" :aria-label="`${t('workflowCollaboration.inputMap')}: ${field}`" :disabled="busy" /></ElFormItem>
          <ElFormItem v-for="field in selected.offer.outputFieldKeys" :key="`out-${field}`" :label="`${t('workflowCollaboration.outputMap')}: ${field}`"><ElInput v-model="outputs[field]" :aria-label="`${t('workflowCollaboration.outputMap')}: ${field}`" :disabled="busy" /></ElFormItem>
          <ElButton native-type="submit" :disabled="busy" data-testid="grant-accept">{{ t('workflowCollaboration.accept') }}</ElButton>
        </ElForm>
        <ElForm v-if="canRevoke" label-position="top" @submit.prevent="mutate('revoke')">
          <ElFormItem :label="t('workflowCollaboration.reason')" required><ElInput v-model="reason" :aria-label="t('workflowCollaboration.reason')" :maxlength="512" data-testid="grant-reason" :disabled="busy" /></ElFormItem>
          <ElButton native-type="submit" type="danger" :disabled="busy" data-testid="grant-revoke">{{ t('workflowCollaboration.revoke') }}</ElButton>
        </ElForm>
      </template>
      <p v-if="receipt" role="status" data-testid="grant-receipt">{{ t('workflowCollaboration.receipt') }}: {{ receipt.id }} · {{ t(`workflowCollaboration.${receipt.statusKey}`) }} · {{ receipt.revision }}</p>
    </ElCard>
    <ElCard v-if="readable && session.can('workflow.collaboration_grants.propose')">
      <h2>{{ t('workflowCollaboration.propose') }}</h2>
      <ElForm label-position="top" @submit.prevent="mutate('propose')" data-testid="grant-propose-form">
        <ElFormItem :label="t('workflowCollaboration.targetScope')" required><ElSelect v-model="targetScope" :aria-label="t('workflowCollaboration.targetScope')" :disabled="busy"><ElOption value="host" :label="t('workflowCollaboration.host')"/><ElOption value="tenant" :label="t('workflowCollaboration.tenant')"/></ElSelect></ElFormItem>
        <ElFormItem v-if="targetScope === 'tenant'" :label="t('workflowCollaboration.targetTenantId')" required><ElInput v-model="proposal.targetTenantId" :aria-label="t('workflowCollaboration.targetTenantId')" data-testid="grant-target-tenant" :disabled="busy" /></ElFormItem>
        <ElFormItem v-for="field in proposalFields" :key="field" :label="t(`workflowCollaboration.${field}`)" required><ElInput v-model="proposal[field]" :aria-label="t(`workflowCollaboration.${field}`)" :data-testid="`grant-${field}`" :disabled="busy" /></ElFormItem>
        <ElFormItem :label="t('workflowCollaboration.inputFields')"><ElInput v-model="proposal.inputFields" :aria-label="t('workflowCollaboration.inputFields')" data-testid="grant-input-fields" :disabled="busy" /></ElFormItem>
        <ElFormItem :label="t('workflowCollaboration.outputFields')"><ElInput v-model="proposal.outputFields" :aria-label="t('workflowCollaboration.outputFields')" data-testid="grant-output-fields" :disabled="busy" /></ElFormItem>
        <ElFormItem :label="t('workflowCollaboration.maximum')" required><ElInputNumber v-model="proposal.maximum" :aria-label="t('workflowCollaboration.maximum')" :min="1" :max="4096" :disabled="busy" /></ElFormItem>
        <ElFormItem :label="t('workflowCollaboration.expires')" required><ElDatePicker v-model="proposal.expires" :aria-label="t('workflowCollaboration.expires')" type="datetime" value-format="YYYY-MM-DDTHH:mm:ssZ" :disabled="busy" /></ElFormItem>
        <ElButton native-type="submit" :disabled="busy" data-testid="grant-propose">{{ t('workflowCollaboration.propose') }}</ElButton>
      </ElForm>
    </ElCard>
  </section>
</template>
<style scoped>
:deep(.el-form) { max-width: 52rem; margin-block: 1rem; }
:deep(.el-descriptions__content) { overflow-wrap: anywhere; }
</style>
