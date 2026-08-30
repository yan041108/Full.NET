<script setup lang="ts">
import { onShow } from '@dcloudio/uni-app';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

// #ifdef H5
import { h5IdentitySession, restoreH5IdentitySession } from '../../features/identity/h5-application-session';
// #endif

const { t } = useI18n();
const username = ref('');
const password = ref('');
const submitting = ref(false);
const failed = ref(false);
let h5Available = false;
// #ifdef H5
h5Available = true;
// #endif

onShow(() => {
  // #ifdef H5
  void routeAuthenticatedSession();
  // #endif
});

// #ifdef H5
async function routeAuthenticatedSession(): Promise<void> {
  const authenticated = h5IdentitySession.snapshot().state === 'authenticated'
    || await restoreH5IdentitySession();
  if (authenticated) await uni.reLaunch({ url: '/pages/workflow/todos' });
}
// #endif

async function submit(): Promise<void> {
  if (!h5Available || submitting.value || !username.value.trim() || !password.value) {
    return;
  }
  submitting.value = true;
  failed.value = false;
  try {
    // #ifdef H5
    await h5IdentitySession.login(username.value.trim(), password.value);
    password.value = '';
    await uni.reLaunch({ url: '/pages/workflow/todos' });
    // #endif
  } catch {
    password.value = '';
    failed.value = true;
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <view class="page-shell">
    <view class="panel">
      <text class="eyebrow">FULL.NET WORKFLOW</text>
      <text class="title">{{ t('identity.login.title') }}</text>
      <text class="description">{{ t('identity.login.description') }}</text>

      <view v-if="h5Available" class="form">
        <uni-easyinput v-model="username" :placeholder="t('identity.login.username')" :disabled="submitting" />
        <uni-easyinput v-model="password" type="password" :placeholder="t('identity.login.password')" :disabled="submitting" />
        <button class="primary" :disabled="submitting || !username.trim() || !password" @click="submit">
          {{ submitting ? t('identity.login.submitting') : t('identity.login.submit') }}
        </button>
        <text v-if="failed" class="error" role="alert">{{ t('identity.login.failed') }}</text>
      </view>
      <text v-else class="notice">{{ t('identity.login.platformUnavailable') }}</text>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; display: flex; align-items: center; box-sizing: border-box; padding: 48rpx 30rpx; background: radial-gradient(circle at top right, #12394a, #071421 58%); }
.panel { width: 100%; max-width: 680px; margin: 0 auto; padding: 54rpx 42rpx; box-sizing: border-box; border: 1px solid rgba(56, 212, 178, .22); border-radius: 28rpx; background: rgba(10, 29, 45, .94); box-shadow: 0 30rpx 90rpx rgba(0, 0, 0, .3); }
.eyebrow, .title, .description, .notice, .error { display: block; }
.eyebrow { color: #38d4b2; font-size: 22rpx; letter-spacing: 5rpx; }
.title { margin-top: 18rpx; color: #f3fbfa; font-size: 48rpx; font-weight: 700; }
.description { margin-top: 14rpx; color: #91a7ad; line-height: 1.7; }
.form { display: grid; gap: 24rpx; margin-top: 42rpx; }
.primary { margin: 8rpx 0 0; color: #041713; background: #38d4b2; border: 0; border-radius: 14rpx; font-weight: 700; }
.notice, .error { margin-top: 28rpx; color: #f4b866; line-height: 1.6; }
</style>
