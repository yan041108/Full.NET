<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElSwitch
} from 'element-plus';
import type { FullNetProblemDetails, OcrProviderConfig } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getOcrProviderConfig,
  PADDLE_OCR_ID_CARD_PROVIDER_KEY,
  testOcrProviderConfig,
  updateOcrProviderConfig
} from '../api/ocr';

defineOptions({ name: 'OcrProviderConfigView' });

const { t } = useAdminI18n();
const loading = ref(false);
const saving = ref(false);
const testing = ref(false);
const problem = ref<FullNetProblemDetails>();
const config = ref<OcrProviderConfig | null>(null);
const form = reactive({
  name: '',
  baseUrl: '',
  apiKey: '',
  isEnabled: false,
  version: 0
});

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const value = await getOcrProviderConfig(PADDLE_OCR_ID_CARD_PROVIDER_KEY);
    config.value = value;
    form.name = value.name;
    form.baseUrl = value.baseUrl;
    form.apiKey = '';
    form.isEnabled = value.isEnabled;
    form.version = value.version;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrProviderConfig.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function save(): Promise<void> {
  saving.value = true;
  problem.value = undefined;
  try {
    await updateOcrProviderConfig(PADDLE_OCR_ID_CARD_PROVIDER_KEY, {
      name: form.name,
      baseUrl: form.baseUrl,
      apiKey: form.apiKey || null,
      isEnabled: form.isEnabled,
      version: form.version
    });
    ElMessage.success(t('ocrProviderConfig.saveSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrProviderConfig.saveFailed');
  } finally {
    saving.value = false;
  }
}

async function runTest(): Promise<void> {
  testing.value = true;
  problem.value = undefined;
  try {
    const result = await testOcrProviderConfig(PADDLE_OCR_ID_CARD_PROVIDER_KEY);
    ElMessage[result.succeeded ? 'success' : 'error'](result.message);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrProviderConfig.testFailed');
  } finally {
    testing.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="page-stack">
    <ElAlert
      v-if="problem"
      :title="problem.title"
      type="error"
      show-icon
      :closable="false"
      class="page-alert"
    />
    <ElCard v-loading="loading">
      <template #header>
        <div class="card-header">
          <span>{{ t('ocrProviderConfig.title') }}</span>
          <PermissionGate code="ocr.providers.test">
            <ElButton
              data-testid="ocr-provider-test"
              :loading="testing"
              @click="runTest"
            >
              {{ t('ocrProviderConfig.test') }}
            </ElButton>
          </PermissionGate>
        </div>
      </template>
      <ElForm label-width="120px">
        <ElFormItem :label="t('ocrProviderConfig.fieldName')">
          <ElInput v-model="form.name" />
        </ElFormItem>
        <ElFormItem :label="t('ocrProviderConfig.fieldBaseUrl')">
          <ElInput v-model="form.baseUrl" />
        </ElFormItem>
        <ElFormItem :label="t('ocrProviderConfig.fieldApiKey')">
          <ElInput v-model="form.apiKey" type="password" show-password autocomplete="new-password" />
        </ElFormItem>
        <ElFormItem :label="t('ocrProviderConfig.fieldEnabled')">
          <ElSwitch v-model="form.isEnabled" />
        </ElFormItem>
        <PermissionGate code="ocr.providers.update">
          <ElFormItem>
            <ElButton
              data-testid="ocr-provider-save"
              type="primary"
              :loading="saving"
              @click="save"
            >
              {{ t('ocrProviderConfig.save') }}
            </ElButton>
          </ElFormItem>
        </PermissionGate>
      </ElForm>
    </ElCard>
  </div>
</template>
