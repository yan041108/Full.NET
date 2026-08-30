<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElMessage } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowFieldPolicies
} from '@fullnet/client-contracts';
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

const { t } = useAdminI18n();
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
    <header>
      <h1 data-route-heading tabindex="-1">{{ t('workflowDefinitions.title') }}</h1>
      <p>{{ t('workflowDefinitions.caption') }}</p>
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
              <td>
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
.workflow-definitions header h1 { margin: 0; color: var(--el-text-color-primary); font-size: clamp(1.35rem, 2vw, 1.8rem); }
.workflow-definitions header p { margin: 0.35rem 0 0; color: var(--el-text-color-secondary); }
.workflow-definitions__table-wrap { overflow-x: auto; }
.workflow-definitions table { width: 100%; border-collapse: collapse; }
.workflow-definitions th, .workflow-definitions td { padding: 0.8rem; border-bottom: 1px solid var(--el-border-color-lighter); text-align: left; }
.workflow-definitions th { color: var(--el-text-color-secondary); font-size: 0.78rem; }
.workflow-definitions code { font-size: 0.76rem; }
.workflow-definitions__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-definitions__versions { display: grid; gap: 0.6rem; margin: 0; padding: 0; list-style: none; }
.workflow-definitions__versions li { display: grid; grid-template-columns: minmax(7rem, 1fr) minmax(12rem, 2fr) auto; align-items: center; gap: 0.75rem; padding: 0.7rem; border: 1px solid var(--el-border-color-lighter); border-radius: 10px; }
.workflow-definitions__business { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
.workflow-definitions__business label { display: grid; gap: 0.4rem; color: var(--el-text-color-regular); font-weight: 650; }
.workflow-definitions__business input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-definitions__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 720px) {
  .workflow-definitions__business { grid-template-columns: 1fr; }
  .workflow-definitions__versions li { grid-template-columns: 1fr; }
}
</style>
