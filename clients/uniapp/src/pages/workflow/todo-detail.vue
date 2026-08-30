<script setup lang="ts">
import type { ActWorkflowTodoRequest, WorkflowSubmission, WorkflowTodoDetail } from '@fullnet/client-contracts';
import { onLoad } from '@dcloudio/uni-app';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import FullNetFormRenderer from '../../features/workflow/FullNetFormRenderer.vue';

// #ifdef H5
import { h5HttpClient, h5IdentitySession, restoreH5IdentitySession } from '../../features/identity/h5-application-session';
import { classifyWorkflowTodoActionFailure } from '../../features/workflow/workflow-todo-action-failure';
import { createWorkflowTodoClient } from '../../features/workflow/workflow-todo-client';
import { createUniWorkflowSchemaCache } from '../../features/workflow/workflow-schema-cache';
const todoClient = createWorkflowTodoClient(h5HttpClient, createUniWorkflowSchemaCache());
// #endif

const { t } = useI18n();
const todoId = ref('');
const detail = ref<WorkflowTodoDetail>();
const patch = ref<WorkflowSubmission>({});
const comment = ref('');
const loading = ref(true);
const submitting = ref(false);
const feedback = ref('');
const renderer = ref<{ validate(): Readonly<Record<string, 'required'>> }>();
const canApprove = computed(() => {
  // #ifdef H5
  return detail.value?.statusKey === 'active'
    && h5IdentitySession.can('workflow.todos.approve');
  // #endif
  // #ifndef H5
  return false;
  // #endif
});
const canReject = computed(() => {
  // #ifdef H5
  return detail.value?.statusKey === 'active'
    && h5IdentitySession.can('workflow.todos.reject');
  // #endif
  // #ifndef H5
  return false;
  // #endif
});
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
    // #ifdef H5
    if (h5IdentitySession.snapshot().state !== 'authenticated'
      && !await restoreH5IdentitySession()) {
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!h5IdentitySession.can('workflow.todos.read')) throw new Error('permission-denied');
    await refreshTodo();
    // #endif
    // #ifndef H5
    feedback.value = t('identity.login.platformUnavailable');
    // #endif
  } catch {
    feedback.value = t('workflow.todo.failed');
  } finally {
    loading.value = false;
  }
}

async function refreshTodo(): Promise<void> {
  // #ifdef H5
  detail.value = await todoClient.get(todoId.value);
  patch.value = {};
  comment.value = '';
  // #endif
}

async function act(action: 'approve' | 'reject'): Promise<void> {
  if (!detail.value || submitting.value) return;
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
            idempotencyKey: crypto.randomUUID()
          }
        };
  } catch {
    feedback.value = t('workflow.todo.idempotencyUnavailable');
    return;
  }
  submitting.value = true;
  feedback.value = '';
  try {
    // #ifdef H5
    await todoClient[action](todoId.value, pendingAction.request);
    pendingAction = undefined;
    feedback.value = t('workflow.todo.completed');
    await uni.navigateBack();
    // #endif
  } catch (error: unknown) {
    // #ifdef H5
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
    // #endif
    // #ifndef H5
    feedback.value = t('workflow.todo.failed');
    // #endif
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
