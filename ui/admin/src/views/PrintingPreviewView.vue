<script setup lang="ts">
import "../printing/printing.css";
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { validHostPrintingTemplate, validHostPrintingVersion } from '../guards/printing-host-guards';
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
import type { FullNetProblemDetails, PrintingTemplate, PrintingTemplatePreview, PrintingTemplateVersion } from '@fullnet/client-contracts';
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

const session = useSessionStore(), route = inject(routeLocationKey, undefined);
const { t } = useAdminI18n();
const templates = ref<PrintingTemplate[]>([]), selectedTemplateId = ref(''), preview = ref<PrintingTemplatePreview>();
// 不可信布局必须在最终插入 DOM 前净化，已保存旧模板也不能例外。
const safePreviewHtml = computed(() => sanitizePrintingHtml(preview.value?.html ?? ''));
const grantDialogVisible = ref(false), viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const host = () => viewActive.value && (!route || route.name === 'printing-preview') && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = computed(() => host() && session.can('printing.templates.read'));
const loading = ref(false), previewing = ref(false), creating = ref(false), publishing = ref(false), recovering = ref(false), catalogReady = ref(false);
const problem = ref<FullNetProblemDetails>(), catalogProblem = ref<FullNetProblemDetails>();
type CreationReceipt = { templateId: string; name: string; versionNumber?: number; versionId?: string; outcome: 'draft' | 'publishing' | 'published' | 'publishUnconfirmed' | 'publishFailed' };
const creationReceipt = ref<CreationReceipt>(), createBlocked = ref(false), createOutcome = ref<'processing' | 'unconfirmed' | 'failed'>();
// 恢复门槛只保存资源标识和修订，布局与表单输入不进入恢复索引。
const publicationBlocks = ref(new Map<string, 'published' | 'unconfirmed'>());
const publicationBases = new Map<string, number>(), revisionFloors = new Map<string, number>(), publishedFloors = new Map<string, number>();
const previewTarget = ref<PrintingTemplate>(), createDialogVisible = ref(false);
const createForm = ref({ templateKey: 'tenant-profile-card', name: '租户档案卡片', layoutHtml: DEFAULT_LAYOUT_HTML });
const createOwner = ref(0), pageOwner = ref(0), createWindow = ref(0), grantOwner = ref(0);
let createStage: 'create' | 'publish' | 'read' | 'preview' | undefined;
const recoveryConfirmation = ref<{ kind: 'create' | 'publish'; resolve: (confirmed: boolean) => void }>();
function finishRecovery(value: boolean): void { const pending = recoveryConfirmation.value; recoveryConfirmation.value = undefined; pending?.resolve(value); }
const recoveryHandlers = computed(() => {
  const pending = recoveryConfirmation.value, finish = (value: boolean) => { if (pending && recoveryConfirmation.value === pending) finishRecovery(value); };
  return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } };
});
function resetCreateForm(): void { createForm.value = { templateKey: 'tenant-profile-card', name: '租户档案卡片', layoutHtml: DEFAULT_LAYOUT_HTML }; }
const scope = useAuthorizedViewScope(session, resetScope, async () => { await load(); });
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let previewRequest: ViewRequest | undefined, loadRequest: ViewRequest | undefined, createRequest: ViewRequest | undefined, publishRequest: ViewRequest | undefined;
let standaloneTarget: PrintingTemplate | undefined;
function resetScope(): void {
  createStage = undefined; standaloneTarget = undefined;
  templates.value = []; selectedTemplateId.value = ''; preview.value = undefined; problem.value = undefined; catalogProblem.value = undefined;
  grantDialogVisible.value = false; createDialogVisible.value = false; creationReceipt.value = undefined; publicationBlocks.value = new Map();
  publicationBases.clear(); revisionFloors.clear(); publishedFloors.clear(); createBlocked.value = false; createOutcome.value = undefined;
  previewTarget.value = undefined; loading.value = false; previewing.value = false; creating.value = false; publishing.value = false; recovering.value = false; catalogReady.value = false;
  createRequest = undefined; publishRequest = undefined; loadRequest = undefined; previewRequest = undefined;
  createOwner.value++; pageOwner.value++; finishRecovery(false); resetCreateForm();
}
watch(() => route?.name, () => { scope.invalidate(); if (canRead.value) void load(); }, { flush: 'sync' });
function begin(permission: string, owner?: ViewRequest): ViewRequest | undefined {
  if (!canRead.value || owner && !owner.current()) return;
  const request = scope.begin(permission); if (!request || !owner) return request;
  const cancel = () => request.cancel(); owner.signal.addEventListener('abort', cancel, { once: true });
  return { ...request, finish: () => { owner.signal.removeEventListener('abort', cancel); return request.finish(); } };
}
const selectedTemplate = computed(() => templates.value.find(item => item.id.toLowerCase() === selectedTemplateId.value.toLowerCase()));
function targetCurrent(target: PrintingTemplate): boolean {
  const current = selectedTemplate.value;
  return !!current && current.id.toLowerCase() === target.id.toLowerCase() && current.version === target.version && current.templateKey === target.templateKey
    && current.name === target.name && current.formSchemaKey === target.formSchemaKey && current.layoutHtml === target.layoutHtml
    && current.isEnabled === target.isEnabled && current.latestPublishedVersionNumber === target.latestPublishedVersionNumber;
}
const canPublishSelected = computed(() => catalogReady.value && !!selectedTemplate.value?.isEnabled && selectedTemplate.value.latestPublishedVersionNumber === 0
  && !publicationBlocks.value.has(selectedTemplate.value.id.toLowerCase()));
const printableTemplates = computed(() => templates.value.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0));
const unknown = (error: unknown) => !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
function toProblem(error: unknown, fallback: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { code: 'client.request_failed', title: t(fallback), status: 500, type: 'about:blank' };
}
function markPublishUnconfirmed(id: string): void {
  publicationBlocks.value = new Map(publicationBlocks.value).set(id.toLowerCase(), 'unconfirmed');
  if (creationReceipt.value?.templateId.toLowerCase() === id.toLowerCase()) creationReceipt.value = { ...creationReceipt.value, outcome: 'publishUnconfirmed' };
}
function cancelCreate(): void {
  if (createRequest?.current()) {
    if (createStage === 'create') { createBlocked.value = true; createOutcome.value = 'unconfirmed'; }
    if (createStage === 'publish' && creationReceipt.value) markPublishUnconfirmed(creationReceipt.value.templateId);
  }
  createRequest?.cancel(); createRequest = undefined; createStage = undefined; creating.value = false;
  if (loadRequest?.signal.aborted) { loading.value = false; loadRequest = undefined; catalogReady.value = false; }
  if (previewRequest?.signal.aborted) { previewing.value = false; previewRequest = undefined; preview.value = undefined; }
}
function cancelStandalonePublish(): void {
  if (publishRequest?.current() && standaloneTarget) markPublishUnconfirmed(standaloneTarget.id);
  publishRequest?.cancel(); publishRequest = undefined; standaloneTarget = undefined; publishing.value = false;
}
watch(createDialogVisible, open => {
  createOwner.value++;
  if (!open) { cancelCreate(); if (recoveryConfirmation.value?.kind === 'create') finishRecovery(false); resetCreateForm(); }
}, { flush: 'sync' });
watch(createForm, () => { createOwner.value++; cancelCreate(); if (recoveryConfirmation.value?.kind === 'create') finishRecovery(false); }, { flush: 'sync', deep: true });
function clearPreview(): void { grantDialogVisible.value = false; previewRequest?.cancel(); previewRequest = undefined; previewTarget.value = undefined; preview.value = undefined; previewing.value = false; problem.value = undefined; pageOwner.value++; }
watch(selectedTemplateId, () => { cancelStandalonePublish(); clearPreview(); if (recoveryConfirmation.value?.kind === 'publish') finishRecovery(false); }, { flush: 'sync' });
watch(templates, () => {
  pageOwner.value++;
  if (previewTarget.value && !targetCurrent(previewTarget.value)) clearPreview();
  if (standaloneTarget && !targetCurrent(standaloneTarget)) cancelStandalonePublish();
}, { flush: 'sync', deep: true });
function openCreate(): void {
  if (!canRead.value || !session.can('printing.templates.create') || creating.value || publishing.value || previewing.value || loading.value || recovering.value) return;
  resetCreateForm(); problem.value = undefined; createWindow.value++; createDialogVisible.value = true;
}
const createIntent = computed(() => {
  if (!createDialogVisible.value) return undefined;
  const token = createWindow.value, owner = createOwner.value;
  const owned = () => canRead.value && createDialogVisible.value && token === createWindow.value && owner === createOwner.value;
  const input = (key: keyof typeof createForm.value, value: string) => { if (owned() && !creating.value && !recovering.value) createForm.value[key] = value; };
  const close = () => { if (owned()) createDialogVisible.value = false; };
  return { token, close, update: (value: boolean) => { if (!value) close(); }, submit: () => { if (owned()) void submitCreate(); },
    key: (value: string) => input('templateKey', value), name: (value: string) => input('name', value), layout: (value: string) => input('layoutHtml', value), recover: () => { if (owned()) void acknowledgeRecovery('create'); } };
});
const pageControls = computed(() => {
  const owner = pageOwner.value, selection = selectedTemplateId.value, template = selectedTemplate.value ? { ...selectedTemplate.value } : undefined;
  const owned = () => canRead.value && owner === pageOwner.value && selection === selectedTemplateId.value && (!template || targetCurrent(template));
  return { refresh: () => { if (owned()) void load(); }, create: () => { if (owned()) openCreate(); }, select: (value: string) => { if (owned() && !creating.value && !publishing.value && !loading.value && !recovering.value) selectedTemplateId.value = value; },
    preview: () => { if (owned()) void runPreview(); }, print: () => { if (owned()) void printPreview(); }, publish: () => { if (owned()) void publishSelected(); },
    recover: () => { if (owned()) void acknowledgeRecovery('publish'); }, grants: () => { if (owned() && template && template.latestPublishedVersionNumber > 0 && session.can('printing.templates.grant_tenants') && !loading.value && !creating.value && !publishing.value && !previewing.value) grantDialogVisible.value = true; } };
});
watch(grantDialogVisible, () => { grantOwner.value++; pageOwner.value++; }, { flush: 'sync' });
const grantControls = computed(() => { const owner = grantOwner.value, open = grantDialogVisible.value, target = selectedTemplate.value;
  return { close: () => { if (owner === grantOwner.value && open && grantDialogVisible.value && target === selectedTemplate.value) grantDialogVisible.value = false; } }; });
async function load(owner?: ViewRequest): Promise<boolean> {
  if (!canRead.value || owner && !owner.current() || !owner && (creating.value || publishing.value || recovering.value)) return false;
  clearPreview(); loadRequest?.cancel(); const request = begin('printing.templates.read', owner); if (!request) return false; loadRequest = request;
  const previous = selectedTemplateId.value; loading.value = true; catalogReady.value = false; catalogProblem.value = undefined; pageOwner.value++;
  try {
    const values = await listPrintingTemplates(undefined, request.signal); if (!request.current() || loadRequest !== request) return false;
    if (!values.every(validHostPrintingTemplate) || new Set(values.map(row => row.id.toLowerCase())).size !== values.length
      || values.some(row => row.version < (revisionFloors.get(row.id.toLowerCase()) ?? 0) || row.latestPublishedVersionNumber < (publishedFloors.get(row.id.toLowerCase()) ?? 0))) throw Error('client.invalid_printing_template_list');
    for (const value of values) { revisionFloors.set(value.id.toLowerCase(), value.version); publishedFloors.set(value.id.toLowerCase(), value.latestPublishedVersionNumber); }
    templates.value = values; selectedTemplateId.value = values.find(row => row.id.toLowerCase() === previous.toLowerCase())?.id ?? printableTemplates.value[0]?.id ?? values[0]?.id ?? '';
    const blocks = new Map(publicationBlocks.value);
    for (const [id, state] of blocks) {
      const current = values.find(row => row.id.toLowerCase() === id), base = publicationBases.get(id) ?? 0;
      if (current && current.version > base && (state === 'unconfirmed' || current.latestPublishedVersionNumber > 0)) {
        blocks.delete(id);
        if (creationReceipt.value?.templateId.toLowerCase() === id && creationReceipt.value.outcome === 'publishUnconfirmed') creationReceipt.value = { ...creationReceipt.value, versionNumber: current.latestPublishedVersionNumber || undefined, outcome: current.latestPublishedVersionNumber > 0 ? 'published' : 'draft' };
      }
    }
    publicationBlocks.value = blocks; catalogReady.value = true; return true;
  } catch (error) { if (request.current() && loadRequest === request) catalogProblem.value = toProblem(error, 'printingPreview.loadFailed'); return false; }
  finally { if (loadRequest === request) { loading.value = false; loadRequest = undefined; pageOwner.value++; } request.finish(); }
}
async function runPreview(printAfter = false, owner?: ViewRequest): Promise<void> {
  const template = selectedTemplate.value ? { ...selectedTemplate.value } : undefined;
  if (!canRead.value || !catalogReady.value || !template?.isEnabled || template.latestPublishedVersionNumber < 1 || loading.value || previewing.value || publishing.value || recovering.value || creating.value && owner !== createRequest) return;
  if (printAfter && (!preview.value || preview.value.templateId.toLowerCase() !== template.id.toLowerCase())) return;
  const versionNumber = printAfter ? preview.value!.versionNumber : template.latestPublishedVersionNumber;
  const request = begin('printing.templates.preview', owner); if (!request) return; previewRequest = request;
  previewTarget.value = template; preview.value = undefined; previewing.value = true; problem.value = undefined; pageOwner.value++;
  try {
    const value = await previewPrintingTemplate(template.id, { versionNumber }, request.signal);
    if (!request.current() || !targetCurrent(template) || previewRequest !== request) return;
    if (value.templateId.toLowerCase() !== template.id.toLowerCase() || value.templateKey !== template.templateKey || value.formSchemaKey !== template.formSchemaKey || value.versionNumber !== versionNumber) throw Error('client.invalid_printing_template_preview');
    preview.value = value; await nextTick();
    if (printAfter && request.current() && previewRequest === request && targetCurrent(template)) window.print();
  } catch (error) { if (request.current() && previewRequest === request) problem.value = toProblem(error, 'printingPreview.previewFailed'); }
  finally { if (previewRequest === request) { previewing.value = false; previewRequest = undefined; pageOwner.value++; } request.finish(); }
}
async function printPreview(): Promise<void> { if (preview.value) await runPreview(true); }
function acceptPublication(target: PrintingTemplate, value: PrintingTemplateVersion): void {
  if (!validHostPrintingVersion(value) || value.templateId.toLowerCase() !== target.id.toLowerCase() || value.versionNumber !== target.latestPublishedVersionNumber + 1
    || value.layoutHtml !== target.layoutHtml || value.changeNote !== null || value.publishedByUserId.toLowerCase() !== session.currentUser?.id.toLowerCase()) throw Error('client.invalid_printing_template_version');
  publicationBlocks.value = new Map(publicationBlocks.value).set(target.id.toLowerCase(), 'published');
  revisionFloors.set(target.id.toLowerCase(), target.version + 1); publishedFloors.set(target.id.toLowerCase(), value.versionNumber);
  creationReceipt.value = { templateId: target.id, name: target.name, outcome: 'published', versionNumber: value.versionNumber, versionId: value.id };
}
function publishFailure(target: PrintingTemplate, error: unknown): void {
  if (unknown(error)) markPublishUnconfirmed(target.id);
  else { const blocks = new Map(publicationBlocks.value); blocks.delete(target.id.toLowerCase()); publicationBlocks.value = blocks; creationReceipt.value = { templateId: target.id, name: target.name, outcome: 'publishFailed' }; }
}
async function publishSelected(): Promise<void> {
  const current = selectedTemplate.value;
  if (!canRead.value || !canPublishSelected.value || !current || creating.value || publishing.value || loading.value || previewing.value || recovering.value) return;
  const target = { ...current }, request = begin('printing.templates.publish'); if (!request) return; publishRequest = request; standaloneTarget = target;
  publishing.value = true; problem.value = undefined; pageOwner.value++;
  creationReceipt.value = { templateId: target.id, name: target.name, outcome: 'publishing' }; publicationBases.set(target.id.toLowerCase(), target.version); publicationBlocks.value = new Map(publicationBlocks.value).set(target.id.toLowerCase(), 'unconfirmed');
  try {
    const value = await publishPrintingTemplate(target.id, { version: target.version }, request.signal); if (!request.current()) return;
    acceptPublication(target, value); standaloneTarget = undefined; publishRequest = undefined; publishing.value = false;
    showSuccess(t('printingPreview.publishSuccess')); await load(request);
  } catch (error) { if (request.current()) { publishFailure(target, error); problem.value = toProblem(error, 'printingPreview.createFailed'); } }
  finally { if (publishRequest === request) { publishing.value = false; publishRequest = undefined; standaloneTarget = undefined; } pageOwner.value++; request.finish(); }
}
async function submitCreate(): Promise<void> {
  if (!canRead.value || !createDialogVisible.value || createBlocked.value || creating.value || publishing.value || previewing.value || loading.value || recovering.value) return;
  const input = { ...createForm.value };
  if (!input.templateKey.trim() || !input.name.trim() || !input.layoutHtml.trim()) { showWarning(t('printingPreview.requiredFields')); return; }
  const request = begin('printing.templates.create'); if (!request) return; createRequest = request;
  const publish = session.can('printing.templates.publish'); let saved = false, target: PrintingTemplate | undefined;
  creating.value = true; createStage = 'create'; createOutcome.value = 'processing'; problem.value = undefined; createOwner.value++; pageOwner.value++;
  try {
    const created = await createPrintingTemplate({ ...input, formSchemaKey: 'printing.tenant_profile_card', isEnabled: true }, request.signal); if (!request.current()) return;
    if (!validHostPrintingTemplate(created) || created.version !== 1 || created.latestPublishedVersionNumber !== 0) throw Error('client.invalid_printing_template');
    // 创建已确认后，后续发布或读取失败只恢复后续步骤，不能再提交创建。
    saved = true; target = { ...created }; createOutcome.value = undefined; revisionFloors.set(created.id.toLowerCase(), created.version);
    templates.value = [...templates.value.filter(row => row.id.toLowerCase() !== created.id.toLowerCase()), created]; selectedTemplateId.value = created.id;
    creationReceipt.value = { templateId: created.id, name: created.name, outcome: publish ? 'publishing' : 'draft' };
    if (publish) {
      createStage = 'publish'; publicationBases.set(created.id.toLowerCase(), created.version); publicationBlocks.value = new Map(publicationBlocks.value).set(created.id.toLowerCase(), 'unconfirmed');
      const value = await publishPrintingTemplate(created.id, { version: created.version }, request.signal); if (!request.current()) return; acceptPublication(created, value);
    }
    createStage = 'read'; showSuccess(t(publish ? 'printingPreview.createSuccess' : 'printingPreview.draftCreated'));
    const loaded = await load(request); if (!request.current() || !loaded || !templates.value.some(row => row.id.toLowerCase() === created.id.toLowerCase())) return;
    selectedTemplateId.value = created.id; createStage = 'preview'; await runPreview(false, request);
  } catch (error) {
    if (request.current()) {
      if (!saved) { createOutcome.value = unknown(error) ? 'unconfirmed' : 'failed'; if (unknown(error)) createBlocked.value = true; }
      else if (createStage === 'publish' && target) publishFailure(target, error);
      problem.value = toProblem(error, 'printingPreview.createFailed');
    }
  } finally {
    if (createRequest === request) {
      createRequest = undefined; creating.value = false; createStage = undefined; createOwner.value++; pageOwner.value++;
      if (saved) createDialogVisible.value = false;
    }
    request.finish();
  }
}
async function acknowledgeRecovery(kind: 'create' | 'publish'): Promise<void> {
  const target = selectedTemplate.value ? { ...selectedTemplate.value } : undefined, owner = createOwner.value, page = pageOwner.value;
  if (!canRead.value || creating.value || publishing.value || previewing.value || loading.value || recovering.value
    || (kind === 'create' ? !createDialogVisible.value || !createBlocked.value : !target || publicationBlocks.value.get(target.id.toLowerCase()) !== 'unconfirmed')) return;
  const request = begin(kind === 'create' ? 'printing.templates.create' : 'printing.templates.publish'); if (!request) return;
  recovering.value = true;
  try {
    const confirmed = await new Promise<boolean>(resolve => { recoveryConfirmation.value = { kind, resolve }; });
    if (!confirmed || !request.current()) return;
    if (kind === 'create' && createDialogVisible.value && createOwner.value === owner) createBlocked.value = false;
    else if (kind === 'publish' && target && pageOwner.value === page && targetCurrent(target)) { const blocks = new Map(publicationBlocks.value); blocks.delete(target.id.toLowerCase()); publicationBlocks.value = blocks; }
  } finally { if (request.current()) recovering.value = false; request.finish(); }
}

</script>

<template>
  <div v-if="canRead" class="printing-preview-view art-page-stack">
    <ElCard shadow="never" class="art-table-card no-print">
      <ArtTableHeader :loading="loading || creating || publishing" v-on="{ refresh: pageControls.refresh }">
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              :disabled="loading || creating || publishing || previewing"
              data-testid="printing-preview-create"
              v-on="{ click: pageControls.create }"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="printing.templates.grant_tenants">
            <ElButton :disabled="!selectedTemplate || selectedTemplate.latestPublishedVersionNumber < 1 || previewing || creating || publishing || loading"
              data-testid="printing-tenant-grants-open" v-on="{ click: pageControls.grants }">{{ t('printingGrants.open') }}</ElButton>
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

      <ElAlert v-if="catalogProblem" type="error" :title="catalogProblem.title" :description="catalogProblem.detail ?? catalogProblem.code" :closable="false" />
      <div v-if="createOutcome" role="status" data-testid="printing-create-outcome">{{ t(createOutcome === 'processing' ? 'printingPreview.creatingReceipt' : createOutcome === 'failed' ? 'printingPreview.createFailed' : 'printingPreview.createUnconfirmedReceipt') }}</div>

      <div v-if="creationReceipt" role="status" data-testid="printing-creation-receipt">
        <p>{{ creationReceipt.name }} · <code translate="no">{{ creationReceipt.templateId }}</code></p>
        <p>{{ t(creationReceipt.outcome === 'draft' ? 'printingPreview.createdDraftReceipt' : creationReceipt.outcome === 'published' ? 'printingPreview.publishedReceipt' : creationReceipt.outcome === 'publishing' ? 'printingPreview.publishingReceipt' : creationReceipt.outcome === 'publishFailed' ? 'printingPreview.publishFailedReceipt' : 'printingPreview.publishUnconfirmedReceipt') }}</p>
        <p v-if="creationReceipt.versionNumber">{{ t('printingPreview.publishedVersionReceipt', { version: String(creationReceipt.versionNumber) }) }}</p>
      </div>

      <ElForm label-width="120px">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect
            :model-value="selectedTemplateId"
            v-on="{ 'update:modelValue': pageControls.select }"
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
            :disabled="!catalogReady || !selectedTemplate || selectedTemplate.latestPublishedVersionNumber < 1 || creating || publishing || loading"
            data-testid="printing-preview-run"
            v-on="{ click: pageControls.preview }"
          >
            {{ t('printingPreview.preview') }}
          </ElButton></PermissionGate>
          <ElButton
            v-if="preview && session.can('printing.templates.preview')"
            :disabled="previewing || creating || publishing || loading"
            data-testid="printing-preview-print"
            v-on="{ click: pageControls.print }"
          >
            {{ t('printingPreview.print') }}
          </ElButton>
          <PermissionGate code="printing.templates.publish"><ElButton v-if="selectedTemplate?.latestPublishedVersionNumber === 0"
            data-testid="printing-preview-publish" :loading="publishing" :disabled="!canPublishSelected || creating || previewing || loading" v-on="{ click: pageControls.publish }">
            {{ t('printingPreview.publishDraft') }}
          </ElButton></PermissionGate>
          <PermissionGate code="printing.templates.publish"><ElButton v-if="selectedTemplate && publicationBlocks.get(selectedTemplate.id.toLowerCase()) === 'unconfirmed'" :disabled="creating || publishing || previewing || loading || recovering" data-testid="printing-publish-recover" v-on="{ click: pageControls.recover }">{{ t('printingPreview.recoveryTitle') }}</ElButton></PermissionGate>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <section v-if="preview" class="printing-preview-surface print-only-surface">
      <div class="printing-preview-html" v-html="safePreviewHtml" />
    </section>

    <PrintingTenantGrantsDialog v-if="grantDialogVisible && selectedTemplate && session.can('printing.templates.grant_tenants')"
      :key="selectedTemplate.id" :template="selectedTemplate" v-on="{ close: grantControls.close }" />
    <ArtFormDialog v-if="recoveryConfirmation" :open="true" :title="t('printingPreview.recoveryTitle')" confirm-test-id="printing-recovery-confirm"
      :confirm-label="t('users.confirm')" :cancel-label="t('common.cancel')" v-on="{ confirm: recoveryHandlers.confirm, 'update:open': recoveryHandlers.update }">
      <p>{{ t('printingPreview.recoveryHint') }}</p>
    </ArtFormDialog>
    <ElDialog
      v-if="createIntent"
      :key="createIntent.token"
      :model-value="createDialogVisible"
      v-on="{ 'update:modelValue': createIntent.update }"
      :title="t('printingPreview.createTitle')"
      width="640px"
      class="no-print"
    >
      <ElAlert v-if="problem" type="error" :title="problem.title" :description="problem.detail ?? problem.code" :closable="false" />
      <ElAlert v-if="createBlocked" type="warning" :title="t('printingPreview.recoveryHint')" :closable="false" />
      <ElButton v-if="createBlocked" :disabled="recovering || creating" data-testid="printing-create-recover" v-on="{ click: createIntent.recover }">{{ t('printingPreview.recoveryTitle') }}</ElButton>
      <ElForm label-width="120px" :disabled="creating || recovering">
        <ElFormItem :label="t('printingPreview.fieldTemplateKey')" required>
          <ElInput :model-value="createForm.templateKey" v-on="{ 'update:modelValue': createIntent.key }" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldName')" required>
          <ElInput :model-value="createForm.name" v-on="{ 'update:modelValue': createIntent.name }" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldLayoutHtml')" required>
          <ElInput :model-value="createForm.layoutHtml" type="textarea" :rows="10" v-on="{ 'update:modelValue': createIntent.layout }" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton data-testid="printing-preview-create-back" v-on="{ click: createIntent.close }">
          {{ t('common.cancel') }}
        </ElButton>
        <PermissionGate code="printing.templates.create"><ElButton type="primary" :loading="creating" :disabled="previewing || publishing || createBlocked || recovering" data-testid="printing-preview-submit" v-on="{ click: createIntent.submit }">
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
