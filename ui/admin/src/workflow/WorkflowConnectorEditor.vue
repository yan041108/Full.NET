<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { listWorkflowConnectors, type WorkflowConnectorCatalogItem } from '../api/workflow-connectors';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowConnector, type WorkflowConnectorConfiguration } from './workflow-connector';
const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowConnectorConfiguration]; cancel: [] }>();
const { t } = useAdminI18n();
const entries = ref<WorkflowConnectorCatalogItem[]>([]); const selected = ref(''); const inputs = ref<Record<string, string>>({}); const outputs = ref<Record<string, string>>({});
const original = ref<Record<string, string>>({}); const failed = ref(false); const invalid = ref(false); const loading = ref(false);
let generation = 0; let controller: AbortController | undefined;
const identity = (entry: WorkflowConnectorCatalogItem) => `${entry.connectorKey}@${entry.connectorVersion}`;
const current = computed(() => entries.value.find(item => identity(item) === selected.value));
watch(() => props.modelValue, value => {
  inputs.value = {}; outputs.value = {}; original.value = {}; selected.value = ''; invalid.value = false;
  if (typeof value !== 'object' || value === null || !('connectorPolicy' in value)) return;
  try { const config = readWorkflowConnector(value); selected.value = `${config.connectorPolicy.connectorKey}@${config.connectorPolicy.connectorVersion}`;
    inputs.value = { ...config.connectorPolicy.inputFields };
    outputs.value = Object.fromEntries(Object.entries(config.connectorPolicy.outputFields).map(([field, parameter]) => [parameter, field])); original.value = config.fieldPolicies;
  } catch { invalid.value = true; }
}, { immediate: true });
async function load(): Promise<void> {
  controller?.abort(); controller = new AbortController(); const expected = ++generation; loading.value = true; failed.value = false;
  try { const result = await listWorkflowConnectors(controller.signal); if (expected === generation) entries.value = [...result.items]; }
  catch { if (expected === generation) { entries.value = []; failed.value = true; } }
  finally { if (expected === generation) loading.value = false; }
}
void load(); onBeforeUnmount(() => { generation++; controller?.abort(); });
function choose(value: unknown): void { selected.value = String(value); inputs.value = {}; outputs.value = {}; invalid.value = false; }
const fieldsFor = (type: string) => props.fields.filter(field => field.fieldTypeKey === type);
const configuration = computed(() => {
  const entry = current.value; if (!entry || loading.value || failed.value || invalid.value) return undefined;
  if (Object.keys(inputs.value).some(key => !entry.inputParameters.some(parameter => parameter.parameterKey === key)) ||
    Object.keys(outputs.value).some(key => !entry.outputParameters.some(parameter => parameter.parameterKey === key))) return undefined;
  try {
    const inputFields: Record<string, string> = {}; const outputFields: Record<string, string> = {}; const access = { ...original.value };
    for (const parameter of entry.inputParameters) {
      const field = inputs.value[parameter.parameterKey]; if (!field) { if (parameter.required) return undefined; continue; }
      if (!fieldsFor(parameter.fieldTypeKey).some(item => item.fieldKey === field)) return undefined;
      inputFields[parameter.parameterKey] = field; if (!['editable', 'required'].includes(access[field] ?? '')) access[field] = 'readOnly';
    }
    for (const parameter of entry.outputParameters) {
      const field = outputs.value[parameter.parameterKey]; if (!field) continue;
      if (!fieldsFor(parameter.fieldTypeKey).some(item => item.fieldKey === field) || field in outputFields) return undefined;
      outputFields[field] = parameter.parameterKey; access[field] = access[field] === 'required' ? 'required' : 'editable';
    }
    return readWorkflowConnector({ fieldPolicies: access, connectorPolicy: { connectorKey: entry.connectorKey, connectorVersion: entry.connectorVersion, inputFields, outputFields } });
  } catch { return undefined; }
});
</script>
<template>
  <div class="workflow-connector-editor">
    <p>{{ t('workflowDesigner.connector.help') }}</p>
    <ElSelect :model-value="selected" :disabled="disabled || loading || failed" :aria-label="t('workflowDesigner.connector.version')" @update:model-value="choose">
      <ElOption v-for="entry in entries" :key="identity(entry)" :value="identity(entry)" :label="identity(entry)" />
    </ElSelect>
    <p v-if="failed" role="alert">{{ t('workflowDesigner.connector.loadFailed') }} <ElButton @click="load">{{ t('workflowDesigner.connector.refresh') }}</ElButton></p>
    <template v-if="current">
      <h3>{{ t('workflowDesigner.connector.inputs') }}</h3>
      <label v-for="parameter in current.inputParameters" :key="parameter.parameterKey">{{ parameter.parameterKey }}{{ parameter.required ? ' *' : '' }}
        <ElSelect v-model="inputs[parameter.parameterKey]" :disabled="disabled" :clearable="!parameter.required" :aria-label="`${t('workflowDesigner.connector.inputs')} ${parameter.parameterKey}`">
          <ElOption v-for="field in fieldsFor(parameter.fieldTypeKey)" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
        </ElSelect>
      </label>
      <h3>{{ t('workflowDesigner.connector.outputs') }}</h3>
      <label v-for="parameter in current.outputParameters" :key="parameter.parameterKey">{{ parameter.parameterKey }}
        <ElSelect v-model="outputs[parameter.parameterKey]" :disabled="disabled" clearable :aria-label="`${t('workflowDesigner.connector.outputs')} ${parameter.parameterKey}`">
          <ElOption v-for="field in fieldsFor(parameter.fieldTypeKey)" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" />
        </ElSelect>
      </label>
    </template>
    <p v-if="!configuration && !loading" role="alert">{{ t('workflowDesigner.connector.invalid') }}</p>
    <div><ElButton :disabled="disabled || !configuration" data-testid="connector-save" @click="configuration && emit('save', configuration)">{{ t('workflowDesigner.delay.save') }}</ElButton><ElButton @click="emit('cancel')">{{ t('common.cancel') }}</ElButton></div>
  </div>
</template>
<style scoped>
.workflow-connector-editor { display: grid; gap: 16px; }
.workflow-connector-editor label { display: grid; grid-template-columns: minmax(90px, 1fr) minmax(160px, 2fr); gap: 12px; align-items: center; }
@media (max-width: 480px) { .workflow-connector-editor label { grid-template-columns: 1fr; } }
</style>
