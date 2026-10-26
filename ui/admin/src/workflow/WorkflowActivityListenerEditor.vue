<script setup lang="ts">
import { ElButton, ElInput, ElOption, ElSelect } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import type { WorkflowActivityListener } from './workflow-activity-listeners';
const props = defineProps<{ modelValue: WorkflowActivityListener[]; disabled: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowActivityListener[]] }>();
const { t } = useAdminI18n();
function update(index: number, patch: Partial<WorkflowActivityListener>) {
  if (!props.disabled) emit('update:modelValue', props.modelValue.map((row, position) => position === index ? { ...row, ...patch } : { ...row }));
}
function add() {
  if (props.disabled || props.modelValue.length >= 8) return;
  let sequence = 1; while (props.modelValue.some(row => row.listenerKey === `listener${sequence}`)) sequence++;
  emit('update:modelValue', [...props.modelValue, { listenerKey: `listener${sequence}`, eventKey: 'completed', actionKey: 'notify.initiator', actionVersion: 1 }]);
}
function remove(index: number) { if (!props.disabled) emit('update:modelValue', props.modelValue.filter((_, position) => position !== index)); }
</script>

<template>
  <section data-testid="workflow-activity-listeners">
    <h3>{{ t('workflowDesigner.listeners.title') }}</h3><p>{{ t('workflowDesigner.listeners.help') }}</p>
    <div v-for="(row, index) in modelValue" :key="index" class="workflow-listener-row">
      <el-input :model-value="row.listenerKey" :aria-label="t('workflowDesigner.listeners.key')" :disabled="disabled" :maxlength="64" @update:model-value="update(index, { listenerKey: String($event) })" />
      <el-select :model-value="row.eventKey" :aria-label="t('workflowDesigner.listeners.event')" :disabled="disabled" @update:model-value="update(index, { eventKey: $event })">
        <el-option value="completed" :label="t('workflowDesigner.listeners.completed')" /><el-option value="rejected" :label="t('workflowDesigner.listeners.rejected')" />
      </el-select>
      <span>{{ t('workflowDesigner.listeners.notifyInitiator') }}</span>
      <el-button :disabled="disabled" @click="remove(index)">{{ t('workflowDesigner.listeners.remove') }}</el-button>
    </div>
    <el-button data-testid="workflow-listener-add" :disabled="disabled || modelValue.length >= 8" @click="add">{{ t('workflowDesigner.listeners.add') }}</el-button>
  </section>
</template>

<style scoped>
.workflow-listener-row { display: grid; grid-template-columns: minmax(8rem, 1fr) minmax(8rem, 1fr); gap: .5rem; margin-block: .75rem; align-items: center; }
@media (max-width: 480px) { .workflow-listener-row { grid-template-columns: 1fr; } }
</style>
