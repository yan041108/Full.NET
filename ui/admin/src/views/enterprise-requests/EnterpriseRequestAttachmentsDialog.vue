<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, enterpriseRequestPermissions,
  type EnterpriseRequestAttachmentsResponse, type EnterpriseRequestAttachmentResponse } from '../../api/enterprise-requests';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';

const props = defineProps<{ requestId: string }>();
const emit = defineEmits<{ close: []; changed: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const api = createEnterpriseRequestsApi(enterpriseRequestsHttp);
const snapshot = ref<EnterpriseRequestAttachmentsResponse>(); const problem = ref<FullNetProblemDetails>();
const busy = ref(false); const file = ref<File>(); const fileInput = ref<HTMLInputElement>();
const removing = ref<EnterpriseRequestAttachmentResponse>(); const urls = new Set<string>();
const canRead = computed(() => session.can(enterpriseRequestPermissions.read));
const canEdit = computed(() => canRead.value && session.can(enterpriseRequestPermissions.update) && snapshot.value?.requestStatus === 'Draft');
const scope = useAuthorizedViewScope(session, reset, load);
function clearFile(): void { file.value = undefined; if (fileInput.value) fileInput.value.value = ''; }
function reset(): void {
  snapshot.value = undefined; problem.value = undefined; busy.value = false; removing.value = undefined; clearFile();
  for (const url of urls) URL.revokeObjectURL(url); urls.clear();
}
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });
async function load(): Promise<void> {
  if (!canRead.value || busy.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read); if (!request) return;
  const id = props.requestId; busy.value = true; problem.value = undefined; snapshot.value = undefined; removing.value = undefined;
  try { const value = await api.attachments(id, request.signal); if (request.current() && id === props.requestId) snapshot.value = value; }
  catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
function selectFile(event: Event): void {
  if (!canEdit.value || busy.value) { clearFile(); return; }
  file.value = (event.target as HTMLInputElement).files?.[0]; problem.value = undefined;
  if (file.value && (file.value.size < 1 || file.value.size > 10 * 1024 * 1024)) {
    clearFile(); problem.value = { status: 400, code: 'validation.failed', title: t('enterpriseRequests.attachmentLimits') };
  }
}
async function upload(): Promise<void> {
  if (!canEdit.value || busy.value || !file.value || !snapshot.value || snapshot.value.items.length >= 20) return;
  const request = scope.begin(enterpriseRequestPermissions.update); if (!request) return;
  const id = props.requestId; const version = snapshot.value.requestVersion; const selected = file.value;
  busy.value = true; problem.value = undefined; let refresh = false;
  try {
    await api.uploadAttachment(id, version, selected, request.signal);
    if (request.current() && id === props.requestId) { clearFile(); emit('changed'); refresh = true; }
  } catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
  if (refresh) await load();
}
function confirmRemove(item: EnterpriseRequestAttachmentResponse): void {
  if (canEdit.value && !busy.value && snapshot.value?.items.includes(item)) removing.value = item;
}
async function remove(): Promise<void> {
  if (!canEdit.value || busy.value || !removing.value || !snapshot.value) return;
  const request = scope.begin(enterpriseRequestPermissions.update); if (!request) return;
  const id = props.requestId; const item = removing.value; const version = snapshot.value.requestVersion;
  busy.value = true; problem.value = undefined; let refresh = false;
  try {
    await api.removeAttachment(id, item.id, version, request.signal);
    if (request.current() && id === props.requestId) { removing.value = undefined; emit('changed'); refresh = true; }
  } catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
  if (refresh) await load();
}
async function download(item: EnterpriseRequestAttachmentResponse): Promise<void> {
  if (!canRead.value || busy.value || !snapshot.value?.items.includes(item)) return;
  const request = scope.begin(enterpriseRequestPermissions.read); if (!request) return;
  const id = props.requestId; busy.value = true; problem.value = undefined;
  try {
    const blob = await api.downloadAttachment(id, item.id, request.signal);
    if (request.current() && id === props.requestId) {
      // 使用认证客户端拉取字节，短生命周期 URL 仅用于下载，不进入 iframe 或新窗口执行。
      const url = URL.createObjectURL(blob); urls.add(url);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = item.originalFileName;
      document.body.append(anchor); anchor.click(); anchor.remove();
      window.setTimeout(() => { URL.revokeObjectURL(url); urls.delete(url); }, 60_000);
    }
  } catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
function showProblem(error: unknown): void {
  problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.enterprise_request_attachments_failed', title: t('enterpriseRequests.attachmentsFailed') };
}
function close(): void { scope.invalidate(); emit('close'); }
</script>

<template>
  <el-dialog :model-value="true" :title="t('enterpriseRequests.attachmentsTitle')" width="min(760px, 96vw)"
    @update:model-value="open => { if (!open) close(); }">
    <div :aria-busy="busy" aria-live="polite">
      <p v-if="!canRead">{{ t('enterpriseRequests.progressAccessDenied') }}</p>
      <div v-if="problem" class="art-inline-alert" role="alert"><strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span></div>
      <p v-if="busy">{{ t('common.loading') }}</p>
      <template v-if="snapshot && canRead">
        <p>{{ t('enterpriseRequests.requestVersion') }}: {{ snapshot.requestVersion }}</p>
        <p v-if="!snapshot.items.length">{{ t('enterpriseRequests.attachmentsEmpty') }}</p>
        <ul v-else class="request-attachments">
          <li v-for="item in snapshot.items" :key="item.id">
            <span>{{ item.originalFileName }} · {{ item.sizeBytes }} {{ t('enterpriseRequests.attachmentBytes') }}</span>
            <el-button :disabled="busy" @click="download(item)">{{ t('enterpriseRequests.downloadAttachment') }}</el-button>
            <el-button v-if="canEdit" :disabled="busy" @click="confirmRemove(item)">{{ t('enterpriseRequests.removeAttachment') }}</el-button>
          </li>
        </ul>
        <fieldset v-if="canEdit" :disabled="busy || snapshot.items.length >= 20">
          <legend>{{ t('enterpriseRequests.uploadAttachment') }}</legend>
          <p id="request-attachment-limits">{{ t('enterpriseRequests.attachmentLimits') }}</p>
          <label>{{ t('enterpriseRequests.selectAttachment') }}<input ref="fileInput" type="file" aria-describedby="request-attachment-limits" @change="selectFile" /></label>
          <el-button :disabled="busy || !file || snapshot.items.length >= 20" @click="upload">{{ t('enterpriseRequests.uploadAttachment') }}</el-button>
        </fieldset>
        <div v-if="removing && canEdit" role="group" :aria-label="t('enterpriseRequests.removeAttachment')">
          <p>{{ t('enterpriseRequests.removeAttachmentConfirm') }} {{ removing.originalFileName }}</p>
          <el-button :disabled="busy" @click="removing = undefined">{{ t('common.cancel') }}</el-button>
          <el-button type="danger" :disabled="busy" @click="remove">{{ t('enterpriseRequests.confirmRemoveAttachment') }}</el-button>
        </div>
      </template>
    </div>
    <template #footer>
      <el-button v-if="canRead" :disabled="busy" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.request-attachments { list-style: none; padding: 0; }
.request-attachments li { display: flex; align-items: center; flex-wrap: wrap; gap: .5rem; padding: .65rem 0; border-bottom: 1px solid var(--el-border-color); }
.request-attachments span { flex: 1; min-width: 0; overflow-wrap: anywhere; }
fieldset { margin-block: 1rem; border: 1px solid var(--el-border-color); }
label { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; margin-bottom: .75rem; }
</style>
