<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard } from 'element-plus';
import {
  createWorkflowFormDraft,
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  createWorkflowForm,
  getWorkflowForm,
  getWorkflowFormComponentCatalog,
  listWorkflowForms,
  publishWorkflowForm,
  updateWorkflowFormDraft,
  type WorkflowFormResponse
} from '../api/workflow-forms';
import { useSessionStore } from '../auth/session';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import VForm3WorkflowDesigner from '../workflow/VForm3WorkflowDesigner.vue';

interface VForm3WorkflowDesignerInstance {
  readSchema: () => WorkflowFormSchema;
}

const { t } = useAdminI18n();
const session = useSessionStore();
const forms = ref<WorkflowFormResponse[]>([]);
const selected = ref<WorkflowFormResponse>();
const localDraft = ref<WorkflowFormSchema>();
const catalog = ref<WorkflowFormComponentCatalogResponse>();
const formKey = ref('');
const creating = ref(false);
const busy = ref(false);
const problem = ref<FullNetProblemDetails>();
const designer = ref<VForm3WorkflowDesignerInstance>();

onMounted(loadForms);

async function loadForms(): Promise<void> {
  if (!session.can('workflow.forms.read')) return;
  await act(async () => {
    forms.value = await listWorkflowForms();
  }, 'workflowForms.loadFailed');
}

function openCreate(): void {
  formKey.value = '';
  creating.value = true;
  problem.value = undefined;
}

async function submitCreate(): Promise<void> {
  const key = formKey.value.trim();
  if (!key || busy.value) return;
  const created = await act(
    () => createWorkflowForm(key, createWorkflowFormDraft()),
    'workflowForms.operationFailed'
  );
  if (created !== undefined) {
    creating.value = false;
    await loadForms();
  }
}

async function openEditor(row: WorkflowFormResponse): Promise<void> {
  if (busy.value) return;
  const result = await act(
    () => Promise.all([getWorkflowForm(row.id), getWorkflowFormComponentCatalog()]),
    'workflowForms.loadFailed'
  );
  if (result !== undefined) {
    selected.value = result[0];
    localDraft.value = structuredClone(result[0].draft);
    catalog.value = result[1];
  }
}

async function saveDraft(): Promise<void> {
  const current = selected.value;
  if (current === undefined || localDraft.value === undefined || busy.value) return;
  let draft: WorkflowFormSchema;
  try {
    draft = designer.value?.readSchema() ?? localDraft.value;
  } catch (error: unknown) {
    showDesignerError(error instanceof Error ? error.message : 'client.invalid_workflow_form_draft');
    return;
  }
  const saved = await act(
    () => updateWorkflowFormDraft(current.id, current.draftRevision, draft),
    'workflowForms.operationFailed'
  );
  if (saved !== undefined) {
    replaceForm(saved);
    selected.value = saved;
    localDraft.value = structuredClone(saved.draft);
  }
}

async function publish(row: WorkflowFormResponse): Promise<void> {
  if (busy.value) return;
  const published = await act(
    () => publishWorkflowForm(row.id, { expectedRevision: row.draftRevision }),
    'workflowForms.operationFailed'
  );
  if (published === undefined) return;
  const authoritative = await act(() => getWorkflowForm(row.id), 'workflowForms.loadFailed');
  if (authoritative !== undefined) {
    replaceForm(authoritative);
    if (selected.value?.id === authoritative.id) {
      selected.value = authoritative;
      localDraft.value = structuredClone(authoritative.draft);
    }
  }
}

function closeEditor(): void {
  selected.value = undefined;
  localDraft.value = undefined;
  catalog.value = undefined;
  problem.value = undefined;
}

function showDesignerError(code: string): void {
  problem.value = {
    status: 400,
    code,
    title: t('workflowForms.operationFailed')
  };
}

function replaceForm(value: WorkflowFormResponse): void {
  forms.value = forms.value.map(item => item.id === value.id ? value : item);
}

async function act<T>(
  action: () => Promise<T>,
  fallbackKey: 'workflowForms.loadFailed' | 'workflowForms.operationFailed'
): Promise<T | undefined> {
  busy.value = true;
  problem.value = undefined;
  try {
    return await action();
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: 'client.workflow_form_failed', title: t(fallbackKey) };
    return undefined;
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="workflow-forms art-page-stack art-full-height" :aria-busy="busy">
    <header class="workflow-forms__header">
      <div>
        <h1 data-route-heading tabindex="-1">{{ t('workflowForms.title') }}</h1>
        <p>{{ t('workflowForms.caption') }}</p>
      </div>
      <PermissionGate code="workflow.forms.create">
        <el-button type="primary" data-testid="workflow-form-create" :disabled="busy" @click="openCreate">
          {{ t('workflowForms.create') }}
        </el-button>
      </PermissionGate>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never">
      <div v-if="forms.length === 0 && !busy" class="workflow-forms__empty">{{ t('workflowForms.empty') }}</div>
      <div v-else class="workflow-forms__table-wrap">
        <table>
          <thead><tr>
            <th>{{ t('workflowForms.formKey') }}</th>
            <th>{{ t('workflowForms.revision') }}</th>
            <th>{{ t('workflowForms.publishedVersion') }}</th>
            <th>{{ t('workflowForms.actions') }}</th>
          </tr></thead>
          <tbody>
            <tr v-for="row in forms" :key="row.id">
              <td><code translate="no">{{ row.formKey }}</code></td>
              <td>Revision {{ row.draftRevision }}</td>
              <td><code translate="no">{{ row.latestPublishedVersionId ?? '—' }}</code></td>
              <td class="workflow-forms__actions">
                <PermissionGate code="workflow.forms.update">
                  <el-button data-testid="workflow-form-edit" :disabled="busy" @click="openEditor(row)">
                    {{ t('workflowForms.edit') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate code="workflow.forms.publish">
                  <el-button type="primary" plain data-testid="workflow-form-publish" :disabled="busy" @click="publish(row)">
                    {{ t('workflowForms.publish') }}
                  </el-button>
                </PermissionGate>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-card>

    <aside v-if="creating" class="workflow-forms__panel" aria-modal="true" role="dialog">
      <h2>{{ t('workflowForms.createTitle') }}</h2>
      <label>
        <span>{{ t('workflowForms.formKey') }}</span>
        <input v-model="formKey" data-testid="workflow-form-key" autocomplete="off" />
      </label>
      <div class="workflow-forms__decision-bar">
        <el-button :disabled="busy" @click="creating = false">{{ t('workflowForms.close') }}</el-button>
        <el-button type="primary" data-testid="workflow-form-create-submit" :disabled="!formKey.trim()" :loading="busy" @click="submitCreate">
          {{ t('workflowForms.create') }}
        </el-button>
      </div>
    </aside>

    <aside v-if="selected && localDraft && catalog" class="workflow-forms__panel workflow-forms__panel--designer" aria-modal="true" role="dialog">
      <div class="workflow-forms__editor-heading">
        <div>
          <h2 translate="no">{{ selected.formKey }}</h2>
          <span>Revision {{ selected.draftRevision }}</span>
        </div>
        <el-button data-testid="workflow-form-close-editor" :disabled="busy" @click="closeEditor">
          {{ t('workflowForms.close') }}
        </el-button>
      </div>
      <VForm3WorkflowDesigner
        ref="designer"
        :schema="localDraft"
        :catalog="catalog"
        :disabled="busy"
        @update:schema="localDraft = $event"
        @validation-error="showDesignerError"
      />
      <div class="workflow-forms__decision-bar">
        <PermissionGate code="workflow.forms.update">
          <el-button type="primary" data-testid="workflow-form-save" :loading="busy" @click="saveDraft">
            {{ t('workflowForms.save') }}
          </el-button>
        </PermissionGate>
      </div>
    </aside>
  </section>
</template>

<style scoped>
.workflow-forms { display: grid; gap: 1rem; }
.workflow-forms__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-forms__header h1, .workflow-forms__panel h2 { margin: 0; color: var(--el-text-color-primary); }
.workflow-forms__header p { margin: 0.35rem 0 0; color: var(--el-text-color-secondary); }
.workflow-forms__table-wrap { overflow-x: auto; }
.workflow-forms table { width: 100%; border-collapse: collapse; }
.workflow-forms th, .workflow-forms td { padding: 0.8rem; border-bottom: 1px solid var(--el-border-color-lighter); text-align: left; }
.workflow-forms th { color: var(--el-text-color-secondary); font-size: 0.78rem; }
.workflow-forms__actions { display: flex; gap: 0.5rem; }
.workflow-forms__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-forms__panel { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--el-border-color); border-top: 4px solid var(--el-color-primary); background: var(--el-bg-color); box-shadow: var(--el-box-shadow-light); }
.workflow-forms__panel--designer { position: fixed; z-index: 2000; inset: 4vh 3vw; overflow: auto; }
.workflow-forms__panel label { display: grid; gap: 0.4rem; }
.workflow-forms__panel input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-forms__editor-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-forms__editor-heading div { display: grid; gap: 0.25rem; }
.workflow-forms__editor-heading span { color: var(--el-text-color-secondary); font-family: var(--art-font-mono, monospace); }
.workflow-forms__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 720px) {
  .workflow-forms__header { align-items: stretch; flex-direction: column; }
  .workflow-forms__panel--designer { inset: 1rem; }
}
</style>
