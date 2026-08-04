<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElMessageBox, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails, HostOnlineSession } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listHostOnlineSessions, revokeHostOnlineSession } from '../api/online-sessions';

defineOptions({ name: 'OnlineSessionsView' });

interface AppliedFilters {
  username: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostOnlineSession[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ username: '' });

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

const filteredItems = computed(() => {
  const keyword = appliedFilters.value.username.trim().toLowerCase();
  if (!keyword) {
    return items.value;
  }
  return items.value.filter(item =>
    item.username.toLowerCase().includes(keyword)
    || item.displayName.toLowerCase().includes(keyword)
  );
});

const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'username',
    label: t('onlineSessions.username'),
    placeholder: t('onlineSessions.searchUsernamePlaceholder')
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const pageResult = await listHostOnlineSessions();
    items.value = pageResult.items;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { username: params.username ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { username: '' };
  resetPage();
}

async function revoke(item: HostOnlineSession): Promise<void> {
  if (changing.value || !session.can('identity.sessions.revoke')) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('onlineSessions.confirmRevoke', { name: item.username }),
      t('onlineSessions.revoke'),
      {
        type: 'warning',
        confirmButtonText: t('onlineSessions.revoke'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await revokeHostOnlineSession(item.id);
    ElMessage.success(t('onlineSessions.revokeSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'onlineSessions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'onlineSessions.loadFailed' | 'onlineSessions.operationFailed' = 'onlineSessions.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_online_session_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="online-sessions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('onlineSessions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="1"
      :show-expand="false"
      :search-label="t('onlineSessions.query')"
      :reset-label="t('onlineSessions.reset')"
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
        />

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

            <el-table-column :label="t('onlineSessions.displayName')" min-width="180">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.username.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.displayName }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.username }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('onlineSessions.clientId')" min-width="140" prop="clientId" />

            <el-table-column :label="t('onlineSessions.createdAt')" min-width="180" prop="createdAtUtc" />

            <el-table-column :label="t('onlineSessions.expiresAt')" min-width="180" prop="expiresAtUtc" />

            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <PermissionGate code="identity.sessions.revoke">
                  <el-button type="danger" plain size="small" :disabled="changing" @click="revoke(row)">
                    {{ t('onlineSessions.revoke') }}
                  </el-button>
                </PermissionGate>
              </template>
            </el-table-column>

            <template #empty>{{ t('onlineSessions.emptyDirectory') }}</template>
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
.online-sessions-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.online-sessions-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
