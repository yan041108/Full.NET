<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElOption, ElPagination, ElSelect, ElTag } from 'element-plus';
import type { WorkflowRecipientCandidateResponse } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import { getWorkflowStarterSelections, listWorkflowStarterSelectionCandidates, type WorkflowStarterSelectionDescriptor,
  type WorkflowAssigneeSelections } from '../api/workflow-starter-selections';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ versionId: string; modelValue: WorkflowAssigneeSelections }>();
const emit = defineEmits<{ 'update:modelValue': [value: WorkflowAssigneeSelections]; ready: [value: boolean] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const descriptors = ref<readonly WorkflowStarterSelectionDescriptor[]>([]);
const currentKey = ref(''); const page = ref(1); const total = ref(0);
const candidates = ref<readonly WorkflowRecipientCandidateResponse[]>([]);
const selectedNames = ref<Record<string, WorkflowRecipientCandidateResponse>>({});
const loaded = ref(false); const loading = ref(false); const failed = ref(false); const metadataFailed = ref(false);
let epoch = 0; let request = 0; let metadataController: AbortController | undefined; let candidatesController: AbortController | undefined;
const current = computed(() => descriptors.value.find(item => item.selectionKey === currentKey.value));
const selected = computed(() => chosenFor(currentKey.value));
const chosenTotal = computed(() => Object.values(props.modelValue).reduce((sum, ids) => sum + ids.length, 0));
const context = computed(() => JSON.stringify([props.versionId, session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.tenantId, session.currentUser?.scope, session.switching, session.can('workflow.instances.start')]));
watch(context, () => {
  epoch++; request++; metadataController?.abort(); candidatesController?.abort(); descriptors.value = []; candidates.value = []; selectedNames.value = {};
  loaded.value = false; loading.value = false; metadataFailed.value = false; failed.value = false; currentKey.value = ''; page.value = 1; total.value = 0;
  emit('update:modelValue', {}); emit('ready', false); void loadMetadata();
}, { immediate: true, flush: 'sync' });
watch([() => props.modelValue, loaded, descriptors], () => {
  emit('ready', loaded.value && descriptors.value.every(item => {
    const ids = chosenFor(item.selectionKey);
    return ids.length >= 1 && ids.length <= item.maximumUsers && new Set(ids).size === ids.length;
  }) && Object.keys(props.modelValue).every(name => descriptors.value.some(item => item.selectionKey === name)) && chosenTotal.value <= 160);
}, { deep: true });
watch(currentKey, () => { page.value = 1; candidates.value = []; total.value = 0; void loadCandidates(); });
onBeforeUnmount(() => { epoch++; request++; metadataController?.abort(); candidatesController?.abort(); });

function chosenFor(key: string): readonly string[] {
  return Object.hasOwn(props.modelValue, key) ? props.modelValue[key] ?? [] : [];
}

async function loadMetadata(): Promise<void> {
  if (!session.can('workflow.instances.start') || session.switching) return;
  const captured = epoch; metadataController?.abort(); metadataController = new AbortController(); metadataFailed.value = false;
  try {
    const metadata = await getWorkflowStarterSelections(props.versionId, metadataController.signal);
    if (captured !== epoch) return;
    descriptors.value = metadata.items; loaded.value = true; currentKey.value = metadata.items[0]?.selectionKey ?? '';
  } catch { if (captured === epoch) metadataFailed.value = true; }
}

async function loadCandidates(): Promise<void> {
  const descriptor = current.value; const captured = epoch; const currentRequest = ++request;
  candidatesController?.abort();
  if (!descriptor || !loaded.value) return;
  candidatesController = new AbortController(); loading.value = true; failed.value = false;
  try {
    const result = await listWorkflowStarterSelectionCandidates(props.versionId, descriptor.selectionKey, page.value, candidatesController.signal);
    if (captured !== epoch || currentRequest !== request) return;
    candidates.value = result.items; total.value = result.total;
  } catch { if (captured === epoch && currentRequest === request) failed.value = true; }
  finally { if (captured === epoch && currentRequest === request) loading.value = false; }
}

function toggle(user: WorkflowRecipientCandidateResponse): void {
  const descriptor = current.value;
  if (!descriptor || loading.value || !session.can('workflow.instances.start') || session.switching) return;
  const removing = selected.value.includes(user.id);
  if (!removing && descriptor.modeKey === 'multiple' && (selected.value.length >= descriptor.maximumUsers || chosenTotal.value >= 160)) return;
  if (!removing && descriptor.modeKey === 'single' && selected.value.length === 0 && chosenTotal.value >= 160) return;
  const users = removing ? selected.value.filter(id => id !== user.id) : descriptor.modeKey === 'single' ? [user.id] : [...selected.value, user.id];
  const next = { ...props.modelValue }; if (users.length) next[descriptor.selectionKey] = users; else delete next[descriptor.selectionKey];
  selectedNames.value = { ...selectedNames.value, [user.id]: user };
  const all = new Set(Object.values(next).flat()); selectedNames.value = Object.fromEntries(Object.entries(selectedNames.value).filter(([id]) => all.has(id)));
  emit('update:modelValue', next);
}
function remove(id: string): void {
  const user = selectedNames.value[id]; if (user) toggle(user);
}
</script>

<template>
  <section v-if="descriptors.length || metadataFailed" class="workflow-starter-selection" :aria-label="t('workflowDefinitions.starterSelection.title')">
    <strong>{{ t('workflowDefinitions.starterSelection.title') }}</strong>
    <p>{{ t('workflowDefinitions.starterSelection.help', { count: chosenTotal }) }}</p>
    <el-button v-if="metadataFailed" @click="loadMetadata">{{ t('workflowDefinitions.starterSelection.retry') }}</el-button>
    <template v-else>
      <el-select v-model="currentKey" data-testid="workflow-starter-selection-source" :aria-label="t('workflowDefinitions.starterSelection.activity')">
        <el-option v-for="item in descriptors" :key="item.selectionKey" :value="item.selectionKey"
          :label="`${item.nodeName} · ${t(`workflowDefinitions.starterSelection.range.${item.rangeKey}` as MessageKey)} (${chosenFor(item.selectionKey).length}/${item.maximumUsers})`" />
      </el-select>
      <div class="workflow-starter-selection__chosen">
        <el-tag v-for="id in selected" :key="id" closable @close="remove(id)">{{ selectedNames[id]?.displayName ?? id }}</el-tag>
      </div>
      <el-button v-if="failed" @click="loadCandidates">{{ t('workflowDefinitions.starterSelection.retry') }}</el-button>
      <div v-for="user in candidates" :key="user.id" class="workflow-starter-selection__candidate">
        <span>{{ user.displayName }} <small>{{ user.username }}</small></span>
        <el-button :disabled="loading || !selected.includes(user.id) && current?.modeKey === 'multiple' && (selected.length >= current.maximumUsers || chosenTotal >= 160)"
          :data-testid="`workflow-starter-candidate-${user.id}`" @click="toggle(user)">{{ selected.includes(user.id) ? t('workflowDefinitions.starterSelection.remove') : t('workflowDefinitions.starterSelection.choose') }}</el-button>
      </div>
      <p v-if="!loading && !failed && candidates.length === 0">{{ t('workflowDefinitions.starterSelection.empty') }}</p>
      <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev, pager, next" @current-change="loadCandidates" />
    </template>
  </section>
</template>

<style scoped>
.workflow-starter-selection { display: grid; gap: .75rem; margin-block: 1rem; padding: 1rem; border: 1px solid var(--el-border-color-lighter); border-radius: 8px; }
.workflow-starter-selection p { margin: 0; color: var(--el-text-color-secondary); }
.workflow-starter-selection__chosen { display: flex; flex-wrap: wrap; gap: .5rem; }
.workflow-starter-selection__candidate { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
.workflow-starter-selection__candidate small { display: block; color: var(--el-text-color-secondary); }
</style>
