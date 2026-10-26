<script setup lang="ts">
import type { DelegateWorkflowTodoRequest, RecallWorkflowTodoDelegationRequest, WorkflowTodoDelegation,
  WorkflowTodoDetail, WorkflowRecipientCandidateResponse } from '@fullnet/client-contracts';
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { WorkflowTodoClient } from './workflow-todo-client';
import { classifyWorkflowTodoActionFailure } from './workflow-todo-action-failure';
import { createIdempotencyKey } from './idempotency-key';

const props = defineProps<{ detail: WorkflowTodoDetail; client: WorkflowTodoClient; mayDelegate: boolean; mayRecall: boolean;
  comment: string; signatureFileId?: string; unsaved: boolean; disabled: boolean }>();
const emit = defineEmits<{ busy: [value: boolean]; completed: [kind: 'delegate' | 'recall']; refresh: [] }>();
const { t } = useI18n();
const grant = ref<WorkflowTodoDelegation | null>(null);
const candidates = ref<readonly WorkflowRecipientCandidateResponse[]>([]);
const page = ref(1); const total = ref(0); const candidateLoading = ref(false); const candidateFailed = ref(false);
const selected = ref(''); const selectedLabel = ref(''); const working = ref(false); const feedback = ref('');
const recallComment = ref('');
let epoch = 0;
let pending: { semantic: string; request: DelegateWorkflowTodoRequest | RecallWorkflowTodoDelegationRequest } | undefined;
const canDelegate = computed(() => props.mayDelegate && props.detail.statusKey === 'active' && props.detail.allowedActions.includes('delegate'));
const canRecall = computed(() => props.mayRecall && grant.value?.canRecall === true && grant.value.originTodoId === props.detail.id);
const options = computed(() => {
  const current = candidates.value.filter(item => item.id !== props.detail.assigneeUserId)
    .map(item => ({ id: item.id, label: item.displayName || item.username }));
  return selected.value && !current.some(item => item.id === selected.value)
    ? [{ id: selected.value, label: selectedLabel.value }, ...current] : current;
});
const names = computed(() => [t('workflow.todo.delegateTarget'), ...options.value.map(item => item.label)]);
const selectedIndex = computed(() => Math.max(0, options.value.findIndex(item => item.id === selected.value) + 1));
const ready = computed(() => !working.value && !props.disabled && !props.unsaved && !!selected.value &&
  (!props.detail.commentRequired || !!props.comment.trim()) && (!props.detail.signRequired || !!props.signatureFileId));

/** 迟到候选只属于原待办；身份切换由父页卸载组件并清空本次授权、签名和幂等状态。 */
async function loadCandidates(nextPage: number): Promise<void> {
  if (!canDelegate.value || working.value || props.disabled || candidateLoading.value) return;
  const current = epoch; candidateLoading.value = true; candidateFailed.value = false;
  try {
    const result = await props.client.listDelegationCandidates(props.detail.id, nextPage);
    if (current !== epoch) return;
    candidates.value = result.items; page.value = result.page; total.value = result.total;
  } catch { if (current === epoch) { candidateFailed.value = true; candidates.value = []; } }
  finally { if (current === epoch) candidateLoading.value = false; }
}
function choose(event: { detail: { value: string | number } }): void {
  const index = Number(event.detail.value);
  if (!Number.isInteger(index) || working.value || props.disabled) return;
  if (index === 0) { selected.value = ''; selectedLabel.value = ''; return; }
  const item = options.value[index - 1];
  if (!item) return;
  selected.value = item.id; selectedLabel.value = item.label;
}
async function loadContext(): Promise<void> {
  const current = ++epoch; grant.value = null; candidates.value = []; selected.value = ''; selectedLabel.value = '';
  page.value = 1; total.value = 0; feedback.value = ''; pending = undefined;
  candidateLoading.value = false; candidateFailed.value = false; recallComment.value = '';
  try {
    const result = await props.client.delegation(props.detail.id);
    if (current !== epoch) return;
    grant.value = result;
    if (canDelegate.value) await loadCandidates(1);
  } catch { if (current === epoch) feedback.value = t('workflow.todo.failed'); }
}

/** 写入不携带字段补丁；相同语义的网络重试保留幂等键，冲突后必须重新读取真实授权修订。 */
async function execute(kind: 'delegate' | 'recall'): Promise<void> {
  if (working.value || props.disabled || (kind === 'delegate' ? !canDelegate.value || !ready.value : !canRecall.value)) return;
  const reason = kind === 'delegate' ? props.comment.trim() : recallComment.value.trim();
  if (props.detail.commentRequired && !reason) { feedback.value = t('workflow.todo.evidenceRequired'); return; }
  const current = epoch;
  try {
    const body = kind === 'delegate' ? { assigneeUserId: selected.value, expectedRevision: props.detail.revision,
      expectedFormRevision: props.detail.submissionRevision, comment: props.comment.trim() || null, signatureFileId: props.signatureFileId }
      : { expectedRevision: grant.value!.proxyRevision, expectedFormRevision: grant.value!.formRevision, comment: reason || null };
    const semantic = JSON.stringify([kind, props.detail.id, body]);
    if (pending?.semantic !== semantic) pending = { semantic, request: { ...body, idempotencyKey: createIdempotencyKey() } };
    working.value = true; emit('busy', true); feedback.value = '';
    if (kind === 'delegate') await props.client.delegate(props.detail.id, pending.request as DelegateWorkflowTodoRequest);
    else await props.client.recallDelegation(props.detail.id, pending.request as RecallWorkflowTodoDelegationRequest);
    if (current !== epoch) return;
    pending = undefined; emit('completed', kind);
  } catch (error) {
    if (current !== epoch) return;
    const failure = classifyWorkflowTodoActionFailure(error);
    if (!failure.retainIdempotencyKey) pending = undefined;
    feedback.value = t(failure.feedbackKey);
    if (failure.refreshTodo) emit('refresh');
  } finally { if (current === epoch) { working.value = false; emit('busy', false); } }
}
watch(() => [props.detail.id, props.detail.revision, props.mayDelegate, props.mayRecall], () => void loadContext(), { immediate: true });
onBeforeUnmount(() => { epoch++; pending = undefined; });
</script>

<template>
  <view class="delegation-panel">
    <view v-if="canDelegate" class="delegation-picker">
      <text>{{ t('workflow.todo.delegateHelp') }}</text>
      <picker :range="names" :value="selectedIndex" :disabled="disabled || working || candidateLoading || options.length === 0"
        data-testid="workflow-delegation-target" @change="choose">
        <view>{{ selectedLabel || t('workflow.todo.delegateTarget') }}</view>
      </picker>
      <view class="delegation-pages">
        <button :disabled="page <= 1 || candidateLoading || disabled || working" @click="loadCandidates(page - 1)">{{ t('workflow.todo.previousPage') }}</button>
        <text>{{ page }} / {{ Math.max(1, Math.ceil(total / 20)) }}</text>
        <button :disabled="page * 20 >= total || candidateLoading || disabled || working" @click="loadCandidates(page + 1)">{{ t('workflow.todo.nextPage') }}</button>
      </view>
      <text v-if="unsaved" role="alert">{{ t('workflow.todo.handoffUnsaved') }}</text>
      <button v-if="candidateFailed" :disabled="disabled || working" @click="loadCandidates(page)">{{ t('workflow.todo.reloadCandidates') }}</button>
      <button :disabled="!ready" data-testid="workflow-delegation-submit" @click="execute('delegate')">{{ t('workflow.todo.delegate') }}</button>
    </view>
    <view v-if="grant" data-testid="workflow-delegation-state">
      <text>{{ t(`workflow.todo.delegationStatus.${grant.statusKey}`) }}</text>
      <text>{{ grant.originalAssigneeUserId }} → {{ grant.proxyUserId }}</text>
      <view v-if="canRecall">
        <text>{{ t('workflow.todo.recallHelp') }}</text>
        <uni-easyinput v-model="recallComment" type="textarea" :maxlength="512" :placeholder="t('workflow.todo.recallReason')" :disabled="disabled || working" />
        <button :disabled="disabled || working || detail.commentRequired && !recallComment.trim()"
          data-testid="workflow-delegation-recall" @click="execute('recall')">{{ t('workflow.todo.recallDelegation') }}</button>
      </view>
    </view>
    <text v-if="feedback" role="alert">{{ feedback }}</text>
  </view>
</template>

<style scoped>
.delegation-panel, .delegation-picker { display: flex; flex-direction: column; gap: 20rpx; }
.delegation-pages { display: flex; align-items: center; justify-content: space-between; gap: 12rpx; }
</style>
