<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput } from 'element-plus';
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
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from '../api/code-generation-templates';

const session = useSessionStore();
const { t } = useAdminI18n();
const defaultSchema = {
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
  columns: []
};
const schemaText = ref(JSON.stringify(defaultSchema, null, 2));
const templates = ref<CodeGenerationTemplateResponse[]>([]);
const selectedTemplate = ref<CodeGenerationTemplateResponse>();
const templateName = ref('');
const templateDescription = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('codegen.templates.create'));
const canUpdate = computed(() => session.can('codegen.templates.update'));
const canDelete = computed(() => session.can('codegen.templates.delete'));
const showForm = computed(() => {
  if (selectedTemplate.value) {
    return canUpdate.value || canDelete.value;
  }
  return canCreate.value;
});

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    templates.value = (await listCodeGenerationTemplates()).items;
  } catch (error: unknown) {
    problem.value = readProblem(error, 'codeGenerationTemplates.loadFailed');
  } finally {
    loading.value = false;
  }
}

function loadTemplate(template: CodeGenerationTemplateResponse): void {
  selectedTemplate.value = template;
  templateName.value = template.name;
  templateDescription.value = template.description ?? '';
  schemaText.value = JSON.stringify(template.schema, null, 2);
}

async function saveTemplate(): Promise<void> {
  const schema = readSchema();
  if (!schema || changing.value || !canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createCodeGenerationTemplate({
      name: templateName.value,
      description: templateDescription.value.trim() || null,
      schema
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
  const schema = readSchema();
  if (!selected || !schema || changing.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await updateCodeGenerationTemplate(selected.id, {
      name: templateName.value,
      description: templateDescription.value.trim() || null,
      schema,
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
  if (!selected || changing.value || !canDelete.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await deleteCodeGenerationTemplate(selected.id, selected.version);
    templates.value = templates.value.filter(item => item.id !== selected.id);
    selectedTemplate.value = undefined;
    templateName.value = '';
    templateDescription.value = '';
    schemaText.value = JSON.stringify(defaultSchema, null, 2);
  } catch (error: unknown) {
    problem.value = readProblem(error);
  } finally {
    changing.value = false;
  }
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
    return;
  }
  templates.value.splice(index, 1, saved);
}

function clientProblem(code: string): FullNetProblemDetails {
  return { status: 400, code, title: t('codeGeneration.invalidInput') };
}

function readProblem(
  error: unknown,
  fallbackCode = 'codeGenerationTemplates.operationFailed'
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

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selectedTemplate ? t('codeGenerationTemplates.editTitle') : t('codeGenerationTemplates.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <ElInput v-model="templateName" data-testid="codegen-template-name" maxlength="128" :placeholder="t('codeGeneration.templateName')" />
        <ElInput v-model="templateDescription" type="textarea" :rows="2" maxlength="512" :placeholder="t('codeGeneration.templateDescription')" />
        <ElInput v-model="schemaText" data-testid="codegen-template-schema" type="textarea" :rows="20" spellcheck="false" />
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
            <ElButton v-if="selectedTemplate" type="danger" plain data-testid="codegen-template-delete" :disabled="changing" @click="removeTemplate">
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
      <nav v-else :aria-label="t('codeGeneration.templatesTitle')">
        <button
          v-for="template in templates"
          :key="template.id"
          type="button"
          data-testid="codegen-template-load"
          :class="{ 'is-active': selectedTemplate?.id === template.id }"
          @click="loadTemplate(template)"
        >
          <strong>{{ template.name }}</strong>
        </button>
      </nav>
    </ElCard>
  </section>
</template>