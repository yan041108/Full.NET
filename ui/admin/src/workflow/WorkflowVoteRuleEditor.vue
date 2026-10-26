<script setup lang="ts">
import { ElButton, ElInputNumber, ElOption, ElSelect } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import type { WorkflowVoteResultRule } from './workflow-vote-policy';
const props = withDefaults(defineProps<{ modelValue: WorkflowVoteResultRule; depth?: number }>(), { depth: 1 });
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowVoteResultRule] }>();
const { t } = useAdminI18n();
const leaf = (): WorkflowVoteResultRule => ({ metricKey: 'yes', comparisonKey: 'gte', value: 1 });
function changeKind(kind: string): void {
  emit('update:modelValue', kind === 'metric' ? leaf() : { operatorKey: kind as 'and' | 'or', conditions: [leaf(), leaf()] });
}
function changeChild(index: number, value: WorkflowVoteResultRule): void {
  if (!('conditions' in props.modelValue)) return;
  emit('update:modelValue', { ...props.modelValue, conditions: props.modelValue.conditions.map((child, position) => position === index ? value : child) });
}
function addChild(): void {
  if ('conditions' in props.modelValue && props.modelValue.conditions.length < 8)
    emit('update:modelValue', { ...props.modelValue, conditions: [...props.modelValue.conditions, leaf()] });
}
function removeChild(index: number): void {
  if ('conditions' in props.modelValue && props.modelValue.conditions.length > 2)
    emit('update:modelValue', { ...props.modelValue, conditions: props.modelValue.conditions.filter((_, position) => position !== index) });
}
</script>
<template>
  <fieldset class="workflow-vote-rule">
    <legend>{{ t('workflowDesigner.votes.rule') }}</legend>
    <ElSelect :model-value="'operatorKey' in modelValue ? modelValue.operatorKey : 'metric'" :aria-label="t('workflowDesigner.votes.ruleKind')" @update:model-value="changeKind">
      <ElOption value="metric" :label="t('workflowDesigner.votes.metricCondition')" />
      <ElOption v-if="depth < 4" value="and" :label="t('workflowDesigner.votes.allConditions')" />
      <ElOption v-if="depth < 4" value="or" :label="t('workflowDesigner.votes.anyCondition')" />
    </ElSelect>
    <template v-if="'metricKey' in modelValue">
      <ElSelect :model-value="modelValue.metricKey" :aria-label="t('workflowDesigner.votes.metric')" @update:model-value="emit('update:modelValue', { ...modelValue, metricKey: $event })">
        <ElOption v-for="metric in (['eligible', 'participated', 'valid', 'yes', 'no', 'abstain'] as const)" :key="metric" :value="metric" :label="t(`workflowDesigner.votes.metrics.${metric}`)" />
      </ElSelect>
      <ElSelect :model-value="modelValue.comparisonKey" :aria-label="t('workflowDesigner.votes.comparison')" @update:model-value="emit('update:modelValue', { ...modelValue, comparisonKey: $event })">
        <ElOption v-for="operation in (['eq', 'gte', 'lte'] as const)" :key="operation" :value="operation" :label="t(`workflowDesigner.votes.comparisons.${operation}`)" />
      </ElSelect>
      <ElInputNumber :model-value="modelValue.value" :min="0" :max="20" :precision="0" :aria-label="t('workflowDesigner.votes.count')"
        @update:model-value="emit('update:modelValue', { ...modelValue, value: $event ?? 0 })" />
    </template>
    <template v-else>
      <div v-for="(child, index) in modelValue.conditions" :key="index">
        <WorkflowVoteRuleEditor :model-value="child" :depth="depth + 1" @update:model-value="changeChild(index, $event)" />
        <ElButton :disabled="modelValue.conditions.length <= 2" @click="removeChild(index)">{{ t('workflowDesigner.votes.removeCondition') }}</ElButton>
      </div>
      <ElButton :disabled="modelValue.conditions.length >= 8" @click="addChild">{{ t('workflowDesigner.votes.addCondition') }}</ElButton>
    </template>
  </fieldset>
</template>
<style scoped>
.workflow-vote-rule { display: grid; gap: .75rem; min-width: 0; border: 1px solid var(--el-border-color); border-radius: .5rem; padding: .75rem; }
.workflow-vote-rule legend { padding-inline: .25rem; }
</style>
