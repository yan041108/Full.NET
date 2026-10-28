<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { getSubprocessAsset, listSubprocessDefinitions, listSubprocessVersions, type SubprocessAsset } from '../api/workflow-subprocesses';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowMultiInstance, readWorkflowSubprocess, readWorkflowSubprocessStarter, type WorkflowSubprocessTimeoutPolicy, type WorkflowSubprocessConfiguration } from './workflow-subprocess';
import WorkflowMultiInstanceEditor from './WorkflowMultiInstanceEditor.vue';
import WorkflowSubprocessStarterEditor from './WorkflowSubprocessStarterEditor.vue';
import WorkflowSubprocessTimeoutEditor from './WorkflowSubprocessTimeoutEditor.vue';
const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; targets?: readonly { key: string; label: string }[]; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowSubprocessConfiguration]; cancel: [] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const definitions = ref<Awaited<ReturnType<typeof listSubprocessDefinitions>>>([]);
const versions = ref<Awaited<ReturnType<typeof listSubprocessVersions>>>([]);
const definitionId = ref(''); const versionId = ref(''); const asset = ref<SubprocessAsset>();
const inputs = ref<Record<string, string>>({}); const outputs = ref<Record<string, string>>({});
const multi = ref<unknown>();
const starter = ref<unknown>();
const timeout = ref<WorkflowSubprocessTimeoutPolicy | null>();
const loading = ref(false); const failed = ref(false); let generation = 0; let controller: AbortController | undefined;
const supported = (field: WorkflowFormField) => ['text', 'textarea', 'integer', 'decimal', 'money', 'date', 'time', 'datetime', 'radio', 'select', 'checkbox', 'switch', 'attachment', 'subtable'].includes(field.fieldTypeKey);
const childFields = computed(() => asset.value?.fields.filter(supported) ?? []);
const parentFields = computed(() => props.fields.filter(supported));
function sameField(left: WorkflowFormField, right: WorkflowFormField): boolean {
  if (left.fieldTypeKey !== right.fieldTypeKey) return false;
  if (left.fieldTypeKey !== 'subtable') return true;
  const columns = (field: WorkflowFormField) => Array.isArray(field.constraints.columns) ? field.constraints.columns.map(value => {
    if (!value || typeof value !== 'object' || Array.isArray(value) || typeof value.columnKey !== 'string' || typeof value.fieldTypeKey !== 'string') return '';
    return `${value.columnKey}:${value.fieldTypeKey}`;
  }).sort() : [];
  const a = columns(left); const b = columns(right); return a.length > 0 && !a.includes('') && JSON.stringify(a) === JSON.stringify(b);
}
const compatible = (target: WorkflowFormField) => parentFields.value.filter(field => sameField(field, target));
function reset(): void { controller?.abort(); generation++; definitions.value = []; versions.value = []; asset.value = undefined; definitionId.value = ''; versionId.value = ''; inputs.value = {}; outputs.value = {}; multi.value = undefined; starter.value = undefined; timeout.value = undefined; }
async function load(mode: 'initial' | 'definition' | 'version' = 'initial'): Promise<void> {
  controller?.abort(); controller = new AbortController(); const signal = controller.signal; const expected = ++generation;
  loading.value = true; failed.value = false; asset.value = undefined;
  try {
    if (mode === 'initial') {
      const foundDefinitions = await listSubprocessDefinitions(signal); if (expected !== generation) return;
      definitions.value = foundDefinitions;
      if (versionId.value) {
        const fixed = await getSubprocessAsset(versionId.value, signal); if (expected !== generation) return;
        definitionId.value = fixed.definitionId; asset.value = fixed;
      }
    }
    if (definitionId.value && mode !== 'version') {
      const found = await listSubprocessVersions(definitionId.value, signal); if (expected !== generation) return; versions.value = found;
    }
    if (versionId.value && !asset.value) {
      const found = await getSubprocessAsset(versionId.value, signal); if (expected !== generation) return;
      if (found.definitionId !== definitionId.value) throw new Error('client.invalid_workflow_subprocess_asset'); asset.value = found;
    }
  } catch { if (expected === generation) { failed.value = true; asset.value = undefined; } }
  finally { if (expected === generation) loading.value = false; }
}
function initialize(): void {
  reset();
  if (props.modelValue && typeof props.modelValue === 'object' && 'subprocessPolicy' in props.modelValue) {
    try { const config = readWorkflowSubprocess(props.modelValue); versionId.value = config.subprocessPolicy.definitionVersionId;
      inputs.value = { ...config.subprocessPolicy.inputFields }; outputs.value = { ...config.subprocessPolicy.outputFields }; multi.value = config.multiInstancePolicy; starter.value = config.starterPolicy; timeout.value = config.childTimeoutPolicy; }
    catch { failed.value = true; return; }
  }
  void load();
}
watch(() => [props.modelValue, session.currentUser, session.switching] as const, () => { if (session.switching) { reset(); loading.value = true; } else initialize(); }, { immediate: true });
onBeforeUnmount(reset);
function chooseDefinition(value: unknown): void { definitionId.value = String(value); versionId.value = ''; versions.value = []; inputs.value = {}; outputs.value = {}; multi.value = undefined; starter.value = undefined; timeout.value = undefined; void load('definition'); }
function chooseVersion(value: unknown): void { versionId.value = String(value); inputs.value = {}; outputs.value = {}; multi.value = undefined; starter.value = undefined; timeout.value = undefined; void load('version'); }
const multiPolicy = computed(() => { try { return multi.value === undefined ? undefined : readWorkflowMultiInstance(multi.value); } catch { return undefined; } });
const itemFieldKey = computed(() => { try { return multi.value === undefined ? undefined : readWorkflowMultiInstance(multi.value).source.itemFieldKey; } catch { return undefined; } });
const configuration = computed(() => {
  if (!asset.value || loading.value || failed.value || session.switching || !definitions.value.some(item => item.id === definitionId.value) ||
      !versions.value.some(item => item.id === versionId.value) || asset.value.fields.some(field => field.required && !supported(field))) return undefined;
  const grants: Record<string, string> = {}; const inputFields: Record<string, string> = {}; const outputFields: Record<string, string> = {};
  if (multi.value === null || starter.value === null || timeout.value === null) return undefined;
  let multiPolicy;
  try { multiPolicy = multi.value === undefined ? undefined : readWorkflowMultiInstance(multi.value); } catch { return undefined; }
  if (multiPolicy?.source.fieldKey) {
    const source = parentFields.value.find(field => field.fieldKey === multiPolicy.source.fieldKey);
    if (source?.fieldTypeKey !== (multiPolicy.source.kindKey === 'number_field' ? 'integer' : 'checkbox')) return undefined;
    grants[source.fieldKey] = 'readOnly';
  }
  if (multiPolicy?.source.itemFieldKey && !childFields.value.some(field => field.fieldKey === multiPolicy.source.itemFieldKey && ['text', 'textarea', 'select', 'radio'].includes(field.fieldTypeKey))) return undefined;
  for (const field of childFields.value) {
    if (field.fieldKey === multiPolicy?.source.itemFieldKey) continue;
    const source = inputs.value[field.fieldKey]; if (!source) { if (field.required) return undefined; continue; }
    if (!compatible(field).some(parent => parent.fieldKey === source)) return undefined;
    inputFields[field.fieldKey] = source; grants[source] = 'readOnly';
  }
  for (const [parentKey, childKey] of Object.entries(outputs.value)) {
    if (!childKey) continue;
    const parent = parentFields.value.find(field => field.fieldKey === parentKey); const child = childFields.value.find(field => field.fieldKey === childKey);
    if (!parent || !child || !sameField(parent, child) || parent.required && !child.required) return undefined;
    outputFields[parentKey] = childKey; grants[parentKey] = parent.required ? 'required' : 'editable';
  }
  let starterPolicy;
  try { starterPolicy = starter.value === undefined ? undefined : readWorkflowSubprocessStarter(starter.value, multiPolicy); } catch { return undefined; }
  if (starterPolicy?.source.fieldKey) grants[starterPolicy.source.fieldKey] ??= 'readOnly';
  if (starterPolicy?.resultFieldKey) {
    const field = parentFields.value.find(field => field.fieldKey === starterPolicy.resultFieldKey);
    if (!field || field.fieldTypeKey !== (multiPolicy ? 'checkbox' : 'text')) return undefined;
    grants[field.fieldKey] = field.required ? 'required' : 'editable';
  }
  try { return readWorkflowSubprocess({ subprocessPolicy: { definitionVersionId: versionId.value, inputFields, outputFields }, fieldPolicies: grants,
    ...(multiPolicy ? { multiInstancePolicy: multiPolicy } : {}), ...(starterPolicy ? { starterPolicy } : {}), ...(timeout.value ? { childTimeoutPolicy: timeout.value } : {}) }); }
  catch { return undefined; }
});
</script>
<template>
  <section class="subprocess-editor">
    <p>{{ t('workflowDesigner.subprocess.help') }}</p>
    <label>{{ t('workflowDesigner.subprocess.definition') }}
      <ElSelect :model-value="definitionId" :disabled="disabled || loading" :aria-label="t('workflowDesigner.subprocess.definition')" @update:model-value="chooseDefinition">
        <ElOption v-for="item in definitions" :key="item.id" :value="item.id" :label="item.definitionKey" />
      </ElSelect>
    </label>
    <label>{{ t('workflowDesigner.subprocess.version') }}
      <ElSelect :model-value="versionId" :disabled="disabled || loading || !definitionId" :aria-label="t('workflowDesigner.subprocess.version')" @update:model-value="chooseVersion">
        <ElOption v-for="item in versions" :key="item.id" :value="item.id" :label="`v${item.versionNumber}`" />
      </ElSelect>
    </label>
    <p v-if="failed" role="alert">{{ t('workflowDesigner.subprocess.loadFailed') }} <ElButton @click="load()">{{ t('workflowDesigner.subprocess.refresh') }}</ElButton></p>
    <template v-if="asset">
      <h3>{{ t('workflowDesigner.subprocess.inputs') }}</h3>
      <label v-for="field in childFields.filter(field => field.fieldKey !== itemFieldKey)" :key="field.fieldKey">{{ field.fieldKey }}{{ field.required ? ' *' : '' }}
        <ElSelect v-model="inputs[field.fieldKey]" :disabled="disabled || loading" :clearable="!field.required" :aria-label="`${t('workflowDesigner.subprocess.inputs')} ${field.fieldKey}`">
          <ElOption v-for="parent in compatible(field)" :key="parent.fieldKey" :value="parent.fieldKey" :label="parent.fieldKey" />
        </ElSelect>
      </label>
      <WorkflowMultiInstanceEditor v-model="multi" :fields="parentFields" :child-fields="childFields" :output-keys="Object.keys(outputs).filter(key => outputs[key])" :disabled="disabled || loading" />
      <WorkflowSubprocessTimeoutEditor v-model="timeout" :targets="targets ?? []" :disabled="disabled || loading" />
      <WorkflowSubprocessStarterEditor v-model="starter" :fields="parentFields" :output-keys="Object.keys(outputs).filter(key => outputs[key])" :multi="multiPolicy" :multiple="multi !== undefined" :disabled="disabled || loading" />
      <h3>{{ t('workflowDesigner.subprocess.outputs') }}</h3>
      <label v-for="field in parentFields" :key="field.fieldKey">{{ field.fieldKey }}
        <ElSelect v-model="outputs[field.fieldKey]" :data-testid="`subprocess-output-${field.fieldKey}`" :disabled="disabled || loading" clearable :aria-label="`${t('workflowDesigner.subprocess.outputs')} ${field.fieldKey}`">
          <ElOption v-for="child in childFields.filter(item => sameField(item, field) && (!field.required || item.required))" :key="child.fieldKey" :value="child.fieldKey" :label="child.fieldKey" />
        </ElSelect>
      </label>
    </template>
    <p v-if="!configuration && !loading && !failed" role="status">{{ t('workflowDesigner.subprocess.invalid') }}</p>
    <div class="actions"><ElButton @click="emit('cancel')">{{ t('common.cancel') }}</ElButton>
      <ElButton type="primary" data-testid="subprocess-save" :disabled="disabled || !configuration" @click="configuration && emit('save', configuration)">{{ t('workflowDesigner.businessCommand.save') }}</ElButton></div>
  </section>
</template>
<style scoped>
.subprocess-editor { display: grid; gap: 16px; padding: 0 20px 20px; }
.subprocess-editor p { margin: 0; line-height: 1.6; }
.subprocess-editor label { display: grid; grid-template-columns: minmax(100px, 1fr) minmax(160px, 2fr); gap: 12px; align-items: center; }
.subprocess-editor h3 { margin: 8px 0 0; font-size: 1rem; }
.actions { display: flex; justify-content: flex-end; gap: 12px; }
@media (max-width: 480px) { .subprocess-editor label { grid-template-columns: 1fr; } }
</style>
