<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, ImportExportTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listImportExportTasks } from '../api/import-export-tasks';

defineOptions({ name: 'ImportExportTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<ImportExportTaskResponse[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);

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

watchLoading(loading);

const canCreate = () => session.can('import_export.import_tasks.create');
const canRead = () => session.can('import_export.import_tasks.read');

function statusTagType(statusKey: string): 'success' | 'warning' | 'danger' | 'info' {
  if (statusKey === 'preview_succeeded') {
    return 'success';
  }
  if (statusKey === 'preview_failed') {
    return 'danger';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  const key = `importExportTasks.status.${statusKey}` as const;
  const translated = t(key);
  return translated === key ? statusKey : translated;
}

async function load() {
  if (!canRead()) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listImportExportTasks(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t('importExportTasks.loadFailed'), status: 500, code: 'importExportTasks.loadFailed' };
}

onMounted(load);
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('importExportTasks.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
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
          layout="refresh,size,fullscreen"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="import_export.import_tasks.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="import-export-task-create"
                disabled
              >
                {{ t('importExportTasks.addTask') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            v-loading="loading"
            :data="items"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
            <el-table-column :label="t('importExportTasks.schema')" prop="schemaDisplayName" min-width="160" />
            <el-table-column :label="t('importExportTasks.worksheet')" prop="worksheetKey" width="140" />
            <el-table-column :label="t('importExportTasks.status')" width="160">
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.statusKey)" size="small">
                  {{ statusLabel(row.statusKey) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('importExportTasks.rows')" width="120">
              <template #default="{ row }">
                {{ row.validRowCount }}/{{ row.totalRows }}
              </template>
            </el-table-column>
            <el-table-column :label="t('importExportTasks.errorCode')" prop="errorCode" min-width="180" />
            <el-table-column :label="t('importExportTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default>
                <ArtTableActionGroup />
              </template>
            </el-table-column>
            <template #empty>{{ t('importExportTasks.emptyDirectory') }}</template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next"
              :page-sizes="[10, 20, 50]"
              @current-change="load"
              @size-change="load"
            />
          </div>
        </div>
      </div>
    </el-card>
  </section>
</template>
