<script setup lang="ts">
import type {
  WorkflowFieldPolicies,
  WorkflowFormField,
  WorkflowFormSchema,
  WorkflowSubmission
} from '@fullnet/client-contracts';
import { ref, watch } from 'vue';
import {
  createWorkflowFormModel,
  type WorkflowFormState
} from './workflow-form-model';

const props = defineProps<{
  schema: WorkflowFormSchema;
  submission?: WorkflowSubmission;
  policies?: WorkflowFieldPolicies;
}>();

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

defineExpose({ validate });
</script>

<template>
  <view class="fullnet-workflow-form">
    <view
      v-for="section in state.sections"
      :key="section.sectionKey"
      class="fullnet-workflow-form__section"
    >
      <text class="fullnet-workflow-form__section-title">{{ section.sectionKey }}</text>

      <view
        v-for="field in section.fields"
        :key="field.fieldKey"
        class="fullnet-workflow-form__field"
      >
        <text class="fullnet-workflow-form__label">
          {{ field.fieldKey }}<text v-if="field.required" class="fullnet-workflow-form__required"> *</text>
        </text>

        <input
          v-if="field.fieldTypeKey === 'text'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          :maxlength="maximumLength(field, 140)"
          @input="updateField(field.fieldKey, $event)"
        />
        <textarea
          v-else-if="field.fieldTypeKey === 'textarea'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          :maxlength="maximumLength(field, 2000)"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          v-else-if="field.fieldTypeKey === 'integer'"
          type="number"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          v-else-if="field.fieldTypeKey === 'decimal'"
          type="digit"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <input
          v-else-if="field.fieldTypeKey === 'money'"
          type="digit"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <picker
          v-else-if="field.fieldTypeKey === 'date'"
          mode="date"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <picker
          v-else-if="field.fieldTypeKey === 'time'"
          mode="time"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <input
          v-else-if="field.fieldTypeKey === 'datetime'"
          :value="String(fieldValue(field.fieldKey) ?? '')"
          :disabled="field.readOnly"
          @input="updateField(field.fieldKey, $event)"
        />
        <radio-group
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
          v-else-if="field.fieldTypeKey === 'select'"
          :range="fieldOptions(field)"
          :disabled="field.readOnly"
          @change="updateSelect(field, $event)"
        >
          <view>{{ fieldValue(field.fieldKey) || '-' }}</view>
        </picker>
        <switch
          v-else-if="field.fieldTypeKey === 'switch'"
          :checked="fieldValue(field.fieldKey) === true"
          :disabled="field.readOnly"
          @change="updateField(field.fieldKey, $event)"
        />
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
</style>
