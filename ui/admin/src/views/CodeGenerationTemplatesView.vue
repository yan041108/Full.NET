<script setup lang="ts">
import { computed, onMounted, ref, toRaw, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs
} from 'element-plus';
import {
  isCodeGenerationPreviewRequest,
  isFullNetProblemDetails,
  type CodeGenerationPreviewRequest,
  type CodeGenerationRelationshipRequest,
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
const router = useRouter();
const { t } = useAdminI18n();
const activeTab = ref('basics');

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
  columns: [
    {
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null,
      ui: {
        controlKind: 'uuid',
        showInList: true,
        includeInCreate: false,
        includeInUpdate: false,
        required: false,
        sortable: true,
        queryable: false,
        queryKind: 'none',
        unique: false,
        includeInImportExport: false
      }
    },
    {
      databaseName: 'TenantId',
      clrPropertyName: 'TenantId',
      jsonPropertyName: 'tenantId',
      scalarType: 'Uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null,
      ui: {
        controlKind: 'uuid',
        showInList: false,
        includeInCreate: false,
        includeInUpdate: false,
        required: false,
        sortable: false,
        queryable: false,
        queryKind: 'none',
        unique: false,
        includeInImportExport: false
      }
    },
    {
      databaseName: 'Name',
      clrPropertyName: 'Name',
      jsonPropertyName: 'displayName',
      scalarType: 'String',
      isNullable: false,
      maxLength: 200,
      numericPrecision: null,
      numericScale: null,
      ui: {
        controlKind: 'text',
        showInList: true,
        includeInCreate: true,
        includeInUpdate: true,
        required: true,
        sortable: true,
        queryable: true,
        queryKind: 'contains',
        unique: false,
        includeInImportExport: true
      }
    },
    {
      databaseName: 'IsActive',
      clrPropertyName: 'IsActive',
      jsonPropertyName: 'isActive',
      scalarType: 'Boolean',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null,
      ui: {
        controlKind: 'switch',
        showInList: true,
        includeInCreate: true,
        includeInUpdate: true,
        required: true,
        sortable: true,
        queryable: true,
        queryKind: 'equals',
        unique: false,
        includeInImportExport: true
      }
    },
    {
      databaseName: 'Version',
      clrPropertyName: 'Version',
      jsonPropertyName: 'version',
      scalarType: 'Int64',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null,
      ui: {
        controlKind: 'number',
        showInList: false,
        includeInCreate: false,
        includeInUpdate: false,
        required: false,
        sortable: false,
        queryable: false,
        queryKind: 'none',
        unique: false,
        includeInImportExport: false
      }
    }
  ]
});

const schema = ref<CodeGenerationPreviewRequest>(defaultSchema());
const schemaText = ref(JSON.stringify(defaultSchema(), null, 2));
const templates = ref<CodeGenerationTemplateResponse[]>([]);
const selectedTemplate = ref<CodeGenerationTemplateResponse>();
const templateName = ref('');
const templateDescription = ref('');
const tableNames = ref<string[]>([]);
const skippedColumnNames = ref<string[]>([]);
const addedColumnNames = ref<string[]>([]);
const filterName = ref('');
const filterTableName = ref('');
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
const showRelationships = computed(
  () => 'scene' in schema.value && schema.value.scene !== 'single'
);
const capabilities = computed({
  get: () => ('entityCapabilities' in schema.value
    ? schema.value.entityCapabilities
    : undefined),
  set: (value) => {
    if (!value || !('entityCapabilities' in schema.value)) {
      return;
    }
    schema.value = {
      ...schema.value,
      entityCapabilities: value
    };
  }
});
const relationships = computed({
  get: () => ('relationships' in schema.value
    ? schema.value.relationships
    : []),
  set: (value: CodeGenerationRelationshipRequest[]) => {
    if (!('relationships' in schema.value)) {
      return;
    }
    schema.value = {
      ...schema.value,
      relationships: value
    };
  }
});

watch(showRelationships, (visible) => {
  if (!visible && activeTab.value === 'relationships') {
    activeTab.value = 'basics';
  }
});

watch(activeTab, (tab, previous) => {
  if (tab === 'json') {
    syncSchemaText();
    return;
  }
  if (previous === 'json') {
    applySchemaText();
  }
});

onMounted(async () => {
  await load();
  await loadTables();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listCodeGenerationTemplates(
      page.value,
      pageSize.value,
      {
        name: filterName.value,
        tableName: filterTableName.value
      }
    );
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

async function search(): Promise<void> {
  page.value = 1;
  await load();
}

async function resetFilters(): Promise<void> {
  filterName.value = '';
  filterTableName.value = '';
  page.value = 1;
  await load();
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
  activeTab.value = 'basics';
}

function clearSelection(): void {
  selectedTemplate.value = undefined;
  templateName.value = '';
  templateDescription.value = '';
  schema.value = defaultSchema();
  schemaText.value = JSON.stringify(defaultSchema(), null, 2);
  skippedColumnNames.value = [];
  addedColumnNames.value = [];
  activeTab.value = 'basics';
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

async function copyTemplate(template: CodeGenerationTemplateResponse): Promise<void> {
  if (changing.value || !canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const copyName = `${template.name} (copy)`.slice(0, 128);
    const saved = await createCodeGenerationTemplate({
      name: copyName,
      description: template.description,
      schema: structuredClone(toRaw(template.schema))
    });
    upsertTemplate(saved);
    loadTemplate(saved);
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
}

async function openPreview(template: CodeGenerationTemplateResponse): Promise<void> {
  await router.push({
    path: '/code-generation/previews',
    query: { templateId: template.id }
  });
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
    clearSelection();
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

function addRelationship(): void {
  if (!('relationships' in schema.value)) {
    return;
  }
  const scope = schema.value.dataScope;
  relationships.value = [
    ...relationships.value,
    {
      principalEntityKey: schema.value.entityKey,
      principalColumnName: 'Id',
      principalDataScope: scope,
      dependentEntityKey: schema.value.entityKey,
      dependentColumnName: 'ParentId',
      dependentDataScope: scope,
      cascadeDelete: false
    }
  ];
  syncSchemaText();
}

function removeRelationship(index: number): void {
  relationships.value = relationships.value.filter((_, i) => i !== index);
  syncSchemaText();
}

function currentSchema(): CodeGenerationPreviewRequest | undefined {
  if (activeTab.value === 'json') {
    return readSchema();
  }
  if (!isCodeGenerationPreviewRequest(schema.value)
    || schema.value.columns.length === 0) {
    problem.value = clientProblem('client.codegen_invalid_schema');
    return undefined;
  }
  syncSchemaText();
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

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('codeGeneration.templatesTitle') }}</h2>
      </template>
      <div class="art-form-grid" data-testid="codegen-template-filters">
        <ElInput
          v-model="filterName"
          data-testid="codegen-template-filter-name"
          clearable
          :placeholder="t('codeGeneration.templateName')"
          @keyup.enter="search"
        />
        <ElInput
          v-model="filterTableName"
          data-testid="codegen-template-filter-table"
          clearable
          :placeholder="t('codeGenerationTemplates.table')"
          @keyup.enter="search"
        />
        <div class="art-form-actions">
          <ElButton type="primary" data-testid="codegen-template-filter-search" :loading="loading" @click="search">
            {{ t('codeGenerationTemplates.search') }}
          </ElButton>
          <ElButton data-testid="codegen-template-filter-reset" :disabled="loading" @click="resetFilters">
            {{ t('codeGenerationTemplates.resetFilters') }}
          </ElButton>
          <PermissionGate code="codegen.templates.create">
            <ElButton plain data-testid="codegen-template-new" @click="clearSelection">
              {{ t('codeGenerationTemplates.createTitle') }}
            </ElButton>
          </PermissionGate>
        </div>
      </div>
      <p v-if="!templates.length" class="art-empty-state">{{ t('codeGeneration.templatesEmpty') }}</p>
      <ElTable v-else :data="templates" stripe data-testid="codegen-template-table">
        <ElTableColumn prop="name" :label="t('codeGeneration.templateName')" min-width="160" />
        <ElTableColumn :label="t('codeGenerationTemplates.table')" min-width="180">
          <template #default="{ row }">
            {{ (row as CodeGenerationTemplateResponse).schema.databaseTableName }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('codeGenerationTemplates.scene')" min-width="110">
          <template #default="{ row }">
            {{ 'scene' in (row as CodeGenerationTemplateResponse).schema
              ? (row as CodeGenerationTemplateResponse).schema.scene
              : '—' }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('codeGenerationTemplates.moduleKey')" min-width="100">
          <template #default="{ row }">
            {{ (row as CodeGenerationTemplateResponse).schema.moduleKey }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('codeGenerationTemplates.entityKey')" min-width="100">
          <template #default="{ row }">
            {{ (row as CodeGenerationTemplateResponse).schema.entityKey }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('codeGenerationTemplates.version')" width="90">
          <template #default="{ row }">
            v{{ (row as CodeGenerationTemplateResponse).version }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('users.columnActions')" width="280" fixed="right">
          <template #default="{ row }">
            <ElButton
              plain
              size="small"
              data-testid="codegen-template-load"
              @click="loadTemplate(row as CodeGenerationTemplateResponse)"
            >
              {{ t('codeGenerationTemplates.editAction') }}
            </ElButton>
            <ElButton
              plain
              size="small"
              data-testid="codegen-template-preview-link"
              @click="openPreview(row as CodeGenerationTemplateResponse)"
            >
              {{ t('codeGenerationTemplates.previewAction') }}
            </ElButton>
            <PermissionGate code="codegen.templates.create">
              <ElButton
                plain
                size="small"
                data-testid="codegen-template-copy"
                :disabled="changing"
                @click="copyTemplate(row as CodeGenerationTemplateResponse)"
              >
                {{ t('codeGenerationTemplates.copyAction') }}
              </ElButton>
            </PermissionGate>
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

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selectedTemplate ? t('codeGenerationTemplates.editTitle') : t('codeGenerationTemplates.createTitle') }}</h2>
      </template>
      <ElTabs v-model="activeTab">
        <ElTabPane :label="t('codeGenerationTemplates.tabBasics')" name="basics">
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
          </div>
        </ElTabPane>

        <ElTabPane v-if="capabilities" :label="t('codeGenerationTemplates.tabCapabilities')" name="capabilities">
          <div class="art-form-grid">
            <ElSelect v-model="capabilities.deleteMode" :placeholder="t('codeGenerationTemplates.deleteMode')">
              <ElOption label="soft.delete" value="soft.delete" />
              <ElOption label="hard.delete" value="hard.delete" />
              <ElOption label="immutable" value="immutable" />
            </ElSelect>
            <ElSelect v-model="capabilities.ownershipMode" :placeholder="t('codeGenerationTemplates.ownershipMode')">
              <ElOption label="none" value="none" />
              <ElOption label="organization.unit" value="organization.unit" />
            </ElSelect>
            <label>
              {{ t('codeGenerationTemplates.hasCreatedAudit') }}
              <ElSwitch v-model="capabilities.hasCreatedAudit" />
            </label>
            <label>
              {{ t('codeGenerationTemplates.hasUpdatedAudit') }}
              <ElSwitch v-model="capabilities.hasUpdatedAudit" />
            </label>
            <label>
              {{ t('codeGenerationTemplates.hasDeletedAudit') }}
              <ElSwitch v-model="capabilities.hasDeletedAudit" />
            </label>
            <label>
              {{ t('codeGenerationTemplates.hasVersion') }}
              <ElSwitch v-model="capabilities.hasVersion" />
            </label>
          </div>
        </ElTabPane>

        <ElTabPane :label="t('codeGenerationTemplates.tabColumns')" name="columns">
          <p v-if="skippedColumnNames.length">
            {{ t('codeGenerationTemplates.skippedColumns', { names: skippedColumnNames.join(', ') }) }}
          </p>
          <p v-if="addedColumnNames.length">
            {{ t('codeGenerationTemplates.addedColumns', { names: addedColumnNames.join(', ') }) }}
          </p>
          <ElTable :data="schema.columns" stripe data-testid="codegen-template-columns">
            <ElTableColumn prop="databaseName" :label="t('codeGenerationTemplates.columnName')" min-width="120" />
            <ElTableColumn prop="scalarType" :label="t('codeGenerationTemplates.scalarType')" min-width="100" />
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
            <ElTableColumn :label="t('codeGenerationTemplates.queryKind')" min-width="110">
              <template #default="{ row }">
                <ElSelect v-if="row.ui" v-model="row.ui.queryKind" data-testid="codegen-column-query-kind">
                  <ElOption label="none" value="none" />
                  <ElOption label="equals" value="equals" />
                  <ElOption label="contains" value="contains" />
                  <ElOption label="range" value="range" />
                </ElSelect>
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.showInList')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.showInList" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.includeInCreate')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.includeInCreate" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.includeInUpdate')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.includeInUpdate" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.required')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.required" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.sortable')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.sortable" data-testid="codegen-column-sortable" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.queryable')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.queryable" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.unique')" width="80">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.unique" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('codeGenerationTemplates.includeInImportExport')" width="90">
              <template #default="{ row }">
                <ElSwitch v-if="row.ui" v-model="row.ui.includeInImportExport" data-testid="codegen-column-import-export" />
              </template>
            </ElTableColumn>
          </ElTable>
        </ElTabPane>

        <ElTabPane
          v-if="showRelationships"
          :label="t('codeGenerationTemplates.tabRelationships')"
          name="relationships"
        >
          <div class="art-form-actions">
            <ElButton data-testid="codegen-relationship-add" @click="addRelationship">
              {{ t('codeGenerationTemplates.addRelationship') }}
            </ElButton>
          </div>
          <ElCard
            v-for="(relationship, index) in relationships"
            :key="index"
            class="art-card"
            shadow="never"
          >
            <div class="art-form-grid">
              <ElInput v-model="relationship.principalEntityKey" :placeholder="t('codeGenerationTemplates.principalEntityKey')" />
              <ElInput v-model="relationship.principalColumnName" :placeholder="t('codeGenerationTemplates.principalColumnName')" />
              <ElSelect v-model="relationship.principalDataScope">
                <ElOption label="TenantRequired" value="TenantRequired" />
                <ElOption label="HostOnly" value="HostOnly" />
                <ElOption label="Global" value="Global" />
              </ElSelect>
              <ElInput v-model="relationship.dependentEntityKey" :placeholder="t('codeGenerationTemplates.dependentEntityKey')" />
              <ElInput v-model="relationship.dependentColumnName" :placeholder="t('codeGenerationTemplates.dependentColumnName')" />
              <ElSelect v-model="relationship.dependentDataScope">
                <ElOption label="TenantRequired" value="TenantRequired" />
                <ElOption label="HostOnly" value="HostOnly" />
                <ElOption label="Global" value="Global" />
              </ElSelect>
              <label>
                {{ t('codeGenerationTemplates.cascadeDelete') }}
                <ElSwitch v-model="relationship.cascadeDelete" />
              </label>
              <ElButton type="danger" plain @click="removeRelationship(index)">
                {{ t('codeGenerationTemplates.removeRelationship') }}
              </ElButton>
            </div>
          </ElCard>
        </ElTabPane>

        <ElTabPane :label="t('codeGenerationTemplates.tabJson')" name="json">
          <p>{{ t('codeGenerationTemplates.tabJsonHint') }}</p>
        </ElTabPane>
      </ElTabs>

      <ElInput
        v-model="schemaText"
        data-testid="codegen-template-schema"
        type="textarea"
        :rows="activeTab === 'json' ? 16 : 8"
        spellcheck="false"
        :aria-label="t('codeGenerationTemplates.tabJson')"
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
    </ElCard>
  </section>
</template>
