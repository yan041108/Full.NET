<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElDrawer, ElMessage, ElTable, ElTableColumn, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowFieldPolicies,
  type WorkflowFormField
} from '@fullnet/client-contracts';
import {
  createWorkflowDefinition,
  deleteWorkflowDefinitionVersion,
  getWorkflowDefinition,
  getWorkflowNodeTypeCatalog,
  publishWorkflowDefinition,
  setWorkflowDefinitionStatus,
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
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';

interface WorkflowVue3DesignerInstance {
  readDraft: () => WorkflowDefinitionDraft;
}

const { t, locale } = useAdminI18n();
const session = useSessionStore();
const definitions = ref<WorkflowDefinitionResponse[]>([]);
const versions = ref<WorkflowDefinitionVersionResponse[]>([]);
const selectedDefinitionId = ref<string>();
const selectedDefinition = ref<WorkflowDefinitionResponse>();
const selectedVersion = ref<WorkflowDefinitionVersionResponse>();
const startSchema = ref<WorkflowFormSchema>();
const initialValues = ref<WorkflowSubmission>({});
const businessType = ref('');
const businessId = ref('');
const loading = ref(false);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({
  bottomOffset: 8
});
watchLoading(loading);
const acting = ref(false);
const problem = ref<FullNetProblemDetails>();
const creating = ref(false);
const definitionKey = ref('');
const editingDefinition = ref<WorkflowDefinitionResponse>();
const workflowTree = ref<WorkflowVue3Node>();
const definitionDesigner = ref<WorkflowVue3DesignerInstance>();
const enabledNodeTypes = ref<readonly string[]>([]);
const publishFormVersionId = ref('');
const publishedForms = ref<WorkflowFormResponse[]>([]);
const businessTitleTemplate = ref('');
const gatewayFields = ref<readonly WorkflowFormField[]>([]);
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
  if (loading.value || acting.value || definitionStatus(definition) === 'archived') return;
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
  enabledNodeTypes.value = catalog.nodeTypes
    .filter(item => item.designable && item.publishable && item.executable)
    .map(item => item.nodeTypeKey);
  const unsupported = authoritative.draft.nodes.find(node =>
    !catalog.nodeTypes.some(item => item.nodeTypeKey === node.nodeTypeKey
      && item.designable && item.publishable && item.executable));
  if (unsupported !== undefined) {
    showDesignerError('client.unsupported_workflow_node');
    return;
  }
  try {
    editingDefinition.value = authoritative;
    businessTitleTemplate.value = authoritative.businessTitleTemplate ?? '';
    workflowTree.value = toWorkflowVue3Tree(authoritative.draft);
    publishedForms.value = forms.filter(form =>
      form.latestPublishedVersionId !== null && formStatus(form) === 'active');
    publishFormVersionId.value = publishedForms.value[0]?.latestPublishedVersionId ?? '';
    await loadGatewayFields();
  } catch (error: unknown) {
    showDesignerError(error instanceof Error ? error.message : 'client.invalid_workflow_definition_draft');
  }
}

/** 读取当前发布目标的不可变表单版本，供排他网关条件选择字段。 */
async function loadGatewayFields(): Promise<void> {
  gatewayFields.value = [];
  if (!publishFormVersionId.value) return;
  const result = await runManagementAction(
    () => getWorkflowStartForm(publishFormVersionId.value),
    'workflowDefinitions.loadFailed'
  );
  gatewayFields.value = result?.schema.sections.flatMap(section => section.fields) ?? [];
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
    () => updateWorkflowDefinitionDraft(
      current.id,
      current.draftRevision,
      draft,
      businessTitleTemplate.value),
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
  enabledNodeTypes.value = [];
  publishedForms.value = [];
  publishFormVersionId.value = '';
  gatewayFields.value = [];
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

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value);
}

function definitionStatusTagType(status: 'active' | 'disabled' | 'archived'): 'success' | 'warning' | 'info' {
  switch (status) {
    case 'active':
      return 'success';
    case 'disabled':
      return 'warning';
    default:
      return 'info';
  }
}

function definitionStatus(definition: WorkflowDefinitionResponse): 'active' | 'disabled' | 'archived' {
  const statusKey = (definition as WorkflowDefinitionResponse & { statusKey?: string }).statusKey;
  if (statusKey === 'disabled' || statusKey === 'archived') {
    return statusKey;
  }
  return 'active';
}

function formStatus(form: WorkflowFormResponse): 'active' | 'disabled' | 'archived' {
  const statusKey = (form as WorkflowFormResponse & { statusKey?: string }).statusKey;
  if (statusKey === 'disabled' || statusKey === 'archived') {
    return statusKey;
  }
  return 'active';
}

async function changeDefinitionStatus(
  definition: WorkflowDefinitionResponse,
  statusKey: 'active' | 'disabled' | 'archived'
): Promise<void> {
  const updated = await runManagementAction(
    () => setWorkflowDefinitionStatus(definition.id, statusKey, definition.version),
    'workflowDefinitions.operationFailed'
  );
  if (updated === undefined) {
    return;
  }
  definitions.value = definitions.value.map(item => item.id === updated.id ? updated : item);
  if (selectedDefinition.value?.id === updated.id) {
    selectedDefinition.value = updated;
  }
  if (editingDefinition.value?.id === updated.id) {
    closeEditor();
  }
  ElMessage.success(t(`workflowDefinitions.statusSuccess.${statusKey}`));
}

async function removeDefinitionVersion(version: WorkflowDefinitionVersionResponse): Promise<void> {
  await runManagementAction(
    async () => {
      await deleteWorkflowDefinitionVersion(version.id);
      if (selectedDefinitionId.value !== undefined) {
        versions.value = await listWorkflowDefinitionVersions(selectedDefinitionId.value);
        const refreshed = await getWorkflowDefinition(selectedDefinitionId.value);
        definitions.value = definitions.value.map(item => item.id === refreshed.id ? refreshed : item);
        selectedDefinition.value = refreshed;
      }
    },
    'workflowDefinitions.operationFailed'
  );
  ElMessage.success(t('workflowDefinitions.deleteVersionSuccess'));
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
    void nextTick(updateTableHeight);
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
    selectedDefinition.value = definition;
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
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowDefinitions.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowDefinitions.caption') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="workflow-definitions__layout art-split-layout">
      <el-card class="workflow-definitions__list art-table-card" shadow="never">
        <template #header>
          <div class="workflow-definitions__list-header">
            <h2>{{ t('workflowDefinitions.listTitle') }}</h2>
            <PermissionGate code="workflow.definitions.create">
              <el-button type="primary" size="small" data-testid="workflow-definition-create" :disabled="acting" @click="openCreate">
                {{ t('workflowDefinitions.create') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>

        <div ref="tableMainRef" class="art-crud-table-main">
        <el-table
          v-loading="loading"
          :data="definitions"
          :height="tableHeight"
          class="workflow-definitions__table"
          highlight-current-row
          row-key="id"
          empty-text=""
          :current-row-key="selectedDefinitionId"
        >
          <el-table-column
            :label="t('workflowDefinitions.definitionKey')"
            min-width="160"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <code translate="no">{{ row.definitionKey }}</code>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowDefinitions.status')" width="96" align="center">
            <template #default="{ row }">
              <el-tag size="small" :type="definitionStatusTagType(definitionStatus(row))">
                {{ t(`workflowDefinitions.statusLabel.${definitionStatus(row)}`) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            :label="t('workflowDefinitions.latestVersion')"
            min-width="120"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <span translate="no">{{ row.latestPublishedVersionId ?? '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowDefinitions.updatedAt')" width="168">
            <template #default="{ row }">
              <span translate="no">{{ formatDateTime(row.updatedAtUtc ?? row.createdAtUtc) }}</span>
            </template>
          </el-table-column>
          <el-table-column
            :label="t('workflowDefinitions.actions')"
            min-width="320"
            fixed="right"
          >
            <template #default="{ row }">
              <div class="workflow-definitions__actions">
                <PermissionGate code="workflow.definitions.update">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-edit"
                    :disabled="loading || acting || definitionStatus(row) === 'archived'"
                    @click="openEditor(row)"
                  >{{ t('workflowDefinitions.edit') }}</el-button>
                </PermissionGate>
                <el-button
                  size="small"
                  data-testid="workflow-definition-versions"
                  :disabled="loading || acting"
                  @click="openVersions(row)"
                >{{ t('workflowDefinitions.versions') }}</el-button>
                <PermissionGate v-if="definitionStatus(row) === 'active'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-disable"
                    :disabled="loading || acting"
                    @click="changeDefinitionStatus(row, 'disabled')"
                  >{{ t('workflowDefinitions.disable') }}</el-button>
                </PermissionGate>
                <PermissionGate v-if="definitionStatus(row) === 'disabled'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-enable"
                    :disabled="loading || acting"
                    @click="changeDefinitionStatus(row, 'active')"
                  >{{ t('workflowDefinitions.enable') }}</el-button>
                </PermissionGate>
                <PermissionGate v-if="definitionStatus(row) !== 'archived'" code="workflow.definitions.manage_status">
                  <el-button
                    size="small"
                    data-testid="workflow-definition-archive"
                    :disabled="loading || acting"
                    @click="changeDefinitionStatus(row, 'archived')"
                  >{{ t('workflowDefinitions.archive') }}</el-button>
                </PermissionGate>
              </div>
            </template>
          </el-table-column>
          <template #empty>
            <p v-if="!loading" class="workflow-definitions__empty">{{ t('workflowDefinitions.empty') }}</p>
          </template>
        </el-table>
        </div>
      </el-card>

      <el-card class="workflow-definitions__versions-panel art-form-card" shadow="never">
        <template #header>
          <h2>{{ t('workflowDefinitions.versionsTitle') }}</h2>
        </template>
        <p v-if="!selectedDefinitionId" class="workflow-definitions__empty workflow-definitions__versions-hint">
          {{ t('workflowDefinitions.selectVersions') }}
        </p>
        <div v-else-if="versions.length === 0" class="workflow-definitions__empty">
          {{ t('workflowDefinitions.noVersions') }}
        </div>
        <ul v-else class="workflow-definitions__versions">
          <li v-for="version in versions" :key="version.id">
            <span>{{ t('workflowDefinitions.version') }} {{ version.versionNumber }}</span>
            <time :datetime="version.publishedAtUtc">{{ formatDateTime(version.publishedAtUtc) }}</time>
            <div class="workflow-definitions__version-actions">
              <PermissionGate
                v-if="selectedDefinition && definitionStatus(selectedDefinition) === 'active'"
                code="workflow.instances.start"
              >
                <el-button
                  type="primary"
                  plain
                  size="small"
                  data-testid="workflow-definition-start"
                  :disabled="loading || acting"
                  @click="openStart(version)"
                >{{ t('workflowDefinitions.start') }}</el-button>
              </PermissionGate>
              <PermissionGate code="workflow.definitions.delete_version">
                <el-button
                  type="danger"
                  plain
                  size="small"
                  data-testid="workflow-definition-delete-version"
                  :disabled="loading || acting"
                  @click="removeDefinitionVersion(version)"
                >{{ t('workflowDefinitions.deleteVersion') }}</el-button>
              </PermissionGate>
            </div>
          </li>
        </ul>
      </el-card>
    </div>

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
      <label class="workflow-definitions__title-template">
        <span>{{ t('workflowDefinitions.businessTitleTemplate') }}</span>
        <input
          v-model="businessTitleTemplate"
          data-testid="workflow-definition-title-template"
          maxlength="256"
          :placeholder="t('workflowDefinitions.businessTitleTemplateHint')"
        />
      </label>
      <WorkflowVue3Designer
        ref="definitionDesigner"
        v-model="workflowTree"
        :disabled="acting"
        :enabled-node-types="enabledNodeTypes"
        :gateway-fields="gatewayFields"
        @validation-error="showDesignerError"
      />
      <div v-if="canLoadPublishForms" class="workflow-definitions__publish-row">
        <label>
          <span>{{ t('workflowDefinitions.formVersion') }}</span>
          <select v-model="publishFormVersionId" data-testid="workflow-definition-form-version" @change="loadGatewayFields">
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
.workflow-definitions { display: grid; gap: 1rem; min-height: 0; }
.workflow-definitions__layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}
.workflow-definitions__list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.workflow-definitions__list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}
.workflow-definitions__list :deep(.el-card__header) {
  padding: 12px 16px;
}
.workflow-definitions__list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.workflow-definitions__list-header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}
.workflow-definitions__table {
  flex: 1;
  min-height: 200px;
}
.workflow-definitions__versions-panel {
  flex: 0 0 320px;
  min-width: 280px;
  max-width: 380px;
  overflow: auto;
}
.workflow-definitions__versions-panel :deep(.el-card__header) {
  padding: 12px 16px;
}
.workflow-definitions__versions-panel :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}
.workflow-definitions__versions-panel :deep(.el-card__body) {
  padding: 12px 16px 16px;
}
.workflow-definitions__versions-hint {
  margin: 12px 0;
}
.workflow-definitions__version-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.workflow-definitions code { font-size: 0.85rem; }
.workflow-definitions__actions { display: flex; flex-wrap: wrap; gap: 0.35rem; }
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
@media (max-width: 960px) {
  .workflow-definitions__layout { flex-direction: column; }
  .workflow-definitions__versions-panel {
    flex: none;
    max-width: none;
    width: 100%;
  }
}
@media (max-width: 720px) {
  .workflow-definitions__panel--designer { inset: 1rem; }
  .workflow-definitions__business { grid-template-columns: 1fr; }
  .workflow-definitions__versions li { grid-template-columns: 1fr; }
}
</style>
