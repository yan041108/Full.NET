<script setup lang="ts">
import { computed, nextTick, onActivated, onDeactivated, onBeforeUnmount, ref, watch } from 'vue';
import type { InputInstance } from 'element-plus';
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
  ElSelect, ElSwitch, ElPopconfirm
} from 'element-plus';
import { Plus, Printer } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, PrintingTemplateSummary, PrintingTemplate, PrintingTemplateVersionSummary } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import PrintingTemplateFields from '../components/PrintingTemplateFields.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createPrintingTemplate,
  listPrintingTemplates,
  publishPrintingTemplate, updatePrintingTemplate, getPrintingTemplate, getPrintingTemplateVersion, listPrintingTemplateVersions
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
const templates = ref<PrintingTemplateSummary[]>([]);
const selectedTemplateId = ref('');
const page = ref(1), total = ref(0), searchValue = ref('');
const history = ref<PrintingTemplateVersionSummary[]>([]), historyPage = ref(1), historyTotal = ref(0), selectedPublished = ref<number>();
const preview = ref<{ html: string; versionNumber: number }>();
const frame = ref<HTMLIFrameElement>();
const active = ref(true), writeUnconfirmed = ref(false), publishOpen = ref(false);
const editingId = ref(''), editingVersion = ref(0), publishTarget = ref<PrintingTemplate>(), changeNote = ref('');
let generation = 0, detailVersion = 0, historyVersion = 0, historyRequest: AbortController | undefined, listRequest: AbortController | undefined, requests = new AbortController(), searchRequest: AbortController | undefined,
  detailRequest: AbortController | undefined, previewRequest: AbortController | undefined;
const available = () => active.value && !session.switching && !!session.currentUser && session.currentUser.actorScope === 'host' && !session.currentUser.tenantId;
const can = (action: string) => available() && session.can('printing.templates.read') && session.can(`printing.templates.${action}`);
// 布局属于可编辑的不可信内容；必须在最终插入 DOM 前净化，覆盖数据库中已经保存的旧模板。
const safePreviewHtml = computed(() => DOMPurify.sanitize(preview.value?.html ?? '', {
  USE_PROFILES: { html: true },
  FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta', 'form', 'input', 'button', 'textarea', 'select', 'option'],
  FORBID_ATTR: ['id', 'name'],
  ALLOW_DATA_ATTR: false
}));
const loading = ref(false);
const previewing = ref(false);
const creating = ref(false);
const publishing = ref(false);
const busy = computed(() => creating.value || publishing.value);
const problem = ref<FullNetProblemDetails>();
const createDialogVisible = ref(false);
const layoutInput = ref<InputInstance>(), editorSchemaKey = ref('printing.tenant_profile_card');
const createForm = ref({
  templateKey: 'tenant-profile-card',
  name: '租户档案卡片',
  layoutHtml: DEFAULT_LAYOUT_HTML,
  isEnabled: true
});
const canSaveDraft = computed(() => !busy.value && !writeUnconfirmed.value && can(editingId.value ? 'update' : 'create')
  && !!createForm.value.name.trim() && createForm.value.name.trim().length <= 128
  && !!createForm.value.layoutHtml.trim() && createForm.value.layoutHtml.length <= 65536
  && (!!editingId.value || (!!createForm.value.templateKey.trim() && createForm.value.templateKey.trim().length <= 128)));

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

function current(value: number) { return value === generation && can('read'); }
function uncertain(error: unknown) { return !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
let templateSearchVersion = 0, previewVersion = 0, listVersion = 0;
async function searchTemplates(keyword: string): Promise<void> {
  if (!can('read')) return;
  const version = ++templateSearchVersion, scope = generation, controller = new AbortController(); searchRequest?.abort(); searchRequest = controller;
  searchValue.value = keyword; page.value = 1;
  try { const result = await listPrintingTemplates(keyword, controller.signal, 1, 20); if (current(scope) && version === templateSearchVersion && !controller.signal.aborted) { templates.value = result.items; total.value = result.total; } }
  catch (error) { if (current(scope) && version === templateSearchVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}

async function load(): Promise<void> {
  if (!can('read') || busy.value) return;
  const scope = generation, version = ++listVersion, controller = new AbortController(); listRequest?.abort(); listRequest = controller;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listPrintingTemplates(searchValue.value.trim() || undefined, controller.signal, page.value, 20);
    if (!current(scope) || version !== listVersion) return;
    templates.value = result.items; total.value = result.total;
    if (!templates.value.some(item => item.id === selectedTemplateId.value)) selectedTemplateId.value = printableTemplates.value[0]?.id ?? templates.value[0]?.id ?? '';
    preview.value = undefined;
    if (!createDialogVisible.value) writeUnconfirmed.value = false;
  } catch (error: unknown) {
    if (current(scope) && version === listVersion) problem.value = toProblem(error, 'printingPreview.loadFailed');
  } finally {
    if (scope === generation && version === listVersion) loading.value = false;
  }
}

async function runPreview(): Promise<void> {
  if (!can('preview') || !selectedPublished.value || previewing.value || busy.value) {
    return;
  }

  const scope = generation, version = ++previewVersion, id = selectedTemplateId.value, published = selectedPublished.value, controller = new AbortController();
  previewRequest?.abort(); previewRequest = controller;
  previewing.value = true; preview.value = undefined;
  problem.value = undefined;
  try {
    const body = await getPrintingTemplateVersion(id, published, controller.signal);
    if (current(scope) && can('preview') && version === previewVersion && id === selectedTemplateId.value && selectedPublished.value === published) preview.value = { html: body.layoutHtml, versionNumber: body.versionNumber };
  } catch (error: unknown) {
    if (current(scope) && version === previewVersion) problem.value = toProblem(error, 'printingPreview.previewFailed');
  } finally {
    if (scope === generation && version === previewVersion) previewing.value = false;
  }
}

function printPreview(): void {
  if (can('preview') && preview.value) frame.value?.contentWindow?.print();
}
/** 历史列表只读取二十条摘要，正文等用户选定具体版本后再读取。 */
async function loadHistory() {
  if (!can('read') || !selectedTemplateId.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++historyVersion, controller = new AbortController(); historyRequest?.abort(); historyRequest = controller;
  try {
    const result = await listPrintingTemplateVersions(id, historyPage.value, controller.signal);
    if (current(scope) && version === historyVersion && selectedTemplateId.value === id && !controller.signal.aborted) { history.value = result.items; historyTotal.value = result.total; }
  } catch (error) { if (current(scope) && version === historyVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}

function openCreate() {
  if (!can('create') || busy.value || writeUnconfirmed.value) return;
  editingId.value = ''; editingVersion.value = 0; editorSchemaKey.value = 'printing.tenant_profile_card'; createForm.value = { templateKey: '', name: '', layoutHtml: DEFAULT_LAYOUT_HTML, isEnabled: true }; createDialogVisible.value = true;
}
/** 仅修改当前草稿文本，按选区替换；字段目录不能触发保存、发布或数据绑定。 */
async function insertLayoutField(fieldKey: string) {
  if (!createDialogVisible.value || !can(editingId.value ? 'update' : 'create') || !session.can('printing.form_schemas.read') || busy.value || writeUnconfirmed.value) return;
  const input = layoutInput.value?.textarea, body = createForm.value.layoutHtml, token = `{{${fieldKey}}}`;
  const start = Math.min(input?.selectionStart ?? body.length, body.length), end = Math.max(start, Math.min(input?.selectionEnd ?? start, body.length));
  if (body.length - (end - start) + token.length > 65536) { ElMessage.warning(t('printingTemplates.layoutFull')); return; }
  const scope = generation; createForm.value.layoutHtml = body.slice(0, start) + token + body.slice(end);
  await nextTick();
  if (current(scope) && createDialogVisible.value && input) { input.focus(); input.setSelectionRange(start + token.length, start + token.length); }
}
async function editSelected() {
  if (!can('update') || !selectedTemplateId.value || busy.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++detailVersion, controller = new AbortController(); detailRequest?.abort(); detailRequest = controller;
  try {
    const draft = await getPrintingTemplate(id, controller.signal); if (!current(scope) || selectedTemplateId.value !== id || version !== detailVersion || controller.signal.aborted) return;
    editingId.value = id; editingVersion.value = draft.version; editorSchemaKey.value = draft.formSchemaKey; createForm.value = { templateKey: draft.templateKey, name: draft.name, layoutHtml: draft.layoutHtml, isEnabled: draft.isEnabled };
    createDialogVisible.value = true; writeUnconfirmed.value = false; problem.value = undefined;
  } catch (error) { if (current(scope) && version === detailVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}
async function confirmPublish() {
  if (!can('publish') || !selectedTemplateId.value || busy.value || writeUnconfirmed.value) return;
  const scope = generation, id = selectedTemplateId.value, version = ++detailVersion, controller = new AbortController(); detailRequest?.abort(); detailRequest = controller;
  try { const target = await getPrintingTemplate(id, controller.signal); if (current(scope) && selectedTemplateId.value === id && version === detailVersion && !controller.signal.aborted) { publishTarget.value = target; changeNote.value = ''; publishOpen.value = true; } }
  catch (error) { if (current(scope) && version === detailVersion && !controller.signal.aborted) problem.value = toProblem(error, 'printingPreview.loadFailed'); }
}
async function publishSelected() {
  const target = publishTarget.value; if (!target || !can('publish') || busy.value || writeUnconfirmed.value) return;
  const scope = generation; publishing.value = true; beginWrite();
  try {
    await publishPrintingTemplate(target.id, { version: target.version, changeNote: changeNote.value.trim() || null }, requests.signal);
    if (!current(scope)) return; publishOpen.value = false; publishing.value = false; ElMessage.success(t('printingTemplates.published')); await load();
  } catch (error) { if (current(scope)) { publishOpen.value = false; writeUnconfirmed.value = uncertain(error); problem.value = toProblem(error, 'printingPreview.createFailed'); } }
  finally { if (scope === generation) publishing.value = false; }
}

async function submitCreate(): Promise<void> {
  if (!can(editingId.value ? 'update' : 'create') || busy.value || writeUnconfirmed.value || !createForm.value.name.trim() || !createForm.value.layoutHtml.trim()) return;
  if (!editingId.value && !createForm.value.templateKey.trim()) return;
  if (createForm.value.name.trim().length > 128 || createForm.value.templateKey.trim().length > 128 || createForm.value.layoutHtml.length > 65536) return;
  const scope = generation;
  creating.value = true;
  beginWrite();
  problem.value = undefined;
  try {
    const body = { name: createForm.value.name.trim(), layoutHtml: createForm.value.layoutHtml, isEnabled: createForm.value.isEnabled };
    const created = editingId.value ? await updatePrintingTemplate(editingId.value, { ...body, version: editingVersion.value }, requests.signal) : await createPrintingTemplate({
      templateKey: createForm.value.templateKey,
      formSchemaKey: editorSchemaKey.value,
      ...body
    }, requests.signal);
    if (!current(scope)) return;
    ElMessage.success(t('printingTemplates.saved'));
    createDialogVisible.value = false;
    creating.value = false;
    await load();
    if (!current(scope)) return;
    if (!templates.value.some(item => item.id === created.id)) { const { layoutHtml: _, ...summary } = created; templates.value = [summary, ...templates.value.slice(0, 19)]; }
    selectedTemplateId.value = created.id;
    preview.value = undefined;
  } catch (error: unknown) {
    if (current(scope)) { writeUnconfirmed.value = uncertain(error); problem.value = toProblem(error, 'printingPreview.createFailed'); }
  } finally {
    if (scope === generation) creating.value = false;
  }
}
/** 写入开始时作废更早的目录/版本读取，未知结果不能被写前快照解除锁定。 */
function beginWrite() {
  listVersion++; templateSearchVersion++; historyVersion++; previewVersion++;
  listRequest?.abort(); searchRequest?.abort(); historyRequest?.abort(); previewRequest?.abort(); loading.value = false; previewing.value = false; preview.value = undefined;
}

function reset() {
  generation++; templateSearchVersion++; previewVersion++; listVersion++; detailVersion++; historyVersion++; requests.abort(); requests = new AbortController(); searchRequest?.abort(); detailRequest?.abort(); previewRequest?.abort(); historyRequest?.abort(); listRequest?.abort();
  templates.value = []; selectedTemplateId.value = ''; preview.value = undefined; problem.value = undefined; createDialogVisible.value = false; publishOpen.value = false;
  publishTarget.value = undefined; writeUnconfirmed.value = false; loading.value = false; previewing.value = false; creating.value = false; publishing.value = false;
  editingId.value = ''; editingVersion.value = 0; createForm.value = { templateKey: '', name: '', layoutHtml: DEFAULT_LAYOUT_HTML, isEnabled: true };
  editorSchemaKey.value = 'printing.tenant_profile_card';
  page.value = 1; total.value = 0; searchValue.value = ''; history.value = []; historyPage.value = 1; historyTotal.value = 0; selectedPublished.value = undefined;
}
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching, active.value], () => { reset(); if (can('read')) void load(); }, { immediate: true, flush: 'sync' });
watch(selectedTemplateId, () => { previewVersion++; detailVersion++; previewRequest?.abort(); detailRequest?.abort(); preview.value = undefined; previewing.value = false; publishOpen.value = false; });
watch(() => [selectedTemplateId.value, selectedTemplate.value?.latestPublishedVersionNumber], () => {
  historyPage.value = 1; history.value = []; selectedPublished.value = selectedTemplate.value?.latestPublishedVersionNumber || undefined;
  if (can('read') && selectedTemplateId.value && selectedTemplate.value) void loadHistory();
});
watch(selectedPublished, () => { previewVersion++; previewRequest?.abort(); preview.value = undefined; previewing.value = false; });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; reset(); });
</script>

<template>
  <div class="printing-preview-view">
    <ElAlert v-if="!can('read')" type="info" :closable="false" :title="t('printingTemplates.hostOnly')" />
    <ElCard v-if="can('read')" shadow="never" class="art-table-card no-print">
      <ArtTableHeader>
        <template #left>
          <PermissionGate code="printing.templates.create">
            <ElButton
              type="primary"
              plain
              :icon="Plus"
              data-testid="printing-preview-create"
              :disabled="busy || writeUnconfirmed"
              @click="openCreate"
            >
              {{ t('printingPreview.createTemplate') }}
            </ElButton>
          </PermissionGate>
        </template>
      </ArtTableHeader>
      <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="t('printingTemplates.unconfirmed')" data-testid="printing-template-unconfirmed" />
      <ElButton :loading="loading" :disabled="busy" data-testid="printing-template-refresh" @click="load">{{ t('common.refresh') }}</ElButton>
      <div class="template-actions"><ElButton :disabled="busy || loading || page <= 1" @click="page--; load()">{{ t('printingTemplates.previous') }}</ElButton><span>{{ page }}</span><ElButton :disabled="busy || loading || page * 20 >= total" @click="page++; load()">{{ t('printingTemplates.next') }}</ElButton></div>

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
            filterable remote :remote-method="searchTemplates"
            data-testid="printing-preview-template"
            :disabled="busy || createDialogVisible"
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
          <ElButton v-if="can('preview')"
            type="primary"
            :icon="Printer"
            :loading="previewing"
            :disabled="!selectedPublished || busy"
            data-testid="printing-preview-run"
            @click="runPreview"
          >
            {{ t('printingTemplates.previewPublished') }}
          </ElButton>
          <ElButton
            v-if="preview && can('preview')"
            :disabled="!preview"
            data-testid="printing-preview-print"
            @click="printPreview"
          >
            {{ t('printingPreview.print') }}
          </ElButton>
        </ElFormItem>
      </ElForm>
      <p v-if="selectedTemplate" data-testid="printing-template-versions">{{ t('printingTemplates.draftVersion') }} {{ selectedTemplate.version }} · {{ t('printingTemplates.publishedVersion') }} {{ selectedTemplate.latestPublishedVersionNumber }}</p>
      <p>{{ t('printingTemplates.versionHelp') }}</p>
      <ElForm v-if="selectedTemplate" label-position="top"><ElFormItem :label="t('printingTemplates.history')">
        <ElSelect v-model="selectedPublished" :disabled="busy" data-testid="printing-template-version" class="w-full">
          <ElOption v-for="item in history" :key="item.id" :value="item.versionNumber" :label="`${item.versionNumber} · ${item.changeNote ?? ''}`" />
        </ElSelect>
      </ElFormItem></ElForm>
      <div v-if="selectedTemplate" class="template-actions"><ElButton :disabled="busy || historyPage <= 1" @click="historyPage--; loadHistory()">{{ t('printingTemplates.previous') }}</ElButton><span>{{ historyPage }}</span><ElButton :disabled="busy || historyPage * 20 >= historyTotal" @click="historyPage++; loadHistory()">{{ t('printingTemplates.next') }}</ElButton></div>
      <div class="template-actions">
        <ElButton v-if="can('update') && selectedTemplate" :disabled="busy || writeUnconfirmed" data-testid="printing-template-edit" @click="editSelected">{{ t('printingTemplates.edit') }}</ElButton>
        <ElButton v-if="can('publish') && selectedTemplate" type="primary" :disabled="busy || writeUnconfirmed || createDialogVisible" data-testid="printing-template-publish" @click="confirmPublish">{{ t('printingTemplates.publish') }}</ElButton>
      </div>
    </ElCard>

    <section v-if="preview && can('preview')" class="printing-preview-surface print-only-surface">
      <p>{{ t('printingTemplates.previewVersion') }} {{ preview.versionNumber }}</p>
      <ElAlert type="info" :closable="false" :title="t('printingTemplates.sampleHelp')" />
      <iframe ref="frame" class="printing-preview-html" :title="t('printingTemplates.layoutTitle')" :srcdoc="safePreviewHtml" sandbox="allow-same-origin allow-modals" referrerpolicy="no-referrer" />
    </section>

    <ElDialog
      v-model="createDialogVisible"
      :title="t(editingId ? 'printingTemplates.edit' : 'printingTemplates.create')"
      width="min(760px, calc(100vw - 32px))"
      :close-on-click-modal="!busy" :close-on-press-escape="!busy" :show-close="!busy"
      class="no-print"
    >
      <ElForm label-position="top">
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="t('printingTemplates.unconfirmed')" />
        <ElFormItem :label="t('printingPreview.fieldTemplateKey')" required>
          <ElInput v-model="createForm.templateKey" :disabled="!!editingId || busy" maxlength="128" data-testid="printing-template-key" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldName')" required>
          <ElInput v-model="createForm.name" :disabled="busy" maxlength="128" data-testid="printing-template-name" />
        </ElFormItem>
        <ElFormItem :label="t('printingPreview.fieldLayoutHtml')" required>
          <ElInput ref="layoutInput" v-model="createForm.layoutHtml" :disabled="busy" type="textarea" :rows="10" :maxlength="65536" show-word-limit data-testid="printing-template-layout" />
        </ElFormItem>
        <ElFormItem v-if="createDialogVisible && session.can('printing.form_schemas.read')" :label="t('printingTemplates.fields')">
          <PrintingTemplateFields :form-schema-key="editorSchemaKey" :disabled="busy || writeUnconfirmed" @insert="insertLayoutField" />
        </ElFormItem>
        <ElFormItem :label="t('printingTemplates.enabled')"><ElSwitch v-model="createForm.isEnabled" :disabled="busy" :aria-label="t('printingTemplates.enabled')" /></ElFormItem>
        <p>{{ t('printingTemplates.enabledHelp') }}</p>
      </ElForm>
      <template #footer>
        <ElButton :disabled="busy" @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <ElPopconfirm v-if="editingId" :title="t('printingTemplates.discardConfirm')" width="280" @confirm="editSelected">
          <template #reference><ElButton :disabled="busy" data-testid="printing-template-reload-draft">{{ t('printingTemplates.reloadDraft') }}</ElButton></template>
        </ElPopconfirm>
        <ElButton v-if="can(editingId ? 'update' : 'create')" type="primary" :loading="creating" :disabled="!canSaveDraft" data-testid="printing-preview-submit" @click="submitCreate">
          {{ t('printingTemplates.save') }}
        </ElButton>
      </template>
    </ElDialog>
    <ElDialog v-model="publishOpen" :title="t('printingTemplates.publish')" width="min(480px, calc(100vw - 32px))" :close-on-click-modal="!busy" :close-on-press-escape="!busy" :show-close="!busy">
      <p>{{ publishTarget?.name }}</p><p>{{ t('printingTemplates.publishHelp') }}</p>
      <ElForm label-position="top"><ElFormItem :label="t('printingTemplates.changeNote')"><ElInput v-model="changeNote" :disabled="busy" maxlength="512" data-testid="printing-template-change-note" /></ElFormItem></ElForm>
      <template #footer><ElButton :disabled="busy" @click="publishOpen = false">{{ t('common.cancel') }}</ElButton><ElButton type="primary" :loading="publishing" :disabled="busy || writeUnconfirmed || !can('publish')" data-testid="printing-template-confirm-publish" @click="publishSelected">{{ t('printingTemplates.publish') }}</ElButton></template>
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
.template-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.printing-preview-html { display: block; width: 100%; min-height: 480px; border: 1px solid var(--el-border-color); margin-top: 12px; }

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
