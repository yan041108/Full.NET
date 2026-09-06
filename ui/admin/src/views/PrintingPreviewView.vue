<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect
} from 'element-plus';
import { Plus, Printer } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, PrintingTemplate, PrintingTemplatePreview } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createPrintingTemplate,
  listPrintingTemplates,
  previewPrintingTemplate,
  publishPrintingTemplate
} from '../api/printing-templates';

defineOptions({ name: 'PrintingPreviewView' });

const DEFAULT_LAYOUT_HTML = `<div class="print-card">
  <h1>{{tenantName}}</h1>
  <p>编码：{{tenantCode}}</p>
  <p>域名：{{tenantDomain}}</p>
  <p>打印人：{{printedByDisplayName}}</p>
  <p>打印时间：{{printedAtUtc}}</p>
</div>`;

const session = useSessionStore();
const { t } = useAdminI18n();
const templates = ref<PrintingTemplate[]>([]);
const selectedTemplateId = ref('');
const preview = ref<PrintingTemplatePreview>();
const loading = ref(false);
const previewing = ref(false);
const creating = ref(false);
const problem = ref<FullNetProblemDetails>();
const createDialogVisible = ref(false);
const createForm = ref({
  templateKey: 'tenant-profile-card',
  name: '租户档案卡片',
  layoutHtml: DEFAULT_LAYOUT_HTML
});

const selectedTemplate = computed(() =>
  templates.value.find(item => item.id === selectedTemplateId.value));

const printableTemplates = computed(() =>
  templates.value.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0));

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    templates.value = await listPrintingTemplates();
    selectedTemplateId.value = printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    preview.value = undefined;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'printingPreview.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function runPreview(): Promise<void> {
  if (!selectedTemplateId.value) {
    return;
  }

  previewing.value = true;
  problem.value = undefined;
  try {
    preview.value = await previewPrintingTemplate(selectedTemplateId.value, {});
  } catch (error: unknown) {
    problem.value = toProblem(error, 'printingPreview.previewFailed');
  } finally {
    previewing.value = false;
  }
}

function printPreview(): void {
  window.print();
}

async function submitCreate(): Promise<void> {
  creating.value = true;
  problem.value = undefined;
  try {
    const created = await createPrintingTemplate({
      templateKey: createForm.value.templateKey,
      name: createForm.value.name,
      formSchemaKey: 'printing.tenant_profile_card',
      layoutHtml: createForm.value.layoutHtml,
      isEnabled: true
    });
    if (session.can('printing.templates.publish')) {
      await publishPrintingTemplate(created.id, { version: created.version });
    }
    ElMessage.success(t('printingPreview.createSuccess'));
    createDialogVisible.value = false;
    await load();
    selectedTemplateId.value = created.id;
    await runPreview();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'printingPreview.createFailed');
  } finally {
    creating.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="printing-preview-view">
    <ElCard shadow="never" class="art-table-card no-print">
      <ArtTableHeader>
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              data-testid="printing-preview-create"
              @click="createDialogVisible = true"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <ElAlert
        v-if="problem"
        type="error"
        :title="problem.title"
        show-icon
        class="mb-4"
      />

      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect
            v-model="selectedTemplateId"
            data-testid="printing-preview-template"
            class="w-full"
          >
            <ElOption
              v-for="template in templates"
              :key="template.id"
              :label="template.name"
              :value="template.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <ElButton
            type="primary"
            :icon="Printer"
            :loading="previewing"
            :disabled="!selectedTemplateId"
            data-testid="printing-preview-run"
            @click="runPreview"
          >
            {{ t('printingPreview.preview') }}
          </ElButton>
          <ElButton
            v-if="preview"
            :disabled="!preview"
            data-testid="printing-preview-print"
            @click="printPreview"
          >
            {{ t('printingPreview.print') }}
          </ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <section v-if="preview" class="printing-preview-surface print-only-surface">
      <div class="printing-preview-html" v-html="preview.html" />
    </section>

    <ElDialog
      v-model="createDialogVisible"
      :title="t('printingPreview.createTitle')"
      width="640px"
      class="no-print"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplateKey')">
          <ElInput v-model="createForm.templateKey" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldName')">
          <ElInput v-model="createForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldLayoutHtml')">
          <ElInput v-model="createForm.layoutHtml" type="textarea" :rows="10" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <ElButton type="primary" :loading="creating" data-testid="printing-preview-submit" @click="submitCreate">
          {{ t('printingPreview.submitCreate') }}
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.printing-preview-surface {
  margin-top: 16px;
  padding: 24px;
  background: #fff;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}

.printing-preview-html :deep(.print-card) {
  max-width: 720px;
  margin: 0 auto;
  font-family: Arial, sans-serif;
}

@media print {
  .no-print {
    display: none !important;
  }

  .printing-preview-view {
    padding: 0;
  }

  .printing-preview-surface {
    margin: 0;
    padding: 0;
    border: none;
  }
}
</style>
