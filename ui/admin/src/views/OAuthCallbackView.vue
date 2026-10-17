<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { onDeactivated, onMounted, onUnmounted, ref } from 'vue';
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
let inactive = false;
onUnmounted(() => { inactive = true; });
onDeactivated(() => { inactive = true; });

onMounted(async () => {
  const oauth = route.query.oauth;
  const oauthError = route.query.oauth_error;
  if (typeof oauthError === 'string') {
    const messageKey = `oauthCallback.errors.${oauthError}` as const;
    const message = translateRuntimeMessage(t, messageKey);
    ElMessage.error(message === messageKey ? t('oauthCallback.restoreFailed') : message);
    await router.replace('/');
    return;
  }

  if (oauth === 'success') {
    try {
      const restored = await session.restore();
      if (inactive) return;
      // 返回页面不等于恢复认证；必须同时确认本次恢复结果和当前会话状态。
      if (!restored || session.state !== 'authenticated') throw new Error('oauth_restore_failed');
      ElMessage.success(t('oauthCallback.success'));
    } catch {
      if (!inactive) ElMessage.error(t('oauthCallback.restoreFailed'));
    }
  }

  if (inactive) return;
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
