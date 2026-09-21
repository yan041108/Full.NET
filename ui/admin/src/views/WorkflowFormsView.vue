<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTable, ElTableColumn, ElTag } from 'element-plus';
import {
  createWorkflowFormDraft,
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowFormComponentCatalogResponse,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  createWorkflowForm,
  deleteWorkflowFormVersion,
  getWorkflowForm,
  getWorkflowFormComponentCatalog,
  listWorkflowFormVersions,
  listWorkflowForms,
  publishWorkflowForm,
  setWorkflowFormStatus,
  updateWorkflowFormDraft,
  type WorkflowFormResponse,
  type WorkflowFormVersionResponse
} from '../api/workflow-forms';
import { useSessionStore } from '../auth/session';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import VForm3WorkflowDesigner from '../workflow/VForm3WorkflowDesigner.vue';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';

interface VForm3WorkflowDesignerInstance {
  readSchema: () => WorkflowFormSchema;
}

const { t, locale } = useAdminI18n();

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value);
}

function formStatusTagType(status: 'active' | 'disabled' | 'archived'): 'success' | 'warning' | 'info' {
  switch (status) {
    case 'active':
      return 'success';
    case 'disabled':
      return 'warning';
    default:
      return 'info';
  }
}
const session = useSessionStore();
const forms = ref<WorkflowFormResponse[]>([]);
const versions = ref<WorkflowFormVersionResponse[]>([]);
const selectedFormId = ref<string>();
const selectedForm = ref<WorkflowFormResponse>();
const selected = ref<WorkflowFormResponse>();
const localDraft = ref<WorkflowFormSchema>();
const catalog = ref<WorkflowFormComponentCatalogResponse>();
const formKey = ref('');
const creating = ref(false);
const busy = ref(false);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({
  bottomOffset: 8
});
watchLoading(busy);
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
  if (busy.value || formStatus(row) === 'archived') return;
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

function formStatus(form: WorkflowFormResponse): 'active' | 'disabled' | 'archived' {
  const statusKey = (form as WorkflowFormResponse & { statusKey?: string }).statusKey;
  if (statusKey === 'disabled' || statusKey === 'archived') {
    return statusKey;
  }
  return 'active';
}

async function changeFormStatus(
  form: WorkflowFormResponse,
  statusKey: 'active' | 'disabled' | 'archived'
): Promise<void> {
  const version = Number((form as WorkflowFormResponse & { version?: number }).version ?? 0);
  const updated = await act(
    () => setWorkflowFormStatus(form.id, statusKey, version),
    'workflowForms.operationFailed'
  );
  if (updated === undefined) {
    return;
  }
  replaceForm(updated);
  if (selectedForm.value?.id === updated.id) {
    selectedForm.value = updated;
  }
  if (selected.value?.id === updated.id) {
    closeEditor();
  }
  ElMessage.success(t(`workflowForms.statusSuccess.${statusKey}`));
}

async function openVersions(form: WorkflowFormResponse): Promise<void> {
  if (busy.value) {
    return;
  }
  const rows = await act(
    () => listWorkflowFormVersions(form.id),
    'workflowForms.loadFailed'
  );
  if (rows === undefined) {
    return;
  }
  versions.value = rows;
  selectedFormId.value = form.id;
  selectedForm.value = form;
}

async function removeFormVersion(version: WorkflowFormVersionResponse): Promise<void> {
  await act(
    async () => {
      await deleteWorkflowFormVersion(version.id);
      if (selectedFormId.value !== undefined) {
        versions.value = await listWorkflowFormVersions(selectedFormId.value);
        const refreshed = await getWorkflowForm(selectedFormId.value);
        replaceForm(refreshed);
        selectedForm.value = refreshed;
      }
    },
    'workflowForms.operationFailed'
  );
  ElMessage.success(t('workflowForms.deleteVersionSuccess'));
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
    void nextTick(updateTableHeight);
  }
}
</script>

<template>
  <section class="workflow-forms art-page-stack art-full-height" :aria-busy="busy">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowForms.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowForms.caption') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div class="workflow-forms__layout art-split-layout">
      <el-card class="workflow-forms__list art-table-card" shadow="never">
        <template #header>
          <div class="workflow-forms__list-header">
            <h2>{{ t('workflowForms.listTitle') }}</h2>
            <PermissionGate code="workflow.forms.create">
              <el-button type="primary" size="small" data-testid="workflow-form-create" :disabled="busy" @click="openCreate">
                {{ t('workflowForms.create') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>
        <div ref="tableMainRef" class="art-crud-table-main">
        <el-table
          v-loading="busy"
          :data="forms"
          :height="tableHeight"
          row-key="id"
          highlight-current-row
          empty-text=""
          :current-row-key="selectedFormId"
          class="workflow-forms__table"
        >
          <el-table-column :label="t('workflowForms.formKey')" min-width="140" show-overflow-tooltip>
            <template #default="{ row }"><code translate="no">{{ row.formKey }}</code></template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.status')" width="96" align="center">
            <template #default="{ row }">
              <el-tag size="small" :type="formStatusTagType(formStatus(row))">
                {{ t(`workflowForms.statusLabel.${formStatus(row)}`) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.revision')" width="110">
            <template #default="{ row }">Revision {{ row.draftRevision }}</template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.publishedVersion')" min-width="120" show-overflow-tooltip>
            <template #default="{ row }"><span translate="no">{{ row.latestPublishedVersionId ?? '—' }}</span></template>
          </el-table-column>
          <el-table-column :label="t('workflowForms.actions')" min-width="360" fixed="right">
            <template #default="{ row }">
              <div class="workflow-forms__actions">
                <PermissionGate code="workflow.forms.update">
                  <el-button size="small" data-testid="workflow-form-edit" :disabled="busy || formStatus(row) === 'archived'" @click="openEditor(row)">
                    {{ t('workflowForms.edit') }}
                  </el-button>
                </PermissionGate>
                <el-button size="small" data-testid="workflow-form-versions" :disabled="busy" @click="openVersions(row)">
                  {{ t('workflowForms.versions') }}
                </el-button>
                <PermissionGate code="workflow.forms.publish">
                  <el-button type="primary" plain size="small" data-testid="workflow-form-publish" :disabled="busy || formStatus(row) !== 'active'" @click="publish(row)">
                    {{ t('workflowForms.publish') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row) === 'active'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-disable" :disabled="busy" @click="changeFormStatus(row, 'disabled')">
                    {{ t('workflowForms.disable') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row) === 'disabled'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-enable" :disabled="busy" @click="changeFormStatus(row, 'active')">
                    {{ t('workflowForms.enable') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate v-if="formStatus(row) !== 'archived'" code="workflow.forms.manage_status">
                  <el-button size="small" data-testid="workflow-form-archive" :disabled="busy" @click="changeFormStatus(row, 'archived')">
                    {{ t('workflowForms.archive') }}
                  </el-button>
                </PermissionGate>
              </div>
            </template>
          </el-table-column>
          <template #empty>
            <p v-if="!busy" class="workflow-forms__empty">{{ t('workflowForms.empty') }}</p>
          </template>
        </el-table>
        </div>
      </el-card>

      <el-card class="workflow-forms__versions-panel art-form-card" shadow="never">
        <template #header><h2>{{ t('workflowForms.versionsTitle') }}</h2></template>
        <p v-if="!selectedFormId" class="workflow-forms__empty">{{ t('workflowForms.selectVersions') }}</p>
        <div v-else-if="versions.length === 0" class="workflow-forms__empty">{{ t('workflowForms.noVersions') }}</div>
        <ul v-else class="workflow-forms__versions">
          <li v-for="version in versions" :key="version.id">
            <span>{{ t('workflowForms.version') }} {{ version.versionNumber }}</span>
            <time :datetime="version.publishedAtUtc">{{ formatDateTime(version.publishedAtUtc) }}</time>
            <PermissionGate code="workflow.forms.delete_version">
              <el-button type="danger" plain size="small" data-testid="workflow-form-delete-version" :disabled="busy" @click="removeFormVersion(version)">
                {{ t('workflowForms.deleteVersion') }}
              </el-button>
            </PermissionGate>
          </li>
        </ul>
      </el-card>
    </div>

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
.workflow-forms { display: grid; gap: 1rem; min-height: 0; }
.workflow-forms__layout { display: flex; flex: 1; gap: 12px; min-height: 0; }
.workflow-forms__list { flex: 1 1 0; min-width: 0; min-height: 0; }
.workflow-forms__list :deep(.el-card__body) { padding-top: 0; }
.workflow-forms__list :deep(.el-card__header) { padding: 12px 16px; }
.workflow-forms__list-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.workflow-forms__list-header h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-forms__versions-panel { flex: 0 0 300px; max-width: 360px; overflow: auto; }
.workflow-forms__versions-panel :deep(.el-card__header) { padding: 12px 16px; }
.workflow-forms__versions-panel :deep(.el-card__header) h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-forms__panel h2 { margin: 0; color: var(--el-text-color-primary); }
.workflow-forms__actions { display: flex; flex-wrap: wrap; gap: 0.35rem; }
.workflow-forms__versions { display: grid; gap: 0.75rem; margin: 0; padding: 0; list-style: none; }
.workflow-forms__versions li { display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; }
.workflow-forms__empty { padding: 2.5rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
.workflow-forms__panel { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--el-border-color); border-top: 4px solid var(--el-color-primary); background: var(--el-bg-color); box-shadow: var(--el-box-shadow-light); }
.workflow-forms__panel--designer { position: fixed; z-index: 2000; inset: 4vh 3vw; overflow: auto; }
.workflow-forms__panel label { display: grid; gap: 0.4rem; }
.workflow-forms__panel input { min-height: 38px; padding: 0.5rem 0.7rem; border: 1px solid var(--el-border-color); border-radius: 8px; color: var(--el-text-color-primary); background: var(--el-bg-color); font: inherit; }
.workflow-forms__editor-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
.workflow-forms__editor-heading div { display: grid; gap: 0.25rem; }
.workflow-forms__editor-heading span { color: var(--el-text-color-secondary); font-family: var(--art-font-mono, monospace); }
.workflow-forms__decision-bar { display: flex; justify-content: flex-end; gap: 0.65rem; padding-top: 1rem; border-top: 1px solid var(--el-border-color-lighter); }
@media (max-width: 960px) {
  .workflow-forms__layout { flex-direction: column; }
  .workflow-forms__versions-panel { flex: none; max-width: none; width: 100%; }
}
@media (max-width: 720px) {
  .workflow-forms__panel--designer { inset: 1rem; }
}
</style>
