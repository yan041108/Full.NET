<script setup lang="ts">
import { computed, onUnmounted, ref, shallowRef, watch } from 'vue';
import { ElAlert, ElButton, ElCheckbox } from 'element-plus';
import type { AiKnowledgeIndexStatusResponse, AiKnowledgeIndexEnqueueResponse, AiKnowledgeIndexReconciliationResponse, AiKnowledgeIndexRebuildRequest } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
const props = defineProps<{ open: boolean; knowledgeBaseId: string; documentId: string; versionId: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const status = ref<AiKnowledgeIndexStatusResponse>(); const accepted = ref<AiKnowledgeIndexEnqueueResponse>();
const reconciliation = ref<AiKnowledgeIndexReconciliationResponse>(); const reconciliationMode = ref(false);
// 只允许当前已交付的原因/动作组合；未知建议不能退回旧状态而误触发收费请求。
const recoveryActions = { ready: 'none', deleted: 'none', not_indexed: 'enqueue', binding_stale: 'enqueue', queued: 'wait', processing: 'wait', lease_expired: 'wait', cleanup_pending: 'wait', task_failed: 'retry', lease_exhausted: 'retry', task_cancelled: 'rebuild', generation_unavailable: 'rebuild', source_unavailable: 'none', format_unsupported: 'none' } as const;
type RecoveryReason = keyof typeof recoveryActions;
const recoveryReason = computed(() => reconciliation.value?.reason as RecoveryReason | undefined);
const recoveryAction = computed(() => reconciliation.value?.recoveryAction as typeof recoveryActions[RecoveryReason] | undefined);
const loading = ref(false); const busy = ref(false);
const canRead = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.index_read'].every(code => session.can(code)));
const canEnqueue = computed(() => canRead.value && ['ai.knowledge_documents.source_read', 'ai.knowledge_documents.upload', 'ai.knowledge_documents.index_enqueue'].every(code => session.can(code)));
const canRebuild = computed(() => canEnqueue.value && session.can('ai.knowledge_documents.index_rebuild'));
const rebuildConfirmed = ref(false);
const pendingRebuild = shallowRef<{ binding: ReturnType<typeof parameters>; body: AiKnowledgeIndexRebuildRequest }>();
const rebuildable = computed(() => !!reconciliation.value?.taskId && recoveryAction.value === 'rebuild');
const canRetry = computed(() => canEnqueue.value && session.can('ai.knowledge_documents.index_retry'));
const retryable = computed(() => reconciliation.value ? reconciliation.value.taskId && recoveryAction.value === 'retry' : status.value?.taskId && ['failed', 'processing'].includes(status.value.state));
const enqueueable = computed(() => reconciliation.value ? recoveryAction.value === 'enqueue' : status.value?.state === 'pending_processing');
let epoch = 0; let readEpoch = 0; let controller: AbortController | undefined;
const parameters = () => ({ knowledgeBaseId: props.knowledgeBaseId, documentId: props.documentId, versionId: props.versionId });
function reset() { ++epoch; ++readEpoch; controller?.abort(); status.value = undefined; reconciliation.value = undefined; reconciliationMode.value = false; accepted.value = undefined; loading.value = false; busy.value = false; pendingRebuild.value = undefined; rebuildConfirmed.value = false; }
async function read(reconcile = reconciliationMode.value) {
  if (!props.open || !canRead.value || busy.value) return;
  controller?.abort(); const scope = epoch; const current = ++readEpoch; const binding = parameters();
  status.value = undefined; reconciliation.value = undefined; accepted.value = undefined; reconciliationMode.value = reconcile; loading.value = true; controller = new AbortController();
  try {
    const result = reconcile ? await api.getKnowledgeIndexReconciliation(binding, controller.signal) : await api.getKnowledgeIndexStatus(binding, controller.signal);
    if (scope !== epoch || current !== readEpoch) return;
    if (result.versionId.toLowerCase() !== binding.versionId.toLowerCase()) throw new Error('client.invalid_knowledge_resource');
    if ('recoveryAction' in result) {
      if (!Object.hasOwn(recoveryActions, result.reason) || recoveryActions[result.reason as RecoveryReason] !== result.recoveryAction
        || !Number.isInteger(result.documentVersion) || result.documentVersion < 1 || !Number.isInteger(result.knowledgeBaseVersion) || result.knowledgeBaseVersion < 1 || (['retry', 'rebuild'].includes(result.recoveryAction) && !result.taskId)) throw new Error('client.invalid_knowledge_resource');
      const pending = pendingRebuild.value;
      if (pending && (result.taskId !== pending.body.expectedTaskId || result.documentVersion !== pending.body.expectedDocumentVersion
        || result.knowledgeBaseVersion !== pending.body.expectedKnowledgeBaseVersion)) {
        pendingRebuild.value = undefined; rebuildConfirmed.value = false;
      }
      reconciliation.value = result;
    } else { status.value = result; }
  } catch (error) { if (scope === epoch && current === readEpoch) showProblem(error, t('common.loadFailed')); }
  finally { if (scope === epoch && current === readEpoch) loading.value = false; }
}
async function write(retry = false) {
  if (!props.open || loading.value || busy.value || (!status.value && !reconciliation.value) || !canEnqueue.value
    || (retry ? !canRetry.value || !retryable.value : !enqueueable.value)) return;
  const binding = parameters(); const taskId = reconciliation.value?.taskId ?? status.value?.taskId; const scope = epoch;
  // 不明结果和冲突都先清除旧状态；用户必须重新读取权威状态，不能自动重发旧任务。
  status.value = undefined; reconciliation.value = undefined; accepted.value = undefined; ++readEpoch; controller?.abort(); busy.value = true;
  try {
    const result = retry && taskId ? await api.retryKnowledgeIndex({ ...binding, taskId }) : await api.enqueueKnowledgeIndex(binding);
    if (scope !== epoch) return;
    if (result.versionId.toLowerCase() !== binding.versionId.toLowerCase()) throw new Error('client.invalid_knowledge_resource');
    accepted.value = result; showSuccess(t('knowledge.indexAccepted'));
  } catch (error) { if (scope === epoch) showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
async function rebuild() {
  if (!props.open || !canRebuild.value || busy.value || loading.value || !rebuildConfirmed.value || (!pendingRebuild.value && !rebuildable.value)) return;
  const current = reconciliation.value; const scope = epoch;
  const pending = pendingRebuild.value ?? (current?.taskId ? {
    binding: parameters(), body: { clientRequestId: crypto.randomUUID(), expectedTaskId: current.taskId,
      expectedDocumentVersion: current.documentVersion, expectedKnowledgeBaseVersion: current.knowledgeBaseVersion, confirmAdditionalCost: true },
  } : undefined);
  if (!pending) return;
  // 结果不明时保留同一请求键；只有资源/身份变化、权威快照改变或成功确认才清除。
  pendingRebuild.value = pending; status.value = undefined; reconciliation.value = undefined; accepted.value = undefined;
  ++readEpoch; controller?.abort(); busy.value = true;
  try {
    const result = await api.rebuildKnowledgeIndex({ ...pending.binding, body: pending.body });
    if (scope !== epoch) return;
    if (result.versionId.toLowerCase() !== pending.binding.versionId.toLowerCase() || !result.taskId) throw new Error('client.invalid_knowledge_resource');
    accepted.value = result; pendingRebuild.value = undefined; rebuildConfirmed.value = false; showSuccess(t('knowledge.indexAccepted'));
  } catch (error) { if (scope === epoch) showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
watch(() => [props.open, props.knowledgeBaseId, props.documentId, props.versionId, session.currentUser, session.switching], () => { reset(); void read(); }, { deep: true, immediate: true });
onUnmounted(reset);
</script>
<template>
  <ArtFormDialog :open="open" :title="t('knowledge.index')" :show-confirm="false" :cancel-label="t('common.cancel')" @update:open="emit('update:open', $event)">
    <el-alert v-if="!canRead" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <template v-else>
      <p>{{ t('knowledge.indexHint') }}</p><p>{{ t('knowledge.version') }}: {{ versionId }}</p>
      <p>{{ t('knowledge.indexReconcileHint') }}</p>
      <div v-if="status" data-testid="knowledge-index-state"><p>{{ t('knowledge.state') }}: {{ status.state }}</p><p v-if="status.taskId">{{ t('knowledge.indexTask') }}: {{ status.taskId }}</p><p v-if="status.failureCode">{{ t('knowledge.failure') }}: {{ status.failureCode }}</p></div>
      <div v-if="reconciliation && recoveryReason && recoveryAction" data-testid="knowledge-index-reconciliation" aria-live="polite">
        <p>{{ t(`knowledge.indexReason.${recoveryReason}`) }}</p><p>{{ t(`knowledge.indexRecovery.${recoveryAction}`) }}</p>
        <p v-if="reconciliation.taskId">{{ t('knowledge.indexTask') }}: {{ reconciliation.taskId }}</p>
        <p>{{ t('knowledge.indexSnapshot', { documentVersion: reconciliation.documentVersion, knowledgeBaseVersion: reconciliation.knowledgeBaseVersion }) }}</p>
      </div>
      <p v-if="accepted" data-testid="knowledge-index-accepted">{{ t('knowledge.indexAccepted') }} · {{ accepted.taskId }}</p>
      <el-checkbox v-if="canRebuild && (rebuildable || pendingRebuild)" v-model="rebuildConfirmed" :disabled="busy || loading" data-testid="knowledge-index-rebuild-confirm">{{ t('knowledge.indexRebuildConfirm') }}</el-checkbox>
      <p v-if="pendingRebuild" data-testid="knowledge-index-rebuild-pending">{{ t('knowledge.indexRebuildPending') }}</p>
      <div class="index-actions">
        <el-button :loading="loading" :disabled="busy || loading" data-testid="knowledge-index-refresh" @click="read()">{{ t('common.refresh') }}</el-button>
        <el-button :disabled="busy || loading" data-testid="knowledge-index-reconcile" @click="read(true)">{{ t('knowledge.indexReconcile') }}</el-button>
        <el-button v-if="canEnqueue && enqueueable" :disabled="busy || loading" data-testid="knowledge-index-enqueue" @click="write()">{{ t('knowledge.indexEnqueue') }}</el-button>
        <el-button v-if="canRebuild && (rebuildable || pendingRebuild)" :disabled="busy || loading || !rebuildConfirmed" data-testid="knowledge-index-rebuild" @click="rebuild()">{{ pendingRebuild ? t('knowledge.indexRebuildResolve') : t('knowledge.indexRebuild') }}</el-button>
        <el-button v-if="canRetry && retryable" :disabled="busy || loading" data-testid="knowledge-index-retry" @click="write(true)">{{ t('knowledge.indexRetry') }}</el-button>
      </div>
    </template>
  </ArtFormDialog>
</template>
<style scoped>.index-actions { display: flex; flex-wrap: wrap; gap: 8px; } p { overflow-wrap: anywhere; }</style>
