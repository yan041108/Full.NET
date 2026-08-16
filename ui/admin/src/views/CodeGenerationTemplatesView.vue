<script setup lang="ts">
import { computed, onMounted, ref, toRaw } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn
} from 'element-plus';
import {
  isCodeGenerationPreviewRequest,
  isFullNetProblemDetails,
  type CodeGenerationPreviewRequest,
  type CodeGenerationTemplateResponse,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  listCodeGenerationCatalogColumns,
  listCodeGenerationCatalogTables,
  syncCodeGenerationCatalogColumns
} from '../api/code-generation-catalog';
import {
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from '../api/code-generation-templates';

const session = useSessionStore();
const { t } = useAdminI18n();
const defaultSchema = (): CodeGenerationPreviewRequest => ({
  ownerKey: 'acme',
  moduleKey: 'catalog',
  entityKey: 'product',
  databaseTableName: 'acme_catalog_product',
  rootNamespace: 'Acme.Modules.Catalog',
  clrTypeName: 'Product',
  apiResourceName: 'products',
  permissionResourceName: 'products',
  dataScope: 'TenantRequired',
  entityCapabilities: {
    deleteMode: 'soft.delete',
    hasCreatedAudit: true,
    hasUpdatedAudit: true,
    hasDeletedAudit: true,
    hasVersion: true,
    ownershipMode: 'none'
  },
  scene: 'single',
  relationships: [],
  columns: []
});

const schema = ref<CodeGenerationPreviewRequest>(defaultSchema());
const schemaText = ref(JSON.stringify(defaultSchema(), null, 2));
const showAdvancedJson = ref(false);
const templates = ref<CodeGenerationTemplateResponse[]>([]);
const selectedTemplate = ref<CodeGenerationTemplateResponse>();
const templateName = ref('');
const templateDescription = ref('');
const tableNames = ref<string[]>([]);
const skippedColumnNames = ref<string[]>([]);
const addedColumnNames = ref<string[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const changing = ref(false);
const confirmingDelete = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('codegen.templates.create'));
const canUpdate = computed(() => session.can('codegen.templates.update'));
const canDelete = computed(() => session.can('codegen.templates.delete'));
const canReadCatalog = computed(() => session.can('codegen.catalog.read'));
const showForm = computed(() => {
  if (selectedTemplate.value) {
    return canUpdate.value || canDelete.value;
  }
  return canCreate.value;
});

onMounted(async () => {
  await load();
  await loadTables();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listCodeGenerationTemplates(page.value, pageSize.value);
    templates.value = result.items;
    total.value = result.total;
    page.value = result.page;
    pageSize.value = result.pageSize;
  } catch (error: unknown) {
    problem.value = readProblem(error, 'codeGenerationTemplates.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadTables(): Promise<void> {
  if (!canReadCatalog.value) {
    return;
  }
  try {
    tableNames.value = (await listCodeGenerationCatalogTables())
      .map(item => item.tableName);
  } catch (error: unknown) {
    problem.value = readProblem(error, 'codeGenerationTemplates.operationFailed');
  }
}

function loadTemplate(template: CodeGenerationTemplateResponse): void {
  selectedTemplate.value = template;
  templateName.value = template.name;
  templateDescription.value = template.description ?? '';
  schema.value = structuredClone(toRaw(template.schema));
  schemaText.value = JSON.stringify(template.schema, null, 2);
  skippedColumnNames.value = [];
  addedColumnNames.value = [];
}

function applySchemaText(): void {
  const parsed = readSchema();
  if (parsed) {
    schema.value = parsed;
  }
}

function syncSchemaText(): void {
  schemaText.value = JSON.stringify(schema.value, null, 2);
}

async function loadColumnsFromTable(): Promise<void> {
  if (!canReadCatalog.value || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const result = await listCodeGenerationCatalogColumns(
      schema.value.databaseTableName
    );
    schema.value = {
      ...schema.value,
      columns: result.columns
    };
    skippedColumnNames.value = result.skippedColumnNames;
    addedColumnNames.value = [];
    syncSchemaText();
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
}

async function syncColumns(): Promise<void> {
  if (!canReadCatalog.value || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const result = await syncCodeGenerationCatalogColumns(
      schema.value.databaseTableName,
      schema.value.columns
    );
    schema.value = {
      ...schema.value,
      columns: result.columns
    };
    skippedColumnNames.value = result.skippedColumnNames;
    addedColumnNames.value = result.addedColumnNames;
    syncSchemaText();
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
}

async function saveTemplate(): Promise<void> {
  const current = currentSchema();
  if (!current || changing.value || !canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createCodeGenerationTemplate({
      name: templateName.value,
      description: templateDescription.value.trim() || null,
      schema: current
    });
    upsertTemplate(saved);
    loadTemplate(saved);
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
}

async function updateTemplate(): Promise<void> {
  const selected = selectedTemplate.value;
  const current = currentSchema();
  if (!selected || !current || changing.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await updateCodeGenerationTemplate(selected.id, {
      name: templateName.value,
      description: templateDescription.value.trim() || null,
      schema: current,
      version: selected.version
    });
    upsertTemplate(saved);
    loadTemplate(saved);
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
}

async function removeTemplate(): Promise<void> {
  const selected = selectedTemplate.value;
  if (!selected || changing.value || confirmingDelete.value || !canDelete.value) {
    return;
  }
  confirmingDelete.value = true;
  try {
    await ElMessageBox.confirm(
      t('codeGenerationTemplates.confirmDelete', { name: selected.name }),
      t('codeGeneration.templateDelete'),
      {
        type: 'warning',
        confirmButtonText: t('codeGeneration.templateDelete'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await deleteCodeGenerationTemplate(selected.id, selected.version);
    templates.value = templates.value.filter(item => item.id !== selected.id);
    total.value = Math.max(0, total.value - 1);
    selectedTemplate.value = undefined;
    templateName.value = '';
    templateDescription.value = '';
    schema.value = defaultSchema();
    schemaText.value = JSON.stringify(defaultSchema(), null, 2);
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
    confirmingDelete.value = false;
  }
}

function currentSchema(): CodeGenerationPreviewRequest | undefined {
  if (showAdvancedJson.value) {
    return readSchema();
  }
  if (!isCodeGenerationPreviewRequest(schema.value)
    || schema.value.columns.length === 0) {
    problem.value = clientProblem('client.codegen_invalid_schema');
    return undefined;
  }
  return schema.value;
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

function upsertTemplate(saved: CodeGenerationTemplateResponse): void {
  const index = templates.value.findIndex(item => item.id === saved.id);
  if (index < 0) {
    templates.value = [saved, ...templates.value];
    total.value += 1;
    return;
  }
  templates.value.splice(index, 1, saved);
}

function clientProblem(code: string): FullNetProblemDetails {
  return { status: 400, code, title: t('codeGeneration.invalidInput') };
}

function readProblem(
  error: unknown,
  fallbackCode: 'codeGenerationTemplates.loadFailed' | 'codeGenerationTemplates.operationFailed'
    = 'codeGenerationTemplates.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="code-generation-templates-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('codeGenerationTemplates.eyebrow') }}</p>
      <h1>{{ t('codeGenerationTemplates.title') }}</h1>
      <p>{{ t('codeGenerationTemplates.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selectedTemplate ? t('codeGenerationTemplates.editTitle') : t('codeGenerationTemplates.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <ElInput v-model="templateName" data-testid="codegen-template-name" maxlength="128" :placeholder="t('codeGeneration.templateName')" />
        <ElInput v-model="templateDescription" type="textarea" :rows="2" maxlength="512" :placeholder="t('codeGeneration.templateDescription')" />
        <ElSelect
          v-if="canReadCatalog"
          v-model="schema.databaseTableName"
          filterable
          data-testid="codegen-template-table"
          :placeholder="t('codeGenerationTemplates.table')"
        >
          <ElOption v-for="tableName in tableNames" :key="tableName" :label="tableName" :value="tableName" />
        </ElSelect>
        <ElInput v-else v-model="schema.databaseTableName" :placeholder="t('codeGenerationTemplates.table')" />
        <div class="art-form-actions">
          <PermissionGate code="codegen.catalog.read">
            <ElButton data-testid="codegen-template-load-table" :disabled="changing" @click="loadColumnsFromTable">
              {{ t('codeGenerationTemplates.loadTable') }}
            </ElButton>
            <ElButton data-testid="codegen-template-sync-columns" :disabled="changing" @click="syncColumns">
              {{ t('codeGenerationTemplates.syncColumns') }}
            </ElButton>
          </PermissionGate>
        </div>
        <ElInput v-model="schema.ownerKey" :placeholder="t('codeGenerationTemplates.ownerKey')" />
        <ElInput v-model="schema.moduleKey" :placeholder="t('codeGenerationTemplates.moduleKey')" />
        <ElInput v-model="schema.entityKey" :placeholder="t('codeGenerationTemplates.entityKey')" />
        <ElInput v-model="schema.rootNamespace" :placeholder="t('codeGenerationTemplates.rootNamespace')" />
        <ElInput v-model="schema.clrTypeName" :placeholder="t('codeGenerationTemplates.clrTypeName')" />
        <ElInput v-model="schema.apiResourceName" :placeholder="t('codeGenerationTemplates.apiResourceName')" />
        <ElInput v-model="schema.permissionResourceName" :placeholder="t('codeGenerationTemplates.permissionResourceName')" />
        <ElSelect v-model="schema.dataScope" :placeholder="t('codeGenerationTemplates.dataScope')">
          <ElOption label="TenantRequired" value="TenantRequired" />
          <ElOption label="HostOnly" value="HostOnly" />
          <ElOption label="Global" value="Global" />
        </ElSelect>
        <ElSelect v-if="'scene' in schema" v-model="schema.scene" :placeholder="t('codeGenerationTemplates.scene')">
          <ElOption label="single" value="single" />
          <ElOption label="tree" value="tree" />
          <ElOption label="master.detail" value="master.detail" />
          <ElOption label="many.to.many" value="many.to.many" />
        </ElSelect>
        <p v-if="skippedColumnNames.length">
          {{ t('codeGenerationTemplates.skippedColumns', { names: skippedColumnNames.join(', ') }) }}
        </p>
        <p v-if="addedColumnNames.length">
          {{ t('codeGenerationTemplates.addedColumns', { names: addedColumnNames.join(', ') }) }}
        </p>
        <h3>{{ t('codeGenerationTemplates.columnsTitle') }}</h3>
        <ElTable :data="schema.columns" stripe>
          <ElTableColumn prop="databaseName" :label="t('codeGenerationTemplates.columnName')" min-width="140" />
          <ElTableColumn :label="t('codeGenerationTemplates.controlKind')" min-width="120">
            <template #default="{ row }">
              <ElSelect v-if="row.ui" v-model="row.ui.controlKind">
                <ElOption label="text" value="text" />
                <ElOption label="textarea" value="textarea" />
                <ElOption label="number" value="number" />
                <ElOption label="switch" value="switch" />
                <ElOption label="datetime" value="datetime" />
                <ElOption label="uuid" value="uuid" />
              </ElSelect>
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.showInList')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.showInList" />
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.includeInCreate')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.includeInCreate" />
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.includeInUpdate')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.includeInUpdate" />
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.required')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.required" />
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.queryable')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.queryable" />
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('codeGenerationTemplates.unique')" width="90">
            <template #default="{ row }">
              <ElSwitch v-if="row.ui" v-model="row.ui.unique" />
            </template>
          </ElTableColumn>
        </ElTable>
        <ElButton text @click="showAdvancedJson = !showAdvancedJson">
          {{ t('codeGenerationTemplates.advancedJson') }}
        </ElButton>
        <ElInput
          v-if="showAdvancedJson"
          v-model="schemaText"
          data-testid="codegen-template-schema"
          type="textarea"
          :rows="16"
          spellcheck="false"
          @change="applySchemaText"
        />
        <div class="art-form-actions">
          <PermissionGate code="codegen.templates.create">
            <ElButton v-if="!selectedTemplate" data-testid="codegen-template-save" :disabled="changing || !templateName.trim()" @click="saveTemplate">
              {{ t('codeGeneration.templateSave') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="codegen.templates.update">
            <ElButton v-if="selectedTemplate" data-testid="codegen-template-update" :disabled="changing" @click="updateTemplate">
              {{ t('codeGeneration.templateUpdate') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="codegen.templates.delete">
            <ElButton v-if="selectedTemplate" type="danger" plain data-testid="codegen-template-delete" :disabled="changing || confirmingDelete" @click="removeTemplate">
              {{ t('codeGeneration.templateDelete') }}
            </ElButton>
          </PermissionGate>
        </div>
      </div>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('codeGeneration.templatesTitle') }}</h2>
      </template>
      <p v-if="!templates.length" class="art-empty-state">{{ t('codeGeneration.templatesEmpty') }}</p>
      <ElTable v-else :data="templates" stripe>
        <ElTableColumn prop="name" :label="t('codeGeneration.templateName')" min-width="180" />
        <ElTableColumn prop="schema.databaseTableName" :label="t('codeGenerationTemplates.table')" min-width="200" />
        <ElTableColumn :label="t('users.columnActions')" width="120">
          <template #default="{ row }">
            <ElButton plain size="small" data-testid="codegen-template-load" @click="loadTemplate(row as CodeGenerationTemplateResponse)">
              {{ t('moduleCatalog.select') }}
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
      <div class="art-table__pagination center custom-pagination">
        <ElPagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </ElCard>
  </section>
</template>
