<script setup lang="ts">
import type { InboxMessage } from '@fullnet/client-contracts';
import { onLoad } from '@dcloudio/uni-app';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  httpClient,
  identitySession,
  isBusinessRuntimeAvailable,
  restoreIdentitySession
} from '../../features/identity/application-session';
import { createInboxMessagesClient } from '../../features/notifications/inbox-messages-client';

const inboxClient = createInboxMessagesClient(httpClient);
const { t } = useI18n();
const messageId = ref('');
const message = ref<InboxMessage>();
const loading = ref(true);
const feedback = ref('');

onLoad(query => {
  messageId.value = typeof query?.id === 'string' ? query.id : '';
  void load();
});

async function load(): Promise<void> {
  loading.value = true;
  feedback.value = '';
  try {
    if (!isBusinessRuntimeAvailable) {
      feedback.value = t('identity.login.platformUnavailable');
      return;
    }
    if (identitySession.snapshot().state !== 'authenticated'
      && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!identitySession.can('notifications.inbox.read')) {
      throw new Error('permission-denied');
    }
    const page = await inboxClient.list(1, 100);
    const found = page.items.find(item => item.id === messageId.value);
    if (!found) {
      throw new Error('not-found');
    }
    message.value = found.status === 'unread'
      && identitySession.can('notifications.inbox.mark_read')
      ? await inboxClient.markRead(found.id)
      : found;
  } catch {
    feedback.value = t('notifications.inbox.detailFailed');
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <view class="page-shell">
    <text v-if="loading" class="state">{{ t('notifications.inbox.detailLoading') }}</text>
    <text v-else-if="feedback" class="state error">{{ feedback }}</text>
    <view v-else-if="message" class="panel">
      <text class="title">{{ message.title }}</text>
      <text class="meta">{{ message.status }} · {{ message.createdAtUtc }}</text>
      <text class="content">{{ message.content }}</text>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 30rpx 26rpx 60rpx; background: #071421; }
.panel { max-width: 760px; margin: 0 auto; padding: 28rpx; border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; background: rgba(12,31,48,.94); }
.title, .meta, .content, .state { display: block; }
.title { color: #f3fbfa; font-size: 40rpx; font-weight: 700; }
.meta { margin-top: 16rpx; color: #7cc4ff; font-size: 24rpx; }
.content { margin-top: 28rpx; color: #d7e4e8; line-height: 1.8; white-space: pre-wrap; }
.state { padding: 42rpx; text-align: center; color: #91a7ad; }
.error { color: #f28d8d; }
</style>
