<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage
} from 'element-plus';
import type { TenantBrandingResponse } from '@fullnet/client-contracts';
import {
  deleteCurrentTenantBrandingLogo,
  downloadCurrentTenantBrandingLogoContent,
  getCurrentTenantBranding,
  updateCurrentTenantBranding,
  uploadCurrentTenantBrandingLogo
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { http } from '../api/http';

defineOptions({ name: 'TenantBrandingView' });

const { t } = useAdminI18n();
const loading = ref(false);
const saving = ref(false);
const uploadingLogo = ref(false);
const removingLogo = ref(false);
const logoPreviewUrl = ref<string | null>(null);
const version = ref(0);
const form = reactive({
  systemTitle: '',
  contactPhone: '',
  contactEmail: '',
  contactAddress: '',
  copyright: ''
});

function revokePreview(url: string | null): void {
  if (url) {
    URL.revokeObjectURL(url);
  }
}

async function refreshLogoPreview(hasLogo: boolean): Promise<void> {
  revokePreview(logoPreviewUrl.value);
  logoPreviewUrl.value = null;
  if (!hasLogo) {
    return;
  }

  try {
    const blob = await downloadCurrentTenantBrandingLogoContent(http);
    logoPreviewUrl.value = URL.createObjectURL(blob);
  } catch {
    logoPreviewUrl.value = null;
  }
}

function applyBranding(branding: TenantBrandingResponse): void {
  version.value = branding.version;
  form.systemTitle = branding.systemTitle ?? '';
  form.contactPhone = branding.contactPhone ?? '';
  form.contactEmail = branding.contactEmail ?? '';
  form.contactAddress = branding.contactAddress ?? '';
  form.copyright = branding.copyright ?? '';
}

async function loadBranding(): Promise<void> {
  loading.value = true;
  try {
    const branding = await getCurrentTenantBranding(http);
    applyBranding(branding);
    await refreshLogoPreview(branding.logoFileId !== null);
  } finally {
    loading.value = false;
  }
}

async function saveBranding(): Promise<void> {
  saving.value = true;
  try {
    const branding = await updateCurrentTenantBranding(http, {
      systemTitle: form.systemTitle || null,
      contactPhone: form.contactPhone || null,
      contactEmail: form.contactEmail || null,
      contactAddress: form.contactAddress || null,
      copyright: form.copyright || null,
      version: version.value
    });
    applyBranding(branding);
    ElMessage.success(t('tenantBranding.saveSuccess'));
  } finally {
    saving.value = false;
  }
}

async function handleLogoSelected(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) {
    return;
  }

  uploadingLogo.value = true;
  try {
    const branding = await uploadCurrentTenantBrandingLogo(http, file);
    applyBranding(branding);
    await refreshLogoPreview(true);
    ElMessage.success(t('tenantBranding.logoUploadSuccess'));
  } finally {
    uploadingLogo.value = false;
  }
}

async function removeLogo(): Promise<void> {
  removingLogo.value = true;
  try {
    const branding = await deleteCurrentTenantBrandingLogo(http);
    applyBranding(branding);
    await refreshLogoPreview(false);
    ElMessage.success(t('tenantBranding.logoRemoveSuccess'));
  } finally {
    removingLogo.value = false;
  }
}

onMounted(() => {
  void loadBranding();
});

onUnmounted(() => {
  revokePreview(logoPreviewUrl.value);
});
</script>

<template>
  <section class="tenant-branding art-page-stack" :aria-busy="loading || saving">
    <header class="tenant-branding__header">
      <div>
        <h1 data-route-heading tabindex="-1">{{ t('tenantBranding.title') }}</h1>
        <p>{{ t('tenantBranding.caption') }}</p>
      </div>
    </header>

    <el-card shadow="never" :aria-busy="loading">
      <el-form label-width="120px" class="tenant-branding__form">
        <el-form-item :label="t('tenantBranding.logo')">
          <div class="tenant-branding__logo-row">
            <div v-if="logoPreviewUrl" class="tenant-branding__logo-preview">
              <img :src="logoPreviewUrl" alt="" />
            </div>
            <div class="tenant-branding__logo-actions">
              <label class="tenant-branding__upload">
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,image/svg+xml"
                  hidden
                  :disabled="uploadingLogo || removingLogo"
                  @change="handleLogoSelected"
                />
                <el-button :loading="uploadingLogo">{{ t('tenantBranding.uploadLogo') }}</el-button>
              </label>
              <el-button
                v-if="logoPreviewUrl"
                :loading="removingLogo"
                @click="removeLogo"
              >
                {{ t('tenantBranding.removeLogo') }}
              </el-button>
            </div>
          </div>
        </el-form-item>
        <el-form-item :label="t('tenantBranding.systemTitle')">
          <el-input v-model="form.systemTitle" maxlength="128" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactPhone')">
          <el-input v-model="form.contactPhone" maxlength="32" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactEmail')">
          <el-input v-model="form.contactEmail" maxlength="256" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.contactAddress')">
          <el-input v-model="form.contactAddress" maxlength="512" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item :label="t('tenantBranding.copyright')">
          <el-input v-model="form.copyright" maxlength="256" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="saveBranding">
            {{ t('users.confirm') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </section>
</template>

<style scoped>
.tenant-branding__header {
  margin-bottom: 16px;
}

.tenant-branding__logo-row {
  display: flex;
  gap: 16px;
  align-items: center;
  flex-wrap: wrap;
}

.tenant-branding__logo-preview {
  width: 72px;
  height: 72px;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.tenant-branding__logo-preview img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}

.tenant-branding__logo-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
