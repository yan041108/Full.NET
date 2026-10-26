<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { listWorkflowBusinessActions, type WorkflowBusinessAction } from '../api/workflow-business-actions';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowBusinessCommand, type WorkflowBusinessCommandConfiguration } from './workflow-business-command';
const props = defineProps<{ modelValue?: unknown; disabled: boolean }>();
const emit = defineEmits<{ save: [value: WorkflowBusinessCommandConfiguration]; cancel: [] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const items = ref<WorkflowBusinessAction[]>([]); const selected = ref(''); const failed = ref(false); const loading = ref(false); const invalid = ref(false);
let generation = 0; let controller: AbortController | undefined;
const identity = (entry: WorkflowBusinessAction) => `${entry.actionKey}@${entry.actionVersion}`;
const current = computed(() => items.value.find(item => identity(item) === selected.value));
const label = (entry: WorkflowBusinessAction) => entry.actionKey === 'data_approval.apply_serial_rule_update' ? t('workflowDesigner.businessCommand.updateRule')
  : entry.actionKey === 'data_approval.apply_serial_rule_disable' ? t('workflowDesigner.businessCommand.disableRule') : entry.actionKey;
watch(() => props.modelValue, value => {
  selected.value = ''; invalid.value = false;
  if (typeof value !== 'object' || value === null || !('businessCommandPolicy' in value)) return;
  try { const policy = readWorkflowBusinessCommand(value).businessCommandPolicy; selected.value = `${policy.actionKey}@${policy.actionVersion}`; }
  catch { invalid.value = true; }
}, { immediate: true });
async function load(): Promise<void> {
  controller?.abort(); controller = new AbortController(); const expected = ++generation;
  items.value = []; failed.value = false; loading.value = true;
  try { const result = await listWorkflowBusinessActions(controller.signal); if (expected === generation && !session.switching) items.value = result; }
  catch { if (expected === generation) failed.value = true; }
  finally { if (expected === generation) loading.value = false; }
}
// 切换身份先使旧目录失效；迟到的Host响应不能恢复到新的租户编辑上下文。
watch(() => [session.currentUser, session.switching] as const, () => {
  if (session.switching) { generation++; controller?.abort(); items.value = []; loading.value = true; }
  else void load();
}, { immediate: true });
onBeforeUnmount(() => { generation++; controller?.abort(); });
function save(): void {
  if (props.disabled || failed.value || loading.value || !current.value || session.switching) return;
  emit('save', { businessCommandPolicy: { actionKey: current.value.actionKey, actionVersion: current.value.actionVersion } });
}
</script>
<template>
  <section class="business-command-editor">
    <p>{{ t('workflowDesigner.businessCommand.placement') }}</p>
    <p v-if="failed || invalid" role="alert">{{ t('workflowDesigner.businessCommand.invalid') }}</p>
    <label for="workflow-business-action">{{ t('workflowDesigner.businessCommand.action') }}</label>
    <ElSelect id="workflow-business-action" v-model="selected" :loading="loading" :disabled="disabled || loading" :aria-label="t('workflowDesigner.businessCommand.action')">
      <ElOption v-for="entry in items" :key="identity(entry)" :value="identity(entry)" :label="`${label(entry)} · v${entry.actionVersion}`" />
    </ElSelect>
    <p v-if="!loading && !failed && items.length === 0" role="status">{{ t('workflowDesigner.businessCommand.empty') }}</p>
    <div class="actions"><ElButton @click="emit('cancel')">{{ t('common.cancel') }}</ElButton>
      <ElButton type="primary" data-testid="business-command-save" :disabled="disabled || loading || failed || !current || session.switching" @click="save">{{ t('workflowDesigner.businessCommand.save') }}</ElButton></div>
  </section>
</template>
<style scoped>
.business-command-editor { display: grid; gap: 16px; padding: 0 20px 20px; }
.business-command-editor p { margin: 0; line-height: 1.6; }
.actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 12px; }
</style>
