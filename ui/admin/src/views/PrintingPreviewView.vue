<script setup lang="ts">
import "../printing/printing.css";
import { computed, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { sanitizePrintingHtml } from '../printing/sanitizePrintingHtml';
import PrintingTenantGrantsDialog from '../components/printing/PrintingTenantGrantsDialog.vue';
import { showSuccess, showWarning } from '../feedback/fullNetMessage';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
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
const safePreviewHtml = computed(() => sanitizePrintingHtml(preview.value?.html ?? ''));
const grantDialogVisible = ref(false);
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; });
onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; });
onBeforeUnmount(() => { viewActive.value = false; });
const host = () => viewActive.value && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = computed(() => host() && session.can('printing.templates.read'));
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
function resetCreateForm(): void {
  createForm.value = { templateKey: 'tenant-profile-card', name: '租户档案卡片', layoutHtml: DEFAULT_LAYOUT_HTML };
}
const scope = useAuthorizedViewScope(session, () => {
  templates.value = []; selectedTemplateId.value = ''; preview.value = undefined; problem.value = undefined;
  grantDialogVisible.value = false; createDialogVisible.value = false; loading.value = false; previewing.value = false; creating.value = false;
  resetCreateForm();
}, async () => { await load(); });
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let previewRequest: ReturnType<typeof scope.begin>;
let loadRequest: ReturnType<typeof scope.begin>;
let createRequest: ReturnType<typeof scope.begin>;

/** 创建派生的目录和预览请求随原弹窗关闭而取消，不影响另一轮独立操作。 */
function begin(permission: string, owner?: ViewRequest): ViewRequest | undefined {
  if (!canRead.value || (owner && !owner.current())) return;
  const request = scope.begin(permission); if (!request || !owner) return request;
  const cancel = () => request.cancel();
  owner.signal.addEventListener('abort', cancel, { once: true });
  return { ...request, finish: () => { owner.signal.removeEventListener('abort', cancel); request.finish(); } };
}

watch(createDialogVisible, open => {
  if (!open) {
    createRequest?.cancel(); createRequest = undefined;
    // 子请求的取消先同步生效；旧 finally 不能清除新弹窗的状态。
    if (loadRequest?.signal.aborted) loading.value = false;
    if (previewRequest?.signal.aborted) { previewing.value = false; preview.value = undefined; }
    creating.value = false; resetCreateForm();
  }
}, { flush: 'sync' });
watch(selectedTemplateId, onTemplateChanged, { flush: 'sync' });

function openCreate(): void {
  if (!canRead.value || !session.can('printing.templates.create') || creating.value || previewing.value || loading.value) return;
  resetCreateForm(); createDialogVisible.value = true;
}

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

async function load(owner?: ViewRequest): Promise<boolean> {
  if (!canRead.value) return false;
  grantDialogVisible.value = false; previewRequest?.cancel(); preview.value = undefined; previewing.value = false;
  loadRequest?.cancel(); const request = begin('printing.templates.read', owner); loadRequest = request;
  if (!request) return false;
  const previousSelection = selectedTemplateId.value;
  loading.value = true;
  problem.value = undefined;
  try {
    const values = await listPrintingTemplates(undefined, request.signal);
    if (!request.current()) return false;
    templates.value = values;
    selectedTemplateId.value = values.some(item => item.id === previousSelection) ? previousSelection
      : printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    preview.value = undefined;
    return true;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.loadFailed');
    return false;
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

async function runPreview(printAfter = false, owner?: ViewRequest): Promise<void> {
  const template = selectedTemplate.value;
  if (!canRead.value || !template || loading.value || previewing.value || (creating.value && owner !== createRequest)) {
    return;
  }
  if (printAfter && (!preview.value || preview.value.templateId !== template.id)) return;
  const request = begin('printing.templates.preview', owner); if (!request) return; previewRequest = request;

  const templateId = template.id;
  const versionNumber = printAfter ? preview.value?.versionNumber : undefined;
  // 打印也重新访问受保护入口；撤权失败不能留下此前已经展示的内容。
  preview.value = undefined;
  previewing.value = true;
  problem.value = undefined;
  try {
    const value = await previewPrintingTemplate(templateId, versionNumber === undefined ? {} : {versionNumber}, request.signal);
    if (!request.current() || selectedTemplateId.value !== templateId) return;
    if (value.templateId !== templateId || (versionNumber !== undefined && value.versionNumber !== versionNumber))
      throw new Error('client.invalid_printing_template_preview');
    preview.value = value;
    await nextTick();
    if (printAfter && request.current() && selectedTemplateId.value === templateId) window.print();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.previewFailed');
  } finally {
    if (request.current()) previewing.value = false; request.finish();
  }
}

async function printPreview(): Promise<void> {
  if (preview.value) await runPreview(true);
}

function onTemplateChanged(): void {
  grantDialogVisible.value = false; previewRequest?.cancel(); preview.value = undefined; previewing.value = false; problem.value = undefined;
}

async function submitCreate(): Promise<void> {
  if (!canRead.value || !createDialogVisible.value || creating.value || previewing.value || loading.value) return;
  if (!createForm.value.templateKey.trim() || !createForm.value.name.trim() || !createForm.value.layoutHtml.trim()) {
    showWarning(t('printingPreview.requiredFields')); return;
  }
  const request = begin('printing.templates.create'); if (!request) return;
  createRequest = request;
  const publish = session.can('printing.templates.publish');
  const payload = { templateKey: createForm.value.templateKey, name: createForm.value.name,
    formSchemaKey: 'printing.tenant_profile_card', layoutHtml: createForm.value.layoutHtml, isEnabled: true };
  let saved = false;
  creating.value = true;
  problem.value = undefined;
  try {
    const created = await createPrintingTemplate(payload, request.signal);
    if (!request.current()) return;
    if (publish) {
      await publishPrintingTemplate(created.id, { version: created.version }, request.signal);
      if (!request.current()) return;
    }
    saved = true;
    showSuccess(t(publish ? 'printingPreview.createSuccess' : 'printingPreview.draftCreated'));
    const loaded = await load(request);
    if (!request.current() || !loaded || !templates.value.some(item => item.id === created.id)) return;
    selectedTemplateId.value = created.id;
    await runPreview(false, request);
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'printingPreview.createFailed');
  } finally {
    if (request.current() && createRequest === request) {
      creating.value = false;
      // 写入成功后即使目录刷新失败，也不能留下可重复提交的旧表单。
      if (saved) createDialogVisible.value = false;
    }
    request.finish();
  }
}

</script>

<template>
  <div v-if="canRead" class="printing-preview-view art-page-stack">
    <ElCard shadow="never" class="art-table-card no-print">
      <ArtTableHeader>
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              :disabled="loading || creating || previewing"
              data-testid="printing-preview-create"
              @click="openCreate"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="printing.templates.grant_tenants">
            <ElButton :disabled="!selectedTemplate || selectedTemplate.latestPublishedVersionNumber < 1 || previewing || creating"
              data-testid="printing-tenant-grants-open" @click="grantDialogVisible = true">{{ t('printingGrants.open') }}</ElButton>
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
            :disabled="creating || loading"
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
            :disabled="!selectedTemplate || creating || loading"
            data-testid="printing-preview-run"
            @click="runPreview()"
          >
            {{ t('printingPreview.preview') }}
          </ElButton></PermissionGate>
          <ElButton
            v-if="preview && session.can('printing.templates.preview')"
            :disabled="previewing || creating || loading"
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

    <PrintingTenantGrantsDialog v-if="grantDialogVisible && selectedTemplate && session.can('printing.templates.grant_tenants')"
      :key="selectedTemplate.id" :template="selectedTemplate" @close="grantDialogVisible = false" />
    <ElDialog
      v-model="createDialogVisible"
      :title="t('printingPreview.createTitle')"
      width="640px"
      class="no-print"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplateKey')" required>
          <ElInput v-model="createForm.templateKey" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldName')" required>
          <ElInput v-model="createForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldLayoutHtml')" required>
          <ElInput v-model="createForm.layoutHtml" type="textarea" :rows="10" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <PermissionGate code="printing.templates.create"><ElButton type="primary" :loading="creating" :disabled="previewing" data-testid="printing-preview-submit" @click="submitCreate">
          {{ t(session.can('printing.templates.publish') ? 'printingPreview.submitCreate' : 'printingPreview.saveDraft') }}
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
