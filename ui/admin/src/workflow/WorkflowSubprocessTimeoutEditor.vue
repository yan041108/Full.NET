<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElInputNumber, ElOption, ElSelect, ElSwitch } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowSubprocessTimeout, type WorkflowSubprocessTimeoutPolicy } from './workflow-subprocess';
const props = defineProps<{ modelValue?: WorkflowSubprocessTimeoutPolicy | null; targets: readonly { key: string; label: string }[]; disabled: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowSubprocessTimeoutPolicy | null | undefined] }>();
const { t } = useAdminI18n(); const enabled = ref(false); const minutes = ref(1440); const pause = ref('freeze'); const action = ref('continue'); const next = ref('');
watch(() => JSON.stringify(props.modelValue), () => {
  if (props.modelValue === null) return; enabled.value = props.modelValue !== undefined;
  minutes.value = props.modelValue?.dueAfterMinutes ?? 1440; pause.value = props.modelValue?.pauseModeKey ?? 'freeze';
  action.value = props.modelValue?.actionKey ?? 'continue'; next.value = props.modelValue?.nextNodeKey ?? '';
}, { immediate: true });
const value = computed(() => {
  if (!enabled.value) return undefined;
  if (action.value === 'route' && !props.targets.some(item => item.key === next.value)) return null;
  try { return readWorkflowSubprocessTimeout({ dueAfterMinutes: minutes.value, pauseModeKey: pause.value, actionKey: action.value, ...(action.value === 'route' ? { nextNodeKey: next.value } : {}) }); } catch { return null; }
});
watch(value, result => { if (JSON.stringify(result) !== JSON.stringify(props.modelValue)) emit('update:modelValue', result); }, { immediate: true });
</script>
<template>
  <fieldset class="child-timeout" :disabled="disabled">
    <legend>{{ t('workflowDesigner.childTimeout.title') }}</legend>
    <ElSwitch v-model="enabled" :disabled="disabled" :aria-label="t('workflowDesigner.childTimeout.title')" data-testid="subprocess-timeout-enabled" />
    <template v-if="enabled">
      <p>{{ t('workflowDesigner.childTimeout.help') }}</p>
      <label>{{ t('workflowDesigner.childTimeout.minutes') }}<ElInputNumber v-model="minutes" :min="1" :max="525600" :disabled="disabled" data-testid="subprocess-timeout-minutes" /></label>
      <label>{{ t('workflowDesigner.childTimeout.pause') }}<ElSelect v-model="pause" :disabled="disabled" :aria-label="t('workflowDesigner.childTimeout.pause')"><ElOption v-for="mode in ['freeze', 'elapsed']" :key="mode" :value="mode" :label="t(`workflowDesigner.childTimeout.${mode}`)" /></ElSelect></label>
      <label>{{ t('workflowDesigner.childTimeout.action') }}<ElSelect v-model="action" :disabled="disabled" :aria-label="t('workflowDesigner.childTimeout.action')"><ElOption v-for="mode in ['continue', 'route']" :key="mode" :value="mode" :label="t(`workflowDesigner.childTimeout.${mode}`)" /></ElSelect></label>
      <label v-if="action === 'route'">{{ t('workflowDesigner.childTimeout.target') }}<ElSelect v-model="next" :disabled="disabled" :aria-label="t('workflowDesigner.childTimeout.target')"><ElOption v-for="item in targets" :key="item.key" :value="item.key" :label="item.label" /></ElSelect></label>
    </template>
  </fieldset>
</template>
<style scoped>
.child-timeout { display: grid; gap: 14px; border: 1px solid var(--el-border-color); border-radius: 6px; padding: 16px; }
.child-timeout label { display: grid; grid-template-columns: minmax(110px, 1fr) minmax(160px, 2fr); gap: 12px; align-items: center; }
.child-timeout p { margin: 0; color: var(--el-text-color-secondary); line-height: 1.6; }
@media (max-width: 480px) { .child-timeout label { grid-template-columns: 1fr; } }
</style>
