<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { ElInputNumber, ElOption, ElSelect, ElSwitch } from 'element-plus';
import type { WorkflowFormField, WorkflowFormVersionResponse } from '@fullnet/client-contracts';
import { listWorkflowForms, listWorkflowFormVersions, type WorkflowFormResponse } from '../api/workflow-forms';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowVoteRuleEditor from './WorkflowVoteRuleEditor.vue';
import type { WorkflowVoteResultRule } from './workflow-vote-policy';
const props = defineProps<{ modelValue: Record<string, unknown>; resultFields: readonly WorkflowFormField[] }>();
const emit = defineEmits<{ 'update:modelValue': [value: Record<string, unknown>] }>();
const { t } = useAdminI18n();
const forms = ref<WorkflowFormResponse[]>([]); const versions = ref<WorkflowFormVersionResponse[]>([]);
const formId = ref(''); const loading = ref(false); const failed = ref(false);
const controller = new AbortController(); let generation = 0;
const resultFields = computed(() => props.resultFields.filter(field => field.fieldTypeKey === 'switch'));
const rule = computed(() => props.modelValue.resultRule as WorkflowVoteResultRule | undefined);
function set(key: string, value: unknown): void {
  const next = { ...props.modelValue };
  if (value === undefined) delete next[key]; else next[key] = value;
  emit('update:modelValue', next);
}
async function selectForm(id: string): Promise<void> {
  const current = ++generation; loading.value = true; failed.value = false;
  try { const rows = await listWorkflowFormVersions(id, controller.signal); if (current === generation) versions.value = rows; }
  catch { if (!controller.signal.aborted && current === generation) { failed.value = true; versions.value = []; } }
  finally { if (current === generation) loading.value = false; }
}
onMounted(async () => {
  loading.value = true;
  try { forms.value = (await listWorkflowForms(controller.signal)).filter(form => form.statusKey === 'active' && form.latestPublishedVersionId); }
  catch { if (!controller.signal.aborted) failed.value = true; }
  finally { loading.value = false; }
});
onBeforeUnmount(() => { generation++; controller.abort(); });
</script>
<template>
  <section class="workflow-vote-policy" data-testid="workflow-vote-policy">
    <p>{{ t('workflowDesigner.votes.privateFormHelp') }}</p>
    <p v-if="failed" role="alert">{{ t('workflowDesigner.votes.formsFailed') }}</p>
    <label><span>{{ t('workflowDesigner.votes.form') }}</span>
      <ElSelect v-model="formId" :loading="loading" data-testid="workflow-vote-form" @change="selectForm">
        <ElOption v-for="form in forms" :key="form.id" :value="form.id" :label="form.formKey" />
      </ElSelect>
    </label>
    <label><span>{{ t('workflowDesigner.votes.version') }}</span>
      <ElSelect :model-value="String(modelValue.ballotFormVersionId ?? '')" :loading="loading" data-testid="workflow-vote-version" @update:model-value="set('ballotFormVersionId', $event)">
        <ElOption v-if="modelValue.ballotFormVersionId && !versions.some(version => version.id === modelValue.ballotFormVersionId)"
          :value="String(modelValue.ballotFormVersionId)" :label="t('workflowDesigner.votes.currentVersion', { id: String(modelValue.ballotFormVersionId) })" />
        <ElOption v-for="version in versions" :key="version.id" :value="version.id" :label="`v${version.versionNumber}`" />
      </ElSelect>
    </label>
    <label><span>{{ t('workflowDesigner.votes.participation') }}</span>
      <ElInputNumber :model-value="Number(modelValue.participationPercentage)" :min="1" :max="100" :precision="0" @update:model-value="set('participationPercentage', $event)" />
    </label>
    <label><span>{{ t('workflowDesigner.votes.approval') }}</span>
      <ElInputNumber :model-value="Number(modelValue.approvalPercentage)" :min="1" :max="100" :precision="0" @update:model-value="set('approvalPercentage', $event)" />
    </label>
    <label><span>{{ t('workflowDesigner.votes.allowAbstain') }}</span><ElSwitch :model-value="modelValue.allowAbstain === true" @update:model-value="set('allowAbstain', $event)" /></label>
    <label><span>{{ t('workflowDesigner.votes.deadlineEnabled') }}</span><ElSwitch :model-value="modelValue.closeAfterSeconds !== undefined" @update:model-value="set('closeAfterSeconds', $event ? 3600 : undefined)" /></label>
    <label v-if="modelValue.closeAfterSeconds !== undefined"><span>{{ t('workflowDesigner.votes.deadlineSeconds') }}</span>
      <ElInputNumber :model-value="Number(modelValue.closeAfterSeconds)" :min="60" :max="2592000" :precision="0" @update:model-value="set('closeAfterSeconds', $event)" />
    </label>
    <p>{{ t('workflowDesigner.votes.deadlineHelp') }}</p>
    <label><span>{{ t('workflowDesigner.votes.resultField') }}</span>
      <ElSelect :model-value="String(modelValue.resultFieldKey ?? '')" clearable @update:model-value="set('resultFieldKey', $event || undefined)">
        <ElOption v-for="field in resultFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
      </ElSelect>
    </label>
    <label><span>{{ t('workflowDesigner.votes.ruleEnabled') }}</span><ElSwitch :model-value="rule !== undefined"
      @update:model-value="set('resultRule', $event ? { metricKey: 'yes', comparisonKey: 'gte', value: 1 } : undefined)" /></label>
    <WorkflowVoteRuleEditor v-if="rule" :model-value="rule" @update:model-value="set('resultRule', $event)" />
  </section>
</template>
<style scoped>
.workflow-vote-policy { display: grid; gap: 1rem; }
.workflow-vote-policy label { display: grid; gap: .35rem; }
.workflow-vote-policy p { color: var(--el-text-color-secondary); margin: 0; line-height: 1.6; }
</style>
