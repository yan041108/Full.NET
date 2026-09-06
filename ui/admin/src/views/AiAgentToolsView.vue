<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElCard,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTabPane,
  ElTabs,
  ElTag
} from 'element-plus';
import type { AiAgentToolCallListItem, AiAgentToolCatalogItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAiAgentToolCalls, listAiAgentTools } from '../api/ai-agent-tools';

defineOptions({ name: 'AiAgentToolsView' });

const { t } = useAdminI18n();
const activeTab = ref('catalog');
const catalogItems = ref<AiAgentToolCatalogItem[]>([]);
const callItems = ref<AiAgentToolCallListItem[]>([]);
const callTotal = ref(0);
const callPage = ref(1);
const callPageSize = ref(20);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const filterToolName = ref('');
const filterStatusKey = ref('');

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight
} = useArtCrudTableLayout();

const statusOptions = computed(() => [
  { value: '', label: t('aiAgentTools.filterAll') },
  { value: 'succeeded', label: t('aiAgentTools.statusSucceeded') },
  { value: 'failed', label: t('aiAgentTools.statusFailed') },
  { value: 'denied', label: t('aiAgentTools.statusDenied') }
]);

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function sideEffectTagType(key: string): 'success' | 'info' | 'warning' {
  if (key === 'none') {
    return 'info';
  }
  if (key === 'read') {
    return 'success';
  }
  return 'warning';
}

function statusTagType(key: string): 'success' | 'danger' | 'warning' {
  if (key === 'succeeded') {
    return 'success';
  }
  if (key === 'denied') {
    return 'warning';
  }
  return 'danger';
}

async function loadCatalog(): Promise<void> {
  catalogItems.value = await listAiAgentTools();
}

async function loadCalls(): Promise<void> {
  const result = await listAiAgentToolCalls({
    page: callPage.value,
    pageSize: callPageSize.value,
    toolName: filterToolName.value.trim() || undefined,
    statusKey: filterStatusKey.value || undefined
  });
  callItems.value = result.items;
  callTotal.value = result.total;
  await updateTableHeight();
}

async function loadActiveTab(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    if (activeTab.value === 'catalog') {
      await loadCatalog();
    } else {
      await loadCalls();
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'aiAgentTools.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function onTabChange(): Promise<void> {
  await loadActiveTab();
}

async function onCallPageChange(page: number): Promise<void> {
  callPage.value = page;
  await loadCalls();
}

async function applyCallFilters(): Promise<void> {
  callPage.value = 1;
  await loadCalls();
}

onMounted(() => {
  void loadActiveTab();
});
</script>

<template>
  <section class="ai-agent-tools-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('aiAgentTools.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-full-height-card" shadow="never">
      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <el-tab-pane :label="t('aiAgentTools.tabCatalog')" name="catalog">
          <el-table :data="catalogItems" stripe border>
            <el-table-column prop="toolName" :label="t('aiAgentTools.fieldToolName')" min-width="180" />
            <el-table-column prop="displayName" :label="t('aiAgentTools.fieldDisplayName')" min-width="140" />
            <el-table-column prop="description" :label="t('aiAgentTools.fieldDescription')" min-width="220" />
            <el-table-column prop="permissionCode" :label="t('aiAgentTools.fieldPermission')" min-width="180" />
            <el-table-column :label="t('aiAgentTools.fieldSideEffect')" width="120">
              <template #default="{ row }">
                <el-tag :type="sideEffectTagType(row.sideEffectKey)">{{ row.sideEffectKey }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="mcpExposureKey" :label="t('aiAgentTools.fieldMcpExposure')" width="120" />
            <el-table-column :label="t('aiAgentTools.fieldEnabled')" width="90">
              <template #default="{ row }">
                <el-tag :type="row.isEnabled ? 'success' : 'info'">
                  {{ row.isEnabled ? t('aiAgentTools.enabledYes') : t('aiAgentTools.enabledNo') }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane :label="t('aiAgentTools.tabAudit')" name="audit">
          <ArtTableHeader>
            <template #left>
              <el-input
                v-model="filterToolName"
                :placeholder="t('aiAgentTools.filterToolName')"
                clearable
                style="width: 220px"
                @keyup.enter="applyCallFilters"
              />
              <el-select v-model="filterStatusKey" style="width: 160px" @change="applyCallFilters">
                <el-option
                  v-for="option in statusOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </template>
          </ArtTableHeader>

          <div ref="tableMainRef" class="art-table-main">
            <el-table
              :data="callItems"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
              :header-cell-class-name="() => (tableHeaderBackground ? 'is-header-background' : '')"
            >
              <el-table-column prop="createdAtUtc" :label="t('aiAgentTools.fieldCreatedAt')" min-width="180" />
              <el-table-column prop="toolName" :label="t('aiAgentTools.fieldToolName')" min-width="160" />
              <el-table-column :label="t('aiAgentTools.fieldStatus')" width="110">
                <template #default="{ row }">
                  <el-tag :type="statusTagType(row.statusKey)">{{ row.statusKey }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="durationMs" :label="t('aiAgentTools.fieldDurationMs')" width="100" />
              <el-table-column prop="actorUserId" :label="t('aiAgentTools.fieldActorUserId')" min-width="220" />
              <el-table-column prop="tenantId" :label="t('aiAgentTools.fieldTenantId')" min-width="220" />
              <el-table-column prop="inputSummary" :label="t('aiAgentTools.fieldInputSummary')" min-width="200" />
              <el-table-column prop="outputSummary" :label="t('aiAgentTools.fieldOutputSummary')" min-width="200" />
              <el-table-column prop="errorCode" :label="t('aiAgentTools.fieldErrorCode')" min-width="160" />
              <el-table-column prop="traceId" :label="t('aiAgentTools.fieldTraceId')" min-width="180" />
            </el-table>
          </div>

          <el-pagination
            v-model:current-page="callPage"
            v-model:page-size="callPageSize"
            layout="total, prev, pager, next"
            :total="callTotal"
            @current-change="onCallPageChange"
          />
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </section>
</template>
