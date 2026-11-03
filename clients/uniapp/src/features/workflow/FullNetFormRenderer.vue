<script setup lang="ts">
import type {
  WorkflowFieldPolicies,
  WorkflowFormField,
  WorkflowFormSchema,
  WorkflowSubmission
} from '@fullnet/client-contracts';
import { readWorkflowSubtableConstraints } from '@fullnet/client-contracts';
import { useI18n } from 'vue-i18n';
import { onBeforeUnmount, ref, watch } from 'vue';
// #ifdef H5
import { useWorkflowControlAccessibility } from './workflow-control-accessibility';
// #endif
import {
  createWorkflowFormModel,
  type WorkflowFormState
} from './workflow-form-model';

const props = defineProps<{
  schema: WorkflowFormSchema;
  submission?: WorkflowSubmission;
  policies?: WorkflowFieldPolicies;
  attachmentNames?: Readonly<Record<string, string>> | null;
  attachmentCandidates?: readonly { fileId: string; name: string }[];
  uploadAttachment?: (fieldKey: string) => Promise<{ fileId: string; fileName: string } | undefined>;
  downloadAttachment?: (fileId: string, name: string) => Promise<void>;
}>();
const { t } = useI18n();
const formRoot = ref<unknown>();
// #ifdef H5
useWorkflowControlAccessibility(formRoot);
// #endif
const uploading = ref(false);
const localNames = ref<Record<string, string>>({});
let contextEpoch = 0;
onBeforeUnmount(() => { contextEpoch++; });

const emit = defineEmits<{
  'update:patch': [patch: WorkflowSubmission];
  validate: [errors: Readonly<Record<string, 'required'>>];
}>();

let model = createWorkflowFormModel(
  props.schema,
  props.submission ?? {},
  props.policies ?? {}
);
const state = ref<WorkflowFormState>(model.state);

watch(
  () => [props.schema, props.submission, props.policies] as const,
  () => {
    contextEpoch++;
    uploading.value = false;
    localNames.value = {};
    model = createWorkflowFormModel(
      props.schema,
      props.submission ?? {},
      props.policies ?? {}
    );
    state.value = model.state;
    emit('update:patch', state.value.patch);
  },
  { deep: true }
);

function updateField(fieldKey: string, event: unknown): void {
  if (!model.updateValue(fieldKey, readEventValue(event))) {
    return;
  }
  state.value = model.state;
  emit('update:patch', state.value.patch);
}

function updateSelect(field: WorkflowFormField, event: unknown): void {
  const index = Number(readEventValue(event));
  const options = fieldOptions(field);
  if (Number.isInteger(index) && options[index] !== undefined) {
    updateField(field.fieldKey, { detail: { value: options[index] } });
  }
}

function rows(field: WorkflowFormField): readonly Record<string, unknown>[] {
  const value = fieldValue(field.fieldKey);
  return Array.isArray(value) ? value : [];
}
function rowSchema(field: WorkflowFormField): WorkflowFormSchema {
  const table = readWorkflowSubtableConstraints(field)!;
  return { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: field.fieldKey,
    fields: table.columns.map(column => ({ ...column, fieldKey: column.columnKey })) }] };
}
function rowPolicies(field: WorkflowFormField, readOnly: boolean): WorkflowFieldPolicies {
  return Object.fromEntries((readWorkflowSubtableConstraints(field)?.columns ?? []).map(column =>
    [column.columnKey, readOnly ? 'readOnly' : column.required ? 'required' : 'editable']));
}
function updateRow(field: WorkflowFormField, index: number, patch: WorkflowSubmission): void {
  if (Object.keys(patch).length === 0) return;
  updateField(field.fieldKey, { detail: { value: rows(field).map((row, current) => current === index ? { ...row, ...patch } : row) } });
}
function changeRows(field: WorkflowFormField, index?: number): void {
  const value = [...rows(field)];
  if (index === undefined) value.push({}); else value.splice(index, 1);
  updateField(field.fieldKey, { detail: { value } });
}
function attachments(field: WorkflowFormField): readonly string[] {
  const value = fieldValue(field.fieldKey);
  return Array.isArray(value) ? value : [];
}
function attachmentName(id: string): string { return localNames.value[id] ?? props.attachmentNames?.[id] ?? t('workflow.form.attachment'); }
function removeAttachment(field: WorkflowFormField, id: string): void {
  updateField(field.fieldKey, { detail: { value: attachments(field).filter(value => value !== id) } });
}
function reuseAttachment(field: WorkflowFormField, id: string): void {
  if (!props.attachmentCandidates?.some(candidate => candidate.fileId === id) || attachments(field).includes(id)) return;
  updateField(field.fieldKey, { detail: { value: [...attachments(field), id] } });
}
async function upload(field: WorkflowFormField): Promise<void> {
  if (!props.uploadAttachment || uploading.value) return;
  const epoch = contextEpoch;
  uploading.value = true;
  try {
    const result = await props.uploadAttachment(field.fieldKey);
    if (!result || epoch !== contextEpoch) return;
    if (model.updateValue(field.fieldKey, [...attachments(field), result.fileId])) {
      localNames.value[result.fileId] = result.fileName;
      state.value = model.state;
      emit('update:patch', state.value.patch);
    }
  } finally { if (epoch === contextEpoch) uploading.value = false; }
}

function validate(): Readonly<Record<string, 'required'>> {
  const errors = model.validate();
  emit('validate', errors);
  return errors;
}

function readEventValue(event: unknown): unknown {
  if (typeof event !== 'object' || event === null || !('detail' in event)) {
    return undefined;
  }
  const detail = (event as { readonly detail?: unknown }).detail;
  return typeof detail === 'object' && detail !== null && 'value' in detail
    ? (detail as { readonly value?: unknown }).value
    : undefined;
}

function fieldOptions(field: WorkflowFormField): readonly string[] {
  const options = field.constraints.options;
  return Array.isArray(options)
    ? options.filter((option): option is string => typeof option === 'string')
    : [];
}

function maximumLength(field: WorkflowFormField, fallback: number): number {
  const value = field.constraints.maxLength;
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0
    ? value
    : fallback;
}

function fieldValue(fieldKey: string): unknown {
  return state.value.values[fieldKey];
}

function isOptionSelected(fieldKey: string, option: string): boolean {
  const value = fieldValue(fieldKey);
  return Array.isArray(value) && value.includes(option);
}

defineExpose({ validate, isUploading: () => uploading.value });
</script>

<template>
  <view ref="formRoot" class="fullnet-workflow-form">
    <view
      v-for="section in state.sections"
      :key="section.sectionKey"
      class="fullnet-workflow-form__section"
    >
      <text class="fullnet-workflow-form__section-title">{{ section.sectionKey }}</text>

      <view
        v-for="field in section.fields"
        :key="field.fieldKey"
        :data-field-key="field.fieldKey"
        class="fullnet-workflow-form__field"
      >
        <text class="fullnet-workflow-form__label">
          {{ field.fieldKey }}<text v-if="field.required" class="fullnet-workflow-form__required"> *</text>
        </text>

        <input
          :aria-label="field.fieldKey"
          v-if="field.fieldTypeKey === 'text'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          :maxlength="maximumLength(field, 140)"
          @input="updateField(field.fieldKey, $event)"
        />
        <textarea
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'textarea'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          :maxlength="maximumLength(field, 2000)"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'integer'"
          type="number"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'decimal'"
          type="digit"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'money'"
          type="digit"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <picker
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'date'"
          mode="date"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <picker
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'time'"
          mode="time"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <input
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'datetime'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <radio-group
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'radio'"
          @change="updateField(field.fieldKey, $event)"
        >
          <label v-for="option in fieldOptions(field)" :key="option">
            <radio
              :value="option"
              :checked="fieldValue(field.fieldKey) === option"
              :disabled="field.readOnly"
            />
            <text>{{ option }}</text>
          </label>
        </radio-group>
        <checkbox-group
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'checkbox'"
          @change="updateField(field.fieldKey, $event)"
        >
          <label v-for="option in fieldOptions(field)" :key="option">
            <checkbox
              :value="option"
              :checked="isOptionSelected(field.fieldKey, option)"
              :disabled="field.readOnly"
            />
            <text>{{ option }}</text>
          </label>
        </checkbox-group>
        <picker
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'select'"
          :range="fieldOptions(field)"
          :disabled="field.readOnly"
          @change="updateSelect(field, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <switch
          :aria-label="field.fieldKey"
          v-else-if="field.fieldTypeKey === 'switch'"
          :checked="fieldValue(field.fieldKey) === true"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        />
        <view v-else-if="field.fieldTypeKey === 'attachment'" class="fullnet-workflow-form__files">
          <view v-for="id in attachments(field)" :key="id" class="fullnet-workflow-form__file">
            <button size="mini" :disabled="!downloadAttachment" @click="downloadAttachment?.(id, attachmentName(id))">{{ attachmentName(id) }}</button>
            <button v-if="!field.readOnly" size="mini" @click="removeAttachment(field, id)">{{ t('workflow.form.remove') }}</button>
          </view>
          <button v-if="!field.readOnly && uploadAttachment" size="mini"
            :disabled="uploading || attachments(field).length >= Number(field.constraints.maxCount)" @click="upload(field)">{{ t('workflow.form.upload') }}</button>
          <template v-if="!field.readOnly">
            <button v-for="candidate in attachmentCandidates" :key="candidate.fileId" size="mini"
              :data-reuse-file-id="candidate.fileId" :disabled="attachments(field).includes(candidate.fileId) || attachments(field).length >= Number(field.constraints.maxCount)"
              @click="reuseAttachment(field, candidate.fileId)">{{ t('workflow.form.reuse', { name: candidate.name }) }}</button>
          </template>
        </view>
        <view v-else-if="field.fieldTypeKey === 'subtable'" class="fullnet-workflow-form__rows">
          <view v-for="(row, index) in rows(field)" :key="index" :data-row-index="index" class="fullnet-workflow-form__row">
            <FullNetFormRenderer :schema="rowSchema(field)" :submission="row" :policies="rowPolicies(field, field.readOnly)"
              @update:patch="updateRow(field, index, $event)" />
            <button v-if="!field.readOnly" size="mini" @click="changeRows(field, index)">{{ t('workflow.form.removeRow') }}</button>
          </view>
          <button v-if="!field.readOnly" size="mini" :disabled="rows(field).length >= (readWorkflowSubtableConstraints(field)?.maxRows ?? 0)"
            @click="changeRows(field)">{{ t('workflow.form.addRow') }}</button>
        </view>
      </view>
    </view>
  </view>
</template>

<style scoped lang="scss">
.fullnet-workflow-form {
  display: flex;
  flex-direction: column;
  gap: 24rpx;
}

.fullnet-workflow-form__section,
.fullnet-workflow-form__field {
  display: flex;
  flex-direction: column;
  gap: 12rpx;
}

.fullnet-workflow-form__section-title {
  font-size: 32rpx;
  font-weight: 600;
}

.fullnet-workflow-form__label {
  font-size: 28rpx;
}

.fullnet-workflow-form__required {
  color: #d03050;
}
.fullnet-workflow-form__file { display: flex; flex-wrap: wrap; gap: 12rpx; }
.fullnet-workflow-form__file button { max-width: 100%; overflow-wrap: anywhere; }
.fullnet-workflow-form__row { border: 1px solid #dcdfe6; padding: 16rpx; margin-bottom: 16rpx; }
</style>
