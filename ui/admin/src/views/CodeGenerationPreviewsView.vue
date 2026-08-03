<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessageBox, ElTag } from 'element-plus';
import {
  isCodeGenerationPreviewRequest,
  isFullNetProblemDetails,
  isPendingCodeGenerationRollbackApply,
  buildCodeGenerationRollbackApplyRunIds,
  type CodeGenerationPreviewArtifact,
  type CodeGenerationPreviewRequest,
  type CodeGenerationPreviewResponse,
  type CodeGenerationRunResponse,
  type CodeGenerationTemplateResponse,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  applyTrackedCodeGeneration,
  executeTrackedCodeGenerationRollback,
  listCodeGenerationRuns,
  previewTrackedCodeGeneration
} from '../api/code-generation-runs';
import {
  listCodeGenerationTemplates
} from '../api/code-generation-templates';

const session = useSessionStore();
const { t } = useAdminI18n();
const schemaText = ref(JSON.stringify({
  ownerKey: 'acme',
  moduleKey: 'catalog',
  entityKey: 'product',
  databaseTableName: 'acme_catalog_product',
  rootNamespace: 'Acme.Modules.Catalog',
  clrTypeName: 'Product',
  apiResourceName: 'products',
  permissionResourceName: 'products',
  dataScope: 'TenantRequired',
  hasVersion: true,
  columns: [
    {
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'TenantId',
      clrPropertyName: 'TenantId',
      jsonPropertyName: 'tenantId',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'Name',
      clrPropertyName: 'Name',
      jsonPropertyName: 'displayName',
      scalarType: 'String',
      isNullable: false,
      maxLength: 200,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'IsActive',
      clrPropertyName: 'IsActive',
      jsonPropertyName: 'isActive',
      scalarType: 'Boolean',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'Version',
      clrPropertyName: 'Version',
      jsonPropertyName: 'version',
      scalarType: 'Int64',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }
  ]
}, null, 2));
const preview = ref<CodeGenerationPreviewResponse>();
const selectedPath = ref('');
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const templates = ref<CodeGenerationTemplateResponse[]>([]);
const selectedTemplate = ref<CodeGenerationTemplateResponse>();
const templateLoading = ref(false);
const runs = ref<CodeGenerationRunResponse[]>([]);
const runLoading = ref(false);
const applying = ref(false);
const rollingBackId = ref<string>();
const reviewedPreview = ref<{
  runId: string;
  templateId: string;
  templateVersion: number;
  artifactCount: number;
}>();
const canReadTemplates = computed(
  () => session.can('codegen.templates.read')
);
const canReadRuns = computed(() => session.can('codegen.runs.read'));
const canExecuteRuns = computed(() => session.can('codegen.runs.execute'));
const canApplyRuns = computed(() => session.can('codegen.runs.apply'));
const canRollbackRuns = computed(() => session.can('codegen.runs.rollback'));

const selectedArtifact = computed<CodeGenerationPreviewArtifact | undefined>(
  () => preview.value?.artifacts.find(
    artifact => artifact.path === selectedPath.value
  )
);

onMounted(() => {
  void loadTemplates();
  void loadRuns();
});

async function loadRuns(): Promise<void> {
  if (!canReadRuns.value || runLoading.value) {
    return;
  }

  runLoading.value = true;
  try {
    runs.value = (await listCodeGenerationRuns()).items;
  } catch (error: unknown) {
    problem.value = readProblem(error, 'client.codegen_run_list_failed');
  } finally {
    runLoading.value = false;
  }
}

async function loadTemplates(): Promise<void> {
  if (!canReadTemplates.value || templateLoading.value) {
    return;
  }

  templateLoading.value = true;
  problem.value = undefined;
  try {
    templates.value = (await listCodeGenerationTemplates()).items;
  } catch (error: unknown) {
    problem.value = readProblem(
      error,
      'client.codegen_template_list_failed'
    );
  } finally {
    templateLoading.value = false;
  }
}

function loadTemplate(template: CodeGenerationTemplateResponse): void {
  invalidateReviewedPreview();
  selectedTemplate.value = template;
  schemaText.value = JSON.stringify(template.schema, null, 2);
}

function readSchema(): CodeGenerationPreviewRequest | undefined {
  let input: unknown;
  try {
    input = JSON.parse(schemaText.value);
  } catch {
    problem.value = clientProblem('client.codegen_invalid_json');
    return undefined;
  }

  if (!isCodeGenerationPreviewRequest(input)) {
    problem.value = clientProblem('client.codegen_invalid_schema');
    return undefined;
  }

  return input;
}

async function generatePreview(): Promise<void> {
  if (loading.value || !canExecuteRuns.value) {
    return;
  }

  problem.value = undefined;
  const input = readSchema();
  if (!input) {
    return;
  }

  loading.value = true;
  invalidateReviewedPreview();
  try {
    const selected = selectedTemplate.value;
    const source = selected
      && JSON.stringify(input) === JSON.stringify(selected.schema)
      ? {
          templateId: selected.id,
          templateVersion: selected.version
        }
      : { schema: input };
    const tracked = await previewTrackedCodeGeneration(source);
    preview.value = tracked.preview;
    if ('templateId' in source && source.templateId) {
      reviewedPreview.value = {
        runId: tracked.runId,
        templateId: source.templateId,
        templateVersion: source.templateVersion,
        artifactCount: tracked.preview.artifacts.length
      };
    }
    selectedPath.value = preview.value.artifacts[0]?.path ?? '';
    await loadRuns();
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : clientProblem('client.codegen_preview_failed');
  } finally {
    loading.value = false;
  }
}

function invalidateReviewedPreview(): void {
  reviewedPreview.value = undefined;
}

async function applyReviewedPreview(): Promise<void> {
  const reviewed = reviewedPreview.value;
  const selected = selectedTemplate.value;
  if (!canApplyRuns.value
    || applying.value
    || !reviewed
    || !selected
    || selected.id !== reviewed.templateId
    || selected.version !== reviewed.templateVersion) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('codeGeneration.applyConfirm', {
        name: selected.name,
        version: selected.version,
        count: reviewed.artifactCount
      }),
      t('codeGeneration.apply'),
      {
        type: 'warning',
        confirmButtonText: t('codeGeneration.apply'),
        cancelButtonText: t('status.back')
      }
    );
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = readProblem(error, 'client.codegen_apply_failed');
    return;
  }

  applying.value = true;
  problem.value = undefined;
  try {
    await applyTrackedCodeGeneration({ previewRunId: reviewed.runId });
    invalidateReviewedPreview();
    await loadRuns();
  } catch (error: unknown) {
    problem.value = readProblem(error, 'client.codegen_apply_failed');
  } finally {
    applying.value = false;
  }
}

async function rollbackApply(run: CodeGenerationRunResponse): Promise<void> {
  if (!canRollbackRuns.value
    || rollingBackId.value
    || run.operationKind !== 'apply'
    || run.status !== 'succeeded') {
    return;
  }

  try {
    const applyRunIds = buildCodeGenerationRollbackApplyRunIds(runs.value, run.id);
    const confirmKey = applyRunIds.length > 1
      ? 'codeGeneration.rollbackChainConfirm'
      : 'codeGeneration.rollbackConfirm';
    const confirmParams: Readonly<Record<string, string | number>> = applyRunIds.length > 1
      ? {
          id: run.id,
          count: applyRunIds.length,
          newest: applyRunIds[0]
        }
      : { id: run.id };
    await ElMessageBox.confirm(
      t(confirmKey, confirmParams),
      t('codeGeneration.rollback'),
      {
        type: 'warning',
        confirmButtonText: t('codeGeneration.rollback'),
        cancelButtonText: t('status.back')
      }
    );
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = readProblem(error, 'client.codegen_rollback_failed');
    return;
  }

  rollingBackId.value = run.id;
  problem.value = undefined;
  try {
    await executeTrackedCodeGenerationRollback(runs.value, run.id);
    await loadRuns();
  } catch (error: unknown) {
    problem.value = readProblem(error, 'client.codegen_rollback_failed');
  } finally {
    rollingBackId.value = undefined;
  }
}

function selectArtifact(artifact: CodeGenerationPreviewArtifact): void {
  selectedPath.value = artifact.path;
}

function clientProblem(code: string): FullNetProblemDetails {
  return {
    status: 400,
    code,
    title: t('codeGeneration.invalidInput')
  };
}

function readProblem(
  error: unknown,
  fallbackCode: string
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : clientProblem(fallbackCode);
}
</script>

<template>
  <section
    class="codegen-workbench art-page-stack art-full-height"
    :aria-busy="loading"
  >
    <header class="art-page-header codegen-workbench__header">
      <div>
        <p class="art-eyebrow">{{ t('codeGeneration.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">
          {{ t('codeGeneration.title') }}
        </h1>
        <p>{{ t('codeGeneration.description') }}</p>
      </div>
      <div class="codegen-workbench__safety">
        <span aria-hidden="true"></span>
        <strong>{{ t('codeGeneration.readOnly') }}</strong>
        <small>{{ t('codeGeneration.readOnlyHint') }}</small>
      </div>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ElCard
      v-if="canReadTemplates"
      shadow="never"
      class="codegen-workbench__templates"
      :aria-busy="templateLoading"
    >
      <template #header>
        <div class="codegen-workbench__card-heading">
          <div>
            <small>00</small>
            <h2>{{ t('codeGeneration.templatesTitle') }}</h2>
          </div>
          <ElButton
            plain
            :loading="templateLoading"
            @click="loadTemplates"
          >
            {{ t('codeGeneration.templatesRefresh') }}
          </ElButton>
        </div>
      </template>
      <nav :aria-label="t('codeGeneration.templatesTitle')">
        <p v-if="templates.length === 0" class="art-empty-state">
          {{ t('codeGeneration.templatesEmpty') }}
        </p>
        <button
          v-for="template in templates"
          :key="template.id"
          type="button"
          data-testid="codegen-template-load"
          :class="{ 'is-active': selectedTemplate?.id === template.id }"
          @click="loadTemplate(template)"
        >
          <strong>{{ template.name }}</strong>
          <small translate="no">
            v{{ template.version }} · {{ template.schemaSha256.slice(0, 12) }}
          </small>
        </button>
      </nav>
    </ElCard>

    <div class="codegen-workbench__grid">
      <ElCard shadow="never" class="codegen-workbench__schema">
        <template #header>
          <div class="codegen-workbench__card-heading">
            <div>
              <small>01</small>
              <h2>{{ t('codeGeneration.schemaTitle') }}</h2>
            </div>
            <ElTag effect="plain">JSON · ≤128</ElTag>
          </div>
        </template>
        <ElInput
          v-model="schemaText"
          data-testid="codegen-schema"
          type="textarea"
          :rows="25"
          resize="vertical"
          spellcheck="false"
          :aria-label="t('codeGeneration.schemaTitle')"
          @update:model-value="invalidateReviewedPreview"
        />
        <div class="codegen-workbench__action">
          <span>{{ t('codeGeneration.explicitScopeHint') }}</span>
          <ElButton
            type="primary"
            data-testid="codegen-preview"
            :loading="loading"
            :disabled="!canExecuteRuns"
            @click="generatePreview"
          >
            {{ t('codeGeneration.preview') }}
          </ElButton>
          <ElButton
            v-if="canApplyRuns"
            type="danger"
            plain
            data-testid="codegen-apply"
            :loading="applying"
            :disabled="!reviewedPreview || applying"
            @click="applyReviewedPreview"
          >
            {{ t('codeGeneration.apply') }}
          </ElButton>
        </div>
      </ElCard>

      <ElCard shadow="never" class="codegen-workbench__output">
        <template #header>
          <div class="codegen-workbench__card-heading">
            <div>
              <small>02</small>
              <h2>{{ t('codeGeneration.artifactsTitle') }}</h2>
            </div>
            <span class="codegen-workbench__count">
              {{ preview?.artifacts.length ?? 0 }}
            </span>
          </div>
        </template>

        <div
          v-if="preview"
          class="codegen-workbench__contract"
          :aria-label="t('codeGeneration.artifactsTitle')"
        >
          <code translate="no">{{ preview.databaseTableName }}</code>
          <span translate="no">{{ preview.readPermission }}</span>
          <span translate="no">{{ preview.writePermission }}</span>
        </div>

        <p v-if="!preview" class="art-empty-state">
          {{ t('codeGeneration.emptyArtifacts') }}
        </p>
        <div v-else class="codegen-workbench__browser">
          <nav :aria-label="t('codeGeneration.artifactsTitle')">
            <button
              v-for="artifact in preview.artifacts"
              :key="artifact.path"
              type="button"
              :class="{ 'is-active': artifact.path === selectedPath }"
              @click="selectArtifact(artifact)"
            >
              <span>{{ artifact.kind }}</span>
              <strong translate="no">{{ artifact.path }}</strong>
              <small translate="no">{{ artifact.sha256.slice(0, 12) }}</small>
            </button>
          </nav>
          <article v-if="selectedArtifact">
            <header>
              <strong translate="no">{{ selectedArtifact.path }}</strong>
              <code translate="no">{{ selectedArtifact.sha256 }}</code>
            </header>
            <pre
              tabindex="0"
              data-testid="codegen-content"
              :aria-label="selectedArtifact.path"
            ><code translate="no">{{ selectedArtifact.content }}</code></pre>
          </article>
        </div>
      </ElCard>
    </div>

    <ElCard
      v-if="canReadRuns"
      shadow="never"
      data-testid="codegen-run-history"
      class="codegen-workbench__runs"
    >
      <template #header>
        <div class="codegen-workbench__card-heading">
          <div>
            <small>03</small>
            <h2>{{ t('codeGeneration.runHistoryTitle') }}</h2>
          </div>
          <span class="codegen-workbench__count">{{ runs.length }}</span>
        </div>
      </template>
      <p v-if="runs.length === 0" class="art-empty-state">
        {{ t('codeGeneration.runHistoryEmpty') }}
      </p>
      <div v-else class="codegen-workbench__run-list">
        <article v-for="run in runs" :key="run.id">
          <strong translate="no">
            {{ run.moduleKey ?? '—' }} / {{ run.entityKey ?? '—' }}
          </strong>
          <span translate="no">{{ run.status }} · {{ run.operationKind }}</span>
          <code translate="no">{{ run.id }}</code>
          <small>
            {{ run.artifactCount }} · {{ run.startedAtUtc }}
          </small>
          <small translate="no">
            schema {{ run.schemaSha256?.slice(0, 12) ?? '—' }}
            · manifest {{ run.manifestSha256?.slice(0, 12) ?? '—' }}
          </small>
          <small translate="no">
            {{ run.requestedByUserId }} · {{ run.finishedAtUtc }}
          </small>
          <ElButton
            v-if="canRollbackRuns
              && isPendingCodeGenerationRollbackApply(runs, run)"
            type="warning"
            plain
            size="small"
            data-testid="codegen-rollback"
            :loading="rollingBackId === run.id"
            :disabled="!!rollingBackId"
            @click="rollbackApply(run)"
          >
            {{ t('codeGeneration.rollback') }}
          </ElButton>
        </article>
      </div>
    </ElCard>
  </section>
</template>

<style scoped>
.codegen-workbench {
  --codegen-ink: #172027;
  --codegen-grid: rgb(23 32 39 / 7%);
}

.codegen-workbench__header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 32px;
}

.codegen-workbench__safety {
  display: grid;
  min-width: 220px;
  grid-template-columns: 10px 1fr;
  gap: 3px 10px;
  padding: 14px 16px;
  border: 1px solid var(--fullnet-color-line);
  background: repeating-linear-gradient(
    135deg,
    transparent 0 8px,
    rgb(66 185 166 / 5%) 8px 9px
  );
}

.codegen-workbench__safety span {
  width: 9px;
  height: 9px;
  margin-top: 3px;
  border-radius: 50%;
  background: var(--fullnet-color-accent-bright);
  box-shadow: 0 0 0 5px rgb(66 185 166 / 12%);
}

.codegen-workbench__safety strong,
.codegen-workbench__safety small {
  grid-column: 2;
}

.codegen-workbench__safety small {
  color: var(--fullnet-color-ink-muted);
}

.codegen-workbench__template-grid {
  display: grid;
  grid-template-columns: minmax(220px, .7fr) minmax(320px, 1.3fr);
  gap: 16px;
}

.codegen-workbench__template-grid.is-write-only {
  grid-template-columns: minmax(320px, 1fr);
}

.codegen-workbench__template-grid nav {
  display: grid;
  max-height: 180px;
  overflow: auto;
}

.codegen-workbench__template-grid nav button {
  display: grid;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--fullnet-color-line);
  background: transparent;
  text-align: left;
  cursor: pointer;
}

.codegen-workbench__template-grid nav button.is-active {
  border-color: var(--fullnet-color-accent);
  background: var(--fullnet-color-canvas);
}

.codegen-workbench__template-grid nav small {
  color: var(--fullnet-color-ink-muted);
  font: 10px/1.3 ui-monospace, SFMono-Regular, Consolas, monospace;
}

.codegen-workbench__template-form {
  display: grid;
  gap: 10px;
}

.codegen-workbench__grid {
  display: grid;
  grid-template-columns: minmax(340px, .82fr) minmax(520px, 1.18fr);
  gap: 18px;
  min-height: 660px;
}

.codegen-workbench__run-list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 10px;
}

.codegen-workbench__run-list article {
  display: grid;
  gap: 5px;
  padding: 12px;
  border: 1px solid var(--fullnet-color-line);
  background: var(--fullnet-color-canvas);
}

.codegen-workbench__run-list code,
.codegen-workbench__run-list small {
  overflow: hidden;
  color: var(--fullnet-color-ink-muted);
  font-size: 10px;
  text-overflow: ellipsis;
}

.codegen-workbench__schema,
.codegen-workbench__output {
  min-width: 0;
}

.codegen-workbench__card-heading,
.codegen-workbench__card-heading > div {
  display: flex;
  align-items: center;
  gap: 12px;
}

.codegen-workbench__card-heading {
  justify-content: space-between;
}

.codegen-workbench__card-heading small {
  color: var(--fullnet-color-accent);
  font-weight: 800;
  letter-spacing: .12em;
}

.codegen-workbench__card-heading h2 {
  margin: 0;
}

.codegen-workbench__schema :deep(textarea) {
  min-height: 520px !important;
  border: 0;
  background:
    linear-gradient(90deg, var(--codegen-grid) 1px, transparent 1px) 0 0 / 24px 24px,
    linear-gradient(var(--codegen-grid) 1px, transparent 1px) 0 0 / 24px 24px,
    #fbfcfa;
  color: var(--codegen-ink);
  font: 12px/1.65 ui-monospace, SFMono-Regular, Consolas, monospace;
  tab-size: 2;
}

.codegen-workbench__action {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  margin-top: 16px;
  color: var(--fullnet-color-ink-muted);
  font-size: 12px;
}

.codegen-workbench__count {
  display: grid;
  width: 32px;
  height: 32px;
  place-items: center;
  border-radius: 50%;
  background: var(--codegen-ink);
  color: white;
  font-weight: 800;
}

.codegen-workbench__contract {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
}

.codegen-workbench__contract > * {
  padding: 5px 8px;
  border: 1px solid var(--fullnet-color-line);
  background: var(--fullnet-color-canvas);
  font-size: 11px;
}

.codegen-workbench__browser {
  display: grid;
  grid-template-columns: minmax(190px, .38fr) minmax(0, 1fr);
  min-height: 520px;
  overflow: hidden;
  border: 1px solid var(--fullnet-color-line);
}

.codegen-workbench__browser nav {
  overflow: auto;
  border-right: 1px solid var(--fullnet-color-line);
  background: #f5f7f4;
}

.codegen-workbench__browser nav button {
  display: grid;
  width: 100%;
  gap: 5px;
  padding: 13px 14px;
  border: 0;
  border-bottom: 1px solid var(--fullnet-color-line);
  background: transparent;
  color: var(--codegen-ink);
  text-align: left;
  cursor: pointer;
}

.codegen-workbench__browser nav button:hover,
.codegen-workbench__browser nav button.is-active {
  background: white;
}

.codegen-workbench__browser nav button.is-active {
  box-shadow: inset 3px 0 var(--fullnet-color-accent);
}

.codegen-workbench__browser nav span,
.codegen-workbench__browser nav small {
  color: var(--fullnet-color-ink-muted);
  font: 9px/1.2 ui-monospace, SFMono-Regular, Consolas, monospace;
  letter-spacing: .06em;
}

.codegen-workbench__browser nav strong {
  overflow-wrap: anywhere;
  font: 600 11px/1.45 ui-monospace, SFMono-Regular, Consolas, monospace;
}

.codegen-workbench__browser article {
  min-width: 0;
  background: var(--codegen-ink);
  color: #dbe7e1;
}

.codegen-workbench__browser article > header {
  display: grid;
  gap: 5px;
  padding: 13px 16px;
  border-bottom: 1px solid rgb(255 255 255 / 10%);
}

.codegen-workbench__browser article > header code {
  overflow: hidden;
  color: #7f918d;
  font-size: 9px;
  text-overflow: ellipsis;
}

.codegen-workbench__browser pre {
  max-height: 500px;
  margin: 0;
  padding: 18px;
  overflow: auto;
  white-space: pre;
}

.codegen-workbench__browser pre code {
  font: 11px/1.65 ui-monospace, SFMono-Regular, Consolas, monospace;
}

@media (max-width: 1100px) {
  .codegen-workbench__grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 700px) {
  .codegen-workbench__header {
    align-items: stretch;
    flex-direction: column;
  }

  .codegen-workbench__browser {
    grid-template-columns: 1fr;
  }

  .codegen-workbench__template-grid {
    grid-template-columns: 1fr;
  }

  .codegen-workbench__browser nav {
    max-height: 240px;
    border-right: 0;
    border-bottom: 1px solid var(--fullnet-color-line);
  }
}
</style>
