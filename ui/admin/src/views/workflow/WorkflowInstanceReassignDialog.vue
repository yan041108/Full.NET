<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput, ElMessageBox } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { reassignWorkflowInstance, type WorkflowInstanceResponse } from '../../api/workflow-instances';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';
import { showSuccess } from '../../feedback/fullNetMessage';

const props = defineProps<{ instance: WorkflowInstanceResponse }>();
const emit = defineEmits<{ close: []; saved: [] }>();
const session = useSessionStore();
const { t } = useAdminI18n();
const userId = ref(''); const reason = ref(''); const busy = ref(false);
const problem = ref<FullNetProblemDetails>();
const scope = useAuthorizedViewScope(session, () => {
  userId.value = ''; reason.value = ''; problem.value = undefined; busy.value = false; emit('close');
}, () => {});
const allowed = computed(() => session.can('workflow.instances.read') && session.can('workflow.instances.recover')
  && props.instance.statusKey === 'active' && !!props.instance.activeTodoId);
const valid = computed(() => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(userId.value.trim())
  && userId.value.trim() !== '00000000-0000-0000-0000-000000000000'
  && reason.value.trim().length <= 500 && !/\p{Cc}/u.test(reason.value));
watch(() => [props.instance.id, props.instance.revision, props.instance.statusKey, props.instance.activeTodoId], close, { flush: 'sync' });
function close(): void { scope.invalidate(); }
async function save(): Promise<void> {
  if (!allowed.value || !valid.value || busy.value) return;
  const request = scope.begin('workflow.instances.recover'); if (!request) return;
  const original = props.instance;
  const body = { assigneeUserId: userId.value.trim(), expectedRevision: original.revision,
    reason: reason.value.trim() || null, idempotencyKey: 'reassign-' + crypto.randomUUID() };
  busy.value = true; problem.value = undefined;
  try {
    await ElMessageBox.confirm(t('workflowInstances.reassignConfirm'), t('workflowInstances.reassign'),
      { type: 'warning', confirmButtonText: t('workflowInstances.reassign'), cancelButtonText: t('common.cancel') });
    // 确认和 HTTP 都属于原会话与实例修订，取消不能承诺服务端回滚。
    if (!request.current() || !allowed.value || props.instance !== original) return;
    const result = await reassignWorkflowInstance(original.id, body, request.signal);
    if (!request.current() || !allowed.value || props.instance !== original) return;
    if (result.id !== original.id || result.revision <= original.revision)
      throw new Error('client.invalid_workflow_instance_identity');
    showSuccess(t('workflowInstances.reassignSuccess')); emit('saved');
  } catch (error: unknown) {
    if (!request.current() || props.instance !== original || error === 'cancel' || error === 'close') return;
    problem.value = isFullNetProblemDetails(error) ? error
      : { status: 500, code: 'client.workflow_reassign_failed', title: t('workflowInstances.reassignFailed') };
  } finally { if (request.current()) busy.value = false; request.finish(); }
}
</script>
<template>
  <ElDialog :model-value="true" :title="t('workflowInstances.reassign')" width="min(560px, 94vw)"
    @update:model-value="value => { if (!value) close(); }">
    <div :aria-busy="busy">
      <div v-if="problem" class="art-inline-alert" role="alert"><strong translate="no">{{ problem.code }}</strong> {{ problem.title }}</div>
      <ElForm label-position="top">
        <ElFormItem :label="t('workflowInstances.reassignUser')" required for="workflow-reassign-user">
          <ElInput id="workflow-reassign-user" v-model="userId" :disabled="busy || !allowed" data-testid="workflow-reassign-user" />
        </ElFormItem>
        <ElFormItem :label="t('workflowInstances.reassignReason')" for="workflow-reassign-reason">
          <ElInput id="workflow-reassign-reason" v-model="reason" type="textarea" :maxlength="500" :disabled="busy || !allowed"
            data-testid="workflow-reassign-reason" />
        </ElFormItem>
      </ElForm>
    </div>
    <template #footer>
      <ElButton data-testid="workflow-reassign-close" @click="close">{{ t('common.cancel') }}</ElButton>
      <ElButton type="primary" data-testid="workflow-reassign-save" :disabled="!allowed || !valid" :loading="busy" @click="save">
        {{ t('workflowInstances.reassign') }}
      </ElButton>
    </template>
  </ElDialog>
</template>
