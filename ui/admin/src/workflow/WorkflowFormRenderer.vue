<script setup lang="ts">
import { reactive, watch } from 'vue';
import type {
  WorkflowFieldPolicy,
  WorkflowFormField,
  WorkflowFormSchema,
  WorkflowSubmission
} from '@fullnet/client-contracts';

const props = defineProps<{
  schema: WorkflowFormSchema;
  submission: WorkflowSubmission;
  fieldPolicies: Readonly<Record<string, WorkflowFieldPolicy>>;
}>();

const emit = defineEmits<{
  'update:patch': [patch: WorkflowSubmission];
}>();

const values = reactive<Record<string, unknown>>({});
const patch = reactive<Record<string, unknown>>({});

watch(
  () => props.submission,
  submission => {
    replaceRecord(values, submission);
    replaceRecord(patch, {});
    emit('update:patch', {});
  },
  { immediate: true }
);

function policyFor(field: WorkflowFormField): WorkflowFieldPolicy {
  return props.fieldPolicies[field.fieldKey]
    ?? (field.required ? 'required' : 'editable');
}

function isReadOnly(field: WorkflowFormField): boolean {
  return policyFor(field) === 'readOnly';
}

function isRequired(field: WorkflowFormField): boolean {
  return policyFor(field) === 'required';
}

function optionsFor(field: WorkflowFormField): readonly string[] {
  const options = field.constraints.options;
  return Array.isArray(options) && options.every(item => typeof item === 'string')
    ? options
    : [];
}

function updateValue(field: WorkflowFormField, rawValue: unknown): void {
  if (isReadOnly(field) || policyFor(field) === 'hidden') {
    return;
  }

  const value = field.fieldTypeKey === 'integer'
    ? parseInteger(rawValue)
    : rawValue;
  values[field.fieldKey] = value;
  patch[field.fieldKey] = value;
  emit('update:patch', { ...patch });
}

function updateCheckbox(field: WorkflowFormField, option: string, checked: boolean): void {
  const current = stringArrayValue(field.fieldKey);
  updateValue(
    field,
    checked
      ? [...new Set([...current, option])]
      : current.filter(item => item !== option)
  );
}

function stringArrayValue(fieldKey: string): string[] {
  const value = values[fieldKey];
  return Array.isArray(value)
    ? value.filter((item: unknown): item is string => typeof item === 'string')
    : [];
}

function parseInteger(value: unknown): number | null {
  const parsed = Number.parseInt(String(value), 10);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function replaceRecord(target: Record<string, unknown>, source: WorkflowSubmission): void {
  for (const key of Object.keys(target)) {
    delete target[key];
  }
  Object.assign(target, source);
}
</script>

<template>
  <div class="workflow-form" data-testid="workflow-form-renderer">
    <section
      v-for="section in schema.sections"
      :key="section.sectionKey"
      class="workflow-form__section"
    >
      <h3 class="workflow-form__section-title" translate="no">{{ section.sectionKey }}</h3>

      <template v-for="field in section.fields" :key="field.fieldKey">
        <label
          v-if="policyFor(field) !== 'hidden'"
          class="workflow-form__field"
          :data-field-key="field.fieldKey"
        >
          <span class="workflow-form__label" translate="no">
            {{ field.fieldKey }}
            <span v-if="isRequired(field)" aria-hidden="true">*</span>
          </span>

          <textarea
            v-if="field.fieldTypeKey === 'textarea'"
            :value="String(values[field.fieldKey] ?? '')"
            :readonly="isReadOnly(field)"
            :required="isRequired(field)"
            rows="4"
            @input="updateValue(field, ($event.target as HTMLTextAreaElement).value)"
          />

          <select
            v-else-if="field.fieldTypeKey === 'select'"
            :value="String(values[field.fieldKey] ?? '')"
            :disabled="isReadOnly(field)"
            :required="isRequired(field)"
            @change="updateValue(field, ($event.target as HTMLSelectElement).value)"
          >
            <option value=""></option>
            <option v-for="option in optionsFor(field)" :key="option" :value="option">
              {{ option }}
            </option>
          </select>

          <span v-else-if="field.fieldTypeKey === 'radio'" class="workflow-form__choices">
            <label v-for="option in optionsFor(field)" :key="option">
              <input
                type="radio"
                :name="field.fieldKey"
                :value="option"
                :checked="values[field.fieldKey] === option"
                :disabled="isReadOnly(field)"
                @change="updateValue(field, option)"
              />
              {{ option }}
            </label>
          </span>

          <span v-else-if="field.fieldTypeKey === 'checkbox'" class="workflow-form__choices">
            <label v-for="option in optionsFor(field)" :key="option">
              <input
                type="checkbox"
                :value="option"
                :checked="stringArrayValue(field.fieldKey).includes(option)"
                :disabled="isReadOnly(field)"
                @change="updateCheckbox(field, option, ($event.target as HTMLInputElement).checked)"
              />
              {{ option }}
            </label>
          </span>

          <input
            v-else-if="field.fieldTypeKey === 'switch'"
            type="checkbox"
            :checked="values[field.fieldKey] === true"
            :disabled="isReadOnly(field)"
            @change="updateValue(field, ($event.target as HTMLInputElement).checked)"
          />

          <input
            v-else
            :type="field.fieldTypeKey === 'integer' ? 'number' : field.fieldTypeKey === 'date' ? 'date' : field.fieldTypeKey === 'time' ? 'time' : field.fieldTypeKey === 'datetime' ? 'datetime-local' : 'text'"
            :inputmode="field.fieldTypeKey === 'money' || field.fieldTypeKey === 'decimal' ? 'decimal' : undefined"
            :value="values[field.fieldKey] ?? ''"
            :readonly="isReadOnly(field)"
            :required="isRequired(field)"
            @input="updateValue(field, ($event.target as HTMLInputElement).value)"
          />
        </label>
      </template>
    </section>
  </div>
</template>

<style scoped>
.workflow-form {
  display: grid;
  gap: 1rem;
}

.workflow-form__section {
  display: grid;
  gap: 0.875rem;
  padding: 1rem;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 12px;
  background: var(--el-fill-color-blank);
}

.workflow-form__section-title {
  margin: 0;
  color: var(--el-text-color-primary);
  font-size: 0.95rem;
}

.workflow-form__field {
  display: grid;
  gap: 0.4rem;
}

.workflow-form__label {
  color: var(--el-text-color-regular);
  font-size: 0.82rem;
  font-weight: 650;
}

.workflow-form input:not([type='checkbox'], [type='radio']),
.workflow-form textarea,
.workflow-form select {
  min-height: 38px;
  padding: 0.5rem 0.7rem;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  color: var(--el-text-color-primary);
  background: var(--el-bg-color);
  font: inherit;
}

.workflow-form input[readonly],
.workflow-form textarea[readonly],
.workflow-form select:disabled {
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-light);
}

.workflow-form__choices {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}
</style>
