<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElDialog, ElForm, ElFormItem, ElInput } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentShareAccessResponse, HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  accessDocumentShareByCode,
  createDocumentSharePreviewTaskByCode,
  getDocumentSharePreviewTaskByCode,
  loadDocumentShareContentByCode,
  loadDocumentSharePreviewTaskContentByCode
} from '../api/document-shares';
import { useBlobPreview } from '../composables/useBlobPreview';
import { openDocumentBlob } from '../api/host-document-items';
import { isPassiveContentType } from '../utils/openContentBlob';

defineOptions({ name: 'DocumentPublicShareView' });

const route = useRoute();
const { t } = useAdminI18n();
const loading = ref(false);
const contentLoading = ref(false);
const initialLoaded = ref(false);
const downloading = ref(false);
const password = ref('');
const access = ref<HostDocumentShareAccessResponse>();
const problem = ref<FullNetProblemDetails>();
const contentProblem = ref<FullNetProblemDetails>();
const downloadProblem = ref<FullNetProblemDetails>();
const previewTask = ref<HostDocumentPreviewTaskResponse>(), previewCreationUnknown = ref(false), owner = ref(0);
const recoveryConfirmation = ref<{ token: number; confirm: () => void; update: (open: boolean) => void }>(); let recoverySequence = 0;
const passwordRequired = ref(false);
const { url: previewUrl, load: loadPreview, clear: clearPreview } = useBlobPreview();

const shareCode = computed(() => typeof route.params.shareCode === 'string' ? route.params.shareCode.trim() : '');
const routeCurrent = () => !route.name || route.name === 'document-public-share';

const showPasswordForm = computed(
  () => initialLoaded.value && !access.value && (passwordRequired.value || Boolean(problem.value))
);

const mimeType = computed(() => access.value?.mimeType?.split(';', 1)[0]?.trim().toLowerCase() ?? '');

const isImage = computed(() => mimeType.value.startsWith('image/'));

const canInlinePreview = computed(() => isPassiveContentType(mimeType.value));

const needsOfficePreview = computed(() => {
  if (!mimeType.value || canInlinePreview.value) {
    return false;
  }
  return (
    mimeType.value.includes('officedocument') ||
    mimeType.value.includes('msword') ||
    mimeType.value.includes('spreadsheet') ||
    mimeType.value.includes('presentation')
  );
});

type ShareRequest = { code: string; body: { password: string | null }; controller: AbortController };
let activeRequest: ShareRequest | undefined;
let active = true;
let downloadController: AbortController | undefined;
let contentController: AbortController | undefined;
const current = (request: ShareRequest) => active && routeCurrent() && request.code === shareCode.value && request === activeRequest && !request.controller.signal.aborted;
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
const nonNilGuid = (value: string) => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const documentKey = (value?: HostDocumentShareAccessResponse) => value ? JSON.stringify([value.shareId.toLowerCase(), value.documentId.toLowerCase(), value.shareCode, value.fileName, value.mimeType]) : '';
const canResetPreview = computed(() => needsOfficePreview.value && (previewCreationUnknown.value || !!previewTask.value && !['pending', 'processing', 'succeeded'].includes(previewTask.value.statusKey)));
const canRetryContent = computed(() => !!access.value && (canInlinePreview.value || needsOfficePreview.value && (!!previewTask.value || !previewCreationUnknown.value)));
const controls = computed(() => {
  const ticket = owner.value, code = shareCode.value, credential = password.value, source = documentKey(access.value);
  const owned = () => active && routeCurrent() && ticket === owner.value && code === shareCode.value && credential === password.value && source === documentKey(access.value);
  return { input: (value: string) => { if (owned() && !loading.value) password.value = value; }, submit: () => { if (owned()) void submitAccess(); }, form: (event: Event) => { event.preventDefault(); if (owned()) void submitAccess(); },
    retry: () => { if (owned() && activeRequest && access.value && canRetryContent.value && !loading.value && !contentLoading.value && !downloading.value) void loadContent(activeRequest, { ...access.value }); },
    download: () => { if (owned()) void downloadContent(); }, recover: () => { if (owned()) acknowledgePreview(); } };
});

// 分享码、密码及所有异步步骤由同一请求快照拥有，路由切换和卸载同步失效。
function resetAccess(): void {
  activeRequest?.controller.abort(); downloadController?.abort(); contentController?.abort(); activeRequest = undefined; contentController = undefined;
  downloadController = undefined;
  clearPreview(); access.value = undefined; problem.value = undefined; contentProblem.value = undefined; downloadProblem.value = undefined;
  previewTask.value = undefined; previewCreationUnknown.value = false; recoveryConfirmation.value = undefined; owner.value++;
  loading.value = false; contentLoading.value = false; downloading.value = false; initialLoaded.value = false; passwordRequired.value = false;
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const cancel = () => { window.clearTimeout(timer); signal.removeEventListener('abort', cancel); reject(signal.reason); };
    const timer = window.setTimeout(() => { signal.removeEventListener('abort', cancel); resolve(); }, ms);
    signal.addEventListener('abort', cancel, { once: true });
  });
}

function checkPreviewTask(task: HostDocumentPreviewTaskResponse, documentId: string,
  original?: HostDocumentPreviewTaskResponse): void {
  if (!nonNilGuid(task.id) || !nonNilGuid(task.sourceFileId) || !Number.isSafeInteger(task.version) || task.version < 1 || !sameId(task.documentItemId, documentId) || task.versionId !== null
    || (original && (!sameId(task.id, original.id) || !sameId(task.sourceFileId, original.sourceFileId) || task.createdAtUtc !== original.createdAtUtc || task.version < original.version)))
    throw new Error('client.invalid_document_preview_task_identity');
}

async function loadOfficePreview(request: ShareRequest, documentId: string, signal: AbortSignal): Promise<void> {
  let task = previewTask.value;
  if (!task) {
    if (previewCreationUnknown.value) throw new Error('document.public_share.preview_creation_unconfirmed');
    // 创建回包丢失时不能自动重复 POST；没有任务身份只能由用户核对后另行提交。
    previewCreationUnknown.value = true;
    try {
      task = await createDocumentSharePreviewTaskByCode(request.code, request.body, signal);
      signal.throwIfAborted(); checkPreviewTask(task, documentId); previewTask.value = task; previewCreationUnknown.value = false;
    } catch (error) {
      if (!signal.aborted && isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500 && error.status !== 408) previewCreationUnknown.value = false;
      throw error;
    }
  }
  for (let attempt = 0; attempt < 40; attempt += 1) {
    const status = await getDocumentSharePreviewTaskByCode(request.code, task.id, request.body, signal);
    signal.throwIfAborted(); checkPreviewTask(status, documentId, previewTask.value ?? task); previewTask.value = status;
    if (!['pending', 'processing', 'succeeded'].includes(status.statusKey)) throw new Error('document.public_share.preview_task_failed');
    if (status.statusKey === 'succeeded') {
      if (!status.outputFileId || !nonNilGuid(status.outputFileId)) throw new Error('client.invalid_document_preview_task_output');
      await loadPreview(() => loadDocumentSharePreviewTaskContentByCode(request.code, task.id, request.body, signal));
      return;
    }
    await delay(1500, signal);
  }
  throw new Error('document.public_share.preview_task_timeout');
}

const remainingAccessLabel = computed(() => {
  const remaining = access.value?.accessCountRemaining;
  if (remaining === undefined) {
    return '';
  }
  if (remaining >= 2_147_483_647) {
    return t('documentPublicShare.remainingUnlimited');
  }
  return String(remaining);
});

function contentError(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { status: 500, code: 'document.public_share.preview_failed', title: t('documentPublicShare.contentFailed') };
}

async function loadContent(request: ShareRequest, document: HostDocumentShareAccessResponse) {
  if (!current(request) || contentLoading.value || downloading.value || documentKey(document) !== documentKey(access.value)) return;
  contentController?.abort(); clearPreview(); const controller = new AbortController(); contentController = controller;
  const abort = () => controller.abort(); request.controller.signal.addEventListener('abort', abort, { once: true });
  const owned = () => current(request) && contentController === controller && !controller.signal.aborted && documentKey(document) === documentKey(access.value);
  owner.value++; recoveryConfirmation.value = undefined;
  contentLoading.value = true; contentProblem.value = undefined;
  try {
    if (needsOfficePreview.value) await loadOfficePreview(request, document.documentId, controller.signal);
    else if (canInlinePreview.value) await loadPreview(() => loadDocumentShareContentByCode(request.code, request.body, controller.signal));
  } catch (error) {
    if (owned()) contentProblem.value = contentError(error);
  } finally {
    request.controller.signal.removeEventListener('abort', abort);
    if (contentController === controller) { contentController = undefined; contentLoading.value = false; owner.value++; }
  }
}

async function downloadContent() {
  const request = activeRequest;
  if (!request || !current(request) || !access.value || contentLoading.value || downloading.value) return;
  const document = { ...access.value };
  const controller = new AbortController(); downloadController = controller; downloading.value = true; downloadProblem.value = undefined; owner.value++; recoveryConfirmation.value = undefined;
  try {
    const blob = await loadDocumentShareContentByCode(request.code, request.body, controller.signal);
    // 下载器即使忽略取消返回Blob，也不能在离页后打开旧内容。
    if (current(request) && !controller.signal.aborted && documentKey(document) === documentKey(access.value)) openDocumentBlob(blob, document.fileName ?? undefined);
  } catch (error) {
    if (current(request) && !controller.signal.aborted) downloadProblem.value = contentError(error);
  } finally {
    if (downloadController === controller) { downloadController = undefined; downloading.value = false; owner.value++; }
  }
}

async function submitAccess() {
  if (!active || !routeCurrent() || !shareCode.value || loading.value || contentLoading.value || downloading.value || access.value) return;
  // 同一码输错密码后仍需保留输入入口；只有切换分享或成功访问才清除已知密码要求。
  const requiresPassword = passwordRequired.value;
  resetAccess(); passwordRequired.value = requiresPassword;
  const request: ShareRequest = { code: shareCode.value, body: { password: password.value.trim() || null }, controller: new AbortController() };
  activeRequest = request; loading.value = true;
  try {
    const result = await accessDocumentShareByCode(request.code, request.body, request.controller.signal);
    if (!current(request)) return;
    if (result.shareCode !== request.code || !nonNilGuid(result.shareId) || !nonNilGuid(result.documentId) || !Number.isSafeInteger(result.fileSizeBytes) || result.fileSizeBytes < 0 || !Number.isSafeInteger(result.accessCountRemaining) || result.accessCountRemaining < 0) throw new Error('client.invalid_document_share_access');
    access.value = result; passwordRequired.value = false; loading.value = false; initialLoaded.value = true; owner.value++;
    await loadContent(request, result);
  } catch (error) {
    if (!current(request)) return;
    access.value = undefined;
    problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'document.public_share.access_failed', title: t('documentPublicShare.accessDenied') };
    if (problem.value.code === 'document.host_share.password_required') { passwordRequired.value = true; problem.value = undefined; }
  } finally {
    if (current(request)) { loading.value = false; initialLoaded.value = true; }
  }
}
function acknowledgePreview(): void {
  const request = activeRequest;
  if (!request || !current(request) || !canResetPreview.value || contentLoading.value || downloading.value) return;
  const ticket = owner.value, token = ++recoverySequence, source = documentKey(access.value);
  const owned = () => current(request) && ticket === owner.value && source === documentKey(access.value) && recoveryConfirmation.value?.token === token && !contentLoading.value && !downloading.value;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { previewTask.value = undefined; previewCreationUnknown.value = false; recoveryConfirmation.value = undefined; owner.value++; } }, update: value => { if (!value && owned()) recoveryConfirmation.value = undefined; } };
}
watch(password, () => {
  if (loading.value) { const required = passwordRequired.value; resetAccess(); passwordRequired.value = required; initialLoaded.value = true; }
  owner.value++;
}, { flush: 'sync' });

watch(() => JSON.stringify({ route: route.name, shareCode: route.params.shareCode }), () => {
  resetAccess(); password.value = '';
  if (active && routeCurrent() && shareCode.value) void submitAccess();
}, { immediate: true, flush: 'sync' });
// 停用清除密码与预览；恢复只重新进行匿名授权，不重放先前密码或在途转换。
onDeactivated(() => { active = false; resetAccess(); password.value = ''; });
onActivated(() => { if (!active) { active = true; void submitAccess(); } });
onBeforeUnmount(() => { active = false; resetAccess(); password.value = ''; });
</script>

<template>
  <section class="document-public-share-view art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPublicShare.title') }}</h1>

    <el-card shadow="never" class="document-public-share-view__card">
      <p v-if="loading && !initialLoaded" class="document-public-share-view__loading">
        {{ t('documentPublicShare.loading') }}
      </p>

      <el-alert
        v-if="problem"
        type="error"
        :title="problem.title ?? t('documentPublicShare.accessDenied')"
        :description="problem.detail ?? problem.code"
        show-icon
        class="art-page-alert"
      />

      <el-form v-if="showPasswordForm" v-on="{ submit: controls.form }">
        <el-form-item v-if="passwordRequired" :label="t('documentShares.passwordOptional')">
          <el-input
            :model-value="password"
            :disabled="loading"
            v-on="{ 'update:modelValue': controls.input }"
            type="password"
            show-password
            autocomplete="current-password"
            data-testid="document-public-share-password"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" data-testid="document-public-share-submit" v-on="{ click: controls.submit }">
            {{ t('documentPublicShare.open') }}
          </el-button>
        </el-form-item>
      </el-form>

      <div v-if="access" class="document-public-share-view__body">
        <div class="document-public-share-view__meta">
          <p><strong>{{ t('documentPublicShare.documentTitle') }}</strong> <span translate="no">{{ access.title }}</span></p>
          <p v-if="access.fileName">
            <strong>{{ t('documentPublicShare.fileName') }}</strong> <span translate="no">{{ access.fileName }}</span>
          </p>
          <p>
            <strong>{{ t('documentPublicShare.remainingAccess') }}</strong>
            {{ remainingAccessLabel }}
          </p>
        </div>

        <p v-if="contentLoading" class="document-public-share-view__loading">
          {{
            needsOfficePreview
              ? t('documentPublicShare.officePreviewLoading')
              : t('documentPublicShare.contentLoading')
          }}
        </p>

        <el-alert
          v-if="contentProblem"
          data-testid="document-public-share-content-problem"
          type="error"
          :title="contentProblem.title ?? t('documentPublicShare.contentFailed')"
          :description="contentProblem.detail ?? contentProblem.code"
          show-icon
          class="art-page-alert"
        />

        <div
          v-if="previewUrl && (canInlinePreview || needsOfficePreview)"
          class="document-public-share-view__preview"
        >
          <img v-if="isImage" :src="previewUrl" :alt="access.title" class="document-public-share-view__image" />
          <iframe
            v-else
            :src="previewUrl"
            class="document-public-share-view__frame"
            title="preview"
            sandbox=""
            referrerpolicy="no-referrer"
          />
        </div>

        <el-alert v-if="downloadProblem" data-testid="document-public-share-download-problem" type="error" :title="downloadProblem.title" show-icon />
        <el-button v-if="contentProblem && canRetryContent" data-testid="document-public-share-content-retry" :disabled="contentLoading || downloading" v-on="{ click: controls.retry }">{{ t('documentPublicShare.retryContent') }}</el-button>
        <el-button v-if="canResetPreview" data-testid="document-public-share-preview-recovery" :disabled="contentLoading || downloading" v-on="{ click: controls.recover }">{{ t('documentPublicShare.reviewPreview') }}</el-button>
        <el-button data-testid="document-public-share-download" :loading="downloading" :disabled="contentLoading" v-on="{ click: controls.download }">
          {{ t('documentPublicShare.download') }}
        </el-button>
      </div>
      <ElDialog :model-value="!!recoveryConfirmation" :title="t('documentPublicShare.reviewPreview')" width="480px" v-on="{ 'update:modelValue': recoveryConfirmation?.update }">
        <p>{{ t('documentPublicShare.previewRecoveryHint') }}</p>
        <template #footer><ElButton data-testid="document-public-share-preview-recovery-confirm" type="warning" v-on="{ click: recoveryConfirmation?.confirm }">{{ t('documentPublicShare.previewRecoveryConfirm') }}</ElButton></template>
      </ElDialog>
    </el-card>
  </section>
</template>

<style scoped>
.document-public-share-view {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
  background: var(--art-default-bg-color, #f5f7fa);
}

.document-public-share-view__card {
  width: min(960px, 100%);
  min-height: 120px;
}

.document-public-share-view__loading {
  margin: 0;
  color: var(--art-gray-600);
  font-size: 14px;
}

.document-public-share-view__meta p {
  margin: 8px 0;
}

.document-public-share-view__body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.document-public-share-view__preview {
  display: flex;
  justify-content: center;
  width: 100%;
}

.document-public-share-view__image {
  max-width: 100%;
  max-height: min(70vh, 720px);
  object-fit: contain;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 8%);
}

.document-public-share-view__frame {
  width: 100%;
  min-height: 480px;
  border: 1px solid var(--art-border-color, #e4e7ed);
  border-radius: 8px;
}
</style>

