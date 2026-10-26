<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElPagination, ElSelect } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import { readFlowAdministratorUserIds } from './workflow-vue3-adapter';
const props = defineProps<{ userIds: readonly string[]; disabled: boolean }>();
const emit = defineEmits<{ save: [ids: string[]]; cancel: [] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const selected = ref([...props.userIds]); const result = ref<Awaited<ReturnType<typeof listWorkflowRecipientCandidates>>>();
const loading = ref(false); const failed = ref(false); const page = ref(1); const closed = ref(false);
const identity = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, ...(session.currentUser?.permissions ?? [])].join('|'));
let epoch = 0; let controller: AbortController | undefined;
function cancel(): void { epoch++; controller?.abort(); controller = undefined; }
function close(): void { if (closed.value) return; closed.value = true; cancel(); selected.value = []; result.value = undefined; emit('cancel'); }
async function load(value = 1): Promise<void> {
  if (closed.value || loading.value || props.disabled) return;
  cancel(); const ticket = epoch; controller = new AbortController(); loading.value = true; failed.value = false; page.value = value;
  try { const candidates = await listWorkflowRecipientCandidates(value, 20, controller.signal); if (ticket === epoch) result.value = candidates; }
  catch { if (ticket === epoch) { result.value = undefined; failed.value = true; } }
  finally { if (ticket === epoch) loading.value = false; }
}
function save(): void { if (!closed.value && !props.disabled && !loading.value) emit('save', readFlowAdministratorUserIds(selected.value)); }
watch(identity, close, { flush: 'sync' }); onMounted(() => load()); onBeforeUnmount(cancel);
</script>
<template>
  <div class="flow-administrators" v-if="!closed" data-testid="workflow-flow-administrators-editor">
    <p>{{ t('workflowDesigner.administrators.help') }}</p>
    <ElAlert v-if="failed" :title="t('workflowDesigner.administrators.loadFailed')" type="error" :closable="false" />
    <ElSelect v-model="selected" multiple filterable :multiple-limit="20" :disabled="disabled || loading" :loading="loading"
      :aria-label="t('workflowDesigner.administrators.title')" data-testid="workflow-flow-administrators-select">
      <ElOption v-for="id in selected.filter(id => !result?.items.some(person => person.id === id))" :key="id" :value="id" :label="id" />
      <ElOption v-for="person in result?.items ?? []" :key="person.id" :value="person.id" :label="person.displayName || person.username" />
    </ElSelect>
    <ElPagination v-if="result && result.total > 20" :current-page="page" :page-size="20" :total="result.total" :disabled="loading || disabled" layout="prev, pager, next" @current-change="load" />
    <ElButton v-if="failed" :disabled="loading || disabled" @click="load(page)">{{ t('workflowDesigner.assignee.retryCandidates') }}</ElButton>
    <ElButton :disabled="loading || disabled" data-testid="workflow-flow-administrators-save" type="primary" @click="save">{{ t('workflowDesigner.administrators.save') }}</ElButton>
  </div>
</template>
<style scoped>
.flow-administrators { display: grid; gap: 1rem; }
.flow-administrators p { margin: 0; line-height: 1.6; }
</style>
