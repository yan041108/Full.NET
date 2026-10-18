<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInputNumber, ElMessageBox, ElTable, ElTableColumn } from 'element-plus';
import type { AiKnowledgeBaseResponse, AiKnowledgeIndexGrantResponse, CreateAiKnowledgeIndexGrantRequest } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { indexGrantText } from '../i18n/index-grants';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { getKnowledgeBase } from '../api/ai-knowledge';
import * as api from '../api/ai-index-grants';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';

const props = defineProps<{ knowledgeBaseId: string }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n();
const text = (key: Parameters<typeof indexGrantText>[1]) => indexGrantText(locale.value, key);
const canRead = computed(() => !session.switching && session.can('ai.knowledge_bases.read') && session.can('ai.knowledge_documents.read') && session.can('ai.knowledge_documents.index_grant_read'));
const canCreate = computed(() => canRead.value && ['ai.knowledge_documents.index_grant_create', 'ai.knowledge_documents.index_enqueue', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.upload'].every(code => session.can(code)));
const canRevoke = computed(() => canRead.value && session.can('ai.knowledge_documents.index_grant_revoke'));
const open = ref(false); const busy = ref(false); const stale = ref(false); const rows = ref<AiKnowledgeIndexGrantResponse[]>([]); const knowledge = ref<AiKnowledgeBaseResponse>();
const minutes = ref(60); const documents = ref(4); const tokens = ref(100000); const cursors = ref<(string | undefined)[]>([undefined]); const page = ref(0);
let epoch = 0; let controller: AbortController | undefined; let confirming = false; let draft: CreateAiKnowledgeIndexGrantRequest | undefined;
function reset() {
  ++epoch; controller?.abort(); controller = undefined; if (confirming) ElMessageBox.close(); confirming = false;
  open.value = false; busy.value = false; stale.value = false; rows.value = []; knowledge.value = undefined;
  cursors.value = [undefined]; page.value = 0; draft = undefined;
}
watch(() => [props.knowledgeBaseId, session.currentUser, session.currentContextName, session.switching, canRead.value, canCreate.value, canRevoke.value], reset, { deep: true, flush: 'sync' });
watch([minutes, documents, tokens], () => { draft = undefined; }, { flush: 'sync' });
onUnmounted(reset);
const current = (run: number, signal: AbortSignal) => run === epoch && !signal.aborted && open.value && canRead.value;
async function perform(action: (signal: AbortSignal, run: number) => Promise<void>, write = false) {
  if (!open.value || !canRead.value || busy.value) return;
  const run = ++epoch; const abort = new AbortController(); controller = abort; busy.value = true;
  try { await action(abort.signal, run); }
  catch (error) { if (current(run, abort.signal) && error !== 'cancel' && error !== 'close') { if (write) stale.value = true; showProblem(error, t('common.requestFailed')); } }
  finally { if (run === epoch) { busy.value = false; controller = undefined; } }
}
async function load(signal: AbortSignal, run: number, target = page.value) {
  const afterId = target > page.value ? rows.value.at(-1)?.id : cursors.value[target];
  if (target < 0 || (target > 0 && !afterId)) return;
  const [base, result] = await Promise.all([getKnowledgeBase({ knowledgeBaseId: props.knowledgeBaseId }, signal),
    api.listIndexGrants({ knowledgeBaseId: props.knowledgeBaseId, ...(afterId ? { afterId } : {}) }, signal)]);
  if (!current(run, signal)) return;
  knowledge.value = base; rows.value = result; page.value = target; cursors.value[target] = afterId; stale.value = false;
}
function show() { if (!canRead.value) return; open.value = true; void perform((signal, run) => load(signal, run, 0)); }
async function create() {
  if (!canCreate.value || stale.value || !knowledge.value?.isEnabled || !Number.isInteger(minutes.value) || minutes.value < 2 || minutes.value > 1440
    || !Number.isInteger(documents.value) || documents.value < 1 || documents.value > 64 || !Number.isInteger(tokens.value) || tokens.value < 1 || tokens.value > 1000000) return;
  const base = knowledge.value;
  draft ??= { clientRequestId: crypto.randomUUID(), knowledgeBaseVersion: base.version, expiresAtUtc: new Date(Date.now() + minutes.value * 60000).toISOString(), documentLimit: documents.value, inputTokenLimitPerDocument: tokens.value, confirmPaidDispatch: true };
  const request = draft;
  await perform(async (signal, run) => {
    confirming = true;
    try { await ElMessageBox.confirm(`${text('confirm')} ${base.name} · r${base.version} · ${request.expiresAtUtc} · ${request.documentLimit} × ${request.inputTokenLimitPerDocument}`, text('title'), { type: 'warning', dangerouslyUseHTMLString: false }); }
    finally { if (run === epoch) confirming = false; }
    if (!current(run, signal) || !canCreate.value) return;
    await api.createIndexGrant({ knowledgeBaseId: props.knowledgeBaseId, body: request }, signal);
    if (!current(run, signal) || !canCreate.value) return;
    draft = undefined; showSuccess(text('saved')); await load(signal, run, 0);
  }, true);
}
async function revoke(value: unknown) {
  const row = rows.value.find(item => item === value);
  if (!row || !canRevoke.value || stale.value || row.isRevoked) return;
  await perform(async (signal, run) => {
    confirming = true;
    try { await ElMessageBox.confirm(`${text('revokeConfirm')} ${row.expiresAtUtc} · r${row.version}`, text('title'), { type: 'warning', dangerouslyUseHTMLString: false }); }
    finally { if (run === epoch) confirming = false; }
    if (!current(run, signal) || !canRevoke.value) return;
    await api.revokeIndexGrant({ knowledgeBaseId: props.knowledgeBaseId, grantId: row.id, body: { version: row.version } }, signal);
    if (!current(run, signal) || !canRevoke.value) return;
    showSuccess(text('stopped')); await load(signal, run);
  }, true);
}
</script>
<template>
  <ElButton v-if="canRead" data-testid="index-grants-open" @click="show">{{ text('open') }}</ElButton>
  <ElDialog v-model="open" :title="text('title')" width="min(960px, 96vw)" @closed="reset">
    <p>{{ text('hint') }}</p><p v-if="stale" role="alert">{{ text('stale') }}</p>
    <ElButton v-if="canRead" :disabled="busy" data-testid="index-grants-refresh" @click="perform((signal, run) => load(signal, run))">{{ text('refresh') }}</ElButton>
    <ElForm v-if="canCreate" inline label-position="top" :aria-busy="busy">
      <ElFormItem :label="text('minutes')" required><ElInputNumber v-model="minutes" :min="2" :max="1440" :precision="0" :disabled="busy" /></ElFormItem>
      <ElFormItem :label="text('documents')" required><ElInputNumber v-model="documents" :min="1" :max="64" :precision="0" :disabled="busy" /></ElFormItem>
      <ElFormItem :label="text('tokens')" required><ElInputNumber v-model="tokens" :min="1" :max="1000000" :precision="0" :disabled="busy" /></ElFormItem>
      <ElButton :disabled="busy || stale || !knowledge?.isEnabled" data-testid="index-grants-create" @click="create">{{ text('create') }}</ElButton>
    </ElForm>
    <ElTable :data="rows" row-key="id" max-height="360" :aria-busy="busy">
      <ElTableColumn prop="expiresAtUtc" :label="text('expires')" min-width="220" />
      <ElTableColumn :label="text('admitted')" min-width="120"><template #default="{ row }">{{ row.admittedDocuments }} / {{ row.documentLimit }}</template></ElTableColumn>
      <ElTableColumn prop="inputTokenLimitPerDocument" :label="text('tokens')" min-width="140" />
      <ElTableColumn :label="text('state')" min-width="130"><template #default="{ row }">{{ text(row.isRevoked ? 'revoked' : 'pending') }}</template></ElTableColumn>
      <ElTableColumn v-if="canRevoke" :label="text('revoke')" width="140"><template #default="{ row }"><ArtTableActionGroup><ElButton v-if="!row.isRevoked" :disabled="busy || stale" data-testid="index-grants-revoke" @click="revoke(row)">{{ text('revoke') }}</ElButton></ArtTableActionGroup></template></ElTableColumn>
    </ElTable>
    <ElButton :disabled="busy || page === 0" @click="perform((signal, run) => load(signal, run, page - 1))">{{ text('previous') }}</ElButton>
    <ElButton :disabled="busy || rows.length < 32" @click="perform((signal, run) => load(signal, run, page + 1))">{{ text('next') }}</ElButton>
    <template #footer><ElButton @click="reset">{{ text('close') }}</ElButton></template>
  </ElDialog>
</template>
