<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElInputNumber, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { listWorkflowRoleCandidates } from '../api/workflow-definitions';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowMultiInstance, type WorkflowMultiInstancePolicy } from './workflow-subprocess';

const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; childFields: readonly WorkflowFormField[]; outputKeys: readonly string[]; disabled: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowMultiInstancePolicy | null | undefined] }>();
const { t } = useAdminI18n();
const session = useSessionStore();
const mode = ref('single'); const kind = ref('fixed_count'); const count = ref(2); const percentage = ref(100); const empty = ref('reject');
const sourceField = ref(''); const roleId = ref(''); const itemField = ref(''); const reducers = ref<Record<string, string>>({});
const roles = ref<{ id: string; name: string }[]>([]); const rolePage = ref(0); const moreRoles = ref(true); const loadingRoles = ref(false); const roleFailed = ref(false);
let generation = 0; let controller: AbortController | undefined;
watch(() => JSON.stringify(props.modelValue), () => {
  if (props.modelValue === null) return;
  let policy: WorkflowMultiInstancePolicy | undefined;
  try { policy = props.modelValue === undefined ? undefined : readWorkflowMultiInstance(props.modelValue); } catch { return; }
  mode.value = policy?.modeKey ?? 'single'; kind.value = policy?.source.kindKey ?? 'fixed_count'; count.value = policy?.source.count ?? policy?.source.maximumCount ?? 2;
  percentage.value = policy?.requiredPercentage ?? 100; empty.value = policy?.emptyModeKey ?? 'reject'; sourceField.value = policy?.source.fieldKey ?? '';
  roleId.value = policy?.source.roleId ?? ''; itemField.value = policy?.source.itemFieldKey ?? ''; reducers.value = { ...policy?.outputReducers };
}, { immediate: true });
const sourceFields = computed(() => props.fields.filter(field => field.fieldTypeKey === (kind.value === 'number_field' ? 'integer' : 'checkbox')));
const itemFields = computed(() => props.childFields.filter(field => ['text', 'textarea', 'select', 'radio'].includes(field.fieldTypeKey)));
type Reducer = 'first' | 'last' | 'sum' | 'min' | 'max' | 'all_true' | 'any_true' | 'append';
function rules(key: string): Reducer[] {
  const type = props.fields.find(field => field.fieldKey === key)?.fieldTypeKey;
  return ['first', 'last', ...(['integer', 'decimal', 'money'].includes(type ?? '') ? ['sum', 'min', 'max'] as const : type === 'switch' ? ['all_true', 'any_true'] as const : ['attachment', 'subtable'].includes(type ?? '') ? ['append'] as const : [])];
}
const value = computed(() => {
  if (session.switching) return null;
  if (mode.value === 'single') return undefined;
  const source: Record<string, unknown> = { kindKey: kind.value };
  if (kind.value === 'fixed_count') source.count = count.value;
  else {
    source.maximumCount = count.value;
    if (kind.value === 'role_members') source.roleId = roleId.value;
    else source.fieldKey = sourceField.value;
    if (['role_members', 'collection_field'].includes(kind.value)) source.itemFieldKey = itemField.value;
  }
  if (kind.value === 'role_members' && (!roles.value.some(role => role.id === roleId.value) || roleFailed.value)) return null;
  if (['number_field', 'collection_field'].includes(kind.value) && !sourceFields.value.some(field => field.fieldKey === sourceField.value) ||
      ['role_members', 'collection_field'].includes(kind.value) && !itemFields.value.some(field => field.fieldKey === itemField.value)) return null;
  const outputReducers = Object.fromEntries(props.outputKeys.map(key => [key, reducers.value[key] ?? 'first']));
  if (props.outputKeys.some(key => !rules(key).some(rule => rule === outputReducers[key]))) return null;
  try { return readWorkflowMultiInstance({ modeKey: mode.value, source, requiredPercentage: percentage.value, emptyModeKey: empty.value, outputReducers }); }
  catch { return null; }
});
watch(value, next => { if (JSON.stringify(next) !== JSON.stringify(props.modelValue)) emit('update:modelValue', next); }, { immediate: true });
async function loadRoles(): Promise<void> {
  if (loadingRoles.value) return;
  controller?.abort(); controller = new AbortController(); const expected = ++generation; loadingRoles.value = true; roleFailed.value = false;
  try {
    const page = await listWorkflowRoleCandidates(rolePage.value + 1, 50, controller.signal); if (expected !== generation) return;
    roles.value = [...roles.value, ...page.items.filter(item => !roles.value.some(role => role.id === item.id))]; rolePage.value++;
    moreRoles.value = roles.value.length < page.total;
  } catch { if (expected === generation) roleFailed.value = true; }
  finally { if (expected === generation) loadingRoles.value = false; }
}
watch([mode, kind], () => { if (mode.value !== 'single' && kind.value === 'role_members' && rolePage.value === 0) void loadRoles(); }, { immediate: true });
watch(() => [session.currentUser, session.switching] as const, () => {
  generation++; controller?.abort(); roles.value = []; rolePage.value = 0; moreRoles.value = true; loadingRoles.value = false; roleFailed.value = false;
  if (!session.switching && mode.value !== 'single' && kind.value === 'role_members') void loadRoles();
});
onBeforeUnmount(() => { generation++; controller?.abort(); });
</script>
<template>
  <fieldset class="multi-editor" :disabled="disabled">
    <legend>{{ t('workflowDesigner.multi.title') }}</legend>
    <label>{{ t('workflowDesigner.multi.mode') }}
      <ElSelect v-model="mode" data-testid="subprocess-multi-mode" :disabled="disabled" :aria-label="t('workflowDesigner.multi.mode')">
        <ElOption v-for="option in ['single', 'serial', 'parallel']" :key="option" :value="option" :label="t(`workflowDesigner.multi.${option}`)" />
      </ElSelect>
    </label>
    <template v-if="mode !== 'single'">
      <p>{{ t('workflowDesigner.multi.help') }}</p>
      <label>{{ t('workflowDesigner.multi.source') }}<ElSelect v-model="kind" data-testid="subprocess-multi-source" :disabled="disabled" :aria-label="t('workflowDesigner.multi.source')">
        <ElOption v-for="option in ['fixed_count', 'number_field', 'collection_field', 'role_members']" :key="option" :value="option" :label="t(`workflowDesigner.multi.${option}`)" />
      </ElSelect></label>
      <label>{{ t(kind === 'fixed_count' ? 'workflowDesigner.multi.count' : 'workflowDesigner.multi.maximum') }}
        <ElInputNumber v-model="count" data-testid="subprocess-multi-count" :disabled="disabled" :min="1" :max="32" :precision="0" :aria-label="t('workflowDesigner.multi.maximum')" />
      </label>
      <label v-if="['number_field', 'collection_field'].includes(kind)">{{ t('workflowDesigner.multi.sourceField') }}
        <ElSelect v-model="sourceField" :disabled="disabled" :aria-label="t('workflowDesigner.multi.sourceField')"><ElOption v-for="field in sourceFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" /></ElSelect>
      </label>
      <template v-if="kind === 'role_members'">
        <label>{{ t('workflowDesigner.multi.role') }}<ElSelect v-model="roleId" :disabled="disabled || loadingRoles" filterable :aria-label="t('workflowDesigner.multi.role')"><ElOption v-for="role in roles" :key="role.id" :value="role.id" :label="role.name" /></ElSelect></label>
        <ElButton v-if="moreRoles || roleFailed" :disabled="disabled || loadingRoles" @click="loadRoles">{{ t(roleFailed ? 'workflowDesigner.subprocess.refresh' : 'workflowDesigner.multi.moreRoles') }}</ElButton>
        <p v-if="roleFailed" role="alert">{{ t('workflowDesigner.subprocess.loadFailed') }}</p>
      </template>
      <label v-if="['role_members', 'collection_field'].includes(kind)">{{ t('workflowDesigner.multi.itemField') }}<ElSelect v-model="itemField" :disabled="disabled" :aria-label="t('workflowDesigner.multi.itemField')"><ElOption v-for="field in itemFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" /></ElSelect></label>
      <label>{{ t('workflowDesigner.multi.percentage') }}<ElInputNumber v-model="percentage" data-testid="subprocess-multi-percentage" :disabled="disabled" :min="1" :max="100" :precision="0" :aria-label="t('workflowDesigner.multi.percentage')" /></label>
      <label>{{ t('workflowDesigner.multi.empty') }}<ElSelect v-model="empty" :disabled="disabled" :aria-label="t('workflowDesigner.multi.empty')"><ElOption v-for="option in ['reject', 'skip']" :key="option" :value="option" :label="t(`workflowDesigner.multi.${option}`)" /></ElSelect></label>
      <label v-for="key in outputKeys" :key="key">{{ key }} · {{ t('workflowDesigner.multi.reducer') }}<ElSelect :model-value="reducers[key] ?? 'first'" :data-testid="`subprocess-multi-reducer-${key}`" :disabled="disabled" :aria-label="`${key} ${t('workflowDesigner.multi.reducer')}`" @update:model-value="reducers[key] = String($event)"><ElOption v-for="rule in rules(key)" :key="rule" :value="rule" :label="t(`workflowDesigner.multi.${rule}`)" /></ElSelect></label>
    </template>
  </fieldset>
</template>
<style scoped>
.multi-editor { display: grid; gap: 14px; border: 1px solid var(--el-border-color); border-radius: 6px; padding: 16px; }
.multi-editor legend { padding: 0 6px; font-weight: 600; }
.multi-editor label { display: grid; grid-template-columns: minmax(110px, 1fr) minmax(160px, 2fr); gap: 12px; align-items: center; }
.multi-editor p { margin: 0; color: var(--el-text-color-secondary); line-height: 1.6; }
@media (max-width: 480px) { .multi-editor label { grid-template-columns: 1fr; } }
</style>
