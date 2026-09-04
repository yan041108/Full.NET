<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
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
import type { FullNetProblemDetails, HostUser, InboxMessage } from '@fullnet/client-contracts';
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
  markAllInboxMessagesRead,
  markInboxMessageRead,
  sendHostInboxMessage
} from '../api/inbox-messages';
import { listHostUsers } from '../api/users';
import { useNotificationsRealtime } from '../notifications/realtime';

defineOptions({ name: 'InboxMessagesView' });

interface AppliedFilters {
  title: string;
  status: '' | 'read' | 'unread';
}

const session = useSessionStore();
const { t } = useAdminI18n();
const pagedItems = ref<InboxMessage[]>([]);
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
const canSend = computed(() => session.can('notifications.inbox.send'));
const canMarkRead = computed(() => session.can('notifications.inbox.mark_read'));
const canMarkAllRead = computed(() => session.can('notifications.inbox.mark_all_read'));
const notificationsRealtime = useNotificationsRealtime();

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

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'title',
    label: t('inboxMessages.fieldTitle'),
    placeholder: t('inboxMessages.searchTitlePlaceholder')
  },
  {
    key: 'status',
    label: t('inboxMessages.status'),
    type: 'select',
    placeholder: t('inboxMessages.searchStatusPlaceholder'),
    options: [
      { label: t('inboxMessages.statusUnread'), value: 'unread' },
      { label: t('inboxMessages.statusRead'), value: 'read' }
    ]
  }
]);

watchLoading(loading);

watch([page, pageSize], () => {
  void load();
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
  loading.value = true;
  problem.value = undefined;
  try {
    const filters = appliedFilters.value;
    const [pageResult, unread] = await Promise.all([
      listInboxMessages({
        page: page.value,
        pageSize: pageSize.value,
        title: filters.title,
        status: filters.status
      }),
      getInboxUnreadCount()
    ]);
    pagedItems.value = pageResult.items;
    total.value = pageResult.total;
    unreadCount.value = unread.unreadCount;
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

    <PermissionGate code="notifications.inbox.send">
      <el-card shadow="never" class="art-form-card" aria-labelledby="send-inbox-message-title">
        <div><h2 id="send-inbox-message-title">{{ t('inboxMessages.sendTitle') }}</h2></div>
        <label>
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
            style="width: 100%"
          >
            <el-option
              v-for="user in hostUserOptions"
              :key="user.id"
              :label="hostUserLabel(user)"
              :value="user.id"
            />
          </el-select>
        </label>
        <label>
          <span>{{ t('inboxMessages.fieldTitle') }}</span>
          <el-input v-model="title" maxlength="200" data-testid="inbox-messages-title" />
        </label>
        <label>
          <span>{{ t('inboxMessages.fieldContent') }}</span>
          <el-input v-model="content" type="textarea" :rows="3" maxlength="4000" data-testid="inbox-messages-content" />
        </label>
        <el-button
          type="primary"
          data-testid="inbox-messages-send"
          :loading="changing"
          :disabled="recipientUserIds.length === 0 || !title.trim() || !content.trim()"
          @click="send"
        >
          {{ t('inboxMessages.send') }}
        </el-button>
      </el-card>
    </PermissionGate>

    <ArtSearchBar
      v-model="searchForm"
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
            <h2 data-testid="inbox-messages-list-title">{{ t('inboxMessages.listTitle') }}</h2>
            <PermissionGate code="notifications.inbox.mark_all_read">
              <el-button
                plain
                data-testid="inbox-messages-mark-all-read"
                :disabled="changing || unreadCount === 0"
                @click="markAllRead"
              >
                {{ t('inboxMessages.markAllRead') }} ({{ unreadCount }})
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
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
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

            <el-table-column :label="t('inboxMessages.fieldContent')" min-width="240" show-overflow-tooltip prop="content" />

            <el-table-column :label="t('inboxMessages.createdAt')" min-width="180" prop="createdAtUtc" />

            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <PermissionGate v-if="row.status === 'unread'" code="notifications.inbox.mark_read">
                  <el-button
                    plain
                    size="small"
                    data-testid="inbox-messages-mark-read"
                    :disabled="changing"
                    @click="markRead(row as InboxMessage)"
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
  </section>
</template>

<style scoped>
.inbox-messages-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.inbox-messages-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
