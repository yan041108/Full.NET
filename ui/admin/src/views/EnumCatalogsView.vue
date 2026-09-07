<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, nextTick, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDialog,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsEnumCatalogDetail,
  SettingsEnumCatalogDictGenerationAction,
  SettingsEnumCatalogDictGenerationPreview,
  SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
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
import {
  generateSettingsEnumCatalogDict,
  getSettingsEnumCatalog,
  listSettingsEnumCatalogs,
  previewSettingsEnumCatalogDictGeneration
} from '../api/enum-catalogs';

defineOptions({ name: 'EnumCatalogsView' });

interface AppliedFilters {
  keyword: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const catalogs = ref<SettingsEnumCatalogSummary[]>([]);
const selected = ref<SettingsEnumCatalogDetail>();
const loading = ref(false);
const generating = ref(false);
const previewOpen = ref(false);
const preview = ref<SettingsEnumCatalogDictGenerationPreview>();
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ keyword: '' });

const canGenerateDict = computed(() => session.can('settings.enums.generate_dict'));

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

const previewSummary = computed(() => {
  if (!preview.value) {
    return '';
  }
  const created = preview.value.items.filter(item => item.action === 'create').length;
  const skipped = preview.value.items.filter(item => item.action === 'skip_exists').length;
  const conflicted = preview.value.items.filter(item => item.action === 'conflict_label').length;
  const invalid = preview.value.items.filter(item => item.action === 'invalid_value').length;
  return t('enumCatalogs.previewSummary', {
    created,
    skipped,
    conflicted,
    invalid
  });
});

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

function dictActionLabel(action: SettingsEnumCatalogDictGenerationAction): string {
  return t(`enumCatalogs.dictAction.${action}`);
}

function dictActionTagType(action: SettingsEnumCatalogDictGenerationAction): 'success' | 'info' | 'warning' | 'danger' {
  switch (action) {
    case 'create':
      return 'success';
    case 'skip_exists':
      return 'info';
    case 'conflict_label':
      return 'warning';
    default:
      return 'danger';
  }
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

async function openGeneratePreview(): Promise<void> {
  if (!selected.value || !canGenerateDict.value) {
    return;
  }

  generating.value = true;
  problem.value = undefined;
  try {
    preview.value = await previewSettingsEnumCatalogDictGeneration(selected.value.key);
    previewOpen.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'enumCatalogs.generateDictFailed');
  } finally {
    generating.value = false;
  }
}

async function confirmGenerateDict(): Promise<void> {
  if (!selected.value || !canGenerateDict.value) {
    return;
  }

  generating.value = true;
  problem.value = undefined;
  try {
    const result = await generateSettingsEnumCatalogDict(selected.value.key);
    preview.value = {
      catalogKey: result.catalogKey,
      dictTypeCode: result.dictTypeCode,
      dictTypeName: selected.value.displayName,
      dictTypeExists: !result.dictTypeCreated,
      willCreateDictType: false,
      items: result.items,
      unmanagedItems: preview.value?.unmanagedItems ?? []
    };
    previewOpen.value = false;
    ElMessage.success(t('enumCatalogs.generateDictSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'enumCatalogs.generateDictFailed');
  } finally {
    generating.value = false;
  }
}

function toProblem(error: unknown, fallbackKey = 'enumCatalogs.loadFailed'): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_enum_catalog_failed',
        title: translateRuntimeMessage(t, fallbackKey)
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
                <el-button plain size="small" @click="openCatalog(row as SettingsEnumCatalogSummary)">
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
        <div class="enum-catalogs-view__members-header">
          <h2>{{ t('enumCatalogs.membersTitle', { name: selected.displayName }) }}</h2>
          <PermissionGate code="settings.enums.generate_dict">
            <el-button
              type="primary"
              size="small"
              :loading="generating"
              data-testid="enum-catalogs-action-generate-dict"
              @click="openGeneratePreview"
            >
              {{ t('enumCatalogs.generateDict') }}
            </el-button>
          </PermissionGate>
        </div>
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

    <el-dialog
      v-model="previewOpen"
      :title="t('enumCatalogs.generateDictPreviewTitle')"
      width="960px"
      destroy-on-close
    >
      <template v-if="preview">
        <p class="enum-catalogs-view__preview-meta">
          <span>{{ t('enumCatalogs.previewDictTypeCode') }}: <code translate="no">{{ preview.dictTypeCode }}</code></span>
          <el-tag :type="preview.willCreateDictType ? 'success' : 'info'" size="small">
            {{ preview.willCreateDictType
              ? t('enumCatalogs.previewWillCreateDictType')
              : t('enumCatalogs.previewDictTypeExists') }}
          </el-tag>
        </p>
        <p>{{ previewSummary }}</p>

        <el-table :data="preview.items" size="small" max-height="360">
          <el-table-column :label="t('enumCatalogs.code')" prop="value" min-width="140" />
          <el-table-column :label="t('enumCatalogs.previewProposedLabel')" prop="proposedLabel" min-width="140" />
          <el-table-column :label="t('enumCatalogs.previewExistingLabel')" min-width="140">
            <template #default="{ row }">
              {{ row.existingLabel ?? '—' }}
            </template>
          </el-table-column>
          <el-table-column :label="t('enumCatalogs.previewPlannedAction')" width="140" align="center">
            <template #default="{ row }">
              <el-tag :type="dictActionTagType(row.action)" size="small">
                {{ dictActionLabel(row.action) }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>

        <section v-if="preview.unmanagedItems.length > 0" class="enum-catalogs-view__unmanaged">
          <h3>{{ t('enumCatalogs.previewUnmanagedTitle') }}</h3>
          <el-table :data="preview.unmanagedItems" size="small" max-height="200">
            <el-table-column :label="t('enumCatalogs.code')" prop="value" min-width="140" />
            <el-table-column :label="t('enumCatalogs.label')" prop="label" min-width="140" />
          </el-table>
        </section>
      </template>

      <template #footer>
        <el-button @click="previewOpen = false">{{ t('enumCatalogs.generateDictCancel') }}</el-button>
        <el-button
          type="primary"
          :loading="generating"
          data-testid="enum-catalogs-action-confirm-generate-dict"
          @click="confirmGenerateDict"
        >
          {{ t('enumCatalogs.generateDictConfirm') }}
        </el-button>
      </template>
    </el-dialog>
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

.enum-catalogs-view__members-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.enum-catalogs-view__members-header h2 {
  margin: 0;
}

.enum-catalogs-view__preview-meta {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.enum-catalogs-view__unmanaged {
  margin-top: 16px;
}

.enum-catalogs-view__unmanaged h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
}
</style>
