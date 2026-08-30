<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  WORKFLOW_FIELD_TYPES,
  addWorkflowFormField,
  addWorkflowFormSection,
  removeWorkflowFormField,
  updateWorkflowFormField,
  type WorkflowFieldType,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormField,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{
  schema: WorkflowFormSchema;
  catalog: WorkflowFormComponentCatalogResponse;
  disabled: boolean;
}>();

const emit = defineEmits<{
  'update:schema': [schema: WorkflowFormSchema];
}>();

const { t } = useAdminI18n();
const workingSchema = ref<WorkflowFormSchema>(props.schema);
const newSectionKey = ref('');
const newFieldKey = ref('');
const newFieldSectionKey = ref(props.schema.sections[0]?.sectionKey ?? '');
const newFieldType = ref<WorkflowFieldType>('text');
const errorCode = ref<string>();

const availableComponents = computed(() => props.catalog.components.filter(component =>
  component.designable
  && component.publishable
  && component.executable
  && isWorkflowFieldType(component.fieldTypeKey)));

watch(
  () => props.schema,
  schema => {
    workingSchema.value = schema;
    if (!schema.sections.some(section => section.sectionKey === newFieldSectionKey.value)) {
      newFieldSectionKey.value = schema.sections[0]?.sectionKey ?? '';
    }
  }
);

watch(availableComponents, components => {
  if (!components.some(component => component.fieldTypeKey === newFieldType.value)) {
    const first = components.find(component => isWorkflowFieldType(component.fieldTypeKey));
    if (first !== undefined) {
      newFieldType.value = first.fieldTypeKey as WorkflowFieldType;
    }
  }
}, { immediate: true });

function addSection(): void {
  mutate(() => addWorkflowFormSection(workingSchema.value, newSectionKey.value.trim()));
  if (errorCode.value === undefined) {
    newFieldSectionKey.value = newSectionKey.value.trim();
    newSectionKey.value = '';
  }
}

function addField(): void {
  mutate(() => addWorkflowFormField(
    workingSchema.value,
    newFieldSectionKey.value,
    newFieldKey.value.trim(),
    newFieldType.value,
    props.catalog
  ));
  if (errorCode.value === undefined) {
    newFieldKey.value = '';
  }
}

function renameField(field: WorkflowFormField, value: string): void {
  mutate(() => updateWorkflowFormField(
    workingSchema.value,
    field.fieldKey,
    { fieldKey: value.trim() },
    props.catalog
  ));
}

function setRequired(field: WorkflowFormField, required: boolean): void {
  mutate(() => updateWorkflowFormField(
    workingSchema.value,
    field.fieldKey,
    { required },
    props.catalog
  ));
}

function updateConstraint(
  field: WorkflowFormField,
  constraintKey: string,
  rawValue: string
): void {
  const constraints: Record<string, unknown> = { ...field.constraints };
  if (rawValue.trim().length === 0) {
    delete constraints[constraintKey];
  } else if (constraintKey === 'options') {
    constraints[constraintKey] = rawValue.split(/\r?\n/u)
      .map(value => value.trim())
      .filter(value => value.length > 0);
  } else if (isNumericConstraint(field, constraintKey)) {
    constraints[constraintKey] = Number(rawValue);
  } else {
    constraints[constraintKey] = rawValue.trim();
  }

  mutate(() => updateWorkflowFormField(
    workingSchema.value,
    field.fieldKey,
    { constraints },
    props.catalog
  ));
}

function removeField(fieldKey: string): void {
  mutate(() => removeWorkflowFormField(workingSchema.value, fieldKey));
}

function constraintKeysFor(field: WorkflowFormField): readonly string[] {
  return availableComponents.value.find(component =>
    component.fieldTypeKey === field.fieldTypeKey)?.constraintKeys ?? [];
}

function constraintValue(field: WorkflowFormField, constraintKey: string): string {
  const value = field.constraints[constraintKey];
  return Array.isArray(value)
    ? value.join('\n')
    : value === undefined ? '' : String(value);
}

function isNumericConstraint(field: WorkflowFormField, constraintKey: string): boolean {
  return constraintKey === 'scale'
    || constraintKey === 'minLength'
    || constraintKey === 'maxLength'
    || field.fieldTypeKey === 'integer' && (constraintKey === 'minimum' || constraintKey === 'maximum');
}

function mutate(action: () => WorkflowFormSchema): void {
  if (props.disabled) {
    return;
  }
  try {
    const schema = action();
    workingSchema.value = schema;
    errorCode.value = undefined;
    emit('update:schema', schema);
  } catch (error: unknown) {
    errorCode.value = error instanceof Error
      ? error.message
      : 'client.invalid_workflow_form_draft';
  }
}

function isWorkflowFieldType(value: string): value is WorkflowFieldType {
  return WORKFLOW_FIELD_TYPES.some(type => type === value);
}
</script>

<template>
  <section class="form-studio" data-testid="workflow-form-designer">
    <header class="form-studio__masthead">
      <div>
        <p class="form-studio__eyebrow">Schema {{ schema.schemaVersion }} · Adapter {{ schema.adapterVersion }}</p>
        <h2>{{ t('workflowFormDesigner.title') }}</h2>
        <p>{{ t('workflowFormDesigner.caption') }}</p>
      </div>
      <span class="form-studio__catalog" translate="no">
        Catalog {{ catalog.catalogVersion }}
      </span>
    </header>

    <div v-if="errorCode" class="form-studio__alert" role="alert">
      <strong translate="no">{{ errorCode }}</strong>
      <span>{{ t('workflowFormDesigner.invalidDraft') }}</span>
    </div>

    <div v-if="!disabled" class="form-studio__tools">
      <fieldset>
        <legend>{{ t('workflowFormDesigner.addSection') }}</legend>
        <label>
          <span>{{ t('workflowFormDesigner.sectionKey') }}</span>
          <input
            v-model="newSectionKey"
            data-testid="workflow-designer-new-section-key"
            autocomplete="off"
          />
        </label>
        <button
          type="button"
          data-designer-action
          data-testid="workflow-designer-add-section"
          @click="addSection"
        >{{ t('workflowFormDesigner.add') }}</button>
      </fieldset>

      <fieldset>
        <legend>{{ t('workflowFormDesigner.addField') }}</legend>
        <label>
          <span>{{ t('workflowFormDesigner.section') }}</span>
          <select v-model="newFieldSectionKey" data-testid="workflow-designer-new-field-section">
            <option
              v-for="section in workingSchema.sections"
              :key="section.sectionKey"
              :value="section.sectionKey"
              translate="no"
            >{{ section.sectionKey }}</option>
          </select>
        </label>
        <label>
          <span>{{ t('workflowFormDesigner.fieldKey') }}</span>
          <input
            v-model="newFieldKey"
            data-testid="workflow-designer-new-field-key"
            autocomplete="off"
          />
        </label>
        <label>
          <span>{{ t('workflowFormDesigner.fieldType') }}</span>
          <select v-model="newFieldType" data-testid="workflow-designer-new-field-type">
            <option
              v-for="component in availableComponents"
              :key="component.fieldTypeKey"
              :value="component.fieldTypeKey"
              translate="no"
            >{{ component.fieldTypeKey }}</option>
          </select>
        </label>
        <button
          type="button"
          data-designer-action
          data-testid="workflow-designer-add-field"
          @click="addField"
        >{{ t('workflowFormDesigner.add') }}</button>
      </fieldset>
    </div>

    <ol class="form-studio__sections">
      <li
        v-for="(section, sectionIndex) in workingSchema.sections"
        :key="section.sectionKey"
        class="form-studio__section"
        :data-section-key="section.sectionKey"
      >
        <header class="form-studio__section-header">
          <span class="form-studio__section-index">{{ String(sectionIndex + 1).padStart(2, '0') }}</span>
          <div>
            <span>{{ t('workflowFormDesigner.section') }}</span>
            <strong translate="no">{{ section.sectionKey }}</strong>
          </div>
        </header>

        <div class="form-studio__fields">
          <article
            v-for="field in section.fields"
            :key="field.fieldKey"
            class="form-studio__field"
            :data-field-key="field.fieldKey"
          >
            <div class="form-studio__field-heading">
              <span class="form-studio__type" translate="no">{{ field.fieldTypeKey }}</span>
              <button
                v-if="!disabled"
                type="button"
                class="form-studio__remove"
                data-designer-action
                data-testid="workflow-designer-remove-field"
                @click="removeField(field.fieldKey)"
              >{{ t('workflowFormDesigner.remove') }}</button>
            </div>

            <div class="form-studio__field-grid">
              <label>
                <span>{{ t('workflowFormDesigner.fieldKey') }}</span>
                <input
                  :value="field.fieldKey"
                  data-field-property="fieldKey"
                  :readonly="disabled"
                  autocomplete="off"
                  @change="renameField(field, ($event.target as HTMLInputElement).value)"
                />
              </label>
              <label class="form-studio__required">
                <input
                  type="checkbox"
                  :checked="field.required"
                  :disabled="disabled"
                  data-field-property="required"
                  @change="setRequired(field, ($event.target as HTMLInputElement).checked)"
                />
                <span>{{ t('workflowFormDesigner.required') }}</span>
              </label>
            </div>

            <div v-if="constraintKeysFor(field).length > 0" class="form-studio__constraints">
              <label v-for="constraintKey in constraintKeysFor(field)" :key="constraintKey">
                <span translate="no">{{ constraintKey }}</span>
                <textarea
                  v-if="constraintKey === 'options'"
                  :value="constraintValue(field, constraintKey)"
                  :data-constraint-key="constraintKey"
                  :readonly="disabled"
                  rows="3"
                  @change="updateConstraint(field, constraintKey, ($event.target as HTMLTextAreaElement).value)"
                />
                <input
                  v-else
                  :type="isNumericConstraint(field, constraintKey) ? 'number' : 'text'"
                  :value="constraintValue(field, constraintKey)"
                  :data-constraint-key="constraintKey"
                  :readonly="disabled"
                  @change="updateConstraint(field, constraintKey, ($event.target as HTMLInputElement).value)"
                />
              </label>
            </div>
          </article>
        </div>
      </li>
    </ol>
  </section>
</template>

<style scoped>
.form-studio {
  --studio-ink: var(--el-text-color-primary);
  --studio-muted: var(--el-text-color-secondary);
  --studio-line: var(--el-border-color-lighter);
  display: grid;
  gap: 1rem;
  color: var(--studio-ink);
}

.form-studio__masthead {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.15rem 1.25rem;
  border: 1px solid var(--studio-line);
  border-left: 4px solid var(--el-color-primary);
  background: linear-gradient(115deg, var(--el-fill-color-blank), var(--el-fill-color-light));
}

.form-studio__eyebrow {
  margin: 0 0 0.3rem;
  color: var(--el-color-primary);
  font-size: 0.7rem;
  font-weight: 750;
  letter-spacing: 0.11em;
  text-transform: uppercase;
}

.form-studio__masthead h2 { margin: 0; font-size: 1.2rem; }
.form-studio__masthead p:last-child { margin: 0.35rem 0 0; color: var(--studio-muted); }
.form-studio__catalog, .form-studio__type { font-family: var(--art-font-mono, monospace); }
.form-studio__catalog { padding: 0.35rem 0.55rem; border: 1px solid var(--studio-line); font-size: 0.72rem; }

.form-studio__alert {
  display: flex;
  gap: 0.6rem;
  padding: 0.75rem 0.9rem;
  border: 1px solid var(--el-color-danger-light-5);
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
}

.form-studio__tools { display: grid; grid-template-columns: minmax(15rem, 0.75fr) minmax(28rem, 1.6fr); gap: 0.75rem; }
.form-studio__tools fieldset { display: flex; align-items: end; gap: 0.6rem; margin: 0; padding: 0.85rem; border: 1px solid var(--studio-line); }
.form-studio__tools legend { padding: 0 0.4rem; color: var(--studio-muted); font-size: 0.72rem; font-weight: 700; text-transform: uppercase; }
.form-studio__tools label, .form-studio__field label { display: grid; gap: 0.3rem; min-width: 0; }
.form-studio__tools label { flex: 1; }
.form-studio label > span { color: var(--studio-muted); font-size: 0.72rem; font-weight: 650; }

.form-studio input:not([type='checkbox']), .form-studio select, .form-studio textarea {
  width: 100%;
  min-height: 36px;
  box-sizing: border-box;
  padding: 0.45rem 0.6rem;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  color: var(--studio-ink);
  background: var(--el-bg-color);
  font: inherit;
}

.form-studio button { min-height: 36px; padding: 0.45rem 0.8rem; border: 1px solid var(--el-color-primary); border-radius: 6px; color: var(--el-color-white); background: var(--el-color-primary); cursor: pointer; }
.form-studio__sections { display: grid; gap: 1rem; margin: 0; padding: 0; list-style: none; }
.form-studio__section { overflow: hidden; border: 1px solid var(--studio-line); background: var(--el-fill-color-blank); }
.form-studio__section-header { display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem 1rem; border-bottom: 1px solid var(--studio-line); background: var(--el-fill-color-light); }
.form-studio__section-index { color: var(--el-color-primary); font-size: 1.15rem; font-weight: 800; }
.form-studio__section-header div { display: grid; gap: 0.1rem; }
.form-studio__section-header div span { color: var(--studio-muted); font-size: 0.65rem; text-transform: uppercase; }
.form-studio__fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(17rem, 1fr)); gap: 0.75rem; padding: 0.85rem; }
.form-studio__field { display: grid; gap: 0.75rem; padding: 0.85rem; border: 1px solid var(--studio-line); border-radius: 8px; }
.form-studio__field-heading { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
.form-studio__type { color: var(--el-color-primary); font-size: 0.72rem; font-weight: 750; text-transform: uppercase; }
.form-studio button.form-studio__remove { min-height: auto; padding: 0.2rem 0.45rem; border-color: transparent; color: var(--el-color-danger); background: transparent; }
.form-studio__field-grid { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: end; gap: 0.75rem; }
.form-studio__required { display: flex !important; align-items: center; min-height: 36px; }
.form-studio__constraints { display: grid; grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr)); gap: 0.55rem; }
.form-studio input[readonly], .form-studio textarea[readonly] { color: var(--studio-muted); background: var(--el-fill-color-light); }

@media (max-width: 880px) {
  .form-studio__tools { grid-template-columns: 1fr; }
  .form-studio__tools fieldset { align-items: stretch; flex-direction: column; }
  .form-studio__field-grid { grid-template-columns: 1fr; }
}
</style>
