<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { FullNetProblemDetails, IdentityModuleCatalogEntry } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { getIdentityModule, listIdentityModules } from '../api/module-catalog';

defineOptions({ name: 'ModuleCatalogView' });

interface AppliedFilters {
  keyword: string;
}

const { t } = useAdminI18n();
const modules = ref<IdentityModuleCatalogEntry[]>([]);
const selected = ref<IdentityModuleCatalogEntry>();
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

const filteredModules = computed(() => {
  const keyword = appliedFilters.value.keyword.trim().toLowerCase();
  if (!keyword) {
    return modules.value;
  }
  return modules.value.filter(entry =>
    entry.displayName.toLowerCase().includes(keyword)
    || entry.moduleKey.toLowerCase().includes(keyword)
  );
});

const { page, pageSize, total, pagedItems: pagedModules, resetPage } = useArtClientPagination(filteredModules);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'keyword',
    label: t('moduleCatalog.directoryTitle'),
    placeholder: t('moduleCatalog.searchPlaceholder')
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
    modules.value = await listIdentityModules();
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

async function openModule(entry: IdentityModuleCatalogEntry): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getIdentityModule(entry.moduleKey);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.identity_module_catalog_failed',
        title: t('moduleCatalog.loadFailed')
      };
}
</script>

<template>
  <section class="module-catalog-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('moduleCatalog.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="1"
      :show-expand="false"
      :search-label="t('moduleCatalog.query')"
      :reset-label="t('moduleCatalog.reset')"
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

        <div class="art-table" :class="{ 'is-empty': pagedModules.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedModules"
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

            <el-table-column :label="t('moduleCatalog.directoryTitle')" min-width="200">
              <template #default="{ row }">
                <div>
                  <div translate="no">{{ row.displayName }}</div>
                  <code translate="no">{{ row.moduleKey }}</code>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('moduleCatalog.version')" width="100" align="center" prop="version" />

            <el-table-column :label="t('moduleCatalog.healthCapability')" min-width="140" prop="sourceClassification">
              <template #default="{ row }">
                <el-tag size="small" effect="plain">{{ row.sourceClassification }}</el-tag>
              </template>
            </el-table-column>

            <el-table-column :label="t('users.columnActions')" width="100" fixed="right" align="center">
              <template #default="{ row }">
              <el-button plain size="small" @click="openModule(row as IdentityModuleCatalogEntry)">
                  {{ t('moduleCatalog.select') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('moduleCatalog.emptyDirectory') }}</template>
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

    <el-card v-if="selected" shadow="never" class="art-table-card module-catalog-view__detail">
      <template #header>
        <h2 translate="no">{{ selected.displayName }}</h2>
      </template>
      <p><strong>{{ t('moduleCatalog.hostProfiles') }}:</strong> {{ selected.hostProfiles.join(', ') }}</p>
      <p><strong>{{ t('moduleCatalog.dependencies') }}:</strong>
        {{ selected.dependencies.length === 0 ? t('moduleCatalog.noDependencies') : selected.dependencies.join(', ') }}
      </p>
      <p><strong>{{ t('moduleCatalog.healthCapability') }}:</strong> {{ selected.healthCapability }}</p>
    </el-card>
  </section>
</template>

<style scoped>
.module-catalog-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.module-catalog-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.module-catalog-view__detail {
  flex: none;
}
</style>
