<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElMessage } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowFieldPolicies
} from '@fullnet/client-contracts';
import {
  createWorkflowDefinition,
  getWorkflowDefinition,
  getWorkflowNodeTypeCatalog,
  publishWorkflowDefinition,
  updateWorkflowDefinitionDraft,
  type WorkflowDefinitionDraft
} from '../api/workflow-definitions';
import { listWorkflowForms, type WorkflowFormResponse } from '../api/workflow-forms';
import { useSessionStore } from '../auth/session';
import {
  getWorkflowStartForm,
  listWorkflowDefinitions,
  listWorkflowDefinitionVersions,
  startWorkflowInstance,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse,
  type WorkflowFormSchema,
  type WorkflowSubmission
} from '../api/workflow-runtime';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';
import WorkflowVue3Designer from '../workflow/WorkflowVue3Designer.vue';
import {
  toWorkflowVue3Tree,
  type WorkflowVue3Node
} from '../workflow/workflow-vue3-adapter';

interface WorkflowVue3DesignerInstance {
  readDraft: () => WorkflowDefinitionDraft;
}

const { t } = useAdminI18n();
const session = useSessionStore();
const definitions = ref<WorkflowDefinitionResponse[]>([]);
const versions = ref<WorkflowDefinitionVersionResponse[]>([]);
const selectedDefinitionId = ref<string>();
const selectedVersion = ref<WorkflowDefinitionVersionResponse>();
const startSchema = ref<WorkflowFormSchema>();
const initialValues = ref<WorkflowSubmission>({});
const businessType = ref('');
const businessId = ref('');
const loading = ref(false);
const acting = ref(false);
const problem = ref<FullNetProblemDetails>();
const creating = ref(false);
const definitionKey = ref('');
const editingDefinition = ref<WorkflowDefinitionResponse>();
const workflowTree = ref<WorkflowVue3Node>();
const definitionDesigner = ref<WorkflowVue3DesignerInstance>();
const publishedForms = ref<WorkflowFormResponse[]>([]);
const publishFormVersionId = ref('');
const canLoadPublishForms = computed(() =>
  session.can('workflow.definitions.publish') && session.can('workflow.forms.read'));

const startPolicies = computed<WorkflowFieldPolicies>(() => {
  const fields = startSchema.value?.sections.flatMap(section => section.fields) ?? [];
  return Object.fromEntries(fields.map(field => [
    field.fieldKey,
    field.required ? 'required' : 'editable'
  ]));
});

const canSubmit = computed(() => {
  if (!businessType.value.trim() || !businessId.value.trim() || startSchema.value === undefined) {
    return false;
  }
  return Object.entries(startPolicies.value).every(([key, policy]) =>
    policy !== 'required' || hasValue(initialValues.value[key]));
});

onMounted(loadDefinitions);

function openCreate(): void {
  definitionKey.value = '';
  creating.value = true;
  problem.value = undefined;
}

async function submitCreate(): Promise<void> {
  const key = definitionKey.value.trim();
  if (!key || acting.value) return;
  const draft = createDefaultDefinitionDraft();
  const created = await runManagementAction(
    () => createWorkflowDefinition(key, draft),
    'workflowDefinitions.operationFailed'
  );
  if (created !== undefined) {
    definitions.value = [...definitions.value, created];
    creating.value = false;
    await openEditor(created);
  }
}

async function openEditor(definition: WorkflowDefinitionResponse): Promise<void> {
  if (loading.value || acting.value) return;
  const result = await runManagementAction(
    () => Promise.all([
      getWorkflowDefinition(definition.id),
      getWorkflowNodeTypeCatalog(),
      canLoadPublishForms.value ? listWorkflowForms() : Promise.resolve([])
    ]),
    'workflowDefinitions.loadFailed'
  );
  if (result === undefined) return;
  const [authoritative, catalog, forms] = result;
  const unsupported = authoritative.draft.nodes.find(node =>
    !catalog.nodeTypes.some(item => item.nodeTypeKey === node.nodeTypeKey
      && item.designable && item.publishable && item.executable));
  if (unsupported !== undefined) {
    showDesignerError('client.unsupported_workflow_node');
    return;
  }
  try {
    editingDefinition.value = authoritative;
    workflowTree.value = toWorkflowVue3Tree(authoritative.draft);
    publishedForms.value = forms.filter(form => form.latestPublishedVersionId !== null);
    publishFormVersionId.value = publishedForms.value[0]?.latestPublishedVersionId ?? '';
  } catch (error: unknown) {
    showDesignerError(error instanceof Error ? error.message : 'client.invalid_workflow_definition_draft');
  }
}

async function saveDefinitionDraft(): Promise<void> {
  const current = editingDefinition.value;
  if (current === undefined || workflowTree.value === undefined || acting.value) return;
  const designer = definitionDesigner.value;
  if (designer?.readDraft === undefined) {
    showDesignerError('client.workflow_designer_not_ready');
    return;
  }
  let draft: WorkflowDefinitionDraft;
  try {
    draft = designer.readDraft();
  } catch (error: unknown) {
    showDesignerError(error instanceof Error ? error.message : 'client.invalid_workflow_definition_draft');
    return;
  }
  const saved = await runManagementAction(
    () => updateWorkflowDefinitionDraft(current.id, current.draftRevision, draft),
    'workflowDefinitions.operationFailed'
  );
  if (saved !== undefined) {
    editingDefinition.value = saved;
    definitions.value = definitions.value.map(item => item.id === saved.id ? saved : item);
    workflowTree.value = toWorkflowVue3Tree(saved.draft);
  }
}

async function publishDefinition(): Promise<void> {
  const current = editingDefinition.value;
  if (current === undefined || !publishFormVersionId.value || acting.value) return;
  const authoritative = await runManagementAction(
    async () => {
      await publishWorkflowDefinition(current.id, current.draftRevision, publishFormVersionId.value);
      return getWorkflowDefinition(current.id);
    },
    'workflowDefinitions.operationFailed'
  );
  if (authoritative !== undefined) {
    editingDefinition.value = authoritative;
    definitions.value = definitions.value.map(item => item.id === authoritative.id ? authoritative : item);
    ElMessage.success(t('workflowDefinitions.publishSuccess'));
  }
}

function closeEditor(): void {
  editingDefinition.value = undefined;
  workflowTree.value = undefined;
  publishedForms.value = [];
  publishFormVersionId.value = '';
}

function createDefaultDefinitionDraft(): WorkflowDefinitionDraft {
  return {
    schemaVersion: 1,
    nodes: [
      { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['approval'] } },
      { nodeKey: 'approval', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1, config: { nodeName: '审批人', nextNodeKeys: ['end'] } },
      { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } }
    ]
  };
}

async function runManagementAction<T>(
  action: () => Promise<T>,
  fallbackKey: 'workflowDefinitions.loadFailed' | 'workflowDefinitions.operationFailed'
): Promise<T | undefined> {
  acting.value = true;
  problem.value = undefined;
  try {
    return await action();
  } catch (error: unknown) {
    problem.value = toProblem(error, fallbackKey);
    return undefined;
  } finally {
    acting.value = false;
  }
}

function showDesignerError(code: string): void {
  problem.value = { status: 400, code, title: t('workflowDefinitions.operationFailed') };
}

async function loadDefinitions(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    definitions.value = await listWorkflowDefinitions();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowDefinitions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function openVersions(definition: WorkflowDefinitionResponse): Promise<void> {
  if (loading.value || acting.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    versions.value = await listWorkflowDefinitionVersions(definition.id);
    selectedDefinitionId.value = definition.id;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowDefinitions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function openStart(version: WorkflowDefinitionVersionResponse): Promise<void> {
  if (loading.value || acting.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    const form = await getWorkflowStartForm(version.formVersionId);
    selectedVersion.value = version;
    startSchema.value = form.schema;
    initialValues.value = {};
    businessType.value = '';
    businessId.value = '';
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowDefinitions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function submitStart(): Promise<void> {
  const version = selectedVersion.value;
  if (acting.value || version === undefined || !canSubmit.value) {
    return;
  }
  acting.value = true;
  problem.value = undefined;
  try {
    await startWorkflowInstance(
      version.id,
      businessType.value.trim(),
      businessId.value.trim(),
      initialValues.value,
      createIdempotencyKey()
    );
    ElMessage.success(t('workflowDefinitions.startSuccess'));
    closeStart();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowDefinitions.operationFailed');
  } finally {
    acting.value = false;
  }
}

function closeStart(): void {
  selectedVersion.value = undefined;
  startSchema.value = undefined;
  initialValues.value = {};
  businessType.value = '';
  businessId.value = '';
}

function hasValue(value: unknown): boolean {
  return value !== null
    && value !== undefined
    && (typeof value !== 'string' || value.trim().length > 0);
}

function createIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
}

function toProblem(
  error: unknown,
  fallbackKey: 'workflowDefinitions.loadFailed' | 'workflowDefinitions.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.workflow_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="workflow-definitions art-page-stack art-full-height" :aria-busy="loading || acting">
    <header class="workflow-definitions__header">
      <div>
        <h1 data-route-heading tabindex="-1">{{ t('workflowDefinitions.title') }}</h1>
        <p>{{ t('workflowDefinitions.caption') }}</p>
      </div>
      <PermissionGate code="workflow.definitions.create">
        <el-button type="primary" data-testid="workflow-definition-create" :disabled="acting" @click="openCreate">
          {{ t('workflowDefinitions.create') }}
        </el-button>
      </PermissionGate>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never">
      <div v-if="definitions.length === 0 && !loading" class="workflow-definitions__empty">
        {{ t('workflowDefinitions.empty') }}
      </div>
      <div v-else class="workflow-definitions__table-wrap">
        <table>
          <thead><tr>
            <th>{{ t('workflowDefinitions.definitionKey') }}</th>
            <th>{{ t('workflowDefinitions.latestVersion') }}</th>
            <th>{{ t('workflowDefinitions.updatedAt') }}</th>
            <th>{{ t('workflowDefinitions.actions') }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="definition in definitions" :key="definition.id">
              <td><code translate="no">{{ definition.definitionKey }}</code></td>
              <td><code translate="no">{{ definition.latestPublishedVersionId ?? '—' }}</code></td>
              <td>{{ definition.updatedAtUtc ?? definition.createdAtUtc }}</td>
              <td class="workflow-definitions__actions">
                <PermissionGate code="workflow.definitions.update">
                  <el-button
                    data-testid="workflow-definition-edit"
                    :disabled="loading || acting"
                    @click="openEditor(definition)"
                  >{{ t('workflowDefinitions.edit') }}</el-button>
                </PermissionGate>
                <el-button
                  data-testid="workflow-definition-versions"
                  :disabled="loading || acting"
                  @click="openVersions(definition)"
                >{{ t('workflowDefinitions.versions') }}</el-button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-card>

    <aside v-if="creating" class="workflow-definitions__panel" aria-modal="true" role="dialog">
      <h2>{{ t('workflowDefinitions.createTitle') }}</h2>
      <label>
        <span>{{ t('workflowDefinitions.definitionKey') }}</span>
        <input v-model="definitionKey" data-testid="workflow-definition-key" autocomplete="off" />
      </label>
      <div class="workflow-definitions__decision-bar">
        <el-button :disabled="acting" @click="creating = false">{{ t('workflowDefinitions.close') }}</el-button>
        <el-button type="primary" data-testid="workflow-definition-create-submit" :disabled="!definitionKey.trim()" :loading="acting" @click="submitCreate">
          {{ t('workflowDefinitions.create') }}
        </el-button>
      </div>
    </aside>

    <aside
      v-if="editingDefinition && workflowTree"
      class="workflow-definitions__panel workflow-definitions__panel--designer"
      aria-modal="true"
      role="dialog"
    >
      <div class="workflow-definitions__editor-heading">
        <div>
          <h2 translate="no">{{ editingDefinition.definitionKey }}</h2>
          <span>Revision {{ editingDefinition.draftRevision }}</span>
        </div>
        <el-button data-testid="workflow-definition-close-editor" :disabled="acting" @click="closeEditor">
          {{ t('workflowDefinitions.close') }}
        </el-button>
      </div>
      <WorkflowVue3Designer
        ref="definitionDesigner"
        v-model="workflowTree"
        :disabled="acting"
        @validation-error="showDesignerError"
      />
      <div v-if="canLoadPublishForms" class="workflow-definitions__publish-row">
        <label>
          <span>{{ t('workflowDefinitions.formVersion') }}</span>
          <select v-model="publishFormVersionId" data-testid="workflow-definition-form-version">
            <option value="">{{ t('workflowDefinitions.selectFormVersion') }}</option>
            <option
              v-for="form in publishedForms"
              :key="form.latestPublishedVersionId ?? form.id"
              :value="form.latestPublishedVersionId ?? ''"
            >{{ form.formKey }}</option>
          </select>
        </label>
      </div>
      <div class="workflow-definitions__decision-bar">
        <PermissionGate code="workflow.definitions.update">
          <el-button type="primary" data-testid="workflow-definition-save" :loading="acting" @click="saveDefinitionDraft">
            {{ t('workflowDefinitions.save') }}
          </el-button>
        </PermissionGate>
        <PermissionGate v-if="canLoadPublishForms" code="workflow.definitions.publish">
          <el-button type="success" data-testid="workflow-definition-publish" :loading="acting" :disabled="!publishFormVersionId" @click="publishDefinition">
            {{ t('workflowDefinitions.publish') }}
          </el-button>
        </PermissionGate>
      </div>
    </aside>

    <el-card v-if="selectedDefinitionId" shadow="never">
      <div v-if="versions.length === 0" class="workflow-definitions__empty">
        {{ t('workflowDefinitions.noVersions') }}
      </div>
      <ul v-else class="workflow-definitions__versions">
        <li v-for="version in versions" :key="version.id">
          <span>{{ t('workflowDefinitions.version') }} {{ version.versionNumber }}</span>
          <time :datetime="version.publishedAtUtc">{{ version.publishedAtUtc }}</time>
          <PermissionGate code="workflow.instances.start">
            <el-button
              type="primary"
              plain
              data-testid="workflow-definition-start"
              :disabled="loading || acting"
              @click="openStart(version)"
            >{{ t('workflowDefinitions.start') }}</el-button>
          </PermissionGate>
        </li>
      </ul>
    </el-card>

    <el-drawer
      :model-value="startSchema !== undefined"
      :title="t('workflowDefinitions.startTitle')"
      size="min(680px, 94vw)"
      @close="closeStart"
    >
      <template v-if="startSchema">
        <div class="workflow-definitions__business">
          <label>
            <span>{{ t('workflowDefinitions.businessType') }}</span>
            <input v-model="businessType" data-testid="workflow-business-type" />
          </label>
          <label>
            <span>{{ t('workflowDefinitions.businessId') }}</span>
            <input v-model="businessId" data-testid="workflow-business-id" />
          </label>
        </div>
        <WorkflowFormRenderer
          :schema="startSchema"
          :submission="{}"
          :field-policies="startPolicies"
          @update:patch="initialValues = $event"
        />
        <div class="workflow-definitions__decision-bar">
          <el-button :disabled="acting" @click="closeStart">
            {{ t('workflowDefinitions.close') }}
          </el-button>
          <el-button
            type="primary"
            data-testid="workflow-start-submit"
            :loading="acting"
            :disabled="!canSubmit"
            @click="submitStart"
          >{{ t('workflowDefinitions.submit') }}</el-button>
        </div>
      </template>
    </el-drawer>
  </section>
</template>

<style scoped>
.workflow-definitions { display: grid; gap: 1rem; }
.workflow-definitions__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-definitions header h1 { margin: 0; color: var(--el-text-color-primary); font-size: clamp(1.35rem, 2vw, 1.8rem); }
.workflow-definitions header p { margin: 0.35rem 0 0; color: var(--el-text-color-secondary); }
.workflow-definitions__table-wrap { overflow-x: auto; }
.workflow-definitions table { width: 100%; border-collapse: collapse; }
.workflow-definitions th, .workflow-definitions td { padding: 0.8rem; border-bottom: 1px solid var(--el-border-color-lighter); text-align: left; }
.workflow-definitions th { color: var(--el-text-color-secondary); font-size: 0.78rem; }
.workflow-definitions code { font-size: 0.76rem; }
.workflow-definitions__actions { display: flex; gap: 0.5rem; }
.workflow-definitions__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-definitions__panel { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--el-border-color); border-top: 4px solid var(--el-color-primary); background: var(--el-bg-color); box-shadow: var(--el-box-shadow-light); }
.workflow-definitions__panel--designer { position: fixed; z-index: 2000; inset: 3vh 2vw; overflow: auto; }
.workflow-definitions__panel h2 { margin: 0; }
.workflow-definitions__panel label { display: grid; gap: 0.4rem; }
.workflow-definitions__panel input, .workflow-definitions__panel select { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-definitions__editor-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-definitions__editor-heading div { display: grid; gap: 0.25rem; }
.workflow-definitions__editor-heading span { color: var(--el-text-color-secondary); font-family: var(--art-font-mono, monospace); }
.workflow-definitions__publish-row { display: grid; grid-template-columns: minmax(18rem, 28rem); justify-content: end; }
.workflow-definitions__versions { display: grid; gap: 0.6rem; margin: 0; padding: 0; list-style: none; }
.workflow-definitions__versions li { display: grid; grid-template-columns: minmax(7rem, 1fr) minmax(12rem, 2fr) auto; align-items: center; gap: 0.75rem; padding: 0.7rem; border: 1px solid var(--el-border-color-lighter); border-radius: 10px; }
.workflow-definitions__business { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
.workflow-definitions__business label { display: grid; gap: 0.4rem; color: var(--el-text-color-regular); font-weight: 650; }
.workflow-definitions__business input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-definitions__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 720px) {
  .workflow-definitions__header { align-items: stretch; flex-direction: column; }
  .workflow-definitions__panel--designer { inset: 1rem; }
  .workflow-definitions__business { grid-template-columns: 1fr; }
  .workflow-definitions__versions li { grid-template-columns: 1fr; }
}
</style>
