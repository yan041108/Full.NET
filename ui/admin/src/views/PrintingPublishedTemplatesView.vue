<script setup lang="ts">
import "../printing/printing.css";
import { computed, nextTick, ref } from 'vue';
import { ElAlert, ElButton, ElCard, ElEmpty, ElForm, ElFormItem, ElOption, ElSelect } from 'element-plus';
import { Printer, Refresh } from '@element-plus/icons-vue';
import { isFullNetProblemDetails, type FullNetProblemDetails, type PrintingPublishedTemplateResponse, type PrintingTemplatePreview } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate } from '../api/printing-templates';
import { sanitizePrintingHtml } from '../printing/sanitizePrintingHtml';

defineOptions({name:'PrintingPublishedTemplatesView'});
const session = useSessionStore(); const { t } = useAdminI18n();
const readPermission = 'printing.published_templates.read';
const previewPermission = 'printing.published_templates.preview';
const tenant = () => session.currentUser?.scope === 'tenant' && Boolean(session.currentUser.tenantId);
const templates = ref<PrintingPublishedTemplateResponse[]>([]); const selectedId = ref('');
const preview = ref<PrintingTemplatePreview>(); const problem = ref<FullNetProblemDetails>();
const loading = ref(false); const previewing = ref(false);
// 同一模板可以获授多个版本；选择身份必须同时包含模板和不可变版本。
const versionKey = (item: PrintingPublishedTemplateResponse) => item.templateId + ':' + item.versionNumber;
const selected = computed(() => templates.value.find(item => versionKey(item) === selectedId.value));
const safeHtml = computed(() => sanitizePrintingHtml(preview.value?.html ?? ''));
const scope = useAuthorizedViewScope(session, () => {
  templates.value = []; selectedId.value = ''; preview.value = undefined; problem.value = undefined;
  loading.value = false; previewing.value = false;
}, load);
let listRequest: ReturnType<typeof scope.begin>; let previewRequest: ReturnType<typeof scope.begin>;
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : {status:500,code:'client.request_failed',title:t('printingPreview.previewFailed')};
}
function clearPreview(): void {
  previewRequest?.cancel(); preview.value = undefined; previewing.value = false; problem.value = undefined;
}
async function load(): Promise<void> {
  if (!tenant()) return;
  clearPreview(); listRequest?.cancel();
  const request = scope.begin(readPermission); if (!request) return; listRequest = request;
  templates.value = []; selectedId.value = ''; loading.value = true;
  try {
    const result = await listPrintingPublishedTemplates(request.signal);
    if (!request.current()) return;
    templates.value = result; selectedId.value = result[0] ? versionKey(result[0]) : '';
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
}
async function render(printAfter = false): Promise<void> {
  if (!tenant() || !selected.value || loading.value || previewing.value) return;
  const request = scope.begin(previewPermission); if (!request) return;
  const template = selected.value; previewRequest = request;
  // 授权可在会话权限不变时撤销；每次打印都重新核验服务器上的精确版本。
  preview.value = undefined; problem.value = undefined; previewing.value = true;
  try {
    const result = await previewPrintingPublishedTemplate(template.templateId,{versionNumber:template.versionNumber},request.signal);
    if (!request.current()) return;
    if (result.templateId !== template.templateId || result.versionNumber !== template.versionNumber)
      throw new Error('client.invalid_printing_template_preview');
    preview.value = result;
    // 浏览器打印之前等待净化后的新内容进入 DOM，并再次核对当前页面代次。
    await nextTick();
    if (printAfter && request.current() && selectedId.value === versionKey(template)) window.print();
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) previewing.value = false; request.finish(); }
}
</script>

<template>
  <div v-if="tenant()" class="printing-published-view art-page-stack">
    <ElCard shadow="never" class="no-print">
      <ArtTableHeader>
        <template #left><span>{{ t('printingPublished.hint') }}</span></template>
        <template #right><PermissionGate :code="readPermission"><ElButton :icon="Refresh" :loading="loading" data-testid="printing-published-refresh" @click="load">{{ t('printingGrants.refresh') }}</ElButton></PermissionGate></template>
      </ArtTableHeader>
      <ElAlert v-if="problem" type="error" :title="problem.title" :closable="false" show-icon />
      <ElEmpty v-if="!loading && !templates.length && !problem" :description="t('printingPublished.empty')" />
      <ElForm label-width="120px" class="printing-published-form">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect v-model="selectedId" :disabled="loading || previewing" class="printing-template-select" data-testid="printing-published-template" @change="clearPreview">
            <ElOption v-for="item in templates" :key="versionKey(item)" :label="item.templateName + ' · v' + item.versionNumber" :value="versionKey(item)" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <PermissionGate :code="previewPermission"><ElButton type="primary" :icon="Printer" :loading="previewing" :disabled="!selected || loading" data-testid="printing-published-preview" @click="render()">{{ t('printingPreview.preview') }}</ElButton></PermissionGate>
          <PermissionGate :code="previewPermission"><ElButton v-if="preview" :disabled="loading || previewing" data-testid="printing-published-print" @click="render(true)">{{ t('printingPreview.print') }}</ElButton></PermissionGate>
        </ElFormItem>
      </ElForm>
    </ElCard>
    <section v-if="preview" class="printing-preview-surface" :aria-label="t('printingPreview.preview')">
      <div class="printing-preview-html" v-html="safeHtml" />
    </section>
  </div>
</template>

<style scoped>
.printing-published-form { margin-top: 16px; }
.printing-template-select { width: 100%; max-width: 600px; }
.printing-preview-surface { padding: 24px; background: #fff; color: #171717; border: 1px solid var(--el-border-color-light); border-radius: 8px; overflow-wrap: anywhere; }
.printing-preview-html :deep(.print-card) { max-width: 720px; margin: 0 auto; }
@media print {
  .no-print { display: none !important; }
  .printing-preview-surface { padding: 0; border: none; }
}
</style>
