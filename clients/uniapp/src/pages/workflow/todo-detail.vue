<script setup lang="ts">
import type { ActWorkflowTodoRequest, WorkflowSubmission, WorkflowTodoDetail } from '@fullnet/client-contracts';
import { onLoad } from '@dcloudio/uni-app';
import { computed, ref } from 'vue';
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

const todoClient = createWorkflowTodoClient(httpClient, createUniWorkflowSchemaCache());
const { t } = useI18n();
const todoId = ref('');
const detail = ref<WorkflowTodoDetail>();
const patch = ref<WorkflowSubmission>({});
const comment = ref('');
const loading = ref(true);
const submitting = ref(false);
const feedback = ref('');
const renderer = ref<{ validate(): Readonly<Record<string, 'required'>> }>();
const canApprove = computed(() =>
  detail.value?.statusKey === 'active'
  && identitySession.can('workflow.todos.approve'));
const canReject = computed(() =>
  detail.value?.statusKey === 'active'
  && identitySession.can('workflow.todos.reject'));
let pendingAction: {
  action: 'approve' | 'reject';
  request: ActWorkflowTodoRequest;
} | undefined;

onLoad(query => {
  todoId.value = typeof query?.id === 'string' ? query.id : '';
  void load();
});

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
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!identitySession.can('workflow.todos.read')) {
      throw new Error('permission-denied');
    }
    await refreshTodo();
  } catch {
    feedback.value = t('workflow.todo.failed');
  } finally {
    loading.value = false;
  }
}

async function refreshTodo(): Promise<void> {
  detail.value = await todoClient.get(todoId.value);
  patch.value = {};
  comment.value = '';
}

async function act(action: 'approve' | 'reject'): Promise<void> {
  if (!detail.value || submitting.value) {
    return;
  }
  if (Object.keys(renderer.value?.validate() ?? {}).length > 0) {
    feedback.value = t('workflow.todo.validationFailed');
    return;
  }
  try {
    pendingAction = pendingAction?.action === action
      ? pendingAction
      : {
          action,
          request: {
            expectedRevision: detail.value.revision,
            fieldPatch: patch.value,
            comment: comment.value.trim() || null,
            idempotencyKey: createIdempotencyKey()
          }
        };
  } catch {
    feedback.value = t('workflow.todo.idempotencyUnavailable');
    return;
  }
  submitting.value = true;
  feedback.value = '';
  try {
    await todoClient[action](todoId.value, pendingAction.request);
    pendingAction = undefined;
    feedback.value = t('workflow.todo.completed');
    await uni.navigateBack();
  } catch (error: unknown) {
    const failure = classifyWorkflowTodoActionFailure(error);
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
    submitting.value = false;
  }
}
</script>

<template>
  <view class="page-shell">
    <text v-if="loading" class="state">{{ t('workflow.todo.loading') }}</text>
    <text v-else-if="feedback && !detail" class="state error">{{ feedback }}</text>
    <view v-else-if="detail" class="panel">
      <view class="meta"><text>{{ detail.statusKey }}</text><text>R{{ detail.revision }}</text></view>
      <FullNetFormRenderer ref="renderer" :schema="detail.formSchema" :submission="detail.submission" :policies="detail.fieldPolicies" @update:patch="patch = $event" />
      <uni-easyinput v-model="comment" type="textarea" :placeholder="t('workflow.todo.comment')" :disabled="submitting" />
      <text v-if="feedback" class="feedback" role="alert">{{ feedback }}</text>
      <view class="actions">
        <button v-if="canReject" class="reject" :disabled="submitting" @click="act('reject')">{{ t('workflow.todo.reject') }}</button>
        <button v-if="canApprove" class="approve" :disabled="submitting" @click="act('approve')">{{ submitting ? t('workflow.todo.submitting') : t('workflow.todo.approve') }}</button>
      </view>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 30rpx 26rpx 60rpx; background: #071421; }.panel { max-width: 760px; margin: 0 auto; padding: 28rpx; border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; background: rgba(12,31,48,.94); }.meta, .actions { display: flex; justify-content: space-between; gap: 20rpx; }.meta { margin-bottom: 28rpx; color: #38d4b2; }.actions { margin-top: 28rpx; }.actions button { flex: 1; margin: 0; }.approve { color: #041713; background: #38d4b2; }.reject { color: #f6b1b1; background: transparent; border: 1px solid #b95661; }.state, .feedback { display: block; padding: 42rpx; text-align: center; color: #91a7ad; }.error, .feedback { color: #f28d8d; }
</style>
