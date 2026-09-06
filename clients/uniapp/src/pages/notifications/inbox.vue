<script setup lang="ts">
import type { InboxMessage } from '@fullnet/client-contracts';
import { onShow } from '@dcloudio/uni-app';
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
const items = ref<readonly InboxMessage[]>([]);
const unreadCount = ref(0);
const page = ref(1);
const total = ref(0);
const loading = ref(false);
const status = ref<'ready' | 'denied' | 'failed' | 'unavailable'>('ready');

onShow(() => void load());

async function load(): Promise<void> {
  if (loading.value) {
    return;
  }
  loading.value = true;
  status.value = 'ready';
  try {
    if (!isBusinessRuntimeAvailable) {
      status.value = 'unavailable';
      return;
    }
    if (identitySession.snapshot().state !== 'authenticated'
      && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!identitySession.can('notifications.inbox.read')) {
      status.value = 'denied';
      return;
    }
    const [pageResult, unread] = await Promise.all([
      inboxClient.list(page.value, 20),
      inboxClient.getUnreadCount()
    ]);
    items.value = pageResult.items;
    total.value = pageResult.total;
    unreadCount.value = unread.unreadCount;
  } catch {
    status.value = 'failed';
  } finally {
    loading.value = false;
  }
}

async function markAllRead(): Promise<void> {
  if (!identitySession.can('notifications.inbox.mark_all_read')) {
    return;
  }
  await inboxClient.markAllRead();
  await load();
}

function openMessage(messageId: string): void {
  void uni.navigateTo({
    url: `/pages/notifications/inbox-detail?id=${encodeURIComponent(messageId)}`
  });
}
</script>

<template>
  <view class="page-shell">
    <header class="header">
      <view>
        <text class="eyebrow">INBOX</text>
        <text class="title">{{ t('notifications.inbox.title') }}</text>
        <text v-if="unreadCount > 0" class="badge">{{ t('notifications.inbox.unread', { count: unreadCount }) }}</text>
      </view>
      <view class="header-actions">
        <button
          v-if="identitySession.can('notifications.inbox.mark_all_read') && unreadCount > 0"
          class="link"
          data-testid="inbox-mark-all-read"
          @click="markAllRead"
        >
          {{ t('notifications.inbox.markAllRead') }}
        </button>
        <button class="refresh" :loading="loading" :disabled="loading" @click="load">{{ t('notifications.inbox.refresh') }}</button>
      </view>
    </header>
    <text v-if="loading" class="state">{{ t('notifications.inbox.loading') }}</text>
    <text v-else-if="status === 'denied'" class="state error">{{ t('notifications.inbox.denied') }}</text>
    <text v-else-if="status === 'failed'" class="state error">{{ t('notifications.inbox.failed') }}</text>
    <text v-else-if="status === 'unavailable'" class="state">{{ t('identity.login.platformUnavailable') }}</text>
    <text v-else-if="items.length === 0" class="state">{{ t('notifications.inbox.empty') }}</text>
    <view v-else class="list">
      <button
        v-for="item in items"
        :key="item.id"
        class="card"
        data-testid="inbox-message-item"
        @click="openMessage(item.id)"
      >
        <view class="card-row">
          <text class="subject">{{ item.title }}</text>
          <text class="status">{{ item.status }}</text>
        </view>
        <text class="preview">{{ item.content }}</text>
        <text class="created">{{ item.createdAtUtc }}</text>
      </button>
    </view>
    <text v-if="total > items.length" class="more">{{ t('notifications.inbox.more', { total }) }}</text>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 36rpx 28rpx 60rpx; background: linear-gradient(160deg, #1a2f3d, #071421 52%); }
.header, .card-row, .header-actions { display: flex; align-items: center; justify-content: space-between; gap: 20rpx; }
.header-actions { flex-wrap: wrap; justify-content: flex-end; }
.eyebrow, .title, .badge, .preview, .created, .state, .more { display: block; }
.eyebrow { color: #7cc4ff; font-size: 21rpx; letter-spacing: 4rpx; }
.title { margin-top: 8rpx; color: #f3fbfa; font-size: 42rpx; font-weight: 700; }
.badge { margin-top: 10rpx; color: #f4b866; font-size: 24rpx; }
.refresh, .link { margin: 0; padding: 0 28rpx; color: #7cc4ff; background: transparent; border: 1px solid rgba(124,196,255,.45); font-size: 26rpx; }
.state { margin-top: 60rpx; padding: 40rpx; text-align: center; color: #91a7ad; }
.error { color: #f28d8d; }
.list { display: grid; gap: 22rpx; margin-top: 34rpx; }
.card { margin: 0; padding: 30rpx; text-align: left; color: #e7f3f2; background: rgba(12,31,48,.94); border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; }
.subject { max-width: 72%; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.status { color: #7cc4ff; font-size: 23rpx; text-transform: uppercase; }
.preview, .created { margin-top: 14rpx; color: #91a7ad; font-size: 23rpx; overflow-wrap: anywhere; }
.more { margin-top: 28rpx; text-align: center; color: #91a7ad; font-size: 24rpx; }
</style>
