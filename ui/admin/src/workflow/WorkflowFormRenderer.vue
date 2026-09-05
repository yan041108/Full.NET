<script setup lang="ts">
import { ref } from 'vue';
import { reactive, watch } from 'vue';
import type {
  WorkflowFieldPolicy,
  WorkflowFormField,
  WorkflowFormSchema,
  WorkflowSubmission,
  WorkflowSubtableColumnDefinition
} from '@fullnet/client-contracts';
import {
  readSubtableRows,
  readWorkflowSubtableConstraints
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  downloadWorkflowFormAttachment,
  openWorkflowFormAttachmentBlob,
  uploadWorkflowFormAttachment
} from './workflow-form-attachments';

const props = defineProps<{
  schema: WorkflowFormSchema;
  submission: WorkflowSubmission;
  fieldPolicies: Readonly<Record<string, WorkflowFieldPolicy>>;
  instanceId?: string;
}>();

const emit = defineEmits<{
  'update:patch': [patch: WorkflowSubmission];
}>();

const { t } = useAdminI18n();
const values = reactive<Record<string, unknown>>({});
const patch = reactive<Record<string, unknown>>({});
const uploadingFieldKey = ref<string>();
const attachmentError = ref<string>();

watch(
  () => props.submission,
  submission => {
    replaceRecord(values, submission);
    replaceRecord(patch, {});
    attachmentError.value = undefined;
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

function attachmentMaxCount(field: WorkflowFormField): number {
  const value = field.constraints.maxCount;
  return Number.isSafeInteger(value) ? Number(value) : 1;
}

function attachmentExtensions(field: WorkflowFormField): readonly string[] {
  const value = field.constraints.allowedExtensions;
  return Array.isArray(value) && value.every(item => typeof item === 'string')
    ? value
    : [];
}

function textLengthConstraint(
  field: WorkflowFormField,
  key: 'minLength' | 'maxLength'
): number | undefined {
  if (field.fieldTypeKey !== 'text' && field.fieldTypeKey !== 'textarea') {
    return undefined;
  }

  const value = field.constraints[key];
  return Number.isSafeInteger(value) && Number(value) >= 0
    ? Number(value)
    : undefined;
}

function integerRangeConstraint(
  field: WorkflowFormField,
  key: 'minimum' | 'maximum'
): number | undefined {
  if (field.fieldTypeKey !== 'integer') {
    return undefined;
  }

  const value = field.constraints[key];
  return Number.isSafeInteger(value) ? Number(value) : undefined;
}

function inputValue(field: WorkflowFormField): unknown {
  const value = values[field.fieldKey] ?? '';
  if (field.fieldTypeKey !== 'datetime' || typeof value !== 'string' || value.length === 0) {
    return value;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (part: number) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
    + `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function updateValue(field: WorkflowFormField, rawValue: unknown): void {
  if (isReadOnly(field) || policyFor(field) === 'hidden') {
    return;
  }

  const value = field.fieldTypeKey === 'integer'
    ? parseInteger(rawValue)
    : field.fieldTypeKey === 'datetime'
      ? parseDateTime(rawValue)
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

async function onAttachmentSelected(field: WorkflowFormField, event: Event): Promise<void> {
  if (isReadOnly(field)) {
    return;
  }

  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (file === undefined) {
    return;
  }

  const extension = file.name.includes('.')
    ? file.name.split('.').pop()?.toLowerCase() ?? ''
    : '';
  if (!attachmentExtensions(field).includes(extension)) {
    attachmentError.value = t('workflowForms.attachment.extensionRejected');
    return;
  }

  const current = stringArrayValue(field.fieldKey);
  if (current.length >= attachmentMaxCount(field)) {
    attachmentError.value = t('workflowForms.attachment.maxCountReached');
    return;
  }

  uploadingFieldKey.value = field.fieldKey;
  attachmentError.value = undefined;
  try {
    const fileId = await uploadWorkflowFormAttachment(file);
    updateValue(field, [...current, fileId]);
  } catch {
    attachmentError.value = t('workflowForms.attachment.uploadFailed');
  } finally {
    uploadingFieldKey.value = undefined;
  }
}

function removeAttachment(field: WorkflowFormField, fileId: string): void {
  updateValue(
    field,
    stringArrayValue(field.fieldKey).filter(item => item !== fileId)
  );
}

async function openAttachment(fileId: string): Promise<void> {
  if (props.instanceId === undefined) {
    return;
  }

  try {
    const blob = await downloadWorkflowFormAttachment(props.instanceId, fileId);
    openWorkflowFormAttachmentBlob(blob);
  } catch {
    attachmentError.value = t('workflowForms.attachment.downloadFailed');
  }
}

function subtableColumns(field: WorkflowFormField): readonly WorkflowSubtableColumnDefinition[] {
  return readWorkflowSubtableConstraints(field)?.columns ?? [];
}

function subtableMaxRows(field: WorkflowFormField): number {
  return readWorkflowSubtableConstraints(field)?.maxRows ?? 1;
}

function subtableRows(field: WorkflowFormField): ReadonlyArray<Readonly<Record<string, unknown>>> {
  return readSubtableRows(field.fieldKey, values);
}

function subtableCellValue(
  field: WorkflowFormField,
  rowIndex: number,
  columnKey: string
): unknown {
  const row = subtableRows(field)[rowIndex];
  return row?.[columnKey] ?? '';
}

function columnOptions(column: WorkflowSubtableColumnDefinition): readonly string[] {
  const options = column.constraints.options;
  return Array.isArray(options) && options.every(item => typeof item === 'string')
    ? options
    : [];
}

function updateSubtableCell(
  field: WorkflowFormField,
  rowIndex: number,
  columnKey: string,
  column: WorkflowSubtableColumnDefinition,
  rawValue: unknown
): void {
  const rows = subtableRows(field).map(row => ({ ...row }));
  const value = column.fieldTypeKey === 'integer'
    ? parseInteger(rawValue)
    : column.fieldTypeKey === 'datetime'
      ? parseDateTime(rawValue)
      : rawValue;
  rows[rowIndex] = { ...rows[rowIndex], [columnKey]: value };
  updateValue(field, rows);
}

function updateSubtableCheckbox(
  field: WorkflowFormField,
  rowIndex: number,
  columnKey: string,
  option: string,
  checked: boolean
): void {
  const current = subtableCheckboxValue(field, rowIndex, columnKey);
  updateSubtableCell(
    field,
    rowIndex,
    columnKey,
    { columnKey, fieldTypeKey: 'checkbox', required: false, constraints: {} },
    checked ? [...new Set([...current, option])] : current.filter(item => item !== option)
  );
}

function subtableCheckboxValue(field: WorkflowFormField, rowIndex: number, columnKey: string): string[] {
  const value = subtableCellValue(field, rowIndex, columnKey);
  return Array.isArray(value)
    ? value.filter((item: unknown): item is string => typeof item === 'string')
    : [];
}

function addSubtableRow(field: WorkflowFormField): void {
  if (isReadOnly(field) || subtableRows(field).length >= subtableMaxRows(field)) {
    return;
  }

  const row: Record<string, unknown> = {};
  for (const column of subtableColumns(field)) {
    if (column.fieldTypeKey === 'checkbox') {
      row[column.columnKey] = [];
    } else if (column.fieldTypeKey === 'switch') {
      row[column.columnKey] = false;
    }
  }
  updateValue(field, [...subtableRows(field), row]);
}

function removeSubtableRow(field: WorkflowFormField, rowIndex: number): void {
  if (isReadOnly(field)) {
    return;
  }

  updateValue(
    field,
    subtableRows(field).filter((_, index) => index !== rowIndex)
  );
}

function parseInteger(value: unknown): number | null {
  const parsed = Number.parseInt(String(value), 10);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function parseDateTime(value: unknown): string | null {
  const text = String(value);
  if (text.length === 0) {
    return null;
  }

  const date = new Date(text);
  return Number.isNaN(date.getTime())
    ? null
    : date.toISOString().replace('.000Z', 'Z');
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
    <p v-if="attachmentError" class="workflow-form__error" role="alert">{{ attachmentError }}</p>

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

          <div
            v-if="field.fieldTypeKey === 'subtable'"
            class="workflow-form__subtable"
            :data-testid="`workflow-form-subtable-${field.fieldKey}`"
          >
            <table class="workflow-form__subtable-table">
              <thead>
                <tr>
                  <th
                    v-for="column in subtableColumns(field)"
                    :key="column.columnKey"
                    translate="no"
                  >{{ column.columnKey }}</th>
                  <th v-if="!isReadOnly(field)" class="workflow-form__subtable-actions-head" />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="(row, rowIndex) in subtableRows(field)"
                  :key="rowIndex"
                  :data-row-index="rowIndex"
                >
                  <td
                    v-for="column in subtableColumns(field)"
                    :key="column.columnKey"
                    :data-column-key="column.columnKey"
                    :data-field-key="field.fieldKey"
                  >
                    <textarea
                      v-if="column.fieldTypeKey === 'textarea'"
                      :value="String(subtableCellValue(field, rowIndex, column.columnKey))"
                      :readonly="isReadOnly(field)"
                      rows="2"
                      @input="updateSubtableCell(field, rowIndex, column.columnKey, column,
                        ($event.target as HTMLTextAreaElement).value)"
                    />
                    <select
                      v-else-if="column.fieldTypeKey === 'select'"
                      :value="String(subtableCellValue(field, rowIndex, column.columnKey))"
                      :disabled="isReadOnly(field)"
                      @change="updateSubtableCell(field, rowIndex, column.columnKey, column,
                        ($event.target as HTMLSelectElement).value)"
                    >
                      <option value=""></option>
                      <option v-for="option in columnOptions(column)" :key="option" :value="option">
                        {{ option }}
                      </option>
                    </select>
                    <span v-else-if="column.fieldTypeKey === 'radio'" class="workflow-form__choices">
                      <label v-for="option in columnOptions(column)" :key="option">
                        <input
                          type="radio"
                          :name="`${field.fieldKey}-${rowIndex}-${column.columnKey}`"
                          :value="option"
                          :checked="subtableCellValue(field, rowIndex, column.columnKey) === option"
                          :disabled="isReadOnly(field)"
                          @change="updateSubtableCell(field, rowIndex, column.columnKey, column, option)"
                        />
                        {{ option }}
                      </label>
                    </span>
                    <span v-else-if="column.fieldTypeKey === 'checkbox'" class="workflow-form__choices">
                      <label v-for="option in columnOptions(column)" :key="option">
                        <input
                          type="checkbox"
                          :value="option"
                          :checked="subtableCheckboxValue(field, rowIndex, column.columnKey).includes(option)"
                          :disabled="isReadOnly(field)"
                          @change="updateSubtableCheckbox(field, rowIndex, column.columnKey, option,
                            ($event.target as HTMLInputElement).checked)"
                        />
                        {{ option }}
                      </label>
                    </span>
                    <input
                      v-else-if="column.fieldTypeKey === 'switch'"
                      type="checkbox"
                      :checked="subtableCellValue(field, rowIndex, column.columnKey) === true"
                      :disabled="isReadOnly(field)"
                      @change="updateSubtableCell(field, rowIndex, column.columnKey, column,
                        ($event.target as HTMLInputElement).checked)"
                    />
                    <input
                      v-else
                      :type="column.fieldTypeKey === 'integer' ? 'number'
                        : column.fieldTypeKey === 'date' ? 'date'
                          : column.fieldTypeKey === 'time' ? 'time'
                            : column.fieldTypeKey === 'datetime' ? 'datetime-local' : 'text'"
                      :inputmode="column.fieldTypeKey === 'money' || column.fieldTypeKey === 'decimal'
                        ? 'decimal' : undefined"
                      :value="String(subtableCellValue(field, rowIndex, column.columnKey))"
                      :readonly="isReadOnly(field)"
                      @input="updateSubtableCell(field, rowIndex, column.columnKey, column,
                        ($event.target as HTMLInputElement).value)"
                    />
                  </td>
                  <td v-if="!isReadOnly(field)" class="workflow-form__subtable-actions">
                    <button
                      type="button"
                      class="workflow-form__subtable-remove"
                      @click="removeSubtableRow(field, rowIndex)"
                    >{{ t('workflowForms.subtable.removeRow') }}</button>
                  </td>
                </tr>
              </tbody>
            </table>

            <button
              v-if="!isReadOnly(field) && subtableRows(field).length < subtableMaxRows(field)"
              type="button"
              class="workflow-form__subtable-add"
              @click="addSubtableRow(field)"
            >{{ t('workflowForms.subtable.addRow') }}</button>
          </div>

          <div
            v-else-if="field.fieldTypeKey === 'attachment'"
            class="workflow-form__attachments"
            :data-testid="`workflow-form-attachment-${field.fieldKey}`"
          >
            <ul v-if="stringArrayValue(field.fieldKey).length > 0" class="workflow-form__attachment-list">
              <li v-for="fileId in stringArrayValue(field.fieldKey)" :key="fileId">
                <button
                  type="button"
                  class="workflow-form__attachment-link"
                  :disabled="instanceId === undefined"
                  @click="openAttachment(fileId)"
                >
                  {{ fileId }}
                </button>
                <button
                  v-if="!isReadOnly(field)"
                  type="button"
                  class="workflow-form__attachment-remove"
                  @click="removeAttachment(field, fileId)"
                >
                  {{ t('workflowForms.attachment.remove') }}
                </button>
              </li>
            </ul>

            <input
              v-if="!isReadOnly(field)"
              type="file"
              :accept="attachmentExtensions(field).map(item => `.${item}`).join(',')"
              :disabled="uploadingFieldKey === field.fieldKey"
              @change="onAttachmentSelected(field, $event)"
            />
            <span v-if="uploadingFieldKey === field.fieldKey" class="workflow-form__hint">
              {{ t('workflowForms.attachment.uploading') }}
            </span>
          </div>

          <textarea
            v-else-if="field.fieldTypeKey === 'textarea'"
            :value="String(values[field.fieldKey] ?? '')"
            :readonly="isReadOnly(field)"
            :required="isRequired(field)"
            :minlength="textLengthConstraint(field, 'minLength')"
            :maxlength="textLengthConstraint(field, 'maxLength')"
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
            :value="inputValue(field)"
            :readonly="isReadOnly(field)"
            :required="isRequired(field)"
            :minlength="textLengthConstraint(field, 'minLength')"
            :maxlength="textLengthConstraint(field, 'maxLength')"
            :min="integerRangeConstraint(field, 'minimum')"
            :max="integerRangeConstraint(field, 'maximum')"
            :step="field.fieldTypeKey === 'datetime' ? 1 : undefined"
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

.workflow-form__error {
  margin: 0;
  color: var(--el-color-danger);
  font-size: 0.82rem;
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

.workflow-form__attachments {
  display: grid;
  gap: 0.5rem;
}

.workflow-form__attachment-list {
  margin: 0;
  padding-left: 1rem;
  display: grid;
  gap: 0.35rem;
}

.workflow-form__attachment-link,
.workflow-form__attachment-remove {
  border: 0;
  background: transparent;
  color: var(--el-color-primary);
  cursor: pointer;
  font: inherit;
}

.workflow-form__attachment-remove {
  margin-left: 0.5rem;
  color: var(--el-color-danger);
}

.workflow-form__hint {
  color: var(--el-text-color-secondary);
  font-size: 0.78rem;
}

.workflow-form__subtable {
  display: grid;
  gap: 0.5rem;
  overflow-x: auto;
}

.workflow-form__subtable-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.workflow-form__subtable-table th,
.workflow-form__subtable-table td {
  padding: 0.35rem 0.45rem;
  border: 1px solid var(--el-border-color-lighter);
  vertical-align: top;
}

.workflow-form__subtable-table th {
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-light);
  font-weight: 650;
}

.workflow-form__subtable-actions-head {
  width: 5.5rem;
}

.workflow-form__subtable-add,
.workflow-form__subtable-remove {
  border: 0;
  background: transparent;
  color: var(--el-color-primary);
  cursor: pointer;
  font: inherit;
}

.workflow-form__subtable-remove {
  color: var(--el-color-danger);
}
</style>
