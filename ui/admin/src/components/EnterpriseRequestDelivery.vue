<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import DOMPurify from 'dompurify';
import { ElAlert, ElButton, ElDialog } from 'element-plus';
import { isFullNetProblemDetails, type EnterpriseRequestAttachmentResponse, type EnterpriseRequestReportResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { enterpriseDeliveryText } from '../i18n/enterprise-request-delivery';
import * as api from '../api/enterprise-request-delivery';
import { useBoundedPolling } from '../composables/useBoundedPolling';

const props = defineProps<{ requestId: string }>();
const emit = defineEmits<{ updated: [] }>();
const open = defineModel<boolean>({ default: false });
const session = useSessionStore();
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof enterpriseDeliveryText>[1]) => enterpriseDeliveryText(locale.value, key);
const canRead = () => !session.switching && !!session.currentUser?.tenantId && session.can('enterprise_request.enterprise_requests.read');
const can = (suffix: string) => canRead() && session.can(`enterprise_request.${suffix}`);
const visible = computed(() => ['attachments.read', 'attachments.manage', 'reports.read', 'documents.print'].some(can));
const busy = ref(false), failure = ref(''), version = ref(''), draft = ref(false);
const attachments = ref<EnterpriseRequestAttachmentResponse[]>([]), reports = ref<EnterpriseRequestReportResponse[]>([]);
const cancelOpen = ref(false), cancelTarget = ref<EnterpriseRequestReportResponse>(), writeUnconfirmed = ref(false);
const attachmentPage = ref(1), reportPage = ref(1), attachmentTotal = ref(0), reportTotal = ref(0);
const selected = ref<File>(), input = ref<HTMLInputElement>(), preview = ref(''), frame = ref<HTMLIFrameElement>();
const pendingReports = computed(() => reports.value.some(report => ['Queued', 'Running', 'FailedCleanup', 'CancelledCleanup', 'ExpiredCleanup', 'DeletedCleanup'].includes(report.phase)));
let uploadKey: string | undefined, uploadVersion: string | undefined, reportKey: string | undefined, reportVersion: string | undefined,
  cancellation = new AbortController(), generation = 0;
// 仅更新当前页任务状态，保留主单版本快照；会话、单据与分页共同绑定本轮读取。
const progress = useBoundedPolling({
  enabled: () => open.value && !busy.value && !cancelOpen.value && !writeUnconfirmed.value && can('reports.read'), pending: () => pendingReports.value,
  key: () => `${session.currentUser?.sessionId}|${session.currentUser?.tenantId}|${props.requestId}|${reportPage.value}`,
  intervalMs: 5000,
  read: signal => api.deliveryReports(props.requestId, reportPage.value, signal),
  accept: tasks => { reports.value = tasks.items; reportTotal.value = tasks.total; },
  failed: error => { failure.value = isFullNetProblemDetails(error) ? `${text('failure')} (${error.code})` : text('failure'); }
});
const progressPaused = progress.paused;
const safePreview = computed(() => DOMPurify.sanitize(preview.value, {
  USE_PROFILES: { html: true }, FORBID_TAGS: ['script', 'style', 'iframe', 'form', 'input', 'button', 'object', 'embed', 'link', 'meta'],
  FORBID_ATTR: ['id', 'name'], ALLOW_DATA_ATTR: false
}));
/** 清理状态已禁止结果访问，不能在文件尚待删除时把业务终态显示成仍在生成。 */
function phase(value: string) {
  switch (value) {
    case 'Ready': return text('ready');
    case 'Failed': case 'FailedCleanup': return text('failed');
    case 'Cancelled': case 'CancelledCleanup': return text('cancelled');
    case 'Expired': case 'ExpiredCleanup': return text('expired');
    case 'Deleted': case 'DeletedCleanup': return text('deleted');
    case 'Deleting': return text('deleting');
    case 'Queued': return text('queuedStatus');
    case 'Running': case 'Uploading': return text('processing');
    default: return `${text('unknownStatus')} (${value})`;
  }
}
/** 只展示服务端给定有效期；浏览器时钟不能决定结果是否已经失效。 */
function expiry(value: string): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'long' }).format(date) : text('expiryUnknown');
}
function reset() {
  progress.stop();
  generation++; cancellation.abort(); cancellation = new AbortController();
  attachments.value = []; reports.value = []; preview.value = ''; selected.value = undefined;
  cancelOpen.value = false; cancelTarget.value = undefined; writeUnconfirmed.value = false;
  uploadKey = undefined; uploadVersion = undefined; reportKey = undefined; reportVersion = undefined;
  version.value = ''; failure.value = ''; busy.value = false;
  draft.value = false; attachmentTotal.value = 0; reportTotal.value = 0;
  attachmentPage.value = 1; reportPage.value = 1; if (input.value) input.value.value = '';
}
watch([() => props.requestId, open], ([, value]) => { reset(); if (value) void act(load); }, { immediate: true });
// 金额权限或精确操作权限变化也会使旧文档失效，不能接纳撤权前的预览与下载结果。
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.permissions.join('|'), session.switching], () => { open.value = false; reset(); }, { flush: 'sync' });
onDeactivated(() => { open.value = false; reset(); });
onBeforeUnmount(reset);
async function refresh() { progress.restart(); await act(async () => { await load(); writeUnconfirmed.value = false; }); }
async function load() {
  const current = generation;
  const request = await api.deliveryRequest(props.requestId, cancellation.signal);
  if (current !== generation || !canRead()) return;
  const [files, tasks] = await Promise.all([
    can('attachments.read') ? api.deliveryAttachments(props.requestId, attachmentPage.value, cancellation.signal) : undefined,
    can('reports.read') ? api.deliveryReports(props.requestId, reportPage.value, cancellation.signal) : undefined
  ]);
  if (current !== generation) return;
  version.value = request.version; draft.value = request.status === 'Draft';
  attachments.value = files?.items ?? []; attachmentTotal.value = files?.total ?? 0;
  reports.value = tasks?.items ?? []; reportTotal.value = tasks?.total ?? 0;
}
async function act(action: () => Promise<unknown>) {
  if (busy.value || !canRead() || !visible.value) return;
  const current = generation; busy.value = true; failure.value = '';
  try { await action(); }
  catch (error) { if (current === generation) failure.value = isFullNetProblemDetails(error) ? `${text('failure')} (${error.code})` : text('failure'); }
  finally { if (current === generation) busy.value = false; }
}
function choose(event: Event) { selected.value = (event.target as HTMLInputElement).files?.[0]; uploadKey = undefined; uploadVersion = undefined; }
async function upload() {
  if (writeUnconfirmed.value) return;
  const file = selected.value;
  if (!file || file.size < 1 || file.size > 10 * 1024 * 1024) { failure.value = text('invalid'); return; }
  uploadKey ??= crypto.randomUUID(); uploadVersion ??= version.value;
  const current = generation;
  await api.deliveryUpload(props.requestId, uploadKey, uploadVersion, file, cancellation.signal);
  if (current !== generation) return;
  uploadKey = undefined; uploadVersion = undefined; selected.value = undefined; if (input.value) input.value.value = '';
  await load(); emit('updated');
}
async function createReport() {
  if (writeUnconfirmed.value) return;
  reportKey ??= crypto.randomUUID(); reportVersion ??= version.value;
  const current = generation;
  await api.deliveryCreateReport(props.requestId, reportKey, reportVersion, cancellation.signal);
  if (current !== generation) return;
  reportKey = undefined; reportVersion = undefined; progress.restart(); await load();
}
function save(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob), link = document.createElement('a');
  link.href = url; link.download = fileName; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function remove(file: EnterpriseRequestAttachmentResponse) {
  if (writeUnconfirmed.value) return;
  const current = generation;
  await api.deliveryDelete(props.requestId, file.id, version.value, file.version, cancellation.signal);
  if (current !== generation) return;
  preview.value = ''; await load(); emit('updated');
}
async function download(kind: 'Attachment' | 'Report', id: string, fileName: string) {
  const current = generation;
  const blob = kind === 'Attachment' ? await api.deliveryDownloadAttachment(props.requestId, id, cancellation.signal)
    : await api.deliveryDownloadReport(props.requestId, id, cancellation.signal);
  if (current === generation) save(blob, fileName);
}
async function cancel(report: EnterpriseRequestReportResponse) {
  const current = generation;
  if (writeUnconfirmed.value || !can('reports.cancel') || !['Queued', 'Running', 'Ready'].includes(report.phase)) return;
  try {
    await api.deliveryCancelReport(props.requestId, report.id, report.version, cancellation.signal);
    if (current === generation) { cancelOpen.value = false; await load(); }
  } catch (error) {
    if (current === generation) { cancelOpen.value = false; writeUnconfirmed.value = !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
    throw error;
  }
}
/** 暂停自动读取并固定确认版本，避免确认期间把另一个服务端版本当作用户意图。 */
function confirmCancel(report: EnterpriseRequestReportResponse) {
  if (busy.value || writeUnconfirmed.value || !can('reports.cancel')) return;
  cancelTarget.value = { ...report }; cancelOpen.value = true;
}
async function cancelConfirmed() { const target = cancelTarget.value; if (target) await act(() => cancel(target)); }
async function printPreview() {
  const current = generation;
  const document = await api.deliveryPrint(props.requestId, cancellation.signal);
  if (current === generation) preview.value = document.html;
}
</script>

<template>
  <el-dialog v-model="open" :title="text('title')" width="min(820px, calc(100vw - 32px))" :close-on-click-modal="false">
    <div class="enterprise-delivery" :aria-busy="busy">
      <el-alert v-if="failure" :title="failure" type="error" :closable="false" role="alert" />
      <ElAlert v-if="writeUnconfirmed" :title="text('writeUnconfirmed')" type="warning" :closable="false" data-testid="enterprise-delivery-unconfirmed" />
      <p>{{ text('masked') }}</p><p>{{ text('limit') }}</p>
      <el-button :disabled="busy" @click="refresh">{{ text('refresh') }}</el-button>
      <section v-if="can('attachments.read') || can('attachments.manage')">
        <h3>{{ text('attachments') }}</h3>
        <div v-if="can('attachments.manage') && draft" class="delivery-upload">
          <label>{{ text('file') }}<input ref="input" type="file" :disabled="busy || writeUnconfirmed" @change="choose" /></label>
          <el-button :disabled="busy || writeUnconfirmed || !selected" @click="act(upload)">{{ text('upload') }}</el-button>
        </div>
        <p v-if="!attachments.length">{{ text('empty') }}</p>
        <article v-for="file in attachments" :key="file.id">
          <strong>{{ file.fileName }}</strong> <span>{{ phase(file.phase) }}</span>
          <el-button v-if="file.phase === 'Ready' && can('attachments.read')" :disabled="busy" @click="act(() => download('Attachment', file.id, file.fileName))">{{ text('download') }}</el-button>
          <el-button v-if="can('attachments.manage') && draft && ['Ready', 'Uploading'].includes(file.phase)" :disabled="busy || writeUnconfirmed" @click="act(() => remove(file))">{{ text('remove') }}</el-button>
        </article>
        <nav v-if="attachmentTotal > 20" :aria-label="text('attachments')">
          <el-button :disabled="busy || attachmentPage <= 1" @click="attachmentPage--; act(load)">{{ text('previous') }}</el-button>
          <el-button :disabled="busy || attachmentPage * 20 >= attachmentTotal" @click="attachmentPage++; act(load)">{{ text('more') }}</el-button>
        </nav>
      </section>
      <section v-if="can('reports.read')">
        <h3>{{ text('reports') }}</h3>
        <p v-if="pendingReports" role="status">{{ text(progressPaused ? 'refreshPaused' : 'refreshing') }}</p>
        <el-button v-if="can('reports.create') && session.can('enterprise_request.enterprise_request_lines.read')" :disabled="busy || writeUnconfirmed || !version" @click="act(createReport)">{{ text('create') }}</el-button>
        <p v-if="!reports.length">{{ text('empty') }}</p>
        <article v-for="report in reports" :key="report.id">
          <code>{{ report.id }}</code> <span>{{ phase(report.phase) }}</span>
          <p>{{ text('expiresAt') }}：<time :datetime="report.expiresAtUtc" data-testid="enterprise-report-expiry">{{ expiry(report.expiresAtUtc) }}</time></p>
          <p v-if="report.phase.endsWith('Cleanup')" role="status" data-testid="enterprise-report-cleanup">{{ text('cleanupPending') }}</p>
          <p v-if="['Expired', 'ExpiredCleanup'].includes(report.phase)" role="status">{{ text('expiredResult') }}</p>
          <p v-else-if="['Queued', 'Running', 'Ready'].includes(report.phase)">{{ text('expiryPolicy') }}</p>
          <p v-if="report.errorCode" role="status">{{ report.errorCode }}</p>
          <p v-if="report.includesAmounts">{{ text('sensitive') }}</p>
          <el-button v-if="report.phase === 'Ready'" :disabled="busy" @click="act(() => download('Report', report.id, `enterprise-request-${report.id}.xlsx`))">{{ text('download') }}</el-button>
          <el-button v-if="can('reports.cancel') && ['Queued', 'Running', 'Ready'].includes(report.phase)" :disabled="busy || writeUnconfirmed" data-testid="enterprise-report-cancel" @click="confirmCancel(report)">{{ text('cancel') }}</el-button>
        </article>
        <nav v-if="reportTotal > 20" :aria-label="text('reports')">
          <el-button :disabled="busy || reportPage <= 1" @click="reportPage--; act(load)">{{ text('previous') }}</el-button>
          <el-button :disabled="busy || reportPage * 20 >= reportTotal" @click="reportPage++; act(load)">{{ text('more') }}</el-button>
        </nav>
      </section>
      <section v-if="can('documents.print') && session.can('enterprise_request.enterprise_request_lines.read')">
        <el-button :disabled="busy" @click="act(printPreview)">{{ text('print') }}</el-button>
        <template v-if="preview">
          <iframe ref="frame" :title="text('print')" :srcdoc="safePreview" sandbox="allow-same-origin allow-modals" referrerpolicy="no-referrer" />
          <el-button :disabled="busy" @click="frame?.contentWindow?.print()">{{ text('printNow') }}</el-button>
        </template>
      </section>
    </div>
    <template #footer><el-button @click="open = false">{{ text('close') }}</el-button></template>
  </el-dialog>
  <ElDialog v-model="cancelOpen" :title="text('cancelConfirmTitle')" width="min(480px, calc(100vw - 32px))" :close-on-click-modal="!busy" :close-on-press-escape="!busy" :show-close="!busy">
    <p>{{ cancelTarget?.id }}</p><p>{{ text('cancelConfirmDescription') }}</p>
    <template #footer>
      <ElButton :disabled="busy" @click="cancelOpen = false">{{ text('keepTask') }}</ElButton>
      <ElButton type="danger" :loading="busy" :disabled="busy || writeUnconfirmed || !cancelTarget || !can('reports.cancel')" data-testid="enterprise-report-confirm-cancel"
        @click="cancelConfirmed">{{ text('cancel') }}</ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.enterprise-delivery { overflow-wrap: anywhere; }
.enterprise-delivery article { border-bottom: 1px solid var(--el-border-color); padding: 12px 0; }
.delivery-upload { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
.delivery-upload input { display: block; max-width: 100%; }
.enterprise-delivery iframe { width: 100%; min-height: 300px; border: 1px solid var(--el-border-color); margin-top: 12px; }
</style>
