<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput, ElPagination } from 'element-plus';
import type { WorkflowRecipientCandidateResponse } from '@fullnet/client-contracts';
import type { WorkflowInstanceResponse } from '../../api/workflow-instances';
import { instanceTrim, validWorkflowInstance } from '../../guards/workflow-instance-guards';
import { workflowFormIdValid } from '../../guards/workflow-form-guards';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';
import { listWorkflowReassignmentCandidates } from '../../api/workflow-reassignment-candidates';

const props = defineProps<{ instance: WorkflowInstanceResponse; busy?: boolean; disabled?: boolean }>();
const emit = defineEmits<{ close: []; submit: [input: { assigneeUserId: string; reason: string | null; todoId: string; expectedTodoRevision: number }] }>();
const session = useSessionStore(), { t } = useAdminI18n(), userId = ref(''), reason = ref(''), closed = ref(false), owner = ref(0);
const candidates = ref<WorkflowRecipientCandidateResponse[]>([]), page = ref(1), total = ref(0), loading = ref(false), failed = ref(false);
const todoRevision = ref<number>();
let currentLoad: { cancel(): void } | undefined;
// 弹窗只收集意图，确认、幂等键、HTTP 和回执由父页面统一持有，关闭后仍能保护未知操作。
const scope = useAuthorizedViewScope(session, () => {
  userId.value = ''; reason.value = ''; candidates.value = []; todoRevision.value = undefined; total.value = 0; loading.value = false; failed.value = false;
  if (!closed.value) { closed.value = true; owner.value++; emit('close'); }
}, () => readCandidates(1));
const allowed = computed(() => { const user = session.currentUser; return !closed.value && !props.disabled && session.can('workflow.instances.read') && session.can('workflow.instances.recover') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()) && validWorkflowInstance(props.instance) && props.instance.statusKey === 'active' && !!props.instance.activeTodoId; });
const valid = computed(() => !!todoRevision.value && !loading.value && !failed.value && candidates.value.some(candidate => candidate.id === userId.value)
  && workflowFormIdValid(userId.value) && instanceTrim(reason.value).length <= 500 && !/\p{Cc}/u.test(reason.value));
watch(() => [props.instance.id, props.instance.revision, props.instance.statusKey, props.instance.activeTodoId], close, { flush: 'sync' });
function close(): void { scope.invalidate(); }
async function readCandidates(nextPage: number, fallback = true): Promise<void> {
  if (!allowed.value || props.busy || !Number.isSafeInteger(nextPage) || nextPage < 1) return;
  currentLoad?.cancel();
  const operation = scope.begin('workflow.instances.recover'); if (!operation) return;
  currentLoad = operation; loading.value = true; failed.value = false; candidates.value = []; todoRevision.value = undefined; userId.value = ''; page.value = nextPage;
  try {
    const value = await listWorkflowReassignmentCandidates(props.instance.id, props.instance.activeTodoId!, nextPage, 20, operation.signal);
    if (operation.current() && allowed.value) {
      if (value.instanceRevision !== props.instance.revision) { close(); return; }
      // 成员变化可能使尾页消失，只按本次权威总数回退一次，避免把用户困在无分页的空页。
      const lastPage = Math.max(1, Math.ceil(value.total / 20));
      if (fallback && !value.items.length && nextPage > lastPage) { await readCandidates(lastPage, false); return; }
      candidates.value = value.items; total.value = value.total; todoRevision.value = value.todoRevision;
    }
  } catch {
    if (operation.current() && allowed.value) { failed.value = true; total.value = 0; }
  } finally {
    if (operation.current() && allowed.value) { loading.value = false; currentLoad = undefined; }
    operation.finish();
  }
}
function save(): void { if (allowed.value && valid.value && !props.busy) emit('submit', { assigneeUserId: instanceTrim(userId.value), reason: instanceTrim(reason.value) || null, todoId: props.instance.activeTodoId!, expectedTodoRevision: todoRevision.value! }); }
const controls = computed(() => { const ticket = owner.value; const current = () => ticket === owner.value && !closed.value; return { close: () => { if (current()) close(); }, save: () => { if (current()) save(); }, model: (value: boolean) => { if (!value && current()) close(); }, user: (value: string) => { if (current() && allowed.value && !props.busy) userId.value = value; }, reason: (value: string) => { if (current() && allowed.value && !props.busy) reason.value = value; } }; });
</script>
<template>
  <ElDialog :model-value="!closed" :title="t('workflowInstances.reassign')" width="min(560px, 94vw)" v-on="{ 'update:model-value': controls.model }">
    <div :aria-busy="busy || loading">
      <ElForm label-position="top" @submit.prevent="controls.save">
        <ElFormItem :label="t('workflowInstances.reassignUser')" required for="workflow-reassign-user">
          <select id="workflow-reassign-user" :value="userId" :disabled="busy || loading || failed || !allowed" data-testid="workflow-reassign-user"
            aria-describedby="workflow-reassign-candidate-hint" @change="controls.user(($event.target as HTMLSelectElement).value)">
            <option value="">{{ t('workflowInstances.reassignUser') }}</option>
            <option v-for="candidate in candidates" :key="candidate.id" :value="candidate.id">{{ candidate.displayName }} · {{ candidate.username }}</option>
          </select>
        </ElFormItem>
        <p id="workflow-reassign-candidate-hint">{{ t('workflowInstances.reassignCandidatesHint') }}</p>
        <p v-if="loading" role="status">{{ t('workflowInstances.reassignCandidatesLoading') }}</p>
        <p v-else-if="failed" role="alert">{{ t('workflowInstances.reassignCandidatesFailed') }}
          <ElButton data-testid="workflow-reassign-retry" :disabled="busy || !allowed" @click="readCandidates(page)">{{ t('workflowInstances.reassignCandidatesRetry') }}</ElButton></p>
        <p v-else-if="!candidates.length" role="status">{{ t('workflowInstances.reassignCandidatesEmpty') }}</p>
        <ElPagination v-if="total > 20" :current-page="page" :page-size="20" :total="total" layout="prev, pager, next"
          :disabled="busy || loading || !allowed" @current-change="readCandidates" />
        <ElFormItem :label="t('workflowInstances.reassignReason')" for="workflow-reassign-reason"><ElInput id="workflow-reassign-reason" :model-value="reason" type="textarea" :maxlength="500" :disabled="busy || !allowed" data-testid="workflow-reassign-reason" v-on="{ 'update:model-value': controls.reason }" /></ElFormItem>
      </ElForm>
    </div>
    <template #footer><ElButton data-testid="workflow-reassign-close" v-on="{ click: controls.close }">{{ t('common.cancel') }}</ElButton><ElButton type="primary" data-testid="workflow-reassign-save" :disabled="!allowed || !valid || busy" :loading="busy" v-on="{ click: controls.save }">{{ t('workflowInstances.reassign') }}</ElButton></template>
  </ElDialog>
</template>
<style scoped>
select { width: 100%; min-height: 34px; padding: 6px 10px; border: 1px solid var(--el-border-color); border-radius: var(--el-border-radius-base); background: var(--el-fill-color-blank); color: var(--el-text-color-primary); }
p { overflow-wrap: anywhere; }
</style>
