<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { adminIdentityAuthMode } from '../config/identity-auth';
import { completeAdminOidcCallback } from '../auth/oidc-center-login';
import { useAdminI18n } from '../i18n/adminI18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';

defineOptions({ name: 'OidcCallbackView' });

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const { t } = useAdminI18n();
const processing = ref(true);

onMounted(async () => {
  if (adminIdentityAuthMode !== 'oidc-center') {
    await router.replace('/');
    return;
  }

  try {
    const token = await completeAdminOidcCallback(route.query);
    await session.completeOidcAuthorization(token);
    ElMessage.success(t('oidcCallback.success'));
  } catch (error: unknown) {
    const code = error instanceof Error ? error.message : 'oidc_callback_failed';
    const messageKey = `oidcCallback.errors.${code}`;
    const translated = translateRuntimeMessage(t, messageKey);
    ElMessage.error(translated === messageKey ? t('oidcCallback.failed') : translated);
  } finally {
    processing.value = false;
    await router.replace('/');
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