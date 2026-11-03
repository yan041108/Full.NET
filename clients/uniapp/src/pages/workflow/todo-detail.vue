<script setup lang="ts">
import type { ActWorkflowTodoRequest, WorkflowSubmission, WorkflowTodoDetail, WorkflowVoteRequest, WorkflowReleaseRequest, WorkflowFieldPolicy } from '@fullnet/client-contracts';
import { onLoad } from '@dcloudio/uni-app';
import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import FullNetFormRenderer from '../../features/workflow/FullNetFormRenderer.vue';
import {
  httpClient,
  identitySession,
  isBusinessRuntimeAvailable,
  restoreIdentitySession
} from '../../features/identity/application-session';
import { createIdempotencyKey } from '../../features/workflow/idempotency-key';
import { classifyWorkflowTodoActionFailure } from '../../features/workflow/workflow-todo-action-failure';
import { createWorkflowTodoClient } from '../../features/workflow/workflow-todo-client';
import { createUniWorkflowSchemaCache } from '../../features/workflow/workflow-schema-cache';
import WorkflowDelegationPanel from '../../features/workflow/WorkflowDelegationPanel.vue';
import WorkflowHumanManagementPanel from '../../features/workflow/WorkflowHumanManagementPanel.vue';
import { HttpProblem } from '../../api/problem-details';
import { chooseWorkflowAttachment, deliverWorkflowAttachment } from '../../features/workflow/workflow-file-platform';
import { workflowResourceLoginUrl } from '../../features/workflow/workflow-resource-links';
// #ifdef H5
import { useWorkflowControlAccessibility } from '../../features/workflow/workflow-control-accessibility';
import { useWorkflowResourceRoute } from '../../features/workflow/workflow-resource-route-h5';
// #endif

const todoClient = createWorkflowTodoClient(httpClient, createUniWorkflowSchemaCache());
const pageRoot = ref<unknown>();
// #ifdef H5
useWorkflowControlAccessibility(pageRoot);
// #endif
const { t } = useI18n();
const todoId = ref('');
const detail = ref<WorkflowTodoDetail>();
const detailReadVersion = ref(0);
const patch = ref<WorkflowSubmission>({});
const ballotPatch = ref<WorkflowSubmission>({});
const ballotChoice = ref<'yes' | 'no' | 'abstain'>();
const ballotRenderer = ref<InstanceType<typeof FullNetFormRenderer>>();
let pendingVote: { semantic: string; request: WorkflowVoteRequest } | undefined;
let pendingRelease: { semantic: string; request: WorkflowReleaseRequest } | undefined;
const canVote = computed(() => detail.value?.statusKey === 'active' && detail.value.activityKindKey === 'human.vote' &&
  detail.value.allowedActions.includes('vote') && detail.value.voteForm?.readOnly === false && identitySession.can('workflow.todos.vote'));
const votePolicies = computed<Readonly<Record<string, WorkflowFieldPolicy>>>(() => canVote.value ? detail.value?.voteForm?.fieldPolicies ?? {} :
  Object.fromEntries((detail.value?.voteForm?.formSchema.sections.flatMap(section => section.fields) ?? [])
    .map(field => [field.fieldKey, detail.value?.voteForm?.fieldPolicies[field.fieldKey] === 'hidden' ? 'hidden' : 'readOnly'])));
const comment = ref('');
const signatureFileId = ref<string>();
const signaturePreview = ref('');
const signatureUploading = ref(false);
const attachmentUploading = ref(false);
const loading = ref(true);
const submitting = ref(false);
const feedback = ref('');
const renderer = ref<{ validate(): Readonly<Record<string, 'required'>> }>();
const canApprove = computed(() =>
  detail.value?.statusKey === 'active'
  && detail.value.allowedActions.includes('approve')
  && identitySession.can('workflow.todos.approve'));
const canReject = computed(() =>
  detail.value?.statusKey === 'active'
  && detail.value.allowedActions.includes('reject')
  && identitySession.can('workflow.todos.reject'));
const canComplete = computed(() =>
  detail.value?.statusKey === 'active'
  && ['human.task', 'human.claim'].includes(detail.value.activityKindKey ?? '')
  && detail.value.allowedActions.includes('complete')
  && identitySession.can('workflow.todos.complete'));
const canRelease = computed(() => detail.value?.statusKey === 'active' && detail.value.activityKindKey === 'human.claim' &&
  detail.value.claimProgress?.allowRelease === true && detail.value.allowedActions.includes('release') && identitySession.can('workflow.todos.release'));
const handoffUnsaved = computed(() => !!detail.value && (Object.entries(patch.value)
  .some(([key, value]) => JSON.stringify(value) !== JSON.stringify(detail.value!.submission[key])) ||
  detail.value.voteForm != null && (ballotChoice.value !== (detail.value.voteForm.choiceKey ?? undefined) || Object.entries(ballotPatch.value)
    .some(([key, value]) => JSON.stringify(value) !== JSON.stringify(detail.value!.voteForm!.values[key])))));
const visiblePolicies = computed<Readonly<Record<string, WorkflowFieldPolicy>>>(() => detail.value?.statusKey === 'active' ? detail.value.fieldPolicies
  : Object.fromEntries((detail.value?.formSchema.sections.flatMap(section => section.fields) ?? [])
    .map(field => [field.fieldKey, detail.value?.fieldPolicies[field.fieldKey] === 'hidden' ? 'hidden' : 'readOnly'])));
const attachmentCandidates = computed(() => {
  const current = detail.value;
  if (!current) return [];
  const values = { ...current.submission, ...patch.value };
  const ids = current.formSchema.sections.flatMap(section => section.fields)
    .filter(field => field.fieldTypeKey === 'attachment' && visiblePolicies.value[field.fieldKey] !== 'hidden')
    .flatMap(field => Array.isArray(values[field.fieldKey]) ? values[field.fieldKey] as string[] : []);
  return [...new Set(ids)].map(fileId => ({ fileId, name: current.attachmentNames?.[fileId] ?? t('workflow.form.attachment') }));
});
let pendingAction: {
  action: 'approve' | 'reject' | 'complete';
  semantic: string;
  request: ActWorkflowTodoRequest & { signatureFileId?: string };
} | undefined;
let identityEpoch = 0;
const unsubscribeIdentity = identitySession.subscribe(() => {
  clearTodoContext();
});
function clearTodoContext(): void {
  identityEpoch++; detail.value = undefined; patch.value = {}; ballotPatch.value = {}; ballotChoice.value = undefined;
  pendingAction = undefined; pendingVote = undefined; pendingRelease = undefined; signatureFileId.value = undefined;
  signaturePreview.value = ''; comment.value = '';
  submitting.value = false; signatureUploading.value = false;
  attachmentUploading.value = false;
}
onBeforeUnmount(() => { identityEpoch++; unsubscribeIdentity(); });

onLoad(query => {
  todoId.value = typeof query?.id === 'string' ? query.id : '';
  void load();
});
// #ifdef H5
useWorkflowResourceRoute('workflow_todo', todoId, () => { clearTodoContext(); void load(); });
// #endif

async function load(): Promise<void> {
  loading.value = true;
  feedback.value = '';
  try {
    if (!isBusinessRuntimeAvailable) {
      feedback.value = t('identity.login.platformUnavailable');
      return;
    }
    if (identitySession.snapshot().state !== 'authenticated'
      && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: workflowResourceLoginUrl({ resourceTypeKey: 'workflow_todo', resourceId: todoId.value }) });
      return;
    }
    if (!identitySession.can('workflow.todos.read')) {
      throw new Error('permission-denied');
    }
    await refreshTodo();
  } catch (error) {
    feedback.value = t(error instanceof HttpProblem && [401, 403, 404].includes(error.status)
      ? 'workflow.resource.unavailable' : 'workflow.todo.failed');
  } finally {
    loading.value = false;
  }
}

async function refreshTodo(): Promise<void> {
  const current = identityEpoch;
  let result: WorkflowTodoDetail;
  try { result = await todoClient.get(todoId.value); }
  catch (error) {
    if (current === identityEpoch && classifyWorkflowTodoActionFailure(error).clearTodo) clearTodoContext();
    throw error;
  }
  if (current !== identityEpoch) throw new Error('workflow.identity_changed');
  // 历史原待办修订不会跟随代理变化，每次权威重读都重建面板以获取最新收回修订。
  detailReadVersion.value++;
  detail.value = result;
  patch.value = {};
  ballotPatch.value = {}; ballotChoice.value = detail.value.voteForm?.choiceKey ?? undefined;
  comment.value = '';
  signatureFileId.value = undefined;
  signaturePreview.value = '';
}

async function delegationCompleted(kind: 'delegate' | 'recall'): Promise<void> {
  feedback.value = t(kind === 'delegate' ? 'workflow.todo.delegated' : 'workflow.todo.delegationRecalled');
  await returnToTodos();
}
async function returnToTodos(): Promise<void> {
  // 深链可能没有页面栈；成功后销毁旧上下文并回到权威列表，避免回到空根路由。
  clearTodoContext(); await uni.reLaunch({ url: '/pages/workflow/todos' });
}
async function managementCompleted(): Promise<void> { await returnToTodos(); }

async function act(action: 'approve' | 'reject' | 'complete'): Promise<void> {
  if (!detail.value || submitting.value || signatureUploading.value || attachmentUploading.value ||
    !(action === 'approve' ? canApprove.value : action === 'complete' ? canComplete.value : canReject.value)) {
    return;
  }
  if (Object.keys(renderer.value?.validate() ?? {}).length > 0) {
    feedback.value = t('workflow.todo.validationFailed');
    return;
  }
  if ((detail.value.commentRequired && !comment.value.trim()) || (detail.value.signRequired && !signatureFileId.value)) {
    feedback.value = t('workflow.todo.evidenceRequired');
    return;
  }
  try {
    const semantic = JSON.stringify([action, detail.value.id, detail.value.revision, detail.value.submissionRevision,
      patch.value, comment.value.trim(), signatureFileId.value]);
    pendingAction = pendingAction?.semantic === semantic
      ? pendingAction
      : {
          action,
          semantic,
          request: {
            expectedRevision: detail.value.revision,
            expectedFormRevision: detail.value.submissionRevision,
            fieldPatch: patch.value,
            comment: comment.value.trim() || null,
            signatureFileId: signatureFileId.value,
            idempotencyKey: createIdempotencyKey()
          }
        };
  } catch {
    feedback.value = t('workflow.todo.idempotencyUnavailable');
    return;
  }
  submitting.value = true;
  feedback.value = '';
  const actionEpoch = identityEpoch;
  try {
    await todoClient[action](todoId.value, pendingAction.request);
    if (actionEpoch !== identityEpoch) return;
    pendingAction = undefined;
    feedback.value = t('workflow.todo.completed');
    await returnToTodos();
  } catch (error: unknown) {
    if (actionEpoch !== identityEpoch) return;
    const failure = classifyWorkflowTodoActionFailure(error);
    if (failure.clearTodo) clearTodoContext();
    if (!failure.retainIdempotencyKey) {
      pendingAction = undefined;
    }
    if (failure.refreshTodo) {
      try {
        await refreshTodo();
      } catch {
        feedback.value = t('workflow.todo.failed');
        return;
      }
    }
    feedback.value = t(failure.feedbackKey);
  } finally {
    if (actionEpoch === identityEpoch) submitting.value = false;
  }
}

/** 个人票据使用独立修订；重试只复用同一选择、答案与证据的幂等键。 */
async function castVote(): Promise<void> {
  const current = detail.value;
  if (!canVote.value || !current?.voteForm || !ballotChoice.value || submitting.value || signatureUploading.value || attachmentUploading.value) return;
  if (Object.keys(ballotRenderer.value?.validate() ?? {}).length > 0) { feedback.value = t('workflow.todo.validationFailed'); return; }
  if ((current.commentRequired && !comment.value.trim()) || (current.signRequired && !signatureFileId.value)) { feedback.value = t('workflow.todo.evidenceRequired'); return; }
  const values = { ...current.voteForm.values, ...ballotPatch.value };
  try {
    const semantic = JSON.stringify([current.id, current.revision, current.voteForm.revision, ballotChoice.value, values, comment.value.trim(), signatureFileId.value]);
    if (pendingVote?.semantic !== semantic) pendingVote = { semantic, request: { expectedRevision: current.revision,
      expectedBallotRevision: current.voteForm.revision, choiceKey: ballotChoice.value, values,
      comment: comment.value.trim() || null, signatureFileId: signatureFileId.value, idempotencyKey: createIdempotencyKey() } };
  } catch { feedback.value = t('workflow.todo.idempotencyUnavailable'); return; }
  submitting.value = true; feedback.value = '';
  const actionEpoch = identityEpoch;
  try { await todoClient.vote(current.id, pendingVote!.request); if (actionEpoch !== identityEpoch) return; pendingVote = undefined; await returnToTodos(); }
  catch (error: unknown) {
    if (actionEpoch !== identityEpoch) return;
    const failure = classifyWorkflowTodoActionFailure(error);
    if (failure.clearTodo) clearTodoContext();
    if (!failure.retainIdempotencyKey) pendingVote = undefined;
    if (failure.refreshTodo) { try { await refreshTodo(); } catch { feedback.value = t('workflow.todo.failed'); return; } }
    feedback.value = t(failure.feedbackKey);
  } finally { if (actionEpoch === identityEpoch) submitting.value = false; }
}
async function releaseClaim(): Promise<void> {
  const current = detail.value;
  if (!canRelease.value || !current?.claimProgress || submitting.value || signatureUploading.value || attachmentUploading.value) return;
  if (Object.entries(patch.value).some(([key, value]) => JSON.stringify(value) !== JSON.stringify(current.submission[key]))) {
    feedback.value = t('workflow.todo.releaseUnsaved'); return;
  }
  if ((current.commentRequired && !comment.value.trim()) || (current.signRequired && !signatureFileId.value)) {
    feedback.value = t('workflow.todo.evidenceRequired'); return;
  }
  try {
    const semantic = JSON.stringify([current.id, current.revision, current.claimProgress.poolRevision, current.submissionRevision, comment.value.trim(), signatureFileId.value]);
    if (pendingRelease?.semantic !== semantic) pendingRelease = { semantic, request: { expectedRevision: current.revision,
      expectedPoolRevision: current.claimProgress.poolRevision, expectedFormRevision: current.submissionRevision,
      idempotencyKey: createIdempotencyKey(), comment: comment.value.trim() || null, signatureFileId: signatureFileId.value } };
  } catch { feedback.value = t('workflow.todo.idempotencyUnavailable'); return; }
  submitting.value = true; feedback.value = '';
  const actionEpoch = identityEpoch;
  try { await todoClient.release(current.id, pendingRelease!.request); if (actionEpoch !== identityEpoch) return; pendingRelease = undefined; await returnToTodos(); }
  catch (error) {
    if (actionEpoch !== identityEpoch) return;
    const failure = classifyWorkflowTodoActionFailure(error);
    if (failure.clearTodo) clearTodoContext();
    if (!failure.retainIdempotencyKey) pendingRelease = undefined;
    if (failure.refreshTodo) { try { await refreshTodo(); } catch { feedback.value = t('workflow.todo.failed'); return; } }
    feedback.value = t(failure.feedbackKey);
  } finally { if (actionEpoch === identityEpoch) submitting.value = false; }
}
async function captureSignature(): Promise<void> {
  const current = detail.value;
  const captureEpoch = identityEpoch;
  if (!current || submitting.value || signatureUploading.value || attachmentUploading.value || !identitySession.can('workflow.todos.sign')) return;
  try {
    const chosen = await uni.chooseImage({ count: 1, sourceType: ['album', 'camera'] });
    const path = chosen.tempFilePaths[0];
    if (!path) return;
    signatureUploading.value = true;
    signatureFileId.value = undefined;
    const uploaded = await todoClient.uploadSignature(current.id, current.revision, path);
    if (captureEpoch === identityEpoch && detail.value?.id === current.id && detail.value.revision === current.revision) {
      signatureFileId.value = uploaded.fileId; signaturePreview.value = path;
    }
  } catch (error) { if (captureEpoch === identityEpoch) {
    if (classifyWorkflowTodoActionFailure(error).clearTodo) clearTodoContext();
    feedback.value = t('workflow.todo.signatureFailed');
  } }
  finally { if (captureEpoch === identityEpoch) signatureUploading.value = false; }
}

function attachmentFailed(error: unknown): void {
  if (error instanceof HttpProblem && [401, 403, 404, 409].includes(error.status)) {
    // 实时授权或修订丢失后，销毁当前表单及草稿，不能继续展示已下载上下文。
    clearTodoContext();
  }
  feedback.value = t('workflow.form.fileFailed');
}
async function downloadAttachment(fileId: string, name: string): Promise<void> {
  const current = detail.value, epoch = identityEpoch;
  if (!current) return;
  try {
    const bytes = await todoClient.downloadAttachment(current.id, fileId);
    if (epoch !== identityEpoch || detail.value !== current) return;
    await deliverWorkflowAttachment(bytes, name);
  } catch (error) { if (epoch === identityEpoch) attachmentFailed(error); }
}
async function uploadAttachment(fieldKey: string, kind: 'main' | 'vote') {
  const current = detail.value, epoch = identityEpoch;
  const policies = kind === 'main' ? visiblePolicies.value : current?.voteForm?.fieldPolicies;
  if (!current || submitting.value || signatureUploading.value || attachmentUploading.value || !identitySession.can('workflow.attachments.upload') || current.statusKey !== 'active' ||
    kind === 'vote' && !canVote.value || ['hidden', 'readOnly'].includes(policies?.[fieldKey] ?? 'editable')) return;
  attachmentUploading.value = true;
  try {
    const path = await chooseWorkflowAttachment();
    if (!path || epoch !== identityEpoch || detail.value !== current) return;
    const result = await todoClient.uploadAttachment(current.id, current.revision, fieldKey, kind, path);
    if (epoch !== identityEpoch || detail.value !== current) return;
    return { fileId: result.fileId, fileName: result.originalFileName };
  } catch (error) { if (epoch === identityEpoch) attachmentFailed(error); }
  finally { if (epoch === identityEpoch) attachmentUploading.value = false; }
}
const uploadMainAttachment = (fieldKey: string) => uploadAttachment(fieldKey, 'main');
const uploadVoteAttachment = (fieldKey: string) => uploadAttachment(fieldKey, 'vote');
</script>

<template>
  <view ref="pageRoot" class="page-shell">
    <text v-if="loading" class="state">{{ t('workflow.todo.loading') }}</text>
    <text v-else-if="feedback && !detail" class="state error">{{ feedback }}</text>
    <view v-else-if="detail" class="panel">
      <view class="meta"><text>{{ detail.statusKey }}</text><text>R{{ detail.revision }}</text></view>
      <text v-if="detail.statusKey !== 'active'" class="state">{{ t('workflow.resource.currentState') }}</text>
      <text v-if="detail.taskProgress" class="task-progress">{{ t('workflow.todo.taskProgress', { completed: detail.taskProgress.completedCount, required: detail.taskProgress.requiredCompletionCount, pending: detail.taskProgress.pendingCount }) }}</text>
      <FullNetFormRenderer :key="`main-${detail.id}-${detailReadVersion}`" ref="renderer" :schema="detail.formSchema" :submission="detail.submission" :policies="visiblePolicies"
        :attachment-names="detail.attachmentNames" :attachment-candidates="attachmentCandidates" :upload-attachment="identitySession.can('workflow.attachments.upload') ? uploadMainAttachment : undefined" :download-attachment="downloadAttachment" @update:patch="patch = $event" />
      <view v-if="detail.voteForm && detail.voteProgress" class="private-ballot">
        <text>{{ t('workflow.todo.privateBallot') }}</text>
        <text>{{ t('workflow.todo.voteProgress', { submitted: detail.voteProgress.submittedCount, total: detail.voteProgress.memberCount }) }}</text>
        <text v-if="detail.voteProgress.closesAtUtc">{{ detail.voteProgress.closesAtUtc }}</text>
        <FullNetFormRenderer :key="`vote-${detail.id}-${detailReadVersion}`" ref="ballotRenderer" :schema="detail.voteForm.formSchema" :submission="detail.voteForm.values" :policies="votePolicies"
          :attachment-names="detail.attachmentNames" :upload-attachment="canVote && identitySession.can('workflow.attachments.upload') ? uploadVoteAttachment : undefined" :download-attachment="downloadAttachment" @update:patch="ballotPatch = $event" />
        <view class="actions">
          <button v-for="choice in (detail.voteForm.allowAbstain ? ['yes', 'no', 'abstain'] as const : ['yes', 'no'] as const)" :key="choice"
            :disabled="!canVote || submitting" :aria-pressed="ballotChoice === choice" @click="ballotChoice = choice">{{ t(`workflow.todo.voteChoice.${choice}`) }}</button>
        </view>
      </view>
      <uni-easyinput v-if="detail.statusKey === 'active'" v-model="comment" type="textarea" :placeholder="t('workflow.todo.comment')" :disabled="submitting" />
      <view v-if="detail.statusKey === 'active' && identitySession.can('workflow.todos.sign')" class="signature">
        <text>{{ t('workflow.todo.signatureHelp') }}</text>
        <image v-if="signatureFileId && signaturePreview" :src="signaturePreview" mode="aspectFit" :aria-label="t('workflow.todo.signatureReady')" />
        <button :disabled="submitting || signatureUploading || attachmentUploading" @click="captureSignature">{{ t(signatureFileId ? 'workflow.todo.signatureReady' : 'workflow.todo.signatureCapture') }}</button>
      </view>
      <text v-else-if="detail.statusKey === 'active' && detail.signRequired" class="feedback">{{ t('workflow.todo.signaturePermissionRequired') }}</text>
      <WorkflowDelegationPanel v-if="identitySession.can('workflow.todos.delegate') || identitySession.can('workflow.todos.recall_delegation')"
        :key="`${detail.id}:${detail.revision}:${detailReadVersion}`" :detail="detail" :client="todoClient"
        :may-delegate="identitySession.can('workflow.todos.delegate')" :may-recall="identitySession.can('workflow.todos.recall_delegation')"
        :comment="comment" :signature-file-id="signatureFileId" :unsaved="handoffUnsaved" :disabled="submitting || signatureUploading || attachmentUploading"
        @busy="submitting = $event" @completed="delegationCompleted" @refresh="refreshTodo" />
      <WorkflowHumanManagementPanel :key="`management:${detail.id}:${detail.revision}:${detailReadVersion}`" :detail="detail" :client="todoClient"
        :permissions="{ transfer: identitySession.can('workflow.todos.transfer'), return: identitySession.can('workflow.todos.return'),
          countersign: identitySession.can('workflow.todos.countersign'), remind: identitySession.can('workflow.instances.remind') }"
        :comment="comment" :signature-file-id="signatureFileId" :unsaved="handoffUnsaved" :disabled="submitting || signatureUploading || attachmentUploading"
        @busy="submitting = $event" @completed="managementCompleted" @refresh="refreshTodo" />
      <text v-if="feedback" class="feedback" role="alert">{{ feedback }}</text>
      <view class="actions">
        <button v-if="canReject" class="reject" :disabled="submitting || signatureUploading || attachmentUploading" @click="act('reject')">{{ t('workflow.todo.reject') }}</button>
        <button v-if="canApprove" class="approve" :disabled="submitting || signatureUploading || attachmentUploading" @click="act('approve')">{{ submitting ? t('workflow.todo.submitting') : t('workflow.todo.approve') }}</button>
        <button v-if="canComplete" class="approve" :disabled="submitting || signatureUploading || attachmentUploading" @click="act('complete')">{{ submitting ? t('workflow.todo.submitting') : t('workflow.todo.complete') }}</button>
        <button v-if="canVote" class="approve" :disabled="submitting || signatureUploading || attachmentUploading || !ballotChoice" @click="castVote">{{ submitting ? t('workflow.todo.submitting') : t('workflow.todo.vote') }}</button>
        <button v-if="canRelease" :disabled="submitting || signatureUploading || attachmentUploading" @click="releaseClaim">{{ t('workflow.todo.release') }}</button>
      </view>
    </view>
  </view>
</template>

<style scoped>
.signature { margin-top: 20rpx; color: #91a7ad; }.signature image { display: block; width: 100%; height: 160rpx; background: white; }.signature button { margin-top: 12rpx; }
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 30rpx 26rpx 60rpx; background: #071421; }.panel { max-width: 760px; margin: 0 auto; padding: 28rpx; border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; background: rgba(12,31,48,.94); }.meta, .actions { display: flex; justify-content: space-between; gap: 20rpx; }.meta { margin-bottom: 28rpx; color: #38d4b2; }.actions { margin-top: 28rpx; }.actions button { flex: 1; margin: 0; }.approve { color: #041713; background: #38d4b2; }.reject { color: #f6b1b1; background: transparent; border: 1px solid #b95661; }.state, .feedback { display: block; padding: 42rpx; text-align: center; color: #91a7ad; }.error, .feedback { color: #f28d8d; }
</style>
