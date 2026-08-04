<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsEnumCatalogDetail,
  SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { getSettingsEnumCatalog, listSettingsEnumCatalogs } from '../api/enum-catalogs';

defineOptions({ name: 'EnumCatalogsView' });

interface AppliedFilters {
  keyword: string;
}

const { t } = useAdminI18n();
const catalogs = ref<SettingsEnumCatalogSummary[]>([]);
const selected = ref<SettingsEnumCatalogDetail>();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ keyword: '' });

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

const filteredCatalogs = computed(() => {
  const keyword = appliedFilters.value.keyword.trim().toLowerCase();
  if (!keyword) {
    return catalogs.value;
  }
  return catalogs.value.filter(catalog =>
    catalog.displayName.toLowerCase().includes(keyword)
    || catalog.key.toLowerCase().includes(keyword)
  );
});

const { page, pageSize, total, pagedItems: pagedCatalogs, resetPage } = useArtClientPagination(filteredCatalogs);

const memberItems = computed(() => selected.value?.members ?? []);
const {
  page: memberPage,
  pageSize: memberPageSize,
  total: memberTotal,
  pagedItems: pagedMembers
} = useArtClientPagination(memberItems);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'keyword',
    label: t('enumCatalogs.directoryTitle'),
    placeholder: t('enumCatalogs.searchPlaceholder')
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function memberRowIndex(index: number): number {
  return (memberPage.value - 1) * memberPageSize.value + index + 1;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    catalogs.value = await listSettingsEnumCatalogs();
    resetPage();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { keyword: params.keyword ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { keyword: '' };
  resetPage();
}

async function openCatalog(summary: SettingsEnumCatalogSummary): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getSettingsEnumCatalog(summary.key);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_enum_catalog_failed',
        title: t('enumCatalogs.loadFailed')
      };
}
</script>

<template>
  <section class="enum-catalogs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('enumCatalogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="1"
      :show-expand="false"
      :search-label="t('enumCatalogs.query')"
      :reset-label="t('enumCatalogs.reset')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card shadow="never" class="art-table-card">
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

        <div class="art-table" :class="{ 'is-empty': pagedCatalogs.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedCatalogs"
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

            <el-table-column :label="t('enumCatalogs.label')" min-width="180">
              <template #default="{ row }">
                <div>
                  <div translate="no">{{ row.displayName }}</div>
                  <code translate="no">{{ row.key }}</code>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('enumCatalogs.memberCount')" width="100" align="center" prop="memberCount" />

            <el-table-column :label="t('users.columnActions')" width="100" fixed="right" align="center">
              <template #default="{ row }">
                <el-button plain size="small" @click="openCatalog(row)">
                  {{ t('enumCatalogs.select') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('enumCatalogs.emptyDirectory') }}</template>
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

    <el-card v-if="selected" shadow="never" class="art-table-card enum-catalogs-view__members">
      <template #header>
        <h2>{{ t('enumCatalogs.membersTitle', { name: selected.displayName }) }}</h2>
      </template>

      <div class="art-table" :class="{ 'is-empty': pagedMembers.length === 0 }">
        <el-table
          :data="pagedMembers"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          class="art-crud-data-table"
          :class="{ 'art-table--header-bg': tableHeaderBackground }"
        >
          <el-table-column :label="t('users.columnIndex')" width="72" align="center">
            <template #default="{ $index }">{{ memberRowIndex($index) }}</template>
          </el-table-column>

          <el-table-column :label="t('enumCatalogs.label')" min-width="160" prop="label" />

          <el-table-column :label="t('enumCatalogs.code')" min-width="160" prop="code" />

          <el-table-column :label="t('enumCatalogs.displayOrder')" width="100" align="center" prop="displayOrder" />

          <template #empty>{{ t('enumCatalogs.emptyMembers') }}</template>
        </el-table>

        <div class="art-table__pagination center custom-pagination">
          <el-pagination
            v-model:current-page="memberPage"
            v-model:page-size="memberPageSize"
            :total="memberTotal"
            background
            layout="total, sizes, prev, pager, next, jumper"
            :page-sizes="[10, 20, 50, 100]"
          />
        </div>
      </div>
    </el-card>

    <p v-else class="art-empty-state">{{ t('enumCatalogs.emptyMembers') }}</p>
  </section>
</template>

<style scoped>
.enum-catalogs-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.enum-catalogs-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.enum-catalogs-view__members {
  flex: none;
}
</style>
