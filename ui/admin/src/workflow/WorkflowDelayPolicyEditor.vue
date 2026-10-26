<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElInput, ElInputNumber, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowDelayPolicy, type WorkflowDelayPolicy } from './workflow-delay-policy';
import WorkflowWorkCalendarPicker from './WorkflowWorkCalendarPicker.vue';
const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowDelayPolicy]; cancel: [] }>();
const { t } = useAdminI18n();
const mode = ref<'relative' | 'absolute' | 'working'>('relative'); const zone = ref('UTC'); const duration = ref(1); const calendarVersion = ref('');
const unit = ref('minute'); const source = ref<'fixed' | 'field'>('fixed'); const at = ref(''); const field = ref('');
const dateFields = computed(() => props.fields.filter(item => item.fieldTypeKey === 'datetime'));
watch(() => props.modelValue, value => {
  try {
    const policy = readWorkflowDelayPolicy(value ?? { modeKey: 'relative', timeZoneKey: 'UTC', duration: 1, unitKey: 'minute' });
    mode.value = policy.modeKey; zone.value = policy.timeZoneKey;
    calendarVersion.value = policy.modeKey === 'working' ? policy.calendarVersionId : '';
    if (policy.modeKey === 'relative' || policy.modeKey === 'working') { duration.value = policy.duration; unit.value = policy.unitKey; }
    else { source.value = policy.fieldKey === undefined ? 'fixed' : 'field'; at.value = policy.at ?? ''; field.value = policy.fieldKey ?? ''; }
  } catch { mode.value = 'relative'; duration.value = 0; calendarVersion.value = ''; }
}, { immediate: true });
const policy = computed(() => {
  try {
    const value = readWorkflowDelayPolicy({ modeKey: mode.value, timeZoneKey: zone.value,
      ...(mode.value === 'relative' || mode.value === 'working' ? { duration: duration.value, unitKey: unit.value,
        ...(mode.value === 'working' ? { calendarVersionId: calendarVersion.value } : {}) } : source.value === 'fixed' ? { at: at.value } : { fieldKey: field.value }) });
    if (value.modeKey === 'absolute' && value.fieldKey !== undefined && !dateFields.value.some(item => item.fieldKey === value.fieldKey)) return undefined;
    return value;
  } catch { return undefined; }
});
</script>
<template>
  <div class="workflow-delay-editor">
    <p>{{ t('workflowDesigner.delay.help') }}</p>
    <label>{{ t('workflowDesigner.delay.mode') }}<ElSelect v-model="mode" :disabled="disabled" data-testid="delay-mode">
      <ElOption v-for="key in (['relative', 'absolute', 'working'] as const)" :key="key" :value="key" :label="t(`workflowDesigner.delay.${key}`)" />
    </ElSelect></label>
    <WorkflowWorkCalendarPicker v-if="mode === 'working'" :model-value="calendarVersion" :disabled="disabled" @select="value => { calendarVersion = value?.id ?? ''; if (value) zone = value.timeZoneKey; }" />
    <p v-if="mode === 'working'">{{ t('workCalendars.workingHelp') }}</p>
    <label>{{ t('workflowDesigner.delay.zone') }}<ElInput v-model="zone" :disabled="disabled || mode === 'working'" data-testid="delay-zone" /></label>
    <template v-if="mode === 'relative' || mode === 'working'">
      <label>{{ t('workflowDesigner.delay.duration') }}<ElInputNumber v-model="duration" :min="1" :max="31622400" :precision="0" :disabled="disabled" data-testid="delay-duration" /></label>
      <ElSelect v-model="unit" :disabled="disabled" :aria-label="t('workflowDesigner.delay.unit')" data-testid="delay-unit">
        <ElOption v-for="key in (['second', 'minute', 'hour', 'day'] as const)" :key="key" :value="key" :label="mode === 'working' && key === 'day' ? t('workCalendars.workingDayUnit') : t(`workflowDesigner.delay.${key}`)" />
      </ElSelect>
    </template>
    <template v-else>
      <ElSelect v-model="source" :disabled="disabled" :aria-label="t('workflowDesigner.delay.source')" data-testid="delay-source">
        <ElOption value="fixed" :label="t('workflowDesigner.delay.fixed')" /><ElOption value="field" :label="t('workflowDesigner.delay.field')" />
      </ElSelect>
      <label v-if="source === 'fixed'">{{ t('workflowDesigner.delay.at') }}<ElInput v-model="at" :disabled="disabled" placeholder="2026-10-26T08:00:00+08:00" data-testid="delay-at" /></label>
      <ElSelect v-else v-model="field" :disabled="disabled" :aria-label="t('workflowDesigner.delay.field')" data-testid="delay-field">
        <ElOption v-for="item in dateFields" :key="item.fieldKey" :value="item.fieldKey" :label="item.fieldKey" />
      </ElSelect>
    </template>
    <p v-if="!policy" role="alert">{{ t('workflowDesigner.delay.invalid') }}</p>
    <div><ElButton :disabled="disabled || !policy" data-testid="delay-save" @click="policy && emit('save', policy)">{{ t('workflowDesigner.delay.save') }}</ElButton>
      <ElButton @click="emit('cancel')">{{ t('common.cancel') }}</ElButton></div>
  </div>
</template>
<style scoped>
.workflow-delay-editor { display: grid; gap: 16px; }
.workflow-delay-editor label { display: grid; gap: 8px; }
</style>
