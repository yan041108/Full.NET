<script setup lang="ts">
import { computed } from 'vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { ElButton, ElInput, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import type { WorkflowConditionConfig } from './workflow-conditions';

const { t } = useAdminI18n();
const props = withDefaults(defineProps<{ modelValue: WorkflowConditionConfig; fields: readonly WorkflowFormField[]; depth?: number }>(), { depth: 0 });
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowConditionConfig] }>();
const group = computed(() => props.modelValue.operator === 'and' || props.modelValue.operator === 'or');
const field = computed(() => props.fields.find(item => item.fieldKey === props.modelValue.fieldKey));
const multiple = computed(() => ['in', 'notIn', 'containsAny', 'containsAll'].includes(props.modelValue.operator));
const empty = computed(() => ['isEmpty', 'isNotEmpty'].includes(props.modelValue.operator));
const options = computed(() => {
  const values = field.value?.constraints?.options;
  return Array.isArray(values) ? values.filter((item): item is string => typeof item === 'string') : [];
});
const operators = computed(() => field.value?.fieldTypeKey === 'checkbox'
  ? [['containsAny', t('workflowDesigner.conditions.containsAny')], ['containsAll', t('workflowDesigner.conditions.containsAll')], ['isEmpty', t('workflowDesigner.conditions.isEmpty')], ['isNotEmpty', t('workflowDesigner.conditions.isNotEmpty')]]
  : [['equals', t('workflowDesigner.conditions.equals')], ['notEquals', t('workflowDesigner.conditions.notEquals')], ['in', t('workflowDesigner.conditions.in')], ['notIn', t('workflowDesigner.conditions.notIn')],
      ...(['integer', 'money', 'decimal', 'date', 'time', 'datetime'].includes(field.value?.fieldTypeKey ?? '')
        ? [['greaterThan', t('workflowDesigner.conditions.greaterThan')], ['greaterThanOrEqual', t('workflowDesigner.conditions.greaterThanOrEqual')], ['lessThan', t('workflowDesigner.conditions.lessThan')], ['lessThanOrEqual', t('workflowDesigner.conditions.lessThanOrEqual')]] : []),
      ['isEmpty', t('workflowDesigner.conditions.isEmpty')], ['isNotEmpty', t('workflowDesigner.conditions.isNotEmpty')]]);
const newLeaf = (): WorkflowConditionConfig => ({ fieldKey: props.fields[0]?.fieldKey ?? '', operator: 'isNotEmpty' });
function setMode(mode: string): void {
  emit('update:modelValue', mode === 'leaf' ? newLeaf() : { operator: mode, conditions: props.modelValue.conditions ?? [props.modelValue, newLeaf()] });
}
function setField(key: string): void {
  emit('update:modelValue', { fieldKey: key, operator: 'isNotEmpty' });
}
function setOperator(operator: string): void {
  emit('update:modelValue', { fieldKey: props.modelValue.fieldKey, operator,
    ...(['isEmpty', 'isNotEmpty'].includes(operator) ? {} : { value: ['in', 'notIn', 'containsAny', 'containsAll'].includes(operator) ? [] : '' }) });
}
function setValue(value: unknown): void {
  const convert = (item: unknown): unknown => field.value?.fieldTypeKey === 'integer' ? Number(item)
    : field.value?.fieldTypeKey === 'switch' ? item === true || item === 'true' : String(item);
  emit('update:modelValue', { ...props.modelValue, value: Array.isArray(value) ? value.map(convert) : convert(value) });
}
function setChild(index: number, value?: WorkflowConditionConfig): void {
  const children = [...(props.modelValue.conditions ?? [])];
  if (value === undefined) children.splice(index, 1); else children[index] = value;
  emit('update:modelValue', { operator: props.modelValue.operator, conditions: children });
}
function addChild(): void {
  emit('update:modelValue', { operator: props.modelValue.operator, conditions: [...(props.modelValue.conditions ?? []), newLeaf()] });
}
</script>

<template>
  <fieldset class="condition-editor" :aria-label="t('workflowDesigner.conditions.layer', { level: depth + 1 })">
    <legend>{{ t(group ? 'workflowDesigner.conditions.group' : 'workflowDesigner.conditions.leaf') }}</legend>
    <el-select :model-value="group ? modelValue.operator : 'leaf'" :aria-label="t('workflowDesigner.conditions.relation')" @update:model-value="setMode">
      <el-option :label="t('workflowDesigner.conditions.comparison')" value="leaf" />
      <el-option :label="t('workflowDesigner.conditions.and')" value="and" :disabled="depth >= 7" />
      <el-option :label="t('workflowDesigner.conditions.or')" value="or" :disabled="depth >= 7" />
    </el-select>
    <template v-if="group">
      <div v-for="(child, index) in modelValue.conditions" :key="index" class="condition-child">
        <WorkflowConditionEditor :model-value="child" :fields="fields" :depth="depth + 1" @update:model-value="setChild(index, $event)" />
        <el-button :disabled="(modelValue.conditions?.length ?? 0) <= 2" :aria-label="t('workflowDesigner.conditions.deleteItem', { index: index + 1 })" @click="setChild(index)">{{ t('workflowDesigner.conditions.remove') }}</el-button>
      </div>
      <el-button :disabled="(modelValue.conditions?.length ?? 0) >= 16" @click="addChild">{{ t('workflowDesigner.conditions.add') }}</el-button>
    </template>
    <template v-else>
      <el-select :model-value="modelValue.fieldKey" :aria-label="t('workflowDesigner.conditions.field')" :placeholder="t('workflowDesigner.conditions.selectField')" @update:model-value="setField">
        <el-option v-for="item in fields" :key="item.fieldKey" :value="item.fieldKey" :label="item.fieldKey" />
      </el-select>
      <el-select :model-value="modelValue.operator" :aria-label="t('workflowDesigner.conditions.operator')" @update:model-value="setOperator">
        <el-option v-for="item in operators" :key="item[0]" :value="item[0]!" :label="item[1]" />
      </el-select>
      <el-select v-if="!empty && multiple" :model-value="Array.isArray(modelValue.value) ? modelValue.value.map(String) : []" :aria-label="t('workflowDesigner.conditions.values')" multiple filterable allow-create default-first-option :multiple-limit="32" @update:model-value="setValue">
        <el-option v-for="option in options" :key="option" :value="option" :label="option" />
      </el-select>
      <el-select v-else-if="!empty && field?.fieldTypeKey === 'switch'" :model-value="modelValue.value === true" :aria-label="t('workflowDesigner.conditions.value')" @update:model-value="setValue">
        <el-option :label="t('workflowDesigner.conditions.yes')" :value="true" /><el-option :label="t('workflowDesigner.conditions.no')" :value="false" />
      </el-select>
      <el-input v-else-if="!empty" :model-value="String(modelValue.value ?? '')" :aria-label="t('workflowDesigner.conditions.value')" @update:model-value="setValue" />
    </template>
  </fieldset>
</template>

<style scoped>
.condition-editor { min-width: 0; display: grid; gap: 12px; margin: 0; padding: 12px; border: 1px solid var(--el-border-color); border-left: 3px solid var(--el-color-primary); border-radius: 6px; }
.condition-editor legend { padding: 0 6px; color: var(--el-text-color-secondary); font-size: 12px; }
.condition-child { display: grid; gap: 8px; padding-left: 8px; }
.condition-child > .el-button { justify-self: end; }
@media (max-width: 600px) { .condition-child { padding-left: 0; } .condition-editor { padding: 8px; } }
</style>
