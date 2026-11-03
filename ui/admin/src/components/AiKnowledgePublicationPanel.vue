<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCheckbox, ElDescriptions, ElDescriptionsItem, ElInput, ElMessageBox } from 'element-plus';
import { isGuid, type AiKnowledgeDirectoryResponse, type AiKnowledgePublicationResponse, type AiKnowledgePublicationHeadResponse, type RequestAiKnowledgePublication } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeGovernanceText } from '../i18n/ai-knowledge-governance';
import * as api from '../api/ai-knowledge-governance';

const props = defineProps<{ open: boolean; knowledgeBaseId: string; document?: AiKnowledgeDirectoryResponse }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof knowledgeGovernanceText>[1]) => knowledgeGovernanceText(locale.value, key);
const readable = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read', 'ai.knowledge_publications.read'].every(code => session.can(code)));
const can = (action: string) => readable.value && session.can(`ai.knowledge_publications.${action}`);
const proposal = ref<AiKnowledgePublicationResponse>(); const head = ref<AiKnowledgePublicationHeadResponse>();
const proposalId = ref(''), busy = ref(false), failed = ref(false), unknown = ref(false), reviewed = ref(false), preview = ref(false);
const from = ref(''), to = ref('');
let epoch = 0, controller = new AbortController(), confirming = false;
let pendingRequest: { fingerprint: string; body: RequestAiKnowledgePublication } | undefined;
const ids = () => ({ knowledgeBaseId: props.knowledgeBaseId, documentId: props.document!.id });
const local = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
function reset() {
  ++epoch; controller.abort(); controller = new AbortController(); if (confirming) ElMessageBox.close(); confirming = false;
  proposal.value = undefined; head.value = undefined; proposalId.value = props.document?.pendingPublicationId ?? props.document?.publicationId ?? '';
  busy.value = false; failed.value = false; unknown.value = false; reviewed.value = false; preview.value = false; pendingRequest = undefined;
  from.value = local(new Date()); to.value = local(new Date(Date.now() + 30 * 86400000));
}
watch(() => [props.open, props.knowledgeBaseId, props.document?.id, session.currentUser, session.currentContextName, session.switching], () => {
  reset(); if (props.open && readable.value && props.document) void act(refresh);
}, { immediate: true, deep: true, flush: 'sync' });
onBeforeUnmount(reset);
async function act(action: (current: number, signal: AbortSignal) => Promise<void>) {
  if (busy.value || !props.open || !props.document || !readable.value) return;
  const current = epoch; busy.value = true; failed.value = false;
  try { await action(current, controller.signal); }
  catch (error) { if (current === epoch && error !== 'cancel' && error !== 'close') failed.value = true; }
  finally { if (current === epoch) busy.value = false; }
}
async function refresh(current: number, signal: AbortSignal) {
  proposal.value = undefined; head.value = undefined; reviewed.value = false;
  const value = await api.readPublicationHead(ids(), signal);
  if (current !== epoch) return; head.value = value;
  if (isGuid(proposalId.value)) {
    const item = await api.readPublication(ids(), proposalId.value, signal); if (current !== epoch) return;
    proposal.value = item;
  }
  unknown.value = false;
}
async function confirm(key: 'confirmRequest' | 'confirmReview' | 'confirmPublish' | 'confirmWithdraw', current: number, signal: AbortSignal) {
  confirming = true;
  try { await ElMessageBox.confirm(text(key), text('publication'), { type: 'warning' }); }
  finally { confirming = false; }
  return current === epoch && !signal.aborted && props.open && readable.value;
}
async function request(current: number, signal: AbortSignal) {
  const versionId = props.document?.candidateVersionId;
  if (!can('request') || !versionId || !head.value || !from.value || !to.value) return;
  const draft = { versionId, expectedHeadVersion: head.value.version, effectiveFromUtc: new Date(from.value).toISOString(), effectiveToUtc: new Date(to.value).toISOString() };
  if (Date.parse(draft.effectiveToUtc) <= Date.parse(draft.effectiveFromUtc)) throw new Error('invalid');
  const fingerprint = JSON.stringify(draft);
  if (pendingRequest?.fingerprint !== fingerprint) pendingRequest = { fingerprint, body: { requestKey: crypto.randomUUID(), ...draft } };
  const original = pendingRequest;
  if (!await confirm('confirmRequest', current, signal)) return;
  const item = await api.requestPublication(ids(), original.body, signal);
  if (current !== epoch) return;
  proposalId.value = item.id; proposal.value = item; pendingRequest = undefined; reviewed.value = false; emit('changed');
}
async function decide(approve: boolean, current: number, signal: AbortSignal) {
  const item = proposal.value;
  if (!item || !reviewed.value || unknown.value || item.stateKey !== 'pending' || item.actorUserId === session.currentUser?.id || !can('review')) return;
  if (!await confirm('confirmReview', current, signal)) return;
  try {
    const result = await api.reviewPublication(ids(), item.id, { approve, version: item.version, snapshotHash: item.snapshotHash }, signal);
    if (current === epoch) { proposal.value = result; reviewed.value = false; emit('changed'); }
  } catch (error) { if (current === epoch) unknown.value = true; throw error; }
}
async function publish(current: number, signal: AbortSignal) {
  const item = proposal.value;
  if (!item || !reviewed.value || unknown.value || item.stateKey !== 'approved' || item.actorUserId !== session.currentUser?.id || !can('publish')) return;
  if (!await confirm('confirmPublish', current, signal)) return;
  try {
    const result = await api.publishPublication(ids(), item.id, { version: item.version, snapshotHash: item.snapshotHash }, signal);
    if (current === epoch) { proposal.value = result; reviewed.value = false; await refresh(current, signal); emit('changed'); }
  } catch (error) { if (current === epoch) unknown.value = true; throw error; }
}
async function withdraw(current: number, signal: AbortSignal) {
  const original = head.value;
  if (!original?.publicationId || unknown.value || !can('withdraw') || !await confirm('confirmWithdraw', current, signal)) return;
  try {
    const result = await api.withdrawPublication(ids(), { expectedHeadVersion: original.version, publicationId: original.publicationId }, signal);
    if (current === epoch) { head.value = result; proposal.value = undefined; proposalId.value = ''; reviewed.value = false; emit('changed'); }
  } catch (error) { if (current === epoch) unknown.value = true; throw error; }
}
watch(proposalId, () => { proposal.value = undefined; reviewed.value = false; }, { flush: 'sync' });
const knownStates = ['pending', 'approved', 'published', 'rejected', 'withdrawn', 'superseded'] as const;
const state = computed(() => knownStates.find(key => key === proposal.value?.stateKey));
</script>
<template>
  <ArtFormDialog :open="open && readable" :title="text('publication')" width="800px" :show-confirm="false" :cancel-label="text('close')" :trap-focus="true" @update:open="emit('update:open', $event)">
    <section class="knowledge-publication" data-testid="knowledge-publication">
      <h2>{{ document?.title }}</h2><p>{{ text('publicationHint') }}</p>
      <ElAlert v-if="failed" :title="text('unavailable')" type="error" :closable="false" />
      <ElAlert v-if="unknown" :title="text('unknown')" type="warning" :closable="false" />
      <div class="knowledge-publication__toolbar">
        <ElButton :disabled="busy" @click="act(refresh)">{{ text('refresh') }}</ElButton>
        <ElButton v-if="head?.publicationId && can('withdraw')" type="danger" :disabled="busy || unknown" @click="act(withdraw)">{{ text('withdraw') }}</ElButton>
      </div>
      <p>{{ text('head') }}: {{ head?.version ?? '—' }} · {{ head?.publicationId ?? text('draft') }}</p>
      <fieldset v-if="can('request') && document?.candidateVersionId" :disabled="busy">
        <legend>{{ text('request') }}</legend><p>{{ text('candidate') }}: {{ document.candidateVersionId }}</p>
        <label>{{ text('from') }} <input v-model="from" type="datetime-local" :aria-label="text('from')" /></label>
        <label>{{ text('to') }} <input v-model="to" type="datetime-local" :aria-label="text('to')" /></label>
        <ElButton :disabled="busy || !head" data-testid="knowledge-publication-request" @click="act(request)">{{ text('request') }}</ElButton>
      </fieldset>
      <div class="knowledge-publication__toolbar">
        <ElInput v-model="proposalId" :aria-label="text('proposalId')" :placeholder="text('proposalId')" :disabled="busy" />
        <ElButton :disabled="busy || !isGuid(proposalId)" @click="act(refresh)">{{ text('readProposal') }}</ElButton>
      </div>
      <ElDescriptions v-if="proposal" :column="1" :label-width="96" border>
        <ElDescriptionsItem :label="text('state')">{{ state ? text(state) : '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('candidate')">{{ proposal.versionId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('hash')"><code>{{ proposal.snapshotHash }}</code></ElDescriptionsItem>
        <ElDescriptionsItem :label="text('policy')">{{ proposal.policyVersion }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('requester')">{{ proposal.actorUserId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('reviewer')">{{ proposal.reviewerUserId ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('from')">{{ new Date(proposal.effectiveFromUtc).toLocaleString(locale) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('to')">{{ new Date(proposal.effectiveToUtc).toLocaleString(locale) }}</ElDescriptionsItem>
      </ElDescriptions>
      <template v-if="proposal">
        <ElButton :disabled="busy" @click="preview = true">{{ text('preview') }}</ElButton>
        <ElCheckbox v-if="['pending', 'approved'].includes(proposal.stateKey)" v-model="reviewed" :disabled="busy || unknown">{{ text('confirmReview') }}</ElCheckbox>
        <div class="knowledge-publication__toolbar">
          <template v-if="proposal.stateKey === 'pending' && proposal.actorUserId !== session.currentUser?.id && can('review')">
            <ElButton type="success" class="knowledge-publication__approve" :disabled="busy || unknown || !reviewed" data-testid="knowledge-publication-approve" @click="act((current, signal) => decide(true, current, signal))">{{ text('approve') }}</ElButton>
            <ElButton type="danger" class="knowledge-publication__reject" :disabled="busy || unknown || !reviewed" @click="act((current, signal) => decide(false, current, signal))">{{ text('reject') }}</ElButton>
          </template>
          <ElButton v-if="proposal.stateKey === 'approved' && proposal.actorUserId === session.currentUser?.id && can('publish')" type="primary" :disabled="busy || unknown || !reviewed" data-testid="knowledge-publication-publish" @click="act(publish)">{{ text('publish') }}</ElButton>
        </div>
      </template>
    </section>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-if="proposal && document" v-model:open="preview" :knowledge-base-id="knowledgeBaseId" :document-id="document.id" :version-id="proposal.versionId" />
</template>
<style scoped>
.knowledge-publication { min-width: 0; overflow-wrap: anywhere; }
.knowledge-publication__toolbar { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
.knowledge-publication__toolbar .el-input { flex: 1 1 260px; }
fieldset { display: grid; gap: 12px; min-width: 0; margin: 16px 0; padding: 16px; }
label { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
.el-checkbox { white-space: normal; height: auto; margin: 16px 0; }
.knowledge-publication__approve { --el-button-bg-color: #166534; --el-button-border-color: #166534; --el-button-hover-bg-color: #14532d; --el-button-hover-border-color: #14532d; --el-button-active-bg-color: #14532d; --el-button-active-border-color: #14532d; --el-button-text-color: #fff; --el-button-hover-text-color: #fff; }
.knowledge-publication__reject { --el-button-bg-color: #b91c1c; --el-button-border-color: #b91c1c; --el-button-hover-bg-color: #991b1b; --el-button-hover-border-color: #991b1b; --el-button-active-bg-color: #991b1b; --el-button-active-border-color: #991b1b; --el-button-text-color: #fff; --el-button-hover-text-color: #fff; }
</style>
