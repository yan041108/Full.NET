<script setup lang="ts">
import { computed } from 'vue';
import { ElButton, ElCheckbox, ElDatePicker, ElInput } from 'element-plus';
import type { WorkflowWorkCalendarDraft, WorkflowWorkCalendarRange } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowWorkCalendar } from './workflow-work-calendar';
const props = defineProps<{ modelValue: WorkflowWorkCalendarDraft; disabled: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowWorkCalendarDraft] }>();
const { t } = useAdminI18n();
const weekdays = [1, 2, 3, 4, 5, 6, 0] as const;
const valid = computed(() => { try { readWorkflowWorkCalendar(props.modelValue); return true; } catch { return false; } });
const rangesFor = (day: number) => props.modelValue.weekdays.find(item => item.dayOfWeek === day)?.ranges ?? [];
function setDay(day: number, enabled: unknown): void {
  if (props.disabled) return;
  const rows = props.modelValue.weekdays.filter(item => item.dayOfWeek !== day);
  if (enabled === true) rows.push({ dayOfWeek: day, ranges: [{ start: '09:00', end: '17:00' }] });
  emit('update:modelValue', { ...props.modelValue, weekdays: rows });
}
function changeRanges(day: number, transform: (rows: readonly WorkflowWorkCalendarRange[]) => WorkflowWorkCalendarRange[]): void {
  if (props.disabled) return;
  emit('update:modelValue', { ...props.modelValue, weekdays: props.modelValue.weekdays.map(item => item.dayOfWeek === day ? { ...item, ranges: transform(item.ranges) } : item) });
}
function setException(index: number, date: unknown): void {
  if (props.disabled) return;
  emit('update:modelValue', { ...props.modelValue, exceptions: (props.modelValue.exceptions ?? []).map((item, position) => position === index ? { ...item, date: String(date ?? '') } : item) });
}
function exceptionRanges(index: number, transform: (rows: readonly WorkflowWorkCalendarRange[]) => WorkflowWorkCalendarRange[]): void {
  if (props.disabled) return;
  emit('update:modelValue', { ...props.modelValue, exceptions: (props.modelValue.exceptions ?? []).map((item, position) => position === index ? { ...item, ranges: transform(item.ranges) } : item) });
}
function addException(): void {
  if (props.disabled || (props.modelValue.exceptions?.length ?? 0) >= 366) return;
  emit('update:modelValue', { ...props.modelValue, exceptions: [...props.modelValue.exceptions ?? [], { date: '', ranges: [] }] });
}
function removeException(index: number): void {
  if (props.disabled) return;emit('update:modelValue', { ...props.modelValue, exceptions: (props.modelValue.exceptions ?? []).filter((_, position) => position !== index) });
}
const change = (rows: readonly WorkflowWorkCalendarRange[], index: number, key: 'start' | 'end', value: unknown) => rows.map((row, position) => position === index ? { ...row, [key]: String(value ?? '') } : row);
const remove = (rows: readonly WorkflowWorkCalendarRange[], index: number) => rows.filter((_, position) => position !== index);
const add = (rows: readonly WorkflowWorkCalendarRange[]) => rows.length < 8 ? [...rows, { start: '', end: '' }] : [...rows];
</script>
<template>
  <div class="work-calendar-editor">
    <label>{{ t('workCalendars.zone') }}<ElInput :model-value="modelValue.timeZoneKey" :disabled="disabled" placeholder="Asia/Shanghai" data-testid="work-calendar-zone"
      @update:model-value="!disabled && emit('update:modelValue', { ...modelValue, timeZoneKey: String($event) })" /></label>
    <p class="work-calendar-editor__help">{{ t('workCalendars.editorHelp') }}</p>
    <section :aria-label="t('workCalendars.weekly')">
      <h3>{{ t('workCalendars.weekly') }}</h3>
      <fieldset v-for="day in weekdays" :key="day">
        <legend><ElCheckbox :model-value="rangesFor(day).length > 0" :disabled="disabled" @update:model-value="setDay(day, $event)">{{ t(`workCalendars.weekday${day}`) }}</ElCheckbox></legend>
        <div v-for="(range, index) in rangesFor(day)" :key="index" class="work-calendar-editor__range">
          <ElInput :model-value="range.start" :disabled="disabled" placeholder="09:00" :aria-label="`${t(`workCalendars.weekday${day}`)} ${index + 1} ${t('workCalendars.start')}`"
            @update:model-value="changeRanges(day, rows => change(rows, index, 'start', $event))" />
          <span aria-hidden="true">–</span>
          <ElInput :model-value="range.end" :disabled="disabled" placeholder="17:00" :aria-label="`${t(`workCalendars.weekday${day}`)} ${index + 1} ${t('workCalendars.end')}`"
            @update:model-value="changeRanges(day, rows => change(rows, index, 'end', $event))" />
          <ElButton :disabled="disabled" :aria-label="`${t('workCalendars.removeRange')} ${t(`workCalendars.weekday${day}`)} ${index + 1}`" @click="changeRanges(day, rows => remove(rows, index))">{{ t('workCalendars.removeRange') }}</ElButton>
        </div>
        <ElButton v-if="rangesFor(day).length" :disabled="disabled || rangesFor(day).length >= 8" @click="changeRanges(day, add)">{{ t('workCalendars.addRange') }}</ElButton>
      </fieldset>
    </section>
    <section :aria-label="t('workCalendars.exceptions')">
      <h3>{{ t('workCalendars.exceptions') }}</h3><p>{{ t('workCalendars.exceptionsHelp') }}</p>
      <fieldset v-for="(day, index) in modelValue.exceptions ?? []" :key="index">
        <legend>{{ t('workCalendars.exceptionDate') }} {{ index + 1 }}</legend>
        <div class="work-calendar-editor__exception">
          <ElDatePicker :model-value="day.date" type="date" value-format="YYYY-MM-DD" :disabled="disabled" :aria-label="`${t('workCalendars.exceptionDate')} ${index + 1}`" @update:model-value="setException(index, $event)" />
          <ElButton :disabled="disabled" @click="removeException(index)">{{ t('workCalendars.removeException') }}</ElButton>
        </div>
        <div v-for="(range, position) in day.ranges" :key="position" class="work-calendar-editor__range">
          <ElInput :model-value="range.start" :disabled="disabled" placeholder="09:00" :aria-label="`${day.date} ${position + 1} ${t('workCalendars.start')}`"
            @update:model-value="exceptionRanges(index, rows => change(rows, position, 'start', $event))" /><span aria-hidden="true">–</span>
          <ElInput :model-value="range.end" :disabled="disabled" placeholder="17:00" :aria-label="`${day.date} ${position + 1} ${t('workCalendars.end')}`"
            @update:model-value="exceptionRanges(index, rows => change(rows, position, 'end', $event))" />
          <ElButton :disabled="disabled" @click="exceptionRanges(index, rows => remove(rows, position))">{{ t('workCalendars.removeRange') }}</ElButton>
        </div>
        <ElButton :disabled="disabled || day.ranges.length >= 8" @click="exceptionRanges(index, add)">{{ t('workCalendars.addRange') }}</ElButton>
      </fieldset>
      <ElButton :disabled="disabled || (modelValue.exceptions?.length ?? 0) >= 366" data-testid="work-calendar-add-exception" @click="addException">{{ t('workCalendars.addException') }}</ElButton>
    </section>
    <p v-if="!valid" role="alert">{{ t('workCalendars.invalid') }}</p>
  </div>
</template>
<style scoped>
.work-calendar-editor { display: grid; gap: 16px; }
.work-calendar-editor > label { display: grid; gap: 8px; }
.work-calendar-editor h3 { margin: 0 0 12px; font-size: 15px; }
.work-calendar-editor fieldset { min-width: 0; margin: 0 0 12px; padding: 12px; border: 1px solid var(--el-border-color-light); border-radius: 6px; }
.work-calendar-editor__range { display: grid; grid-template-columns: minmax(80px, 1fr) auto minmax(80px, 1fr) auto; gap: 8px; align-items: center; margin-bottom: 8px; }
.work-calendar-editor__exception { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 12px; }
.work-calendar-editor__help { margin: 0; color: var(--el-text-color-secondary); }
@media (max-width: 600px) { .work-calendar-editor__range { grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); } .work-calendar-editor__range > :last-child { grid-column: 1 / -1; justify-self: end; } }
</style>
