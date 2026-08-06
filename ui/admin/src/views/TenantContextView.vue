<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElCard, ElMessage, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';

defineOptions({ name: 'TenantContextView' });

interface AppliedFilters {
  keyword: string;
}

const session = useSessionStore();
const router = useRouter();
const { t } = useAdminI18n();
const problem = ref<FullNetProblemDetails>();
const pendingTenantId = ref<string | null>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ keyword: '' });
const canSwitch = computed(() => session.can('tenancy.tenants.switch'));

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

const filteredTenants = computed(() => {
  const keyword = appliedFilters.value.keyword.trim().toLowerCase();
  if (!keyword) {
    return session.availableTenants;
  }
  return session.availableTenants.filter(tenant =>
    tenant.name.toLowerCase().includes(keyword)
    || tenant.identifier.toLowerCase().includes(keyword)
    || tenant.domain.toLowerCase().includes(keyword)
  );
});

const { page, pageSize, total, pagedItems: pagedTenants, resetPage } = useArtClientPagination(filteredTenants);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'keyword',
    label: t('tenant.availableTitle'),
    placeholder: t('tenant.searchPlaceholder')
  }
]);

const loading = computed(() => session.switching);

watchLoading(loading);

onMounted(() => {
  void nextTick(updateTableHeight);
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { keyword: params.keyword ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { keyword: '' };
  resetPage();
}

async function selectContext(tenantId: string | null): Promise<void> {
  if (session.switching) {
    return;
  }

  pendingTenantId.value = tenantId;
  problem.value = undefined;
  try {
    await session.switchTenant(tenantId);
    if (tenantId) {
      ElMessage.success(t('tenant.enterSuccess'));
      await router.push('/');
    } else {
      ElMessage.success(t('tenant.returnHostSuccess'));
      await router.push('/tenant-context');
    }
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.context_switch_failed',
          title: t('shell.contextSwitchFailed')
        };
  } finally {
    pendingTenantId.value = undefined;
  }
}
</script>

<template>
  <section class="tenant-context-view art-page-stack art-full-height" :aria-busy="session.switching">
    <div class="tenant-context-view__toolbar">
      <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenant.title') }}</h1>
      <span class="art-page-badge">{{ t('tenant.sessionBound') }}</span>
    </div>

    <section class="art-context-banner" :aria-label="t('tenant.currentAria')">
      <div class="art-context-banner__label">
        <i aria-hidden="true" />
        <span>{{ t('tenant.currentLabel') }}</span>
      </div>
      <strong translate="no">{{ session.currentContextName }}</strong>
      <code translate="no">{{ session.currentUser?.scope }}</code>
      <el-button
        v-if="canSwitch && session.currentUser?.tenantId"
        :loading="session.switching && pendingTenantId === null"
        :disabled="session.switching"
        data-testid="return-host"
        @click="selectContext(null)"
      >
        {{ t('tenant.returnHost') }}
      </el-button>
    </section>

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
      :search-label="t('tenant.query')"
      :reset-label="t('tenant.reset')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never" aria-labelledby="tenant-directory-title">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="session.switching"
          full-class="art-crud-table-main"
          layout="size,fullscreen,settings"
        />

        <div class="art-table" :class="{ 'is-empty': pagedTenants.length === 0 }">
          <el-table
            :data="pagedTenants"
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

            <el-table-column :label="t('tenant.name')" min-width="180">
              <template #default="{ row }">
                <div>
                  <div translate="no">{{ row.name }}</div>
                  <code translate="no">{{ row.identifier }}</code>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('tenant.domain')" min-width="160" prop="domain" />

            <el-table-column :label="t('users.status')" width="120" align="center">
              <template #default="{ row }">
                <el-tag :type="session.currentUser?.tenantId === row.id ? 'success' : 'info'">
                  {{ session.currentUser?.tenantId === row.id ? t('tenant.current') : t('tenant.available') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <el-button
                  v-if="canSwitch && session.currentUser?.tenantId !== row.id"
                  class="art-contrast-primary"
                  :data-tenant-id="row.id"
                  :loading="session.switching && pendingTenantId === row.id"
                  :disabled="session.switching"
                  type="primary"
                  size="small"
                  @click="selectContext(row.id)"
                >
                  {{ t('tenant.enter') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('tenant.directoryEmpty') }}</template>
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
.tenant-context-view__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 4px;
}

.tenant-context-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.tenant-context-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
