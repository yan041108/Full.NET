<script setup lang="ts">
import { computed, ref } from 'vue';
import DOMPurify from 'dompurify';
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
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
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
// 布局属于可编辑的不可信内容；必须在最终插入 DOM 前净化，覆盖数据库中已经保存的旧模板。
const safePreviewHtml = computed(() => DOMPurify.sanitize(preview.value?.html ?? '', {
  USE_PROFILES: { html: true },
  FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'option'],
  FORBID_ATTR: ['id', 'name'],
  ALLOW_DATA_ATTR: false
}));
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
const scope = useAuthorizedViewScope(session, () => {
  templates.value = []; selectedTemplateId.value = ''; preview.value = undefined; problem.value = undefined;
  createDialogVisible.value = false; loading.value = false; previewing.value = false; creating.value = false;
  createForm.value = { templateKey: 'tenant-profile-card', name: '租户档案卡片', layoutHtml: DEFAULT_LAYOUT_HTML };
}, load);
let previewRequest: ReturnType<typeof scope.begin>;
let loadRequest: ReturnType<typeof scope.begin>;

const selectedTemplate = computed(() =>
  templates.value.find(item => item.id === selectedTemplateId.value));

const printableTemplates = computed(() =>
  templates.value.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0));

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loadRequest?.cancel(); const request = scope.begin('printing.templates.read'); loadRequest = request;
  if (!request) return;
  loading.value = true;
  problem.value = undefined;
  try {
    const values = await listPrintingTemplates(undefined, request.signal);
    if (!request.current()) return;
    templates.value = values;
    selectedTemplateId.value = printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    preview.value = undefined;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.loadFailed');
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

async function runPreview(): Promise<void> {
  if (!selectedTemplateId.value || previewing.value || creating.value) {
    return;
  }
  const request = scope.begin('printing.templates.preview'); if (!request) return; previewRequest = request;

  previewing.value = true;
  problem.value = undefined;
  try {
    const value = await previewPrintingTemplate(selectedTemplateId.value, {}, request.signal);
    if (request.current()) preview.value = value;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.previewFailed');
  } finally {
    if (request.current()) previewing.value = false; request.finish();
  }
}

function printPreview(): void {
  if (preview.value && session.can('printing.templates.preview')) window.print();
}

function onTemplateChanged(): void {
  previewRequest?.cancel(); preview.value = undefined; previewing.value = false; problem.value = undefined;
}

async function submitCreate(): Promise<void> {
  if (creating.value || previewing.value) return;
  const request = scope.begin('printing.templates.create'); if (!request) return;
  creating.value = true;
  problem.value = undefined;
  try {
    const created = await createPrintingTemplate({
      templateKey: createForm.value.templateKey,
      name: createForm.value.name,
      formSchemaKey: 'printing.tenant_profile_card',
      layoutHtml: createForm.value.layoutHtml,
      isEnabled: true
    }, request.signal);
    if (!request.current()) return;
    if (session.can('printing.templates.publish')) {
      await publishPrintingTemplate(created.id, { version: created.version }, request.signal);
      if (!request.current()) return;
    }
    ElMessage.success(t('printingPreview.createSuccess'));
    createDialogVisible.value = false;
    await load();
    if (!request.current()) return;
    selectedTemplateId.value = created.id;
    creating.value = false;
    await runPreview();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.createFailed');
  } finally {
    if (request.current()) creating.value = false; request.finish();
  }
}

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
            :disabled="creating"
            @change="onTemplateChanged"
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
          <PermissionGate code="printing.templates.preview"><ElButton
            type="primary"
            :icon="Printer"
            :loading="previewing"
            :disabled="!selectedTemplateId || creating"
            data-testid="printing-preview-run"
            @click="runPreview"
          >
            {{ t('printingPreview.preview') }}
          </ElButton></PermissionGate>
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
      <div class="printing-preview-html" v-html="safePreviewHtml" />
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
        <PermissionGate code="printing.templates.create"><ElButton type="primary" :loading="creating" :disabled="previewing" data-testid="printing-preview-submit" @click="submitCreate">
          {{ t('printingPreview.submitCreate') }}
        </ElButton></PermissionGate>
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
