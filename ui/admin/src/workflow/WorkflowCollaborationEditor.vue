<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElInput } from 'element-plus';
import { isWorkflowCollaborationId, type WorkflowCollaborationGrant } from '@fullnet/client-contracts';
import { getWorkflowCollaborationGrant } from '../api/workflow-collaboration-grants';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showWarning } from '../feedback/fullNetMessage';
import { readWorkflowCollaboration, type WorkflowCollaborationConfiguration } from './workflow-collaboration-node';

const props = defineProps<{ modelValue?: unknown; definitionId?: string; formVersionId?: string; canRead: boolean; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowCollaborationConfiguration]; cancel: [] }>();
const { t } = useAdminI18n();
const grantId = ref(''); const grant = ref<WorkflowCollaborationGrant>(); const loading = ref(false);
let epoch = 0; let controller: AbortController | undefined;
function clear(): void { epoch++; controller?.abort(); controller = undefined; grant.value = undefined; loading.value = false; }
function eligible(value: WorkflowCollaborationGrant): boolean {
  return value.statusKey === 'active' && value.revision === 2 && value.policyHash !== null && value.acceptance !== null &&
    value.offer.sourceDefinitionId === props.definitionId && value.offer.sourceFormVersionId === props.formVersionId && Date.parse(value.expiresAtUtc) > Date.now();
}
const canLookup = computed(() => props.canRead && !props.disabled && isWorkflowCollaborationId(props.definitionId) &&
  isWorkflowCollaborationId(props.formVersionId) && isWorkflowCollaborationId(grantId.value));
const ready = computed(() => canLookup.value && grant.value !== undefined && eligible(grant.value));
watch(() => [props.modelValue, props.definitionId, props.formVersionId, props.canRead, props.disabled], () => {
  clear(); grantId.value = '';
  try { grantId.value = readWorkflowCollaboration(props.modelValue).collaborationPolicy.grantId; } catch { /* 新节点可以尚未选择授权。 */ }
}, { immediate: true });
onBeforeUnmount(clear);
async function load(): Promise<WorkflowCollaborationGrant | undefined> {
  clear(); if (!canLookup.value) return;
  const request = epoch; const current = new AbortController(); controller = current; loading.value = true;
  try {
    const value = await getWorkflowCollaborationGrant(grantId.value, current.signal);
    if (request !== epoch || current.signal.aborted) return;
    if (!eligible(value)) { showWarning(t('workflowDesigner.collaboration.mismatch')); return; }
    grant.value = value; return value;
  } catch (error) {
    if (request === epoch && !current.signal.aborted) showProblem(error, t('workflowCollaboration.failed'));
  } finally { if (request === epoch) loading.value = false; }
}
async function save(): Promise<void> {
  // 保存前重新读取真实授权，不能从抽屉打开时的旧事实复用许可；服务端发布仍独立复核。
  const value = await load();
  if (!value || props.disabled || !eligible(value)) return;
  emit('save', readWorkflowCollaboration({ collaborationPolicy: { schemaVersion: 1, grantId: value.id, grantRevision: 2, policyHash: value.policyHash } }));
}
</script>

<template>
  <section class="workflow-collaboration-editor" data-testid="workflow-collaboration-editor">
    <p>{{ t('workflowDesigner.collaboration.help') }}</p>
    <p v-if="!canRead" role="status">{{ t('workflowDesigner.collaboration.permissionRequired') }}</p>
    <p v-else-if="!definitionId || !formVersionId" role="status">{{ t('workflowDesigner.collaboration.selectForm') }}</p>
    <label><span>{{ t('workflowCollaboration.lookup') }}</span>
      <ElInput v-model="grantId" :disabled="disabled || !canRead || loading" :maxlength="36" data-testid="workflow-collaboration-grant-id" @input="clear" />
    </label>
    <ElButton v-if="canRead" :disabled="!canLookup || loading" data-testid="workflow-collaboration-load" @click="load">{{ t('workflowCollaboration.load') }}</ElButton>
    <dl v-if="grant">
      <dt>{{ t('workflowCollaboration.target') }}</dt><dd>{{ grant.targetTenantScopeKey }}</dd>
      <dt>{{ t('workflowCollaboration.targetWorkflowKey') }}</dt><dd>{{ grant.targetSummary?.definitionKey }}</dd>
      <dt>{{ t('workflowCollaboration.expires') }}</dt><dd>{{ grant.expiresAtUtc }}</dd>
    </dl>
    <div class="workflow-collaboration-editor__actions">
      <ElButton v-if="canRead" type="primary" :disabled="!ready || disabled || loading" data-testid="workflow-collaboration-save" @click="save">{{ t('workflowDesigner.collaboration.save') }}</ElButton>
      <ElButton :disabled="loading" @click="emit('cancel')">{{ t('workflowDesigner.collaboration.cancel') }}</ElButton>
    </div>
  </section>
</template>

<style scoped>
.workflow-collaboration-editor { display: grid; gap: 12px; padding: 0 20px; }
.workflow-collaboration-editor label { display: grid; gap: 6px; }
.workflow-collaboration-editor dd { margin: 0; overflow-wrap: anywhere; }
.workflow-collaboration-editor__actions { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
