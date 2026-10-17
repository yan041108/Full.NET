<script setup lang="ts">
import { onDeactivated, onMounted, onUnmounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { adminIdentityAuthMode } from '../config/identity-auth';
import { completeAdminOidcCallback } from '../auth/oidc-center-login';
import { useAdminI18n } from '../i18n/adminI18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { clearOidcRefreshCredential, readOidcRefreshCredentialRevision } from '../auth/oidc-session-credentials';

defineOptions({ name: 'OidcCallbackView' });

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const { t } = useAdminI18n();
const processing = ref(true);
const controller = new AbortController();
let inactive = false;
let credentialRevision: number | undefined;
/** 兑换成功但快照未确认时，仅清理本回调仍拥有的凭据，不影响后来的登录。 */
function clearUnconfirmedCredential(): void {
  if (credentialRevision !== undefined && credentialRevision === readOidcRefreshCredentialRevision()
    && session.state !== 'authenticated') clearOidcRefreshCredential();
}
function suspend(): void { inactive = true; controller.abort(); clearUnconfirmedCredential(); }
onUnmounted(suspend);
onDeactivated(suspend);

onMounted(async () => {
  if (adminIdentityAuthMode !== 'oidc-center') {
    await router.replace('/');
    return;
  }

  try {
    const token = await completeAdminOidcCallback(route.query, controller.signal,
      revision => { credentialRevision = revision; });
    if (inactive) return;
    if (credentialRevision === undefined || credentialRevision !== readOidcRefreshCredentialRevision()) {
      throw new Error('oidc_callback_cancelled');
    }
    await session.completeOidcAuthorization(token, controller.signal);
    if (inactive) return;
    if (session.state !== 'authenticated') throw new Error('oidc_callback_failed');
    ElMessage.success(t('oidcCallback.success'));
  } catch (error: unknown) {
    clearUnconfirmedCredential();
    if (inactive) return;
    const code = error instanceof Error ? error.message : 'oidc_callback_failed';
    const messageKey = `oidcCallback.errors.${code}`;
    const translated = translateRuntimeMessage(t, messageKey);
    ElMessage.error(translated === messageKey ? t('oidcCallback.failed') : translated);
  } finally {
    if (!inactive) {
      processing.value = false;
      await router.replace('/');
    }
  }
});
</script>

<template>
  <section class="oidc-callback-view">
    <p>{{ processing ? t('oidcCallback.processing') : t('oidcCallback.redirecting') }}</p>
  </section>
</template>

<style scoped>
.oidc-callback-view {
  display: flex;
  justify-content: center;
  padding: 48px 16px;
}
</style>
