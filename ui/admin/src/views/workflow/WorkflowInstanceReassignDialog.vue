<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput } from 'element-plus';
import type { WorkflowInstanceResponse } from '../../api/workflow-instances';
import { instanceTrim, validWorkflowInstance } from '../../api/workflow-instance-guards';
import { workflowFormIdValid } from '../../api/workflow-form-guards';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';

const props = defineProps<{ instance: WorkflowInstanceResponse; busy?: boolean; disabled?: boolean }>();
const emit = defineEmits<{ close: []; submit: [input: { assigneeUserId: string; reason: string | null }] }>();
const session = useSessionStore(), { t } = useAdminI18n(), userId = ref(''), reason = ref(''), closed = ref(false), owner = ref(0);
// 弹窗只收集意图，确认、幂等键、HTTP 和回执由父页面统一持有，关闭后仍能保护未知操作。
const scope = useAuthorizedViewScope(session, () => { userId.value = ''; reason.value = ''; if (!closed.value) { closed.value = true; owner.value++; emit('close'); } }, () => {});
const allowed = computed(() => { const user = session.currentUser; return !closed.value && !props.disabled && session.can('workflow.instances.read') && session.can('workflow.instances.recover') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()) && validWorkflowInstance(props.instance) && props.instance.statusKey === 'active' && !!props.instance.activeTodoId; });
const valid = computed(() => workflowFormIdValid(instanceTrim(userId.value)) && instanceTrim(reason.value).length <= 500 && !/\p{Cc}/u.test(reason.value));
watch(() => [props.instance.id, props.instance.revision, props.instance.statusKey, props.instance.activeTodoId], close, { flush: 'sync' });
function close(): void { scope.invalidate(); }
function save(): void { if (allowed.value && valid.value && !props.busy) emit('submit', { assigneeUserId: instanceTrim(userId.value), reason: instanceTrim(reason.value) || null }); }
const controls = computed(() => { const ticket = owner.value; const current = () => ticket === owner.value && !closed.value; return { close: () => { if (current()) close(); }, save: () => { if (current()) save(); }, model: (value: boolean) => { if (!value && current()) close(); }, user: (value: string) => { if (current() && allowed.value && !props.busy) userId.value = value; }, reason: (value: string) => { if (current() && allowed.value && !props.busy) reason.value = value; } }; });
</script>
<template>
  <ElDialog :model-value="!closed" :title="t('workflowInstances.reassign')" width="min(560px, 94vw)" v-on="{ 'update:model-value': controls.model }">
    <div :aria-busy="busy">
      <ElForm label-position="top" @submit.prevent="controls.save">
        <ElFormItem :label="t('workflowInstances.reassignUser')" required for="workflow-reassign-user"><ElInput id="workflow-reassign-user" :model-value="userId" :disabled="busy || !allowed" data-testid="workflow-reassign-user" v-on="{ 'update:model-value': controls.user }" /></ElFormItem>
        <ElFormItem :label="t('workflowInstances.reassignReason')" for="workflow-reassign-reason"><ElInput id="workflow-reassign-reason" :model-value="reason" type="textarea" :maxlength="500" :disabled="busy || !allowed" data-testid="workflow-reassign-reason" v-on="{ 'update:model-value': controls.reason }" /></ElFormItem>
      </ElForm>
    </div>
    <template #footer><ElButton data-testid="workflow-reassign-close" v-on="{ click: controls.close }">{{ t('common.cancel') }}</ElButton><ElButton type="primary" data-testid="workflow-reassign-save" :disabled="!allowed || !valid || busy" :loading="busy" v-on="{ click: controls.save }">{{ t('workflowInstances.reassign') }}</ElButton></template>
  </ElDialog>
</template>
