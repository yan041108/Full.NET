<script setup lang="ts">
import type { InboxMessage } from '@fullnet/client-contracts';
import { onLoad, onShow } from '@dcloudio/uni-app';
import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  httpClient,
  identitySession,
  isBusinessRuntimeAvailable,
  restoreIdentitySession
} from '../../features/identity/application-session';
import { createInboxMessagesClient } from '../../features/notifications/inbox-messages-client';
import { readWorkflowResourceTarget, workflowResourceLoginUrl, workflowResourcePermission, workflowResourceUrl } from '../../features/workflow/workflow-resource-links';
// #ifdef H5
import { useWorkflowControlAccessibility } from '../../features/workflow/workflow-control-accessibility';
import { useWorkflowResourceRoute } from '../../features/workflow/workflow-resource-route-h5';
// #endif

const inboxClient = createInboxMessagesClient(httpClient);
const pageRoot = ref<unknown>();
// #ifdef H5
useWorkflowControlAccessibility(pageRoot);
// #endif
const { t } = useI18n();
const messageId = ref('');
const message = ref<InboxMessage>();
const loading = ref(true);
const feedback = ref('');
let epoch = 0;
let disposed = false;
const opening = ref(false);
const target = computed(() => message.value?.revokedAtUtc ? undefined : readWorkflowResourceTarget(message.value?.deepLink));
const canOpen = computed(() => !!target.value && identitySession.can(workflowResourcePermission(target.value)));
const unsubscribe = identitySession.subscribe(() => { epoch++; message.value = undefined; opening.value = false; loading.value = false; feedback.value = t('workflow.resource.unavailable'); });
onBeforeUnmount(() => { disposed = true; epoch++; unsubscribe(); });

onLoad(query => {
  messageId.value = typeof query?.id === 'string' ? query.id : '';
});
onShow(() => { void load(); });
// #ifdef H5
useWorkflowResourceRoute('inbox_message', messageId, () => { epoch++; message.value = undefined; void load(); });
// #endif

async function load(): Promise<void> {
  let current = ++epoch;
  loading.value = true;
  message.value = undefined;
  feedback.value = '';
  try {
    if (!isBusinessRuntimeAvailable) {
      feedback.value = t('identity.login.platformUnavailable');
      return;
    }
    if (identitySession.snapshot().state !== 'authenticated'
      && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: workflowResourceLoginUrl({ resourceTypeKey: 'inbox_message', resourceId: messageId.value }) });
      return;
    }
    if (!identitySession.can('notifications.inbox.read')) {
      throw new Error('permission-denied');
    }
    if (disposed) return;
    current = ++epoch;
    const found = await inboxClient.get(messageId.value);
    if (current !== epoch) return;
    const result = found.status === 'unread' && !found.revokedAtUtc
      && identitySession.can('notifications.inbox.mark_read')
      ? await inboxClient.markRead(found.id)
      : found;
    if (current === epoch) message.value = result;
  } catch {
    if (current === epoch) feedback.value = t('workflow.resource.unavailable');
  } finally {
    if (current === epoch) loading.value = false;
  }
}

async function openResource(): Promise<void> {
  if (opening.value || !canOpen.value) return;
  opening.value = true;
  const current = epoch;
  try {
    // 点击时重读撤销及链接，再由目的页面重新核对成员、作用域和活动状态。
    const latest = await inboxClient.get(messageId.value);
    if (current !== epoch) return;
    message.value = latest;
    const resource = latest.revokedAtUtc ? undefined : readWorkflowResourceTarget(latest.deepLink);
    if (!resource || !identitySession.can(workflowResourcePermission(resource))) {
      feedback.value = t('workflow.resource.unavailable'); return;
    }
    await uni.navigateTo({ url: workflowResourceUrl(resource) });
  } catch {
    if (current === epoch) { message.value = undefined; feedback.value = t('workflow.resource.unavailable'); }
  } finally { if (current === epoch) opening.value = false; }
}
</script>

<template>
  <view ref="pageRoot" class="page-shell">
    <text v-if="loading" class="state">{{ t('notifications.inbox.detailLoading') }}</text>
    <text v-else-if="feedback" class="state error">{{ feedback }}</text>
    <view v-else-if="message" class="panel">
      <text class="title">{{ message.title }}</text>
      <text class="meta">{{ message.status }} · {{ message.createdAtUtc }}</text>
      <text class="content">{{ message.content }}</text>
      <text v-if="message.revokedAtUtc" class="state">{{ t('workflow.resource.revoked') }}</text>
      <button v-else-if="canOpen" :disabled="opening" @click="openResource">{{ t('workflow.resource.open') }}</button>
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
