<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { adminIdentityAuthMode } from '../config/identity-auth';
import { completeAdminOidcCallback } from '../auth/oidc-center-login';
import { useAdminI18n } from '../i18n/adminI18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { takeLoginDestination } from '../auth/login-destination';
import { flattenNavigation } from '../navigation/catalog';

defineOptions({ name: 'OidcCallbackView' });

const router = useRouter();
const session = useSessionStore();
const { t } = useAdminI18n();
const processing = ref(true);
let active = true;
onBeforeUnmount(() => { active = false; });

onMounted(async () => {
  const destination = takeLoginDestination();
  if (adminIdentityAuthMode !== 'oidc-center') {
    await router.replace('/');
    return;
  }

  let authenticated = false;
  try {
    await completeAdminOidcCallback();
    if (!active) return;
    await session.restore();
    if (!active) return;
    if (!session.isAuthenticated) throw new Error('oidc_invalid_callback');
    authenticated = true;
    ElMessage.success(t('oidcCallback.success'));
  } catch (error: unknown) {
    if (!active) return;
    const code = error instanceof Error ? error.message : 'oidc_callback_failed';
    const messageKey = `oidcCallback.errors.${code}`;
    const translated = translateRuntimeMessage(t, messageKey);
    ElMessage.error(translated === messageKey ? t('oidcCallback.failed') : translated);
  } finally {
    processing.value = false;
    const canResume = authenticated && destination && session.can('tenancy.tenants.read')
      && flattenNavigation(session.navigation).some(node => node.path === destination);
    if (active) await router.replace(canResume ? destination : '/');
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
