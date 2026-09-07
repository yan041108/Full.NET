<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElButton, ElCard, ElForm, ElFormItem, ElInput, ElMessage, ElTable, ElTableColumn } from 'element-plus';
import type { OAuthUserLink, PublicOAuthProvider } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';
import { buildOAuthAuthorizeUrl, deleteOAuthUserLink, listOAuthUserLinks } from '../api/oauth-links';
import { listPublicOAuthProviders } from '../api/oauth-providers';

defineOptions({ name: 'SecuritySettingsView' });

const session = useSessionStore();
const route = useRoute();
const router = useRouter();
const { t } = useAdminI18n();
const saving = ref(false);
const linksLoading = ref(false);
const oauthLinks = ref<OAuthUserLink[]>([]);
const availableProviders = ref<PublicOAuthProvider[]>([]);
const forced = computed(() =>
  session.currentUser?.passwordChangeRequired === true
  || route.query.forced === '1');
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
});

function oauthReturnUrl(): string {
  const { origin, pathname, search } = window.location;
  return `${origin}${pathname}${search}#/oauth/callback`;
}

async function loadOAuthSection(): Promise<void> {
  linksLoading.value = true;
  try {
    const [links, providers] = await Promise.all([
      listOAuthUserLinks(),
      listPublicOAuthProviders()
    ]);
    oauthLinks.value = links;
    const linkedKeys = new Set(links.map(link => link.providerKey));
    availableProviders.value = providers.filter(provider => !linkedKeys.has(provider.providerKey));
  } catch {
    oauthLinks.value = [];
    availableProviders.value = [];
  } finally {
    linksLoading.value = false;
  }
}

function startOAuthBind(providerKey: string): void {
  window.location.href = buildOAuthAuthorizeUrl(providerKey, 'bind', oauthReturnUrl());
}

async function unbindLink(link: OAuthUserLink): Promise<void> {
  await deleteOAuthUserLink(link.id);
  ElMessage.success(t('oauthLinks.unbindSuccess'));
  await loadOAuthSection();
}

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
    if (forced.value) {
      await router.replace('/');
    }
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

onMounted(() => {
  void loadOAuthSection();
});
</script>

<template>
  <section class="security-settings-view">
    <el-card shadow="never" class="security-settings-card">
      <template #header>
        <h1 class="security-settings-card__title">{{ t('securitySettings.title') }}</h1>
        <p class="security-settings-card__subtitle">
          {{ forced ? t('securitySettings.forcedSubtitle') : t('securitySettings.subtitle') }}
        </p>
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

    <el-card shadow="never" class="security-settings-card security-settings-card--oauth">
      <template #header>
        <h2 class="security-settings-card__title">{{ t('oauthLinks.title') }}</h2>
        <p class="security-settings-card__subtitle">{{ t('oauthLinks.subtitle') }}</p>
      </template>

      <el-table v-loading="linksLoading" :data="oauthLinks" style="width: 100%; margin-bottom: 16px;">
        <el-table-column prop="providerDisplayName" :label="t('oauthLinks.fieldProvider')" min-width="160" />
        <el-table-column prop="subject" :label="t('oauthLinks.fieldSubject')" min-width="180" />
        <el-table-column prop="email" :label="t('oauthLinks.fieldEmail')" min-width="180" />
        <!-- @vue-generic {OAuthUserLink} -->
          <el-table-column width="120">
          <template #default="{ row }">
            <el-button type="danger" link @click="unbindLink(row)">{{ t('oauthLinks.unbind') }}</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div v-if="availableProviders.length > 0" class="security-settings-oauth-bind">
        <p>{{ t('oauthLinks.bindHint') }}</p>
        <el-button
          v-for="provider in availableProviders"
          :key="provider.providerKey"
          @click="startOAuthBind(provider.providerKey)"
        >
          {{ t('oauthLinks.bindAction', { name: provider.displayName }) }}
        </el-button>
      </div>
    </el-card>
  </section>
</template>

<style scoped>
.security-settings-view {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 24px;
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

.security-settings-card--oauth {
  width: min(760px, 100%);
}

.security-settings-oauth-bind {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
</style>
