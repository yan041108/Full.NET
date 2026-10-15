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
const publishing = ref(false);
const creationReceipt = ref<{ templateId: string; name: string; outcome: 'draft' | 'publishing' | 'published' | 'publishUnconfirmed' }>();
// 最近一条回执可以被新操作替换，发布门槛必须按模板保留，直到对应目录读取完成核对。
const publicationBlocks = ref(new Map<string, 'published' | 'unconfirmed'>());
let createSequence = 0;
const createIntent = ref<{ token: number; update: (open: boolean) => void; close: () => void; submit: () => void }>();
const previewTarget = ref<PrintingTemplate>();
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
  grantDialogVisible.value = false; createDialogVisible.value = false; createIntent.value = undefined; creationReceipt.value = undefined; publicationBlocks.value = new Map(); previewTarget.value = undefined; loading.value = false; previewing.value = false; creating.value = false; publishing.value = false;
  resetCreateForm();
}, async () => { await load(); });
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let previewRequest: ReturnType<typeof scope.begin>;
let loadRequest: ReturnType<typeof scope.begin>;
let createRequest: ReturnType<typeof scope.begin>;
let publishRequest: ReturnType<typeof scope.begin>;

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
    if (createRequest?.current() && creationReceipt.value?.outcome === 'publishing') creationReceipt.value.outcome = 'publishUnconfirmed';
    createRequest?.cancel(); createRequest = undefined;
    // 子请求的取消先同步生效；旧 finally 不能清除新弹窗的状态。
    if (loadRequest?.signal.aborted) loading.value = false;
    if (previewRequest?.signal.aborted) { previewing.value = false; preview.value = undefined; }
    creating.value = false; createIntent.value = undefined; resetCreateForm();
  }
}, { flush: 'sync' });
watch(selectedTemplateId, onTemplateChanged, { flush: 'sync' });

function openCreate(): void {
  if (!canRead.value || !session.can('printing.templates.create') || creating.value || publishing.value || previewing.value || loading.value) return;
  const token = ++createSequence, close = () => { if (createIntent.value?.token === token) createDialogVisible.value = false; };
  createIntent.value = { token, close, update: open => { if (!open) close(); }, submit: () => { if (createIntent.value?.token === token) void submitCreate(); } };
  resetCreateForm(); createDialogVisible.value = true;
}

const selectedTemplate = computed(() =>
  templates.value.find(item => item.id.toLowerCase() === selectedTemplateId.value.toLowerCase()));
const canPublishSelected = computed(() => !!selectedTemplate.value?.isEnabled && selectedTemplate.value.latestPublishedVersionNumber === 0
  && !publicationBlocks.value.has(selectedTemplate.value.id.toLowerCase()));
function targetCurrent(target: PrintingTemplate): boolean {
  const current = selectedTemplate.value;
  return !!current && current.id.toLowerCase() === target.id.toLowerCase() && current.version === target.version
    && current.templateKey === target.templateKey && current.name === target.name
    && current.formSchemaKey === target.formSchemaKey && current.layoutHtml === target.layoutHtml && current.isEnabled === target.isEnabled
    && current.latestPublishedVersionNumber === target.latestPublishedVersionNumber;
}
watch(templates, () => { if (previewTarget.value && !targetCurrent(previewTarget.value)) onTemplateChanged(); }, { deep: true, flush: 'sync' });

const printableTemplates = computed(() =>
  templates.value.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0));

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(owner?: ViewRequest): Promise<boolean> {
  if (!canRead.value || (!owner && (creating.value || publishing.value))) return false;
  grantDialogVisible.value = false; previewRequest?.cancel(); previewTarget.value = undefined; preview.value = undefined; previewing.value = false;
  loadRequest?.cancel(); const request = begin('printing.templates.read', owner); loadRequest = request;
  if (!request) return false;
  const previousSelection = selectedTemplateId.value;
  loading.value = true;
  problem.value = undefined;
  try {
    const values = await listPrintingTemplates(undefined, request.signal);
    if (!request.current()) return false;
    templates.value = values;
    selectedTemplateId.value = values.find(item => item.id.toLowerCase() === previousSelection.toLowerCase())?.id
      ?? printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    for (const [id, state] of publicationBlocks.value) {
      const current = values.find(item => item.id.toLowerCase() === id);
      if (current && (current.latestPublishedVersionNumber > 0 || state === 'unconfirmed')) publicationBlocks.value.delete(id);
    }
    if (creationReceipt.value?.outcome === 'publishUnconfirmed') {
      const current = values.find(item => item.id.toLowerCase() === creationReceipt.value!.templateId.toLowerCase());
      if (current) creationReceipt.value.outcome = current.latestPublishedVersionNumber > 0 ? 'published' : 'draft';
    }
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
  const current = selectedTemplate.value, template = current ? { ...current } : undefined;
  if (!canRead.value || !template || template.latestPublishedVersionNumber < 1 || loading.value || previewing.value || publishing.value || (creating.value && owner !== createRequest)) {
    return;
  }
  if (printAfter && (!preview.value || preview.value.templateId.toLowerCase() !== template.id.toLowerCase())) return;
  const request = begin('printing.templates.preview', owner); if (!request) return; previewRequest = request;

  const templateId = template.id;
  previewTarget.value = template;
  const versionNumber = printAfter ? preview.value?.versionNumber : undefined;
  // 打印也重新访问受保护入口；撤权失败不能留下此前已经展示的内容。
  preview.value = undefined;
  previewing.value = true;
  problem.value = undefined;
  try {
    const value = await previewPrintingTemplate(templateId, versionNumber === undefined ? {} : {versionNumber}, request.signal);
    if (!request.current() || !targetCurrent(template)) return;
    if (value.templateId.toLowerCase() !== templateId.toLowerCase() || value.formSchemaKey !== template.formSchemaKey || (versionNumber !== undefined && value.versionNumber !== versionNumber))
      throw new Error('client.invalid_printing_template_preview');
    preview.value = value;
    await nextTick();
    if (printAfter && request.current() && targetCurrent(template)) window.print();
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
  grantDialogVisible.value = false; previewRequest?.cancel(); previewTarget.value = undefined; preview.value = undefined; previewing.value = false; problem.value = undefined;
}

async function publishSelected(): Promise<void> {
  const current = selectedTemplate.value;
  if (!canRead.value || !current || current.latestPublishedVersionNumber > 0 || !current.isEnabled || creating.value || publishing.value || loading.value || previewing.value) return;
  if (publicationBlocks.value.has(current.id.toLowerCase())) return;
  const target = { ...current }, request = begin('printing.templates.publish'); if (!request) return; publishRequest = request;
  publishing.value = true; problem.value = undefined;
  creationReceipt.value = { templateId: target.id, name: target.name, outcome: 'publishing' };
  publicationBlocks.value.set(target.id.toLowerCase(), 'unconfirmed');
  try {
    await publishPrintingTemplate(target.id, { version: target.version }, request.signal); if (!request.current()) return;
    publicationBlocks.value.set(target.id.toLowerCase(), 'published'); creationReceipt.value.outcome = 'published'; showSuccess(t('printingPreview.publishSuccess')); await load(request);
  } catch (error) { if (request.current()) { creationReceipt.value.outcome = 'publishUnconfirmed'; problem.value = toProblem(error, 'printingPreview.createFailed'); } }
  finally { if (request.current() && publishRequest === request) publishing.value = false; request.finish(); }
}

async function submitCreate(): Promise<void> {
  if (!canRead.value || !createDialogVisible.value || creating.value || publishing.value || previewing.value || loading.value) return;
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
    // 创建与发布不是原子操作；先记录已创建事实，后续失败不能重交创建表单。
    saved = true;
    const existing = templates.value.find(item => item.id.toLowerCase() === created.id.toLowerCase());
    if (!existing || existing.version <= created.version) templates.value = [...templates.value.filter(item => item.id.toLowerCase() !== created.id.toLowerCase()), created];
    selectedTemplateId.value = created.id;
    creationReceipt.value = { templateId: created.id, name: created.name, outcome: publish ? 'publishing' : 'draft' };
    if (publish) {
      publicationBlocks.value.set(created.id.toLowerCase(), 'unconfirmed');
      await publishPrintingTemplate(created.id, { version: created.version }, request.signal);
      if (!request.current()) return;
      publicationBlocks.value.set(created.id.toLowerCase(), 'published');
      creationReceipt.value.outcome = 'published';
    }
    showSuccess(t(publish ? 'printingPreview.createSuccess' : 'printingPreview.draftCreated'));
    const loaded = await load(request);
    if (!request.current() || !loaded || !templates.value.some(item => item.id === created.id)) return;
    selectedTemplateId.value = created.id;
    await runPreview(false, request);
  } catch (error: unknown) {
    if (request.current()) {
      if (saved && creationReceipt.value?.outcome === 'publishing') creationReceipt.value.outcome = 'publishUnconfirmed';
      problem.value = toProblem(error, 'printingPreview.createFailed');
    }
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
      <ArtTableHeader :loading="loading || creating || publishing" @refresh="load()">
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              :disabled="loading || creating || publishing || previewing"
              data-testid="printing-preview-create"
              @click="openCreate"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="printing.templates.grant_tenants">
            <ElButton :disabled="!selectedTemplate || selectedTemplate.latestPublishedVersionNumber < 1 || previewing || creating || publishing || loading"
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

      <div v-if="creationReceipt" role="status" data-testid="printing-creation-receipt">
        <p>{{ creationReceipt.name }} · <code translate="no">{{ creationReceipt.templateId }}</code></p>
        <p>{{ t(creationReceipt.outcome === 'draft' ? 'printingPreview.createdDraftReceipt' : creationReceipt.outcome === 'published' ? 'printingPreview.publishedReceipt' : creationReceipt.outcome === 'publishing' ? 'printingPreview.publishingReceipt' : 'printingPreview.publishUnconfirmedReceipt') }}</p>
      </div>

      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect
            v-model="selectedTemplateId"
            data-testid="printing-preview-template"
            class="w-full"
            :disabled="creating || publishing || loading"
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
            :disabled="!selectedTemplate || selectedTemplate.latestPublishedVersionNumber < 1 || creating || publishing || loading"
            data-testid="printing-preview-run"
            @click="runPreview()"
          >
            {{ t('printingPreview.preview') }}
          </ElButton></PermissionGate>
          <ElButton
            v-if="preview && session.can('printing.templates.preview')"
            :disabled="previewing || creating || publishing || loading"
            data-testid="printing-preview-print"
            @click="printPreview"
          >
            {{ t('printingPreview.print') }}
          </ElButton>
          <PermissionGate code="printing.templates.publish"><ElButton v-if="selectedTemplate?.latestPublishedVersionNumber === 0"
            data-testid="printing-preview-publish" :loading="publishing" :disabled="!canPublishSelected || creating || previewing || loading" @click="publishSelected">
            {{ t('printingPreview.publishDraft') }}
          </ElButton></PermissionGate>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <section v-if="preview" class="printing-preview-surface print-only-surface">
      <div class="printing-preview-html" v-html="safePreviewHtml" />
    </section>

    <PrintingTenantGrantsDialog v-if="grantDialogVisible && selectedTemplate && session.can('printing.templates.grant_tenants')"
      :key="selectedTemplate.id" :template="selectedTemplate" @close="grantDialogVisible = false" />
    <ElDialog
      v-if="createIntent"
      :key="createIntent.token"
      :model-value="createDialogVisible"
      v-on="{ 'update:modelValue': createIntent.update }"
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
        <ElButton data-testid="printing-preview-create-back" v-on="{ click: createIntent.close }">
          {{ t('common.cancel') }}
        </ElButton>
        <PermissionGate code="printing.templates.create"><ElButton type="primary" :loading="creating" :disabled="previewing || publishing" data-testid="printing-preview-submit" v-on="{ click: createIntent.submit }">
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
