<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

defineOptions({ name: 'OAuthCallbackView' });

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const { t } = useAdminI18n();
const processing = ref(true);

onMounted(async () => {
  const oauth = route.query.oauth;
  const oauthError = route.query.oauth_error;
  if (typeof oauthError === 'string') {
    const messageKey = `oauthCallback.errors.${oauthError}` as const;
    ElMessage.error(t(messageKey));
    await router.replace('/');
    return;
  }

  if (oauth === 'success') {
    try {
      await session.restore();
      ElMessage.success(t('oauthCallback.success'));
    } catch {
      ElMessage.error(t('oauthCallback.restoreFailed'));
    }
  }

  processing.value = false;
  await router.replace('/');
});
</script>

<template>
  <section class="oauth-callback-view">
    <p>{{ processing ? t('oauthCallback.processing') : t('oauthCallback.redirecting') }}</p>
  </section>
</template>

<style scoped>
.oauth-callback-view {
  display: flex;
  justify-content: center;
  padding: 48px 16px;
}
</style>
