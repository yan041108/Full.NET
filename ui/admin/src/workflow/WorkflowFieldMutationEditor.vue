<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElInput, ElInputNumber, ElOption, ElSelect, ElSwitch } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowConditionEditor from './WorkflowConditionEditor.vue';
import { readWorkflowFieldMutation, workflowConditionFields, type WorkflowFieldAssignment, type WorkflowFieldMutationConfiguration } from './workflow-field-mutation';
import type { WorkflowConditionConfig } from './workflow-conditions';
const props = defineProps<{ modelValue?: unknown; kind: 'data.modify' | 'data.clear'; fields: readonly WorkflowFormField[]; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowFieldMutationConfiguration]; cancel: [] }>();
const { t } = useAdminI18n();
const rows = ref<WorkflowFieldAssignment[]>([]); const condition = ref<WorkflowConditionConfig>(); const conditional = ref(false);
const invalidInput = ref(false);
const originalAccess = ref<Record<string, string>>({});
watch(() => [props.modelValue, props.kind], () => {
  invalidInput.value = false;
  originalAccess.value = {};
  try {
    if (props.modelValue === undefined || typeof props.modelValue === 'object' && props.modelValue !== null && !('mutationPolicy' in props.modelValue)) { rows.value = [{ fieldKey: '', value: '' }]; conditional.value = false; condition.value = undefined; return; }
    const value = readWorkflowFieldMutation(props.kind, props.modelValue);
    originalAccess.value = value.fieldPolicies;
    rows.value = value.mutationPolicy.assignments ?? value.mutationPolicy.fieldKeys!.map(fieldKey => ({ fieldKey }));
    condition.value = value.mutationPolicy.condition; conditional.value = condition.value !== undefined;
  } catch { rows.value = []; invalidInput.value = true; }
}, { immediate: true });
const targetFields = computed(() => props.fields.filter(field => props.kind !== 'data.clear' || !field.required));
watch(conditional, value => { if (value && !condition.value && props.fields[0]) condition.value = { fieldKey: props.fields[0].fieldKey, operator: 'isEmpty' }; });
const fieldType = (row: WorkflowFieldAssignment) => props.fields.find(field => field.fieldKey === row.fieldKey)?.fieldTypeKey;
const sources = (row: WorkflowFieldAssignment) => props.fields.filter(field => field.fieldTypeKey === fieldType(row));
const scalar = (row: WorkflowFieldAssignment) => ['text', 'textarea', 'integer', 'decimal', 'switch', 'date', 'datetime', 'select', 'radio'].includes(fieldType(row) ?? '');
function changeTarget(row: WorkflowFieldAssignment, value: unknown): void { row.fieldKey = String(value); delete row.sourceFieldKey; row.value = fieldType(row) === 'switch' ? false : ['integer', 'decimal'].includes(fieldType(row) ?? '') ? 0 : ''; }
function changeSource(row: WorkflowFieldAssignment, value: unknown): void { if (value === 'field') { delete row.value; row.sourceFieldKey = ''; } else { delete row.sourceFieldKey; row.value = fieldType(row) === 'switch' ? false : ['integer', 'decimal'].includes(fieldType(row) ?? '') ? 0 : ''; } }
const configuration = computed(() => {
  try {
    if (invalidInput.value || conditional.value && !condition.value || rows.value.some(row => !props.fields.some(field => field.fieldKey === row.fieldKey) || props.kind === 'data.clear' && props.fields.find(field => field.fieldKey === row.fieldKey)?.required)) return undefined;
    const access: Record<string, string> = { ...originalAccess.value };
    for (const row of rows.value) { access[row.fieldKey] = originalAccess.value[row.fieldKey] === 'required' ? 'required' : 'editable'; if (row.sourceFieldKey && access[row.sourceFieldKey] !== 'editable' && access[row.sourceFieldKey] !== 'required') access[row.sourceFieldKey] = 'readOnly'; }
    if (conditional.value && condition.value) for (const field of workflowConditionFields(condition.value)) access[field] ??= 'readOnly';
    return readWorkflowFieldMutation(props.kind, { fieldPolicies: access, mutationPolicy: {
      ...(props.kind === 'data.clear' ? { fieldKeys: rows.value.map(row => row.fieldKey) } : { assignments: rows.value }),
      ...(conditional.value ? { condition: condition.value } : {}) } });
  } catch { return undefined; }
});
</script>
<template>
  <div class="workflow-field-editor">
    <p>{{ t('workflowDesigner.fieldActivity.help') }}</p>
    <div v-for="(row, index) in rows" :key="index" class="workflow-field-row">
      <ElSelect :model-value="row.fieldKey" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.target')" @update:model-value="changeTarget(row, $event)">
        <ElOption v-for="field in targetFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
      </ElSelect>
      <template v-if="kind === 'data.modify'">
        <ElSelect :model-value="row.sourceFieldKey === undefined ? 'constant' : 'field'" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.source')" @update:model-value="changeSource(row, $event)">
          <ElOption value="constant" :disabled="!scalar(row)" :label="t('workflowDesigner.fieldActivity.constant')" /><ElOption value="field" :label="t('workflowDesigner.fieldActivity.copy')" />
        </ElSelect>
        <ElSelect v-if="row.sourceFieldKey !== undefined" v-model="row.sourceFieldKey" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.copy')">
          <ElOption v-for="field in sources(row)" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
        </ElSelect>
        <ElSwitch v-else-if="fieldType(row) === 'switch'" :model-value="row.value === true" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.value')" @update:model-value="row.value = $event" />
        <ElInputNumber v-else-if="['integer', 'decimal'].includes(fieldType(row) ?? '')" :model-value="typeof row.value === 'number' ? row.value : 0" :precision="fieldType(row) === 'integer' ? 0 : undefined" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.value')" @update:model-value="row.value = $event" />
        <ElInput v-else-if="scalar(row)" :model-value="typeof row.value === 'string' ? row.value : ''" :disabled="disabled" :aria-label="t('workflowDesigner.fieldActivity.value')" @update:model-value="row.value = $event" />
        <span v-else>{{ t('workflowDesigner.fieldActivity.useCopy') }}</span>
      </template>
      <ElButton :disabled="disabled" @click="rows.splice(index, 1)">{{ t('workflowDesigner.fieldActivity.remove') }}</ElButton>
    </div>
    <ElButton :disabled="disabled || rows.length >= 16 || invalidInput" @click="rows.push({ fieldKey: '', value: '' })">{{ t('workflowDesigner.fieldActivity.add') }}</ElButton>
    <label>{{ t('workflowDesigner.fieldActivity.conditional') }}<ElSwitch v-model="conditional" :disabled="disabled" /></label>
    <fieldset v-if="conditional && condition" :disabled="disabled"><WorkflowConditionEditor :model-value="condition" :fields="fields" @update:model-value="condition = $event" /></fieldset>
    <p v-if="!configuration" role="alert">{{ t('workflowDesigner.fieldActivity.invalid') }}</p>
    <div><ElButton :disabled="disabled || !configuration" data-testid="field-activity-save" @click="configuration && emit('save', configuration)">{{ t('workflowDesigner.delay.save') }}</ElButton><ElButton @click="emit('cancel')">{{ t('common.cancel') }}</ElButton></div>
  </div>
</template>
<style scoped>
.workflow-field-editor { display: grid; gap: 16px; }
.workflow-field-row { display: grid; gap: 8px; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); align-items: start; }
</style>
