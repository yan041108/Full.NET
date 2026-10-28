<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElInput, ElInputNumber, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowConditionEditor from './WorkflowConditionEditor.vue';
import { readWorkflowRoutePolicy, type WorkflowRoutePolicy, type WorkflowRouteTarget } from './workflow-route';

const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; nodes: readonly { key: string; label: string }[]; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowRoutePolicy]; cancel: [] }>();
const { t } = useAdminI18n();
const mode = ref<WorkflowRoutePolicy['modeKey']>('rules'); const decisionField = ref('');
const targets = ref<WorkflowRouteTarget[]>([]);
watch(() => props.modelValue, value => {
  try { const policy = readWorkflowRoutePolicy(value); mode.value = policy.modeKey; decisionField.value = policy.decisionFieldKey ?? ''; targets.value = structuredClone(policy.targets) as WorkflowRouteTarget[]; }
  catch { mode.value = 'rules'; decisionField.value = ''; targets.value = []; }
}, { immediate: true });
const decisionFields = computed(() => props.fields.filter(field => field.fieldTypeKey === 'select'));
const policy = computed(() => {
  try {
    const value = readWorkflowRoutePolicy({ modeKey: mode.value, ...(mode.value === 'decision_field' ? { decisionFieldKey: decisionField.value } : {}),
      targets: targets.value.map(target => ({ targetKey: target.targetKey, nodeKey: target.nodeKey, regionKey: target.regionKey, maximumIterations: target.maximumIterations,
        ...(mode.value === 'rules' ? { condition: target.condition } : {}) })) });
    if (value.targets.some(target => !props.nodes.some(node => node.key === target.nodeKey)) ||
      mode.value === 'decision_field' && !decisionFields.value.some(field => field.fieldKey === decisionField.value)) return null;
    return value;
  } catch { return null; }
});
function add(): void {
  if (props.disabled || targets.value.length >= 8) return;
  targets.value.push({ targetKey: '', nodeKey: '', regionKey: '', maximumIterations: 3,
    condition: { fieldKey: props.fields[0]?.fieldKey ?? '', operator: 'isNotEmpty' } });
}
function update(index: number, patch: Partial<WorkflowRouteTarget>): void {
  if (props.disabled || !targets.value[index]) return;
  targets.value[index] = { ...targets.value[index]!, ...patch };
}
</script>
<template>
  <form class="route-editor" @submit.prevent="policy && !disabled && emit('save', policy)">
    <p class="route-hint">{{ t('workflowDesigner.route.help') }}</p>
    <label><span>{{ t('workflowDesigner.route.mode') }}</span><ElSelect v-model="mode" :disabled="disabled" :aria-label="t('workflowDesigner.route.mode')">
      <ElOption value="rules" :label="t('workflowDesigner.route.rules')" /><ElOption value="decision_field" :label="t('workflowDesigner.route.decisionField')" />
    </ElSelect></label>
    <label v-if="mode === 'decision_field'"><span>{{ t('workflowDesigner.route.decisionField') }}</span><ElSelect v-model="decisionField" :disabled="disabled" :aria-label="t('workflowDesigner.route.decisionField')">
      <ElOption v-for="field in decisionFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
    </ElSelect></label>
    <fieldset v-for="(target, index) in targets" :key="index" :disabled="disabled" class="route-target">
      <legend>{{ t('workflowDesigner.route.target') }} {{ index + 1 }}</legend>
      <label><span>{{ t('workflowDesigner.route.targetKey') }}</span><ElInput :model-value="target.targetKey" :maxlength="64" :disabled="disabled" @update:model-value="update(index, { targetKey: $event })" /></label>
      <label><span>{{ t('workflowDesigner.route.target') }}</span><ElSelect :model-value="target.nodeKey" :disabled="disabled" @update:model-value="update(index, { nodeKey: $event })">
        <ElOption v-for="node in nodes" :key="node.key" :value="node.key" :label="node.label" />
      </ElSelect></label>
      <div class="route-grid">
        <label><span>{{ t('workflowDesigner.route.regionKey') }}</span><ElInput :model-value="target.regionKey" :maxlength="64" :disabled="disabled" @update:model-value="update(index, { regionKey: $event })" /></label>
        <label><span>{{ t('workflowDesigner.route.iterations') }}</span><ElInputNumber :model-value="target.maximumIterations" :min="1" :max="32" :precision="0" :disabled="disabled" @update:model-value="update(index, { maximumIterations: $event ?? 1 })" /></label>
      </div>
      <WorkflowConditionEditor v-if="mode === 'rules'" :model-value="target.condition ?? { operator: 'isNotEmpty', fieldKey: fields[0]?.fieldKey ?? '' }" :fields="fields"
        @update:model-value="update(index, { condition: $event })" />
      <ElButton :disabled="disabled" @click="targets.splice(index, 1)">{{ t('workflowDesigner.route.remove') }}</ElButton>
    </fieldset>
    <ElButton :disabled="disabled || targets.length >= 8" @click="add">{{ t('workflowDesigner.route.add') }}</ElButton>
    <p v-if="!policy" role="status">{{ t('workflowDesigner.route.invalid') }}</p>
    <footer><ElButton :disabled="disabled" @click="emit('cancel')">{{ t('workflowDesigner.route.cancel') }}</ElButton>
      <ElButton type="primary" native-type="submit" :disabled="disabled || !policy" data-testid="workflow-save-route">{{ t('workflowDesigner.route.save') }}</ElButton></footer>
  </form>
</template>
<style scoped>
.route-editor, .route-target { display: grid; gap: 16px; min-width: 0; }
.route-editor label { display: grid; gap: 6px; }
.route-hint { margin: 0; color: var(--el-text-color-secondary); line-height: 1.7; }
.route-target { margin: 0; padding: 16px; border: 1px solid var(--el-border-color); border-left: 3px solid var(--el-color-primary); border-radius: 8px; }
.route-target legend { padding: 0 6px; font-weight: 600; }
.route-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 12px; }
footer { display: flex; justify-content: flex-end; gap: 8px; padding-top: 8px; }
@media (max-width: 600px) { .route-grid { grid-template-columns: 1fr; } }
</style>
