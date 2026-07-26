<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElTag } from 'element-plus';
import type { FullNetProblemDetails, InboxMessage } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getInboxUnreadCount,
  listInboxMessages,
  markAllInboxMessagesRead,
  markInboxMessageRead,
  sendHostInboxMessage
} from '../api/inbox-messages';
import { useNotificationsRealtime } from '../notifications/realtime';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<InboxMessage[]>([]);
const unreadCount = ref(0);
const recipientUserId = ref('');
const title = ref('');
const content = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('notifications.inbox.write'));
const notificationsRealtime = useNotificationsRealtime();

onMounted(load);
watch(notificationsRealtime.inboxRevision, () => {
  void load();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [page, unread] = await Promise.all([
      listInboxMessages(),
      getInboxUnreadCount()
    ]);
    items.value = page.items;
    unreadCount.value = unread.unreadCount;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function send(): Promise<void> {
  if (changing.value || !recipientUserId.value.trim() || !title.value.trim() || !content.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await sendHostInboxMessage(
      recipientUserId.value.trim(),
      title.value.trim(),
      content.value.trim()
    );
    recipientUserId.value = '';
    title.value = '';
    content.value = '';
    ElMessage.success(t('inboxMessages.sendSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'inboxMessages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function markRead(item: InboxMessage): Promise<void> {
  if (changing.value || item.status === 'read') return;
  changing.value = true;
  try {
    await markInboxMessageRead(item.id);
    ElMessage.success(t('inboxMessages.markReadSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'inboxMessages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function markAllRead(): Promise<void> {
  if (changing.value || unreadCount.value === 0) return;
  changing.value = true;
  try {
    await markAllInboxMessagesRead();
    ElMessage.success(t('inboxMessages.markAllReadSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'inboxMessages.operationFailed');
  } finally {
    changing.value = false;
  }
}

function statusLabel(status: InboxMessage['status']): string {
  return status === 'read'
    ? t('inboxMessages.statusRead')
    : t('inboxMessages.statusUnread');
}

function toProblem(
  error: unknown,
  fallbackKey: 'inboxMessages.loadFailed' | 'inboxMessages.operationFailed' = 'inboxMessages.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.inbox_message_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="inbox-messages-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('inboxMessages.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" shadow="never" class="art-form-card" aria-labelledby="send-inbox-message-title">
      <div><h2 id="send-inbox-message-title">{{ t('inboxMessages.sendTitle') }}</h2></div>
      <label>
        <span>{{ t('inboxMessages.recipientUserId') }}</span>
        <el-input v-model="recipientUserId" />
      </label>
      <label>
        <span>{{ t('inboxMessages.fieldTitle') }}</span>
        <el-input v-model="title" maxlength="200" />
      </label>
      <label>
        <span>{{ t('inboxMessages.fieldContent') }}</span>
        <el-input v-model="content" type="textarea" :rows="3" maxlength="4000" />
      </label>
      <el-button
        type="primary"
        :loading="changing"
        :disabled="!recipientUserId.trim() || !title.trim() || !content.trim()"
        @click="send"
      >
        {{ t('inboxMessages.send') }}
      </el-button>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('inboxMessages.listTitle') }}</h2>
          <span class="art-table-card__count">{{ unreadCount }}</span>
          <el-button plain :disabled="changing || unreadCount === 0" @click="markAllRead">
            {{ t('inboxMessages.markAllRead') }}
          </el-button>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('inboxMessages.emptyList') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong translate="no">{{ item.title }}</strong>
          <el-tag :type="item.status === 'unread' ? 'warning' : 'info'">{{ statusLabel(item.status) }}</el-tag>
          <p>{{ item.content }}</p>
          <small>{{ t('inboxMessages.createdAt') }}: {{ item.createdAtUtc }}</small>
        </div>
        <div v-if="item.status === 'unread'" class="art-data-row__actions">
          <el-button plain :disabled="changing" @click="markRead(item)">{{ t('inboxMessages.markRead') }}</el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
