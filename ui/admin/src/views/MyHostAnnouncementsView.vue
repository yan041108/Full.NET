<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDrawer,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  ReceivedHostAnnouncementDetail,
  ReceivedHostAnnouncementListItem
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getMyHostAnnouncement,
  getMyHostAnnouncementUnreadCount,
  listMyHostAnnouncements,
  markAllMyHostAnnouncementsRead,
  markMyHostAnnouncementRead
} from '../api/my-host-announcements';
import { useNotificationsRealtime } from '../notifications/realtime';

defineOptions({ name: 'MyHostAnnouncementsView' });

interface AppliedFilters {
  title: string;
  status: '' | 'read' | 'unread';
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const pagedItems = ref<ReceivedHostAnnouncementListItem[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const unreadCount = ref(0);
const loading = ref(false);
const changing = ref(false);
const detailLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '', status: '' });
const drawerOpen = ref(false);
const selectedDetail = ref<ReceivedHostAnnouncementDetail | null>(null);
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

const canMarkRead = computed(() => session.can('notifications.announcements.received.mark_read'));
const canMarkAllRead = computed(() => session.can('notifications.announcements.received.mark_all_read'));

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'title',
    label: t('myHostAnnouncements.fieldTitle'),
    placeholder: t('myHostAnnouncements.searchTitlePlaceholder')
  },
  {
    key: 'status',
    label: t('myHostAnnouncements.status'),
    type: 'select',
    placeholder: t('myHostAnnouncements.searchStatusPlaceholder'),
    options: [
      { label: t('myHostAnnouncements.statusUnread'), value: 'unread' },
      { label: t('myHostAnnouncements.statusRead'), value: 'read' }
    ]
  }
]);

watch([page, pageSize], () => {
  void load();
});

watchLoading(loading);

onMounted(() => {
  void load();
});

watch(notificationsRealtime.announcementRevision, () => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function kindLabel(kind: ReceivedHostAnnouncementListItem['kind']): string {
  return kind === 'notice'
    ? t('myHostAnnouncements.kindNotice')
    : t('myHostAnnouncements.kindAnnouncement');
}

function readStatusLabel(isRead: boolean): string {
  return isRead
    ? t('myHostAnnouncements.statusRead')
    : t('myHostAnnouncements.statusUnread');
}

function resolveIsReadFilter(status: AppliedFilters['status']): boolean | undefined {
  if (status === 'read') {
    return true;
  }
  if (status === 'unread') {
    return false;
  }
  return undefined;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const filters = appliedFilters.value;
    const [pageResult, unread] = await Promise.all([
      listMyHostAnnouncements({
        page: page.value,
        pageSize: pageSize.value,
        title: filters.title,
        isRead: resolveIsReadFilter(filters.status)
      }),
      getMyHostAnnouncementUnreadCount()
    ]);
    pagedItems.value = pageResult.items;
    total.value = pageResult.total;
    unreadCount.value = unread.unreadCount;
    if (selectedDetail.value) {
      const refreshed = pageResult.items.find(item => item.id === selectedDetail.value?.id);
      if (refreshed) {
        selectedDetail.value = {
          ...selectedDetail.value,
          isRead: refreshed.isRead,
          readAtUtc: refreshed.readAtUtc
        };
      }
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

async function openDetail(item: ReceivedHostAnnouncementListItem): Promise<void> {
  drawerOpen.value = true;
  detailLoading.value = true;
  problem.value = undefined;
  try {
    let detail = await getMyHostAnnouncement(item.id);
    if (!detail.isRead && canMarkRead.value) {
      detail = await markMyHostAnnouncementRead(item.id);
    }
    selectedDetail.value = detail;
    if (!item.isRead && detail.isRead) {
      await load();
    }
  } catch (error: unknown) {
    drawerOpen.value = false;
    selectedDetail.value = null;
    problem.value = toProblem(error, 'myHostAnnouncements.operationFailed');
  } finally {
    detailLoading.value = false;
  }
}

function closeDetail(): void {
  drawerOpen.value = false;
  selectedDetail.value = null;
}

async function markRead(item: ReceivedHostAnnouncementListItem): Promise<void> {
  if (changing.value || item.isRead || !canMarkRead.value) {
    return;
  }
  changing.value = true;
  try {
    const detail = await markMyHostAnnouncementRead(item.id);
    if (selectedDetail.value?.id === item.id) {
      selectedDetail.value = detail;
    }
    ElMessage.success(t('myHostAnnouncements.markReadSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'myHostAnnouncements.operationFailed');
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
    await markAllMyHostAnnouncementsRead();
    ElMessage.success(t('myHostAnnouncements.markAllReadSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'myHostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'myHostAnnouncements.loadFailed' | 'myHostAnnouncements.operationFailed' = 'myHostAnnouncements.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.my_host_announcement_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="my-host-announcements-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('myHostAnnouncements.title') }}</h1>
    <p class="art-page-description">{{ t('myHostAnnouncements.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="2"
      :search-label="t('myHostAnnouncements.query')"
      :reset-label="t('myHostAnnouncements.reset')"
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
            <h2 data-testid="my-host-announcements-list-title">{{ t('myHostAnnouncements.listTitle') }}</h2>
            <PermissionGate code="notifications.announcements.received.mark_all_read">
              <el-button
                plain
                data-testid="my-host-announcements-mark-all-read"
                :disabled="changing || unreadCount === 0"
                @click="markAllRead"
              >
                {{ t('myHostAnnouncements.markAllRead') }} ({{ unreadCount }})
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
            highlight-current-row
            @row-click="(row: ReceivedHostAnnouncementListItem) => openDetail(row)"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('myHostAnnouncements.fieldTitle')" min-width="180">
              <template #default="{ row }">
                <button
                  type="button"
                  class="my-host-announcements-title-link"
                  data-testid="my-host-announcements-open-detail"
                  translate="no"
                  @click.stop="openDetail(row as ReceivedHostAnnouncementListItem)"
                >
                  {{ row.title }}
                </button>
              </template>
            </el-table-column>

            <el-table-column :label="t('myHostAnnouncements.fieldKind')" width="100" align="center">
              <template #default="{ row }">{{ kindLabel(row.kind) }}</template>
            </el-table-column>

            <el-table-column :label="t('myHostAnnouncements.status')" width="100" align="center">
              <template #default="{ row }">
                <el-tag :type="row.isRead ? 'info' : 'warning'">
                  {{ readStatusLabel(row.isRead) }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column :label="t('myHostAnnouncements.publishedAt')" min-width="180">
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.publishedAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <PermissionGate v-if="!row.isRead" code="notifications.announcements.received.mark_read">
                  <el-button
                    plain
                    size="small"
                    data-testid="my-host-announcements-mark-read"
                    :disabled="changing"
                    @click.stop="markRead(row as ReceivedHostAnnouncementListItem)"
                  >
                    {{ t('myHostAnnouncements.markRead') }}
                  </el-button>
                </PermissionGate>
              </template>
            </el-table-column>

            <template #empty>{{ t('myHostAnnouncements.emptyList') }}</template>
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

    <el-drawer
      :model-value="drawerOpen"
      :title="selectedDetail?.title ?? t('myHostAnnouncements.detailTitle')"
      size="min(680px, 94vw)"
      data-testid="my-host-announcements-detail-drawer"
      @close="closeDetail"
    >
      <div v-loading="detailLoading">
        <template v-if="selectedDetail">
          <div class="my-host-announcements-detail__meta">
            <el-tag size="small">{{ kindLabel(selectedDetail.kind) }}</el-tag>
            <el-tag size="small" :type="selectedDetail.isRead ? 'info' : 'warning'">
              {{ readStatusLabel(selectedDetail.isRead) }}
            </el-tag>
            <span>
              {{ t('myHostAnnouncements.publishedAt') }}:
              {{ formatDateTime(selectedDetail.publishedAtUtc) }}
            </span>
            <span v-if="selectedDetail.readAtUtc">
              {{ t('myHostAnnouncements.readAt') }}:
              {{ formatDateTime(selectedDetail.readAtUtc) }}
            </span>
          </div>
          <PermissionGate code="notifications.announcements.received.mark_read">
            <el-button
              v-if="!selectedDetail.isRead"
              type="primary"
              plain
              class="my-host-announcements-detail__mark-read"
              data-testid="my-host-announcements-detail-mark-read"
              :loading="changing"
              @click="markRead(selectedDetail)"
            >
              {{ t('myHostAnnouncements.markRead') }}
            </el-button>
          </PermissionGate>
          <pre class="my-host-announcements-detail__content" translate="no">{{ selectedDetail.content }}</pre>
        </template>
      </div>
    </el-drawer>
  </section>
</template>

<style scoped>
.my-host-announcements-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.my-host-announcements-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.my-host-announcements-title-link {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--art-theme-color);
  cursor: pointer;
  text-align: left;
}

.my-host-announcements-title-link:hover {
  text-decoration: underline;
}

.my-host-announcements-detail__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.my-host-announcements-detail__mark-read {
  margin-bottom: 12px;
}

.my-host-announcements-detail__content {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  line-height: 1.6;
}
</style>
