<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElButton, ElCard, ElForm, ElFormItem, ElInput, ElTable, ElTableColumn } from 'element-plus';
import type { OAuthUserLink, PublicOAuthProvider } from '@fullnet/client-contracts';
import {
  showProblem,
  showSuccess,
  showWarning
} from '../feedback/fullNetMessage';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';
import { buildOAuthAuthorizeUrl, deleteOAuthUserLink, listOAuthUserLinks } from '../api/oauth-links';
import { listPublicOAuthProviders } from '../api/oauth-providers';
import { regenerateMyMfaRecoveryCodes } from '../api/mfaRecoveryCodes';
import TotpEnrollmentCard from '../components/TotpEnrollmentCard.vue';

defineOptions({ name: 'SecuritySettingsView' });

const session = useSessionStore();
const route = useRoute();
const router = useRouter();
const { t } = useAdminI18n();
const saving = ref(false);
const regeneratingRecoveryCodes = ref(false);
const recoveryCodes = ref<string[]>([]);
const totpBusy = ref(false);
const unbinding = ref(false);
const linksLoading = ref(false);
const oauthLinks = ref<OAuthUserLink[]>([]);
const availableProviders = ref<PublicOAuthProvider[]>([]);
const forced = computed(() =>
  session.currentUser?.passwordChangeRequired === true
  || route.query.forced === '1');
let disposed = false;
let operationGeneration = 0;
let viewGeneration = 0;
let inactive = false;
let oauthGeneration = 0;
let oauthController: AbortController | undefined;
let unbindController: AbortController | undefined;
onBeforeUnmount(() => { disposed = true; invalidateView(); });
onDeactivated(() => { inactive = true; invalidateView(); });
onActivated(() => { if (inactive) { inactive = false; void loadOAuthSection(); } });
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
});

// 恢复码和密码属于发起会话；会话切换后清除显示并使旧结果失效。
watch([() => session.currentUser?.id, () => session.currentUser?.sessionId], ([userId], [previousId]) => {
  if (userId !== previousId) viewGeneration++;
  operationGeneration++; recoveryCodes.value = [];
  form.currentPassword = ''; form.newPassword = ''; form.confirmPassword = '';
  invalidateOAuth();
  if (!disposed && !inactive) void loadOAuthSection();
}, { flush: 'sync' });
watch(forced, () => { invalidateOAuth(); if (!forced.value) void loadOAuthSection(); });
function invalidateOAuth(): void {
  oauthGeneration++; oauthController?.abort(); unbindController?.abort();
  oauthLinks.value = []; availableProviders.value = []; linksLoading.value = false; unbinding.value = false;
}
function invalidateView(): void {
  viewGeneration++; operationGeneration++; recoveryCodes.value = [];
  form.currentPassword = ''; form.newPassword = ''; form.confirmPassword = '';
  invalidateOAuth();
}
function isCurrentOperation(generation: number): boolean { return !disposed && !inactive && generation === operationGeneration; }

function oauthReturnUrl(): string {
  const { origin, pathname, search } = window.location;
  return `${origin}${pathname}${search}#/oauth/callback`;
}

async function loadOAuthSection(): Promise<void> {
  if (disposed || inactive || forced.value || !session.currentUser) return;
  const generation = ++oauthGeneration;
  oauthController?.abort();
  const controller = new AbortController();
  oauthController = controller;
  const current = () => !disposed && !inactive && generation === oauthGeneration;
  linksLoading.value = true;
  try {
    const [links, providers] = await Promise.all([
      listOAuthUserLinks(controller.signal),
      listPublicOAuthProviders(controller.signal)
    ]);
    if (!current()) return;
    oauthLinks.value = links;
    const linkedKeys = new Set(links.map(link => link.providerKey));
    availableProviders.value = providers.filter(provider => !linkedKeys.has(provider.providerKey));
  } catch (error: unknown) {
    if (!current()) return;
    oauthLinks.value = [];
    availableProviders.value = [];
    showProblem(error, t('oauthLinks.loadFailed'));
  } finally {
    if (current()) { linksLoading.value = false; oauthController = undefined; }
  }
}

function startOAuthBind(providerKey: string): void {
  if (disposed || inactive || forced.value || saving.value || regeneratingRecoveryCodes.value || totpBusy.value || unbinding.value || linksLoading.value
    || !availableProviders.value.some(provider => provider.providerKey === providerKey)) return;
  window.location.href = buildOAuthAuthorizeUrl(providerKey, 'bind', oauthReturnUrl());
}

async function unbindLink(link: OAuthUserLink): Promise<void> {
  if (disposed || inactive || forced.value || saving.value || regeneratingRecoveryCodes.value || totpBusy.value || unbinding.value || linksLoading.value
    || !oauthLinks.value.some(current => current.id === link.id)) return;
  const generation = operationGeneration;
  const controller = new AbortController();
  unbindController = controller; unbinding.value = true;
  try {
    await deleteOAuthUserLink(link.id, controller.signal);
    if (!isCurrentOperation(generation)) return;
    showSuccess(t('oauthLinks.unbindSuccess'));
    await loadOAuthSection();
  } catch (error: unknown) {
    if (isCurrentOperation(generation)) showProblem(error, t('oauthLinks.unbindFailed'));
  } finally {
    if (isCurrentOperation(generation)) { unbinding.value = false; unbindController = undefined; }
  }
}

async function submit(): Promise<void> {
  if (disposed || inactive || saving.value || regeneratingRecoveryCodes.value || totpBusy.value || unbinding.value) return;
  if (!form.currentPassword || !form.newPassword) {
    showWarning(t('securitySettings.requiredFields'));
    return;
  }
  if (form.newPassword !== form.confirmPassword) {
    showWarning(t('securitySettings.passwordMismatch'));
    return;
  }
  if (!isIdentityPasswordValid(form.newPassword)) {
    showWarning(t('securitySettings.passwordInvalid'));
    return;
  }

  const generation = operationGeneration;
  const view = viewGeneration;
  const userId = session.currentUser?.id;
  const wasForced = forced.value;
  saving.value = true;
  recoveryCodes.value = [];
  try {
    // 改密自己轮换 sessionId；由控制器确认本次操作有效，再核对页面和账号边界。
    const applied = await session.changePassword(form.currentPassword, form.newPassword);
    if (!applied || disposed || inactive || view !== viewGeneration || session.currentUser?.id !== userId) return;
    form.currentPassword = '';
    form.newPassword = '';
    form.confirmPassword = '';
    showSuccess(t('securitySettings.changeSuccess'));
    if (wasForced) {
      await router.replace('/');
    }
  } catch (error: unknown) {
    if (isCurrentOperation(generation)) showProblem(error, t('securitySettings.changeFailed'));
  } finally {
    saving.value = false;
  }
}

async function regenerateRecoveryCodes(): Promise<void> {
  if (disposed || inactive || saving.value || regeneratingRecoveryCodes.value || totpBusy.value || unbinding.value) return;
  const generation = operationGeneration;
  regeneratingRecoveryCodes.value = true;
  recoveryCodes.value = [];
  try {
    const result = await regenerateMyMfaRecoveryCodes();
    if (!isCurrentOperation(generation)) return;
    recoveryCodes.value = [...result.recoveryCodes];
    showSuccess(t('mfaRecovery.regenerateSuccess'));
  } catch (error: unknown) {
    if (isCurrentOperation(generation)) showProblem(error, t('mfaRecovery.regenerateFailed'));
  } finally {
    regeneratingRecoveryCodes.value = false;
  }
}

onMounted(() => {
  if (!forced.value) {
    void loadOAuthSection();
  }
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
        <el-form-item required :label="t('securitySettings.currentPassword')">
          <el-input
            v-model="form.currentPassword"
            type="password"
            show-password
            :disabled="saving || regeneratingRecoveryCodes || totpBusy || unbinding"
            autocomplete="current-password"
          />
        </el-form-item>
        <el-form-item required :label="t('securitySettings.newPassword')">
          <el-input
            v-model="form.newPassword"
            type="password"
            show-password
            :disabled="saving || regeneratingRecoveryCodes || totpBusy || unbinding"
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item required :label="t('securitySettings.confirmPassword')">
          <el-input
            v-model="form.confirmPassword"
            type="password"
            show-password
            :disabled="saving || regeneratingRecoveryCodes || totpBusy || unbinding"
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" :disabled="regeneratingRecoveryCodes || totpBusy || unbinding" native-type="submit">
            {{ t('securitySettings.submit') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <totp-enrollment-card v-if="!forced && session.currentUser?.scope === 'host' && session.currentUser.actorScope === 'host'"
      :disabled="saving || regeneratingRecoveryCodes || unbinding" @busy="totpBusy = $event" />

    <el-card v-if="!forced" shadow="never" class="security-settings-card security-settings-card--oauth">
      <template #header>
        <h2 class="security-settings-card__title">{{ t('mfaRecovery.title') }}</h2>
        <p class="security-settings-card__subtitle">{{ t('mfaRecovery.subtitle') }}</p>
      </template>
      <el-button type="primary" :loading="regeneratingRecoveryCodes" :disabled="saving || totpBusy || unbinding" @click="regenerateRecoveryCodes">
        {{ t('mfaRecovery.regenerate') }}
      </el-button>
      <el-button v-if="recoveryCodes.length > 0" @click="recoveryCodes = []">{{ t('mfaRecovery.hide') }}</el-button>
      <ul v-if="recoveryCodes.length > 0" class="security-settings-recovery-codes">
        <li v-for="code in recoveryCodes" :key="code">{{ code }}</li>
      </ul>
    </el-card>

    <el-card v-if="!forced" shadow="never" class="security-settings-card security-settings-card--oauth">
      <template #header>
        <h2 class="security-settings-card__title">{{ t('oauthLinks.title') }}</h2>
        <p class="security-settings-card__subtitle">{{ t('oauthLinks.subtitle') }}</p>
      </template>

      <el-button :disabled="linksLoading || unbinding" @click="loadOAuthSection">{{ t('oauthLinks.refresh') }}</el-button>
      <el-table v-loading="linksLoading" :data="oauthLinks" style="width: 100%; margin-bottom: 16px;">
        <el-table-column prop="providerDisplayName" :label="t('oauthLinks.fieldProvider')" min-width="160" />
        <el-table-column prop="subject" :label="t('oauthLinks.fieldSubject')" min-width="180" />
        <el-table-column prop="email" :label="t('oauthLinks.fieldEmail')" min-width="180" />
        <!-- @vue-generic {OAuthUserLink} -->
          <el-table-column width="120">
          <template #default="{ row }">
            <el-button type="danger" link :disabled="linksLoading || unbinding || saving || regeneratingRecoveryCodes || totpBusy" @click="unbindLink(row)">{{ t('oauthLinks.unbind') }}</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div v-if="availableProviders.length > 0" class="security-settings-oauth-bind">
        <p>{{ t('oauthLinks.bindHint') }}</p>
        <el-button
          v-for="provider in availableProviders"
          :key="provider.providerKey"
          :disabled="linksLoading || unbinding || saving || regeneratingRecoveryCodes || totpBusy"
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

.security-settings-recovery-codes {
  margin: 16px 0 0;
  padding-left: 20px;
  font-family: ui-monospace, monospace;
  font-size: 13px;
}
</style>
