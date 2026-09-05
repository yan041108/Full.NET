<script setup lang="ts">
import { reactive, ref } from 'vue';
import { ElButton, ElCard, ElForm, ElFormItem, ElInput, ElMessage } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';

defineOptions({ name: 'SecuritySettingsView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const saving = ref(false);
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
});

async function submit(): Promise<void> {
  if (!form.currentPassword || !form.newPassword) {
    ElMessage.warning(t('securitySettings.requiredFields'));
    return;
  }
  if (form.newPassword !== form.confirmPassword) {
    ElMessage.warning(t('securitySettings.passwordMismatch'));
    return;
  }
  if (!isIdentityPasswordValid(form.newPassword)) {
    ElMessage.warning(t('securitySettings.passwordInvalid'));
    return;
  }

  saving.value = true;
  try {
    await session.changePassword(form.currentPassword, form.newPassword);
    form.currentPassword = '';
    form.newPassword = '';
    form.confirmPassword = '';
    ElMessage.success(t('securitySettings.changeSuccess'));
  } catch (error: unknown) {
    if (isFullNetProblemDetails(error)) {
      ElMessage.error(error.title || error.detail || t('securitySettings.changeFailed'));
      return;
    }
    ElMessage.error(t('securitySettings.changeFailed'));
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <section class="security-settings-view">
    <el-card shadow="never" class="security-settings-card">
      <template #header>
        <h1 class="security-settings-card__title">{{ t('securitySettings.title') }}</h1>
        <p class="security-settings-card__subtitle">{{ t('securitySettings.subtitle') }}</p>
      </template>

      <el-form label-width="120px" class="security-settings-form" @submit.prevent="submit">
        <el-form-item :label="t('securitySettings.currentPassword')">
          <el-input
            v-model="form.currentPassword"
            type="password"
            show-password
            autocomplete="current-password"
          />
        </el-form-item>
        <el-form-item :label="t('securitySettings.newPassword')">
          <el-input
            v-model="form.newPassword"
            type="password"
            show-password
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item :label="t('securitySettings.confirmPassword')">
          <el-input
            v-model="form.confirmPassword"
            type="password"
            show-password
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="submit">
            {{ t('securitySettings.submit') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </section>
</template>

<style scoped>
.security-settings-view {
  display: flex;
  justify-content: center;
  padding: 24px;
}

.security-settings-card {
  width: min(560px, 100%);
}

.security-settings-card__title {
  margin: 0;
  font-size: 18px;
}

.security-settings-card__subtitle {
  margin: 8px 0 0;
  color: var(--art-gray-600);
  font-size: 13px;
}

.security-settings-form {
  margin-top: 8px;
}
</style>
