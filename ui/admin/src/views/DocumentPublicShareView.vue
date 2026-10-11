<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentShareAccessResponse, HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
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
const passwordRequired = ref(false);
const { url: previewUrl, load: loadPreview, clear: clearPreview } = useBlobPreview();

const shareCode = computed(() => String(route.params.shareCode ?? '').trim());

const showPasswordForm = computed(
  () => initialLoaded.value && !access.value && (passwordRequired.value || Boolean(problem.value))
);

const mimeType = computed(() => access.value?.mimeType?.split(';', 1)[0]?.trim().toLowerCase() ?? '');

const isImage = computed(() => mimeType.value.startsWith('image/'));

const isPdf = computed(() => mimeType.value === 'application/pdf');

const isText = computed(() => mimeType.value.startsWith('text/'));

const canInlinePreview = computed(() => isImage.value || isPdf.value || isText.value);

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
let downloadController: AbortController | undefined;
const current = (request: ShareRequest) => request === activeRequest && !request.controller.signal.aborted;
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

// 分享码、密码及所有异步步骤由同一请求快照拥有，路由切换和卸载同步失效。
function resetAccess(): void {
  activeRequest?.controller.abort(); downloadController?.abort(); activeRequest = undefined;
  clearPreview(); access.value = undefined; problem.value = undefined; contentProblem.value = undefined;
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
  if (!sameId(task.documentItemId, documentId) || task.versionId !== null
    || (original && (!sameId(task.id, original.id) || !sameId(task.sourceFileId, original.sourceFileId))))
    throw new Error('client.invalid_document_preview_task_identity');
}

async function loadOfficePreview(request: ShareRequest, documentId: string): Promise<void> {
  const signal = request.controller.signal;
  const task = await createDocumentSharePreviewTaskByCode(request.code, request.body, signal);
  signal.throwIfAborted(); checkPreviewTask(task, documentId);
  for (let attempt = 0; attempt < 40; attempt += 1) {
    const status = await getDocumentSharePreviewTaskByCode(request.code, task.id, request.body, signal);
    signal.throwIfAborted(); checkPreviewTask(status, documentId, task);
    if (status.statusKey === 'failed') throw new Error('document.public_share.preview_task_failed');
    if (status.statusKey === 'succeeded') {
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
  contentLoading.value = true; contentProblem.value = undefined;
  try {
    if (needsOfficePreview.value) await loadOfficePreview(request, document.documentId);
    else await loadPreview(() => loadDocumentShareContentByCode(request.code, request.body, request.controller.signal));
  } catch (error) {
    if (current(request)) contentProblem.value = contentError(error);
  } finally {
    if (current(request)) contentLoading.value = false;
  }
}

async function downloadContent() {
  const request = activeRequest;
  if (!request || !current(request) || !access.value || downloading.value) return;
  const controller = new AbortController(); downloadController = controller; downloading.value = true;
  try {
    const blob = await loadDocumentShareContentByCode(request.code, request.body, controller.signal);
    // 下载器即使忽略取消返回Blob，也不能在离页后打开旧内容。
    if (current(request) && !controller.signal.aborted) openDocumentBlob(blob);
  } catch (error) {
    if (current(request) && !controller.signal.aborted) contentProblem.value = contentError(error);
  } finally {
    if (downloadController === controller) { downloadController = undefined; downloading.value = false; }
  }
}

async function submitAccess() {
  if (!shareCode.value || loading.value) return;
  // 同一码输错密码后仍需保留输入入口；只有切换分享或成功访问才清除已知密码要求。
  const requiresPassword = passwordRequired.value;
  resetAccess(); passwordRequired.value = requiresPassword;
  const request: ShareRequest = { code: shareCode.value, body: { password: password.value.trim() || null }, controller: new AbortController() };
  activeRequest = request; loading.value = true;
  try {
    const result = await accessDocumentShareByCode(request.code, request.body, request.controller.signal);
    if (!current(request)) return;
    access.value = result; passwordRequired.value = false;
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

watch(shareCode, code => {
  resetAccess(); password.value = '';
  if (code) void submitAccess();
}, { immediate: true, flush: 'sync' });
onBeforeUnmount(resetAccess);
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

      <el-form v-if="showPasswordForm" @submit.prevent="submitAccess">
        <el-form-item v-if="passwordRequired" :label="t('documentShares.passwordOptional')">
          <el-input
            v-model="password"
            type="password"
            show-password
            autocomplete="current-password"
            data-testid="document-public-share-password"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" data-testid="document-public-share-submit" @click="submitAccess">
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
          />
        </div>

        <el-button
          v-if="access && !previewUrl && !contentLoading && !needsOfficePreview"
          type="primary"
          data-testid="document-public-share-download"
          :loading="downloading"
          @click="downloadContent"
        >
          {{ t('documentPublicShare.download') }}
        </el-button>
        <el-button
          v-else-if="access && previewUrl"
          data-testid="document-public-share-download"
          :loading="downloading"
          @click="downloadContent"
        >
          {{ t('documentPublicShare.download') }}
        </el-button>
      </div>
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

