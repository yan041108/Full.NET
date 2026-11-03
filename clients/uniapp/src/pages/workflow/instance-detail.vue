<script setup lang="ts">
import { onLoad, onShow } from '@dcloudio/uni-app';
import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import type { WorkflowInstanceResponse } from '@fullnet/client-contracts';
import { httpClient, identitySession, isBusinessRuntimeAvailable, restoreIdentitySession } from '../../features/identity/application-session';
import { createWorkflowInstanceClient } from '../../features/workflow/workflow-instance-client';
import { readWorkflowResourceTarget, workflowResourceLoginUrl, workflowResourceUrl } from '../../features/workflow/workflow-resource-links';
import { HttpProblem } from '../../api/problem-details';
// #ifdef H5
import { useWorkflowControlAccessibility } from '../../features/workflow/workflow-control-accessibility';
import { useWorkflowResourceRoute } from '../../features/workflow/workflow-resource-route-h5';
// #endif

const { t } = useI18n();
const client = createWorkflowInstanceClient(httpClient);
const pageRoot = ref<unknown>();
// #ifdef H5
useWorkflowControlAccessibility(pageRoot);
// #endif
const id = ref(''); const instance = ref<WorkflowInstanceResponse>(); const loading = ref(true); const feedback = ref('');
let epoch = 0;
let disposed = false;
const unsubscribe = identitySession.subscribe(() => { epoch++; instance.value = undefined; loading.value = false; feedback.value = t('workflow.resource.unavailable'); });
onBeforeUnmount(() => { disposed = true; epoch++; unsubscribe(); });
onLoad(query => { id.value = typeof query?.id === 'string' ? query.id : ''; });
onShow(() => { void load(); });
// #ifdef H5
useWorkflowResourceRoute('workflow_instance', id, () => { epoch++; instance.value = undefined; void load(); });
// #endif
const activeTodo = computed(() => instance.value?.statusKey === 'active' && identitySession.can('workflow.todos.read')
  ? readWorkflowResourceTarget({ resourceTypeKey: 'workflow_todo', resourceId: instance.value?.activeTodoId }) : undefined);
const statusLabel = computed(() => t(`workflow.resource.states.${['active', 'suspended', 'approved', 'rejected', 'cancelled', 'completed'].includes(instance.value?.statusKey ?? '')
  ? instance.value!.statusKey : 'unknown'}`));
async function load(): Promise<void> {
  let current = ++epoch;
  loading.value = true; feedback.value = ''; instance.value = undefined;
  try {
    if (!isBusinessRuntimeAvailable) { feedback.value = t('identity.login.platformUnavailable'); return; }
    if (identitySession.snapshot().state !== 'authenticated' && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: workflowResourceLoginUrl({ resourceTypeKey: 'workflow_instance', resourceId: id.value }) }); return;
    }
    if (!identitySession.can('workflow.instances.read')) { feedback.value = t('workflow.resource.unavailable'); return; }
    if (disposed) return;
    current = ++epoch;
    try { const result = await client.get(id.value); if (current === epoch) instance.value = result; }
    catch (error) { if (current === epoch) feedback.value = t(error instanceof HttpProblem && [401, 403, 404].includes(error.status)
      ? 'workflow.resource.unavailable' : 'workflow.resource.failed'); }
  } finally { if (current === epoch) loading.value = false; }
}
async function openTodo(): Promise<void> { if (activeTodo.value) await uni.navigateTo({ url: workflowResourceUrl(activeTodo.value) }); }
</script>

<template>
  <view ref="pageRoot" class="page-shell">
    <text v-if="loading">{{ t('workflow.todo.loading') }}</text>
    <text v-else-if="feedback" role="alert">{{ feedback }}</text>
    <view v-else-if="instance" class="panel">
      <text class="title">{{ instance.businessTitle || instance.businessType }}</text>
      <text>{{ t('workflow.resource.status') }}: {{ statusLabel }}</text>
      <text>{{ t('workflow.resource.started') }}: {{ instance.startedAtUtc }}</text>
      <text>{{ t('workflow.resource.definitionVersion') }}: {{ instance.definitionVersionId }}</text>
      <text v-if="instance.statusKey !== 'active'">{{ t('workflow.resource.currentState') }}</text>
      <button v-if="activeTodo" @click="openTodo">{{ t('workflow.resource.openTodo') }}</button>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 30rpx 26rpx; background: #071421; color: #d7e4e8; }
.panel { display: grid; gap: 24rpx; max-width: 760px; margin: 0 auto; padding: 28rpx; border: 1px solid #29404b; border-radius: 20rpx; overflow-wrap: anywhere; }
.title { color: #f3fbfa; font-size: 40rpx; font-weight: 700; }
</style>
