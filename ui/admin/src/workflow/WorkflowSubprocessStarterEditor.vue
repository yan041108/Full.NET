<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowSubprocessStarter, type WorkflowMultiInstancePolicy, type WorkflowSubprocessStarterPolicy } from './workflow-subprocess';

const props = defineProps<{ modelValue?: unknown; fields: readonly WorkflowFormField[]; outputKeys: readonly string[]; multi?: WorkflowMultiInstancePolicy; multiple: boolean; disabled: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowSubprocessStarterPolicy | null | undefined] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const kind = ref('parent'); const userId = ref(''); const fieldKey = ref(''); const resultFieldKey = ref('');
const users = ref<{ id: string; name: string }[]>([]); const page = ref(0); const more = ref(true); const loading = ref(false); const failed = ref(false);
let generation = 0; let controller: AbortController | undefined;
const uuid = (value: unknown) => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/iu.test(value);
const sourceFields = computed(() => props.fields.filter(field => ['select', 'radio'].includes(field.fieldTypeKey) && Array.isArray(field.constraints.options) && field.constraints.options.length > 0 && field.constraints.options.every(uuid)));
const resultFields = computed(() => props.fields.filter(field => field.fieldTypeKey === (props.multiple ? 'checkbox' : 'text') && !props.outputKeys.includes(field.fieldKey) &&
  (!props.multiple || Array.isArray(field.constraints.options) && field.constraints.options.length > 0 && field.constraints.options.every(option => uuid(option) && option === String(option).toLowerCase()))));
watch(() => JSON.stringify(props.modelValue), () => {
  if (props.modelValue === null) return;
  let policy: WorkflowSubprocessStarterPolicy | undefined;
  try { policy = props.modelValue === undefined ? undefined : readWorkflowSubprocessStarter(props.modelValue, props.multi); } catch { return; }
  kind.value = policy?.source.kindKey ?? 'parent'; userId.value = policy?.source.userId ?? ''; fieldKey.value = policy?.source.fieldKey ?? ''; resultFieldKey.value = policy?.resultFieldKey ?? '';
}, { immediate: true });
const value = computed(() => {
  if (session.switching) return null;
  if (kind.value === 'parent' && !resultFieldKey.value) return undefined;
  if (kind.value === 'specified_user' && (failed.value || !users.value.some(user => user.id === userId.value)) ||
      kind.value === 'field_user' && !sourceFields.value.some(field => field.fieldKey === fieldKey.value) ||
      resultFieldKey.value && !resultFields.value.some(field => field.fieldKey === resultFieldKey.value)) return null;
  const source = kind.value === 'specified_user' ? { kindKey: kind.value, userId: userId.value } : kind.value === 'field_user' ? { kindKey: kind.value, fieldKey: fieldKey.value } : { kindKey: kind.value };
  try { return readWorkflowSubprocessStarter({ source, ...(resultFieldKey.value ? { resultFieldKey: resultFieldKey.value } : {}) }, props.multi); } catch { return null; }
});
watch(value, next => { if (JSON.stringify(next) !== JSON.stringify(props.modelValue)) emit('update:modelValue', next); }, { immediate: true });
async function loadUsers(): Promise<void> {
  if (loading.value) return;
  controller?.abort(); controller = new AbortController(); const expected = ++generation; loading.value = true; failed.value = false;
  try { const found = await listWorkflowRecipientCandidates(page.value + 1, 50, controller.signal); if (expected !== generation) return;
    users.value = [...users.value, ...found.items.filter(item => !users.value.some(user => user.id === item.id)).map(item => ({ id: item.id, name: `${item.displayName} (${item.username})` }))]; page.value++; more.value = users.value.length < found.total;
  } catch { if (expected === generation) failed.value = true; }
  finally { if (expected === generation) loading.value = false; }
}
watch(kind, () => { if (kind.value === 'specified_user' && page.value === 0) void loadUsers(); }, { immediate: true });
watch(() => [session.currentUser, session.switching] as const, () => { generation++; controller?.abort(); users.value = []; page.value = 0; more.value = true; loading.value = false; failed.value = false;
  if (!session.switching && kind.value === 'specified_user') void loadUsers(); });
onBeforeUnmount(() => { generation++; controller?.abort(); });
</script>
<template>
  <fieldset class="starter-editor" :disabled="disabled">
    <legend>{{ t('workflowDesigner.childStarter.title') }}</legend>
    <p>{{ t('workflowDesigner.childStarter.help') }}</p>
    <label>{{ t('workflowDesigner.childStarter.source') }}<ElSelect v-model="kind" data-testid="subprocess-starter-source" :disabled="disabled" :aria-label="t('workflowDesigner.childStarter.source')">
      <ElOption v-for="option in ['parent', 'specified_user', 'field_user', 'item_user']" :key="option" :value="option" :disabled="option === 'item_user' && multi?.source.kindKey !== 'role_members'" :label="t(`workflowDesigner.childStarter.${option}`)" />
    </ElSelect></label>
    <template v-if="kind === 'specified_user'">
      <label>{{ t('workflowDesigner.childStarter.user') }}<ElSelect v-model="userId" :disabled="disabled || loading" filterable :aria-label="t('workflowDesigner.childStarter.user')"><ElOption v-for="user in users" :key="user.id" :value="user.id" :label="user.name" /></ElSelect></label>
      <ElButton v-if="more || failed" :disabled="disabled || loading" @click="loadUsers">{{ t(failed ? 'workflowDesigner.subprocess.refresh' : 'workflowDesigner.childStarter.more') }}</ElButton>
      <p v-if="failed" role="alert">{{ t('workflowDesigner.subprocess.loadFailed') }}</p>
    </template>
    <label v-if="kind === 'field_user'">{{ t('workflowDesigner.childStarter.field') }}<ElSelect v-model="fieldKey" :disabled="disabled" :aria-label="t('workflowDesigner.childStarter.field')"><ElOption v-for="field in sourceFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" /></ElSelect></label>
    <label>{{ t('workflowDesigner.childStarter.result') }}<ElSelect v-model="resultFieldKey" data-testid="subprocess-starter-result" :disabled="disabled" clearable :aria-label="t('workflowDesigner.childStarter.result')"><ElOption v-for="field in resultFields" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" /></ElSelect></label>
  </fieldset>
</template>
<style scoped>
.starter-editor { display: grid; gap: 14px; border: 1px solid var(--el-border-color); border-radius: 6px; padding: 16px; }
.starter-editor legend { padding: 0 6px; font-weight: 600; }
.starter-editor label { display: grid; grid-template-columns: minmax(110px, 1fr) minmax(160px, 2fr); gap: 12px; align-items: center; }
.starter-editor p { margin: 0; color: var(--el-text-color-secondary); line-height: 1.6; }
@media (max-width: 480px) { .starter-editor label { grid-template-columns: 1fr; } }
</style>
