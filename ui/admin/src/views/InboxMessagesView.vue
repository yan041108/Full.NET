<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { EditPen, Message, Promotion } from '@element-plus/icons-vue';
import {
  ElBadge,
  ElButton,
  ElCard,
  ElDrawer,
  ElIcon,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostUser,
  InboxMessage,
  SentInboxMessage
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getInboxUnreadCount,
  listInboxMessages,
  listSentInboxMessages,
  markAllInboxMessagesRead,
  markInboxMessageRead,
  sendHostInboxMessage
} from '../api/inbox-messages';
import { listHostUsers } from '../api/users';
import { useNotificationsRealtime } from '../notifications/realtime';

defineOptions({ name: 'InboxMessagesView' });

type InboxPane = 'inbox' | 'compose' | 'sent';

type InboxDetailRecord =
  | { mode: 'inbox'; message: InboxMessage }
  | { mode: 'sent'; message: SentInboxMessage };

interface AppliedFilters {
  title: string;
  status: '' | 'read' | 'unread';
}

const session = useSessionStore();
const { t } = useAdminI18n();
const pagedItems = ref<InboxMessage[]>([]);
const sentItems = ref<SentInboxMessage[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const unreadCount = ref(0);
const recipientUserIds = ref<string[]>([]);
const title = ref('');
const content = ref('');
const hostUserOptions = ref<HostUser[]>([]);
const hostUsersLoading = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '', status: '' });
const detailOpen = ref(false);
const detailRecord = ref<InboxDetailRecord | null>(null);
const detailTitle = computed(() => detailRecord.value?.message.title ?? t('inboxMessages.detailTitle'));
const canSend = computed(() => session.can('notifications.inbox.send'));
const canMarkRead = computed(() => session.can('notifications.inbox.mark_read'));
const canMarkAllRead = computed(() => session.can('notifications.inbox.mark_all_read'));
const notificationsRealtime = useNotificationsRealtime();
const activePane = ref<InboxPane>('inbox');
const showInboxPane = computed(() => activePane.value === 'inbox');
const showSentPane = computed(() => canSend.value && activePane.value === 'sent');
const showComposePane = computed(() => canSend.value && activePane.value === 'compose');
const hostUserLabelById = computed(() => {
  const labels = new Map<string, string>();
  for (const user of hostUserOptions.value) {
    labels.set(user.id, hostUserLabel(user));
  }
  return labels;
});

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

const searchItems = computed<ArtSearchBarItem[]>(() => {
  const items: ArtSearchBarItem[] = [
    {
      key: 'title',
      label: t('inboxMessages.fieldTitle'),
      placeholder: t('inboxMessages.searchTitlePlaceholder')
    }
  ];
  if (activePane.value === 'inbox') {
    items.push({
      key: 'status',
      label: t('inboxMessages.status'),
      type: 'select',
      placeholder: t('inboxMessages.searchStatusPlaceholder'),
      options: [
        { label: t('inboxMessages.statusUnread'), value: 'unread' },
        { label: t('inboxMessages.statusRead'), value: 'read' }
      ]
    });
  }
  return items;
});

watchLoading(loading);

watch([page, pageSize], () => {
  if (activePane.value !== 'compose') {
    void load();
  }
});

watch(activePane, () => {
  page.value = 1;
  if (activePane.value !== 'compose') {
    void load();
  }
});

onMounted(() => {
  void load();
  if (canSend.value) {
    void ensureHostUserOptions();
  }
});

watch(notificationsRealtime.inboxRevision, () => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function hostUserLabel(user: HostUser): string {
  return `${user.displayName} (${user.username})`;
}

function openCompose(): void {
  activePane.value = 'compose';
  void ensureHostUserOptions();
}

function openInbox(): void {
  activePane.value = 'inbox';
}

function openSent(): void {
  activePane.value = 'sent';
  void ensureHostUserOptions();
}

function recipientLabel(recipientUserId: string): string {
  return hostUserLabelById.value.get(recipientUserId) ?? recipientUserId;
}

function openInboxDetail(row: InboxMessage): void {
  detailRecord.value = { mode: 'inbox', message: row };
  detailOpen.value = true;
}

function openSentDetail(row: SentInboxMessage): void {
  detailRecord.value = { mode: 'sent', message: row };
  detailOpen.value = true;
}

async function markReadFromDetail(): Promise<void> {
  if (detailRecord.value?.mode !== 'inbox' || detailRecord.value.message.status === 'read') {
    return;
  }

  await markRead(detailRecord.value.message);
  const updated = pagedItems.value.find(item => item.id === detailRecord.value!.message.id);
  if (updated && detailRecord.value?.mode === 'inbox') {
    detailRecord.value = { mode: 'inbox', message: updated };
  }
}

function discardCompose(): void {
  recipientUserIds.value = [];
  title.value = '';
  content.value = '';
  activePane.value = 'inbox';
}

async function ensureHostUserOptions(): Promise<void> {
  if (hostUsersLoading.value || hostUserOptions.value.length > 0) {
    return;
  }
  hostUsersLoading.value = true;
  try {
    const result = await listHostUsers(1, 200);
    hostUserOptions.value = result.items.filter(user => user.isActive);
  } finally {
    hostUsersLoading.value = false;
  }
}

async function load(): Promise<void> {
  if (activePane.value === 'compose') {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const filters = appliedFilters.value;
    const unread = await getInboxUnreadCount();
    unreadCount.value = unread.unreadCount;

    if (activePane.value === 'sent') {
      if (!canSend.value) {
        return;
      }

      const pageResult = await listSentInboxMessages({
        page: page.value,
        pageSize: pageSize.value,
        title: filters.title
      });
      sentItems.value = pageResult.items;
      total.value = pageResult.total;
    } else {
      const pageResult = await listInboxMessages({
        page: page.value,
        pageSize: pageSize.value,
        title: filters.title,
        status: filters.status
      });
      pagedItems.value = pageResult.items;
      total.value = pageResult.total;
    }

    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    title: params.title ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch(): void {
  appliedFilters.value = { title: '', status: '' };
  page.value = 1;
  void load();
}

async function send(): Promise<void> {
  const recipients = [...new Set(recipientUserIds.value)];
  if (
    changing.value
    || !canSend.value
    || recipients.length === 0
    || !title.value.trim()
    || !content.value.trim()
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('inboxMessages.confirmSend', { count: recipients.length }),
      t('inboxMessages.send'),
      {
        type: 'warning',
        confirmButtonText: t('inboxMessages.send'),
        cancelButtonText: t('users.cancel')
      }
    );
  } catch {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  const normalizedTitle = title.value.trim();
  const normalizedContent = content.value.trim();
  try {
    for (const recipientUserId of recipients) {
      await sendHostInboxMessage(recipientUserId, normalizedTitle, normalizedContent);
    }
    recipientUserIds.value = [];
    title.value = '';
    content.value = '';
    ElMessage.success(t('inboxMessages.sendSuccess'));
    activePane.value = 'inbox';
    page.value = 1;
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'inboxMessages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function markRead(item: InboxMessage): Promise<void> {
  if (changing.value || item.status === 'read' || !canMarkRead.value) {
    return;
  }
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
  if (changing.value || unreadCount.value === 0 || !canMarkAllRead.value) {
    return;
  }
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

    <div class="inbox-mailbox">
      <aside class="inbox-mailbox__sidebar" aria-label="inbox navigation">
        <PermissionGate code="notifications.inbox.send">
          <el-button
            type="primary"
            class="inbox-mailbox__compose-btn"
            data-testid="inbox-messages-nav-compose"
            @click="openCompose"
          >
            <el-icon><EditPen /></el-icon>
            {{ t('inboxMessages.navCompose') }}
          </el-button>
        </PermissionGate>

        <p class="inbox-mailbox__section-label">{{ t('inboxMessages.folders') }}</p>
        <nav class="inbox-mailbox__nav">
          <button
            type="button"
            class="inbox-mailbox__nav-item"
            :class="{ 'is-active': showInboxPane }"
            data-testid="inbox-messages-nav-inbox"
            @click="openInbox"
          >
            <span class="inbox-mailbox__nav-item-main">
              <el-icon><Message /></el-icon>
              <span>{{ t('inboxMessages.navInbox') }}</span>
            </span>
            <el-badge
              v-if="unreadCount > 0"
              :value="unreadCount"
              :max="999"
              class="inbox-mailbox__badge"
            />
          </button>
          <PermissionGate code="notifications.inbox.send">
            <button
              type="button"
              class="inbox-mailbox__nav-item"
              :class="{ 'is-active': showSentPane }"
              data-testid="inbox-messages-nav-sent"
              @click="openSent"
            >
              <span class="inbox-mailbox__nav-item-main">
                <el-icon><Promotion /></el-icon>
                <span>{{ t('inboxMessages.navSent') }}</span>
              </span>
            </button>
          </PermissionGate>
        </nav>
      </aside>

      <div class="inbox-mailbox__main">
        <div
          v-if="showComposePane"
          class="inbox-compose"
          aria-labelledby="send-inbox-message-title"
        >
          <header class="inbox-compose__header">
            <h2 id="send-inbox-message-title">{{ t('inboxMessages.navCompose') }}</h2>
            <div class="inbox-compose__header-actions">
              <el-button plain :disabled="changing" @click="discardCompose">
                {{ t('inboxMessages.discard') }}
              </el-button>
              <el-button
                type="primary"
                data-testid="inbox-messages-send"
                :loading="changing"
                :disabled="recipientUserIds.length === 0 || !title.trim() || !content.trim()"
                @click="send"
              >
                {{ t('inboxMessages.send') }}
              </el-button>
            </div>
          </header>

          <form class="inbox-compose__form" @submit.prevent="send">
            <label class="inbox-compose__field">
              <span>{{ t('inboxMessages.recipientUsers') }}</span>
              <el-select
                v-model="recipientUserIds"
                multiple
                filterable
                collapse-tags
                collapse-tags-tooltip
                :loading="hostUsersLoading"
                :placeholder="t('inboxMessages.recipientUsersPlaceholder')"
                data-testid="inbox-messages-recipient"
              >
                <el-option
                  v-for="user in hostUserOptions"
                  :key="user.id"
                  :label="hostUserLabel(user)"
                  :value="user.id"
                />
              </el-select>
            </label>
            <label class="inbox-compose__field">
              <span>{{ t('inboxMessages.fieldTitle') }}</span>
              <el-input v-model="title" maxlength="200" data-testid="inbox-messages-title" />
            </label>
            <label class="inbox-compose__field inbox-compose__field--grow">
              <span>{{ t('inboxMessages.fieldContent') }}</span>
              <el-input
                v-model="content"
                type="textarea"
                :rows="12"
                maxlength="4000"
                resize="none"
                data-testid="inbox-messages-content"
              />
            </label>
          </form>
        </div>

        <div v-if="showInboxPane" class="inbox-inbox-pane">
          <ArtSearchBar
            v-model="searchForm"
            class="inbox-inbox-pane__search"
            :items="searchItems"
            :default-visible-count="2"
            :search-label="t('inboxMessages.query')"
            :reset-label="t('inboxMessages.reset')"
            @search="handleSearch"
            @reset="resetSearch"
          />

          <el-card class="art-table-card" shadow="never">
            <div ref="tableMainRef" class="art-crud-table-main">
              <ArtTableHeader
                v-model:table-size="tableSize"
                v-model:zebra="tableZebra"
                v-model:border="tableBorder"
                v-model:header-background="tableHeaderBackground"
                :loading="loading"
                full-class="art-crud-table-main"
                layout="refresh,size,fullscreen,settings"
                @refresh="load"
              >
                <template #left>
                  <h2 data-testid="inbox-messages-list-title">
                    {{ t('inboxMessages.navInbox') }}
                    <span class="inbox-inbox-pane__count">({{ unreadCount }})</span>
                  </h2>
                  <PermissionGate code="notifications.inbox.mark_all_read">
                    <el-button
                      plain
                      data-testid="inbox-messages-mark-all-read"
                      :disabled="changing || unreadCount === 0"
                      @click="markAllRead"
                    >
                      {{ t('inboxMessages.markAllRead') }}
                    </el-button>
                  </PermissionGate>
                </template>
              </ArtTableHeader>

              <div class="art-table" :class="{ 'is-empty': pagedItems.length === 0 }">
                <el-table
                  v-loading="loading"
                  :data="pagedItems"
                  :height="tableHeight"
                  :size="tableSize"
                  :stripe="tableZebra"
                  :border="tableBorder"
                  :header-cell-style="tableHeaderCellStyle"
                  class="art-crud-data-table inbox-messages-data-table"
                  :class="{ 'art-table--header-bg': tableHeaderBackground }"
                  highlight-current-row
                  @row-click="openInboxDetail"
                >
                  <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                    <template #default="{ $index }">{{ rowIndex($index) }}</template>
                  </el-table-column>

                  <el-table-column :label="t('inboxMessages.fieldTitle')" min-width="180">
                    <template #default="{ row }">
                      <div translate="no">{{ row.title }}</div>
                    </template>
                  </el-table-column>

                  <el-table-column :label="t('inboxMessages.status')" width="100" align="center">
                    <template #default="{ row }">
                      <el-tag :type="row.status === 'unread' ? 'warning' : 'info'">
                        {{ statusLabel(row.status) }}
                      </el-tag>
                    </template>
                  </el-table-column>

                  <el-table-column
                    :label="t('inboxMessages.fieldContent')"
                    min-width="240"
                    show-overflow-tooltip
                    prop="content"
                  />

                  <el-table-column :label="t('inboxMessages.createdAt')" min-width="180" prop="createdAtUtc" />

                  <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
                    <template #default="{ row }">
                      <PermissionGate v-if="row.status === 'unread'" code="notifications.inbox.mark_read">
                        <el-button
                          plain
                          size="small"
                          data-testid="inbox-messages-mark-read"
                          :disabled="changing"
                          @click.stop="markRead(row as InboxMessage)"
                        >
                          {{ t('inboxMessages.markRead') }}
                        </el-button>
                      </PermissionGate>
                    </template>
                  </el-table-column>

                  <template #empty>{{ t('inboxMessages.emptyList') }}</template>
                </el-table>

                <div class="art-table__pagination center custom-pagination">
                  <el-pagination
                    v-model:current-page="page"
                    v-model:page-size="pageSize"
                    :total="total"
                    background
                    layout="total, sizes, prev, pager, next, jumper"
                    :page-sizes="[10, 20, 50, 100]"
                  />
                </div>
              </div>
            </div>
          </el-card>
        </div>

        <div v-if="showSentPane" class="inbox-inbox-pane">
          <ArtSearchBar
            v-model="searchForm"
            class="inbox-inbox-pane__search"
            :items="searchItems"
            :default-visible-count="1"
            :search-label="t('inboxMessages.query')"
            :reset-label="t('inboxMessages.reset')"
            @search="handleSearch"
            @reset="resetSearch"
          />

          <el-card class="art-table-card" shadow="never">
            <div ref="tableMainRef" class="art-crud-table-main">
              <ArtTableHeader
                v-model:table-size="tableSize"
                v-model:zebra="tableZebra"
                v-model:border="tableBorder"
                v-model:header-background="tableHeaderBackground"
                :loading="loading"
                full-class="art-crud-table-main"
                layout="refresh,size,fullscreen,settings"
                @refresh="load"
              >
                <template #left>
                  <h2 data-testid="inbox-messages-sent-list-title">
                    {{ t('inboxMessages.navSent') }}
                    <span class="inbox-inbox-pane__count">({{ total }})</span>
                  </h2>
                </template>
              </ArtTableHeader>

              <div class="art-table" :class="{ 'is-empty': sentItems.length === 0 }">
                <el-table
                  v-loading="loading"
                  :data="sentItems"
                  :height="tableHeight"
                  :size="tableSize"
                  :stripe="tableZebra"
                  :border="tableBorder"
                  :header-cell-style="tableHeaderCellStyle"
                  class="art-crud-data-table inbox-messages-data-table"
                  :class="{ 'art-table--header-bg': tableHeaderBackground }"
                  highlight-current-row
                  @row-click="openSentDetail"
                >
                  <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                    <template #default="{ $index }">{{ rowIndex($index) }}</template>
                  </el-table-column>

                  <el-table-column :label="t('inboxMessages.fieldRecipient')" min-width="160">
                    <template #default="{ row }">
                      <span translate="no">{{ recipientLabel(row.recipientUserId) }}</span>
                    </template>
                  </el-table-column>

                  <el-table-column :label="t('inboxMessages.fieldTitle')" min-width="180">
                    <template #default="{ row }">
                      <div translate="no">{{ row.title }}</div>
                    </template>
                  </el-table-column>

                  <el-table-column :label="t('inboxMessages.status')" width="100" align="center">
                    <template #default="{ row }">
                      <el-tag :type="row.status === 'unread' ? 'warning' : 'info'">
                        {{ statusLabel(row.status) }}
                      </el-tag>
                    </template>
                  </el-table-column>

                  <el-table-column
                    :label="t('inboxMessages.fieldContent')"
                    min-width="240"
                    show-overflow-tooltip
                    prop="content"
                  />

                  <el-table-column :label="t('inboxMessages.sentAt')" min-width="180" prop="createdAtUtc" />

                  <template #empty>{{ t('inboxMessages.emptyList') }}</template>
                </el-table>

                <div class="art-table__pagination center custom-pagination">
                  <el-pagination
                    v-model:current-page="page"
                    v-model:page-size="pageSize"
                    :total="total"
                    background
                    layout="total, sizes, prev, pager, next, jumper"
                    :page-sizes="[10, 20, 50, 100]"
                  />
                </div>
              </div>
            </div>
          </el-card>
        </div>
      </div>
    </div>

    <el-drawer
      v-model="detailOpen"
      :title="detailTitle"
      size="480px"
      data-testid="inbox-messages-detail-drawer"
    >
      <template v-if="detailRecord">
        <dl class="inbox-message-detail">
          <template v-if="detailRecord.mode === 'sent'">
            <div class="inbox-message-detail__row">
              <dt>{{ t('inboxMessages.fieldRecipient') }}</dt>
              <dd translate="no">{{ recipientLabel(detailRecord.message.recipientUserId) }}</dd>
            </div>
          </template>
          <div class="inbox-message-detail__row">
            <dt>{{ t('inboxMessages.status') }}</dt>
            <dd>
              <el-tag :type="detailRecord.message.status === 'unread' ? 'warning' : 'info'">
                {{ statusLabel(detailRecord.message.status) }}
              </el-tag>
            </dd>
          </div>
          <div class="inbox-message-detail__row">
            <dt>
              {{ detailRecord.mode === 'sent' ? t('inboxMessages.sentAt') : t('inboxMessages.createdAt') }}
            </dt>
            <dd translate="no">{{ detailRecord.message.createdAtUtc }}</dd>
          </div>
          <div
            v-if="detailRecord.message.readAtUtc"
            class="inbox-message-detail__row"
          >
            <dt>{{ t('inboxMessages.readAt') }}</dt>
            <dd translate="no">{{ detailRecord.message.readAtUtc }}</dd>
          </div>
          <div class="inbox-message-detail__row inbox-message-detail__row--content">
            <dt>{{ t('inboxMessages.fieldContent') }}</dt>
            <dd class="inbox-message-detail__content" translate="no">{{ detailRecord.message.content }}</dd>
          </div>
        </dl>
        <div v-if="detailRecord.mode === 'inbox'" class="inbox-message-detail__actions">
          <PermissionGate
            v-if="detailRecord.message.status === 'unread'"
            code="notifications.inbox.mark_read"
          >
            <el-button
              type="primary"
              data-testid="inbox-messages-detail-mark-read"
              :disabled="changing"
              @click="markReadFromDetail"
            >
              {{ t('inboxMessages.markRead') }}
            </el-button>
          </PermissionGate>
        </div>
      </template>
    </el-drawer>
  </section>
</template>

<style scoped>
.inbox-messages-view {
  min-height: 0;
}

.inbox-messages-view :deep(.inbox-messages-data-table .el-table__row) {
  cursor: pointer;
}

.inbox-message-detail {
  margin: 0;
}

.inbox-message-detail__row {
  display: grid;
  grid-template-columns: 96px 1fr;
  gap: 8px 16px;
  margin-bottom: 16px;
}

.inbox-message-detail__row dt {
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.inbox-message-detail__row dd {
  margin: 0;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

.inbox-message-detail__content {
  white-space: pre-wrap;
  line-height: 1.6;
}

.inbox-message-detail__actions {
  margin-top: 8px;
}

.inbox-mailbox {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
}

.inbox-mailbox__sidebar {
  flex: 0 0 220px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px 12px;
  border-right: 1px solid var(--el-border-color-lighter);
  background: var(--el-fill-color-blank);
}

.inbox-mailbox__compose-btn {
  width: 100%;
  justify-content: center;
  gap: 6px;
}

.inbox-mailbox__section-label {
  margin: 8px 0 0;
  padding: 0 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.inbox-mailbox__nav {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.inbox-mailbox__nav-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  margin: 0;
  padding: 10px 12px;
  border: none;
  border-radius: var(--el-border-radius-base);
  background: transparent;
  color: var(--el-text-color-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.inbox-mailbox__nav-item:hover {
  background: var(--el-fill-color-light);
}

.inbox-mailbox__nav-item.is-active {
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
}

.inbox-mailbox__nav-item-main {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.inbox-mailbox__badge :deep(.el-badge__content) {
  position: static;
  transform: none;
}

.inbox-mailbox__main {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.inbox-inbox-pane,
.inbox-compose {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.inbox-inbox-pane__search {
  flex-shrink: 0;
  padding: 12px 16px 0;
}

.inbox-inbox-pane__count {
  font-weight: 400;
  color: var(--el-text-color-secondary);
}

.inbox-compose__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 16px 20px 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.inbox-compose__header h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}

.inbox-compose__header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.inbox-compose__form {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 16px 20px 20px;
  overflow: auto;
}

.inbox-compose__field {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.inbox-compose__field--grow {
  flex: 1;
  min-height: 200px;
}

.inbox-compose__field--grow :deep(.el-textarea),
.inbox-compose__field--grow :deep(textarea) {
  height: 100%;
  min-height: 200px;
}

.inbox-messages-view :deep(.inbox-inbox-pane .art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  margin: 12px 16px 16px;
  border: none;
}

.inbox-messages-view :deep(.inbox-inbox-pane .art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding: 0;
}
</style>
