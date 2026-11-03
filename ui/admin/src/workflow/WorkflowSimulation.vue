<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { ElButton, ElDrawer } from 'element-plus';
import type { WorkflowSimulationResponse, WorkflowFormSchema, WorkflowSubmission, WorkflowFieldPolicy } from '@fullnet/client-contracts';
import { simulateWorkflowDefinition } from '../api/workflow-observability';
import { getWorkflowStartForm } from '../api/workflow-runtime';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowFormRenderer from './WorkflowFormRenderer.vue';
import WorkflowStarterSelectionPicker from './WorkflowStarterSelectionPicker.vue';
import type { WorkflowAssigneeSelections } from '../api/workflow-starter-selections';

const props = defineProps<{ definitionId: string; draftRevision: number; formVersionId: string; draftState: string }>();
const { t } = useAdminI18n(), session = useSessionStore();
const open = ref(false), loading = ref(false), failed = ref(false), schema = ref<WorkflowFormSchema>(), sample = ref<WorkflowSubmission>({});
const empty = Object.freeze({}); const result = ref<WorkflowSimulationResponse>(); let epoch = 0; let controller: AbortController | undefined;
const selections = ref<WorkflowAssigneeSelections>({}), selectionsReady = ref(false);
const selectionSource = computed(() => ({ kind: 'simulation' as const, definitionId: props.definitionId,
  draftRevision: props.draftRevision, formVersionId: props.formVersionId }));
const allowed = computed(() => !session.switching && session.can('workflow.definitions.simulate') && session.can('workflow.definitions.read') && session.can('workflow.forms.read'));
const policies = computed(() => Object.fromEntries(schema.value?.sections.flatMap(section => section.fields).map(field => [field.fieldKey, field.required ? 'required' : 'editable']) ?? []) as Record<string, WorkflowFieldPolicy>);
function clear(): void { epoch++; controller?.abort(); controller = undefined; loading.value = false; failed.value = false; result.value = undefined; schema.value = undefined; sample.value = {}; selections.value = {}; selectionsReady.value = false; }
watch(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope,
  session.currentUser?.profileVersion, allowed.value, props.definitionId, props.draftRevision, props.formVersionId, props.draftState]), () => { open.value = false; clear(); }, { flush: 'sync' });
watch(open, value => { if (!value) clear(); }); onBeforeUnmount(clear);
onDeactivated(() => { open.value = false; clear(); });
async function show(): Promise<void> {
  if (!allowed.value || !props.formVersionId) return;
  clear(); open.value = true; loading.value = true; const current = epoch; const cancellation = new AbortController(); controller = cancellation;
  try { const form = await getWorkflowStartForm(props.formVersionId, cancellation.signal);
    if (current === epoch && allowed.value && !cancellation.signal.aborted) schema.value = form.schema;
  } catch { if (current === epoch && !cancellation.signal.aborted) failed.value = true; }
  finally { if (current === epoch) loading.value = false; }
}
async function simulate(): Promise<void> {
  if (!allowed.value || !schema.value || loading.value || !selectionsReady.value) return;
  result.value = undefined; failed.value = false; loading.value = true; const current = ++epoch;
  controller?.abort(); const cancellation = new AbortController(); controller = cancellation;
  try {
    const response = await simulateWorkflowDefinition(props.definitionId, { expectedRevision: props.draftRevision, formVersionId: props.formVersionId,
      values: sample.value, assigneeSelections: selections.value }, cancellation.signal);
    if (current !== epoch || cancellation.signal.aborted || !allowed.value) return;
    if (response.definitionId !== props.definitionId || response.draftRevision !== props.draftRevision || response.formVersionId !== props.formVersionId) throw new TypeError('workflow.simulation.binding_mismatch');
    result.value = response;
  } catch (error) {
    if (current === epoch && !cancellation.signal.aborted) {
      if (error !== null && typeof error === 'object' && 'status' in error && [401, 403, 404].includes(Number(error.status))) clear();
      result.value = undefined; failed.value = true;
    }
  }
  finally { if (current === epoch) loading.value = false; }
}
function editSample(value: WorkflowSubmission): void { epoch++; controller?.abort(); loading.value = false; sample.value = value; result.value = undefined; failed.value = false; }
function editSelections(value: WorkflowAssigneeSelections): void { epoch++; controller?.abort(); loading.value = false; selections.value = value; result.value = undefined; failed.value = false; }
</script>

<template>
  <div v-if="allowed">
    <el-button data-testid="workflow-simulation-open" :disabled="!formVersionId" @click="show">{{ t('workflowR5.simulation') }}</el-button>
    <el-drawer v-model="open" :title="t('workflowR5.simulation')" size="min(900px, 100vw)" destroy-on-close append-to-body>
      <div v-if="open && allowed" class="workflow-simulation">
        <p>{{ t('workflowR5.simulationHelp', { revision: draftRevision }) }}</p>
        <WorkflowFormRenderer v-if="schema" :key="formVersionId" :schema="schema" :submission="empty" :field-policies="policies" @update:patch="editSample" />
        <WorkflowStarterSelectionPicker v-if="schema" :source="selectionSource" :model-value="selections" @update:model-value="editSelections" @ready="selectionsReady = $event" />
        <p v-if="loading" role="status">{{ t('workflowR5.loading') }}</p>
        <p v-if="failed" role="alert">{{ t('workflowR5.failed') }}</p>
        <el-button data-testid="workflow-simulation-run" type="primary" :disabled="loading || !schema || !selectionsReady" @click="simulate">{{ t('workflowR5.run') }}</el-button>
        <section v-if="result" data-testid="workflow-simulation-result">
          <p>{{ t('workflowR5.boundaries') }}</p>
          <ol><li v-for="(item, index) in result.trace" :key="index">
            <code translate="no">{{ item.nodeKey }} · {{ item.nodeTypeKey }}</code>
            <span v-if="item.branchKey" translate="no"> / {{ item.branchKey }}</span>
            <p>{{ t('workflowR5.outcome') }}: <code translate="no">{{ item.outcomeKey }}</code> · {{ t('workflowR5.members', { count: item.memberCount }) }}</p>
            <p v-if="item.fixedDefinitionVersionId" translate="no">{{ item.fixedDefinitionVersionId }}</p>
            <p v-if="item.errorCode" role="status"><code translate="no">{{ item.errorCode }}</code></p>
          </li></ol>
        </section>
      </div>
    </el-drawer>
  </div>
</template>
<style scoped>
.workflow-simulation { display: grid; gap: 1rem; min-width: 0; } code { overflow-wrap: anywhere; } li { margin-block: .75rem; }
</style>
