<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentShareAccessResponse } from '@fullnet/client-contracts';
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
const accessStarted = ref(false);
const password = ref('');
const access = ref<HostDocumentShareAccessResponse>();
const problem = ref<FullNetProblemDetails>();
const contentProblem = ref<FullNetProblemDetails>();
const passwordRequired = ref(false);
const preview = useBlobPreview();

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

function shareAccessBody() {
  return { password: password.value.trim() || null };
}

function delay(ms: number): Promise<void> {
  return new Promise(resolve => {
    window.setTimeout(resolve, ms);
  });
}

async function loadOfficePreview(): Promise<void> {
  const task = await createDocumentSharePreviewTaskByCode(shareCode.value, shareAccessBody());
  for (let attempt = 0; attempt < 40; attempt += 1) {
    const status = await getDocumentSharePreviewTaskByCode(shareCode.value, task.id, shareAccessBody());
    if (status.statusKey === 'failed') {
      throw new Error('document.public_share.preview_task_failed');
    }
    if (status.statusKey === 'succeeded') {
      await preview.load(() =>
        loadDocumentSharePreviewTaskContentByCode(shareCode.value, task.id, shareAccessBody())
      );
      return;
    }
    await delay(1500);
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

async function loadContent() {
  if (!shareCode.value || !access.value) {
    return;
  }
  contentLoading.value = true;
  contentProblem.value = undefined;
  try {
    if (needsOfficePreview.value) {
      await loadOfficePreview();
    } else {
      await preview.load(() => loadDocumentShareContentByCode(shareCode.value, shareAccessBody()));
    }
  } catch (error) {
    if (isFullNetProblemDetails(error)) {
      contentProblem.value = error;
    } else {
      contentProblem.value = {
        status: 500,
        code: 'document.public_share.preview_failed',
        title: t('documentPublicShare.contentFailed')
      };
    }
  } finally {
    contentLoading.value = false;
  }
}

function downloadContent() {
  if (!preview.url.value && !access.value) {
    return;
  }
  void (async () => {
    try {
      const blob = await loadDocumentShareContentByCode(shareCode.value, {
        password: password.value.trim() || null
      });
      openDocumentBlob(blob);
    } catch (error) {
      contentProblem.value = isFullNetProblemDetails(error) ? error : undefined;
    }
  })();
}

async function submitAccess() {
  if (!shareCode.value || loading.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  contentProblem.value = undefined;
  preview.clear();
  if (!password.value.trim()) {
    passwordRequired.value = false;
  }
  try {
    access.value = await accessDocumentShareByCode(shareCode.value, {
      password: password.value.trim() || null
    });
    passwordRequired.value = false;
    await loadContent();
  } catch (error) {
    access.value = undefined;
    problem.value = isFullNetProblemDetails(error) ? error : undefined;
    if (problem.value?.code === 'document.host_share.password_required') {
      passwordRequired.value = true;
      problem.value = undefined;
    }
  } finally {
    loading.value = false;
    initialLoaded.value = true;
  }
}

watch(
  shareCode,
  code => {
    if (!code || accessStarted.value) {
      return;
    }
    accessStarted.value = true;
    void submitAccess();
  },
  { immediate: true }
);
</script>

<template>
  <section class="document-public-share-view art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPublicShare.title') }}</h1>

    <el-card shadow="never" class="document-public-share-view__card">
      <p v-if="loading && !initialLoaded" class="document-public-share-view__loading">
        {{ t('documentPublicShare.loading') }}
      </p>

      <el-alert
        v-if="problem && !passwordRequired"
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
          v-if="preview.url && (canInlinePreview || needsOfficePreview)"
          class="document-public-share-view__preview"
        >
          <img v-if="isImage" :src="preview.url" :alt="access.title" class="document-public-share-view__image" />
          <iframe
            v-else
            :src="preview.url"
            class="document-public-share-view__frame"
            title="preview"
          />
        </div>

        <el-button
          v-if="access && !preview.url && !contentLoading && !needsOfficePreview"
          type="primary"
          data-testid="document-public-share-download"
          @click="downloadContent"
        >
          {{ t('documentPublicShare.download') }}
        </el-button>
        <el-button
          v-else-if="access && preview.url"
          data-testid="document-public-share-download"
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

