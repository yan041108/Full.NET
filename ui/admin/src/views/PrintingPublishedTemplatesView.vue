<script setup lang="ts">
import "../printing/printing.css";
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElEmpty, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import { Printer, Refresh } from '@element-plus/icons-vue';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type PrintingPublishedTemplateResponse, type PrintingTemplatePreview } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate } from '../api/printing-templates';
import { sanitizePrintingHtml } from '../printing/sanitizePrintingHtml';
import { isPrintingRecordId, resolvePrintingRecordLink } from '../printing/record-print-link';

defineOptions({name:'PrintingPublishedTemplatesView'});
const session = useSessionStore(); const { t } = useAdminI18n();
const route = inject(routeLocationKey, undefined);
const readPermission = 'printing.published_templates.read';
const previewPermission = 'printing.published_templates.preview';
const active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; });
onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
// 有效作用域携带规范化租户 UUID；Host 账号切租户仍须匹配当前上下文，不能按 actorScope 判断。
const tenant = () => {
  const user = session.currentUser;
    if (!user?.tenantId || !active.value || route && route.name !== 'printing-published-templates') return false;
  return user.scope === `tenant:${user.tenantId.replaceAll('-', '').toLowerCase()}`;
};
const templates = ref<PrintingPublishedTemplateResponse[]>([]); const selectedId = ref('');
const preview = ref<PrintingTemplatePreview>(); const problem = ref<FullNetProblemDetails>();
const catalogProblem = ref<FullNetProblemDetails>(), catalogReady = ref(false);
const recordId = ref('');
const loading = ref(false); const previewing = ref(false);
const readable = () => tenant() && session.can(readPermission);
const previewTarget = ref<PrintingPublishedTemplateResponse>();
let controlSequence = 0;
interface Controls { token: number; select: (id: string) => void; record: (id: string) => void; preview: () => void; print: () => void; refresh: () => void }
const controls = ref<Controls>(makeControls());
function makeControls(): Controls {
  const token = ++controlSequence, owned = () => controls.value.token === token && readable();
  return { token, select: id => { if (owned() && !loading.value) selectedId.value = id; }, record: id => { if (owned() && !loading.value) recordId.value = id; },
    preview: () => { if (owned()) void render(); }, print: () => { if (owned()) void render(true); }, refresh: () => { if (owned()) void load(true); } };
}
let linkBlocked = false; let lastLoadKey: string | undefined, acceptedLoadKey: string | undefined;
// 同一模板可以获授多个版本；选择身份必须同时包含模板和不可变版本。
const versionKey = (item: PrintingPublishedTemplateResponse) => item.templateId.toLowerCase() + ':' + item.versionNumber;
const selected = computed(() => templates.value.find(item => versionKey(item) === selectedId.value.toLowerCase()));
const validRecord = computed(() => !selected.value?.requiresRecordId || isPrintingRecordId(recordId.value.trim()));
const safeHtml = computed(() => sanitizePrintingHtml(preview.value?.html ?? ''));
const scope = useAuthorizedViewScope(session, () => {
  // 旧 URL 不能在新账号、租户或激活代次中自动重新解释；显式新路由才重新定位。
  linkBlocked = true; lastLoadKey = undefined; acceptedLoadKey = undefined; catalogReady.value = false; catalogProblem.value = undefined;
  templates.value = []; selectedId.value = ''; recordId.value = ''; preview.value = undefined; problem.value = undefined;
  loading.value = false; previewing.value = false; previewTarget.value = undefined; controls.value = makeControls();
}, () => load(false));
let listRequest: ReturnType<typeof scope.begin>; let previewRequest: ReturnType<typeof scope.begin>;
function toProblem(error: unknown, catalog = false): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : {status:500,code:'client.request_failed',title:t(catalog ? 'printingPublished.loadFailed' : 'printingPreview.previewFailed')};
}
function clearPreview(): void {
  previewRequest?.cancel(); previewTarget.value = undefined; preview.value = undefined; previewing.value = false; problem.value = undefined; controls.value = makeControls();
}
// 业务编号变化立即取消旧请求，避免迟到内容或旧打印覆盖新选择。
watch(recordId, clearPreview, {flush:'sync'});
watch(selectedId, changeTemplate, {flush:'sync'});
function targetCurrent(target: PrintingPublishedTemplateResponse): boolean {
  const current = selected.value;
  return !!current && versionKey(current) === versionKey(target) && current.formSchemaKey === target.formSchemaKey
    && current.templateKey === target.templateKey && current.templateName === target.templateName && !!current.requiresRecordId === !!target.requiresRecordId;
}
watch(templates, () => { if (previewTarget.value && !targetCurrent(previewTarget.value)) clearPreview(); }, {deep:true,flush:'sync'});
function changeTemplate(_next: string, previous: string): void {
  const before = templates.value.find(item => versionKey(item) === previous.toLowerCase());
  const linked = resolvePrintingRecordLink(route?.query ?? {}, templates.value);
  // 同表单的已授版本共用记录上下文，不能将用户修正的编号替换成旧 URL 默认值。
  if (!(before?.requiresRecordId && selected.value?.requiresRecordId && before.formSchemaKey === selected.value.formSchemaKey))
    recordId.value = !linkBlocked && linked.kind === 'bound' && selected.value?.formSchemaKey === linked.templates[0]?.formSchemaKey ? linked.recordId : '';
  clearPreview();
}
watch(() => selected.value ? JSON.stringify(selected.value) : '', (value, previous) => {
  if (value && previous) {
    const current = JSON.parse(value) as PrintingPublishedTemplateResponse, before = JSON.parse(previous) as PrintingPublishedTemplateResponse;
    if (versionKey(current) === versionKey(before) && (current.formSchemaKey !== before.formSchemaKey || !!current.requiresRecordId !== !!before.requiresRecordId)) recordId.value = '';
  }
  if (previewTarget.value && !targetCurrent(previewTarget.value)) clearPreview();
  controls.value = makeControls();
}, { flush: 'sync' });
watch(() => JSON.stringify({ route: route?.name, formSchemaKey: route?.query.formSchemaKey, recordId: route?.query.recordId }), () => {
  if (route?.name !== 'printing-published-templates') {
    clearPreview(); listRequest?.cancel(); templates.value = []; selectedId.value = ''; recordId.value = ''; loading.value = false; linkBlocked = true; lastLoadKey = undefined; acceptedLoadKey = undefined; catalogReady.value = false; catalogProblem.value = undefined; return;
  }
  linkBlocked = false; void load(false);
}, { flush: 'sync' });
async function load(force = true): Promise<void> {
  if (!readable()) return;
  const copy = (value: unknown) => Array.isArray(value) ? [...value] : value;
  const query = { formSchemaKey: copy(route?.query.formSchemaKey), recordId: copy(route?.query.recordId) };
  // 对象编码区分缺省与显式空查询，不能把无链接和非法空链接判为同一个目录请求。
  const key = JSON.stringify({ ...query, linkBlocked });
  // 同轮上下文恢复与路由定位只发起一次目录读取；用户刷新仍允许重试失败请求。
  if (!force && key === lastLoadKey) return;
  const previous = selected.value ? { ...selected.value } : undefined, previousRecord = recordId.value, preserve = key === acceptedLoadKey;
  clearPreview(); listRequest?.cancel();
  const request = scope.begin(readPermission); if (!request) return; listRequest = request;
  lastLoadKey = key;
  catalogReady.value = false; catalogProblem.value = undefined;
  if (!preserve) { templates.value = []; selectedId.value = ''; recordId.value = ''; }
  loading.value = true;
  try {
    const result = await listPrintingPublishedTemplates(request.signal);
    if (!request.current()) return;
    if (result.some(row => !isGuid(row.templateId) || row.templateId === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1 || !row.formSchemaKey.trim() || !row.templateKey.trim())
      || new Set(result.map(versionKey)).size !== result.length) throw new Error('client.invalid_printing_published_template_list');
    const linked = resolvePrintingRecordLink(query, result);
    if (linked.kind !== 'none' && (linkBlocked || linked.kind === 'invalid')) {
      templates.value = []; selectedId.value = ''; recordId.value = '';
      acceptedLoadKey = undefined;
      catalogProblem.value = {status:400,code:'client.invalid_printing_record_link',title:t('printingPublished.linkUnavailable')};
    } else {
      const rows = linked.kind === 'bound' ? linked.templates : result;
      const retained = preserve && previous ? rows.find(row => versionKey(row) === versionKey(previous) && row.formSchemaKey === previous.formSchemaKey && row.templateKey === previous.templateKey && row.templateName === previous.templateName && !!row.requiresRecordId === !!previous.requiresRecordId) : undefined;
      templates.value = rows;
      selectedId.value = retained ? versionKey(retained) : rows[0] ? versionKey(rows[0]) : '';
      recordId.value = retained ? previousRecord : linked.kind === 'bound' ? linked.recordId : '';
      acceptedLoadKey = key; catalogReady.value = true;
    }
  } catch (error) { if (request.current()) catalogProblem.value = toProblem(error, true); }
  finally { if (request.current()) { loading.value = false; controls.value = makeControls(); } request.finish(); }
}
async function render(printAfter = false): Promise<void> {
  if (!readable() || !catalogReady.value || !selected.value || !validRecord.value || loading.value || previewing.value) return;
  if (printAfter && !preview.value) return;
  const request = scope.begin(previewPermission); if (!request) return;
  const template = { ...selected.value }; const bindingRecordId = recordId.value.trim(); previewRequest = request; previewTarget.value = template;
  // 授权可在会话权限不变时撤销；每次打印都重新核验服务器上的精确版本。
  preview.value = undefined; problem.value = undefined; previewing.value = true; controls.value = makeControls();
  try {
    const result = await previewPrintingPublishedTemplate(template.templateId,{versionNumber:template.versionNumber,...(template.requiresRecordId ? {recordId:bindingRecordId} : {})},request.signal);
    if (!request.current() || !readable() || !targetCurrent(template) || recordId.value.trim() !== bindingRecordId) return;
    if (result.templateId.toLowerCase() !== template.templateId.toLowerCase() || result.versionNumber !== template.versionNumber || result.formSchemaKey !== template.formSchemaKey)
      throw new Error('client.invalid_printing_template_preview');
    preview.value = result;
    // 浏览器打印之前等待净化后的新内容进入 DOM，并再次核对当前页面代次。
    await nextTick();
    if (printAfter && request.current() && readable() && targetCurrent(template) && recordId.value.trim() === bindingRecordId) window.print();
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) { previewing.value = false; controls.value = makeControls(); } request.finish(); }
}
</script>

<template>
  <div v-if="readable()" class="printing-published-view art-page-stack">
    <ElCard shadow="never" class="no-print">
      <ArtTableHeader>
        <template #left><span>{{ t('printingPublished.hint') }}</span></template>
        <template #right><PermissionGate :code="readPermission"><ElButton :icon="Refresh" :loading="loading" data-testid="printing-published-refresh" v-on="{ click: controls.refresh }">{{ t('printingGrants.refresh') }}</ElButton></PermissionGate></template>
      </ArtTableHeader>
      <ElAlert v-if="catalogProblem" data-testid="printing-published-catalog-problem" type="error" :title="catalogProblem.title" :closable="false" show-icon />
      <ElAlert v-if="problem" data-testid="printing-published-preview-problem" type="error" :title="problem.title" :closable="false" show-icon />
      <p v-if="!catalogReady && !loading && templates.length">{{ t('printingPublished.catalogNeedsRefresh') }}</p>
      <ElEmpty v-if="!loading && !templates.length && !problem && !catalogProblem" :description="t('printingPublished.empty')" />
      <ElForm label-width="120px" class="printing-published-form">
        <ElFormItem :label="t('printingPreview.fieldTemplate')">
          <ElSelect :model-value="selectedId" :disabled="loading || previewing" class="printing-template-select" data-testid="printing-published-template" v-on="{ 'update:modelValue': controls.select }">
            <ElOption v-for="item in templates" :key="versionKey(item)" :label="item.templateName + ' · v' + item.versionNumber" :value="versionKey(item)" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem v-if="selected?.requiresRecordId" :label="t('printingPublished.recordId')" required>
          <ElInput :model-value="recordId" :disabled="loading" data-testid="printing-published-record" :placeholder="t('printingPublished.recordIdHint')" :maxlength="36" v-on="{ 'update:modelValue': controls.record }" />
        </ElFormItem>
        <ElFormItem>
          <PermissionGate :code="previewPermission"><ElButton type="primary" :icon="Printer" :loading="previewing" :disabled="!catalogReady || !selected || !validRecord || loading" data-testid="printing-published-preview" v-on="{ click: controls.preview }">{{ t('printingPreview.preview') }}</ElButton></PermissionGate>
          <PermissionGate :code="previewPermission"><ElButton v-if="preview" :disabled="loading || previewing" data-testid="printing-published-print" v-on="{ click: controls.print }">{{ t('printingPreview.print') }}</ElButton></PermissionGate>
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
