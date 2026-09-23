<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onMounted, ref, watch } from 'vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElOption,
  ElPagination,
  ElRow,
  ElSelect,
  ElStatistic,
  ElTable,
  ElTableColumn,
  ElTabPane,
  ElTabs
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostDocumentAccessLogResponse,
  HostDocumentStatisticsResponse
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentAccessLogs, type DocumentAccessLogListFilters } from '../api/document-access-logs';
import { getDocumentStatistics } from '../api/document-statistics';
import {
  getDocumentVersionRetentionSettings,
  updateDocumentVersionRetentionSettings
} from '../api/document-version-retention';

defineOptions({ name: 'DocumentStatisticsView' });

const { t } = useAdminI18n();
const session = useSessionStore();
const activeTab = ref<'statistics' | 'accessLogs' | 'retention'>('statistics');
const statisticsLoading = ref(false);
const accessLogsLoading = ref(false);
const retentionLoading = ref(false);
const statisticsProblem = ref<FullNetProblemDetails>();
const accessLogsProblem = ref<FullNetProblemDetails>();
const retentionProblem = ref<FullNetProblemDetails>();
const statistics = ref<HostDocumentStatisticsResponse | null>(null);
const accessLogs = ref<HostDocumentAccessLogResponse[]>([]);
const accessLogsPage = ref(1);
const accessLogsPageSize = ref(20);
const accessLogsTotal = ref(0);
const accessLogFilters = ref<DocumentAccessLogListFilters>({
  documentItemId: '',
  accessTypeKey: '',
  sourceKey: ''
});
const retentionSettings = ref<Awaited<ReturnType<typeof getDocumentVersionRetentionSettings>> | null>(null);
const retentionSaving = ref(false);
const retentionForm = ref({
  minimumRetainedVersionsPerItem: 1,
  maximumRetainedHistoryVersions: 0,
  pollSeconds: 300,
  batchSize: 50
});

const canReadAccessLogs = computed(() => session.can('document.host_access_logs.read'));
const canUpdateRetention = computed(() => session.can('document.host_documents.update'));

const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(accessLogsLoading);

/** 统计面板优先展示服务端已格式化好的总大小文案，避免前端再复制一套单位换算规则。 */
function formatTotalSize(): string {
  return statistics.value?.summary.totalSizeInfo ?? '';
}

function accessTypeLabel(accessTypeKey: string): string {
  const key = `documentStatistics.accessLogs.accessType.${accessTypeKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? accessTypeKey : translated;
}

function sourceLabel(sourceKey: string): string {
  const key = `documentStatistics.accessLogs.source.${sourceKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? sourceKey : translated;
}

/** 页面只有一份汇总快照，失败时清空旧数据，避免用户把历史统计误认为最新结果。 */
async function loadStatistics() {
  statisticsLoading.value = true;
  statisticsProblem.value = undefined;
  try {
    statistics.value = await getDocumentStatistics();
  } catch (error) {
    statistics.value = null;
    statisticsProblem.value = toProblem(error, 'documentStatistics.loadFailed');
  } finally {
    statisticsLoading.value = false;
  }
}

/** 访问日志按服务端分页拉取，避免把全量日志一次性加载到浏览器。 */
async function loadAccessLogs() {
  if (!canReadAccessLogs.value) {
    return;
  }

  accessLogsLoading.value = true;
  accessLogsProblem.value = undefined;
  try {
    const result = await listDocumentAccessLogs(
      accessLogsPage.value,
      accessLogsPageSize.value,
      buildAccessLogFilters()
    );
    accessLogs.value = result.items;
    accessLogsPage.value = result.page;
    accessLogsPageSize.value = result.pageSize;
    accessLogsTotal.value = result.total;
  } catch (error) {
    accessLogs.value = [];
    accessLogsProblem.value = toProblem(error, 'documentStatistics.accessLogs.loadFailed');
  } finally {
    accessLogsLoading.value = false;
    void syncTableLayout();
  }
}

/** 统一把未知异常收敛成当前页可展示的错误结构，保证空态和告警文案来源一致。 */
function toProblem(error: unknown, fallbackCode: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: translateRuntimeMessage(t, fallbackCode), status: 500, code: fallbackCode };
}

function buildAccessLogFilters(): DocumentAccessLogListFilters {
  const documentItemId = accessLogFilters.value.documentItemId?.trim();
  const accessTypeKey = accessLogFilters.value.accessTypeKey?.trim();
  const sourceKey = accessLogFilters.value.sourceKey?.trim();
  return {
    ...(documentItemId ? { documentItemId } : {}),
    ...(accessTypeKey ? { accessTypeKey } : {}),
    ...(sourceKey ? { sourceKey } : {}),
  };
}

function applyAccessLogFilters(): void {
  accessLogsPage.value = 1;
  void loadAccessLogs();
}

function resetAccessLogFilters(): void {
  accessLogFilters.value = { documentItemId: '', accessTypeKey: '', sourceKey: '' };
  accessLogsPage.value = 1;
  void loadAccessLogs();
}

async function loadRetentionSettings() {
  retentionLoading.value = true;
  retentionProblem.value = undefined;
  try {
    retentionSettings.value = await getDocumentVersionRetentionSettings();
    retentionForm.value = { ...retentionSettings.value };
  } catch (error) {
    retentionSettings.value = null;
    retentionProblem.value = toProblem(error, 'documentStatistics.retention.loadFailed');
  } finally {
    retentionLoading.value = false;
  }
}

async function saveRetentionSettings() {
  if (!canUpdateRetention.value || retentionSaving.value) {
    return;
  }
  retentionSaving.value = true;
  retentionProblem.value = undefined;
  try {
    retentionSettings.value = await updateDocumentVersionRetentionSettings(retentionForm.value);
    retentionForm.value = { ...retentionSettings.value };
    ElMessage.success(t('documentStatistics.retention.saveSuccess'));
  } catch (error) {
    retentionProblem.value = toProblem(error, 'documentStatistics.retention.saveFailed');
  } finally {
    retentionSaving.value = false;
  }
}

watch(activeTab, tab => {
  if (tab === 'accessLogs' && canReadAccessLogs.value && accessLogs.value.length === 0 && !accessLogsLoading.value) {
    void loadAccessLogs();
  }
  if (tab === 'retention' && !retentionSettings.value && !retentionLoading.value) {
    void loadRetentionSettings();
  }
});

onMounted(() => {
  void loadStatistics();
});
</script>

<template>
  <section
    class="document-statistics-view art-page-stack art-full-height"
    :class="{ 'document-statistics-view--logs': activeTab === 'accessLogs' }"
  >
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentStatistics.title') }}</h1>

    <el-tabs v-model="activeTab" data-testid="document-statistics-tabs">
      <el-tab-pane :label="t('documentStatistics.tabs.statistics')" name="statistics" />
      <el-tab-pane
        v-if="canReadAccessLogs"
        :label="t('documentStatistics.tabs.accessLogs')"
        name="accessLogs"
      />
      <el-tab-pane :label="t('documentStatistics.tabs.retention')" name="retention" />
    </el-tabs>

    <template v-if="activeTab === 'statistics'">
      <el-alert
        v-if="statisticsProblem"
        type="error"
        :title="statisticsProblem.title"
        :description="statisticsProblem.detail ?? statisticsProblem.code"
        show-icon
        class="art-page-alert"
      />

      <el-card v-loading="statisticsLoading" shadow="never" data-testid="document-statistics-panel">
        <template v-if="statistics">
          <el-row :gutter="16" class="document-statistics__summary">
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.totalItems')" :value="statistics.summary.totalItems" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.totalVersions')" :value="statistics.summary.totalVersions" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic
                :title="t('documentStatistics.totalSize')"
                :value="statistics.summary.totalSizeKb"
                :formatter="formatTotalSize"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.shareCount')" :value="statistics.shareCount" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.recycleBinCount')" :value="statistics.recycleBinCount" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic :title="t('documentStatistics.todayCreated')" :value="statistics.todayCreatedCount" />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic
                :title="t('documentStatistics.todayAccess')"
                :value="statistics.todayAccessCount"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="8">
              <el-statistic
                :title="t('documentStatistics.todayDownload')"
                :value="statistics.todayDownloadCount"
              />
            </el-col>
          </el-row>

          <h2 class="document-statistics__section-title">{{ t('documentStatistics.byTypeTitle') }}</h2>
          <el-table
            v-if="statistics.byType.length"
            :data="statistics.byType"
            size="small"
            data-testid="document-statistics-by-type"
          >
            <el-table-column prop="extension" :label="t('documentStatistics.byType.extension')" />
            <el-table-column prop="count" :label="t('documentStatistics.byType.count')" />
            <el-table-column prop="totalSizeKb" :label="t('documentStatistics.byType.totalSizeKb')" />
          </el-table>
          <p v-else data-testid="document-statistics-by-type-empty">{{ t('documentStatistics.byType.empty') }}</p>
        </template>
        <p v-else-if="!statisticsLoading">{{ t('documentStatistics.empty') }}</p>
      </el-card>
    </template>

    <template v-else-if="activeTab === 'accessLogs' && canReadAccessLogs">
      <el-card class="document-statistics__filters-card" shadow="never">
        <el-form inline label-width="96px" @submit.prevent="applyAccessLogFilters">
          <el-form-item :label="t('documentStatistics.accessLogs.documentId')">
            <el-input
              v-model="accessLogFilters.documentItemId"
              clearable
              :placeholder="t('documentStatistics.accessLogs.documentIdPlaceholder')"
            />
          </el-form-item>
          <el-form-item :label="t('documentStatistics.accessLogs.accessType')">
            <el-select v-model="accessLogFilters.accessTypeKey" clearable>
              <el-option :label="t('documentStatistics.accessLogs.accessType.download')" value="download" />
              <el-option :label="t('documentStatistics.accessLogs.accessType.preview')" value="preview" />
              <el-option
                :label="t('documentStatistics.accessLogs.accessType.share_access')"
                value="share_access"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="t('documentStatistics.accessLogs.source')">
            <el-select v-model="accessLogFilters.sourceKey" clearable>
              <el-option
                :label="t('documentStatistics.accessLogs.source.authenticated')"
                value="authenticated"
              />
              <el-option :label="t('documentStatistics.accessLogs.source.share')" value="share" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" @click="applyAccessLogFilters">{{ t('documentStatistics.accessLogs.filter') }}</el-button>
            <el-button @click="resetAccessLogFilters">{{ t('documentStatistics.accessLogs.resetFilters') }}</el-button>
          </el-form-item>
        </el-form>
      </el-card>

      <el-alert
        v-if="accessLogsProblem"
        type="error"
        :title="accessLogsProblem.title"
        :description="accessLogsProblem.detail ?? accessLogsProblem.code"
        show-icon
        class="art-page-alert"
      />

      <el-card
        v-loading="accessLogsLoading"
        class="art-table-card art-full-height document-statistics__logs-card"
        shadow="never"
        data-testid="document-access-logs-panel"
      >
        <div ref="tableMainRef" class="art-crud-table-main">
          <div class="art-table" :class="{ 'is-empty': accessLogs.length === 0 }">
            <el-table
              v-if="accessLogs.length || accessLogsLoading"
              :data="accessLogs"
              :height="tableHeight"
              size="small"
              class="art-crud-data-table"
              data-testid="document-access-logs-table"
            >
              <el-table-column prop="documentTitle" :label="t('documentStatistics.accessLogs.documentTitle')" />
              <el-table-column :label="t('documentStatistics.accessLogs.accessType')">
                <template #default="{ row }">
                  <span translate="no">{{ accessTypeLabel(row.accessTypeKey) }}</span>
                </template>
              </el-table-column>
              <el-table-column :label="t('documentStatistics.accessLogs.source')">
                <template #default="{ row }">
                  <span translate="no">{{ sourceLabel(row.sourceKey) }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="occurredAtUtc" :label="t('documentStatistics.accessLogs.occurredAt')" />
              <template #empty>{{ t('documentStatistics.accessLogs.empty') }}</template>
            </el-table>
            <p v-else-if="!accessLogsLoading" data-testid="document-access-logs-empty">
              {{ t('documentStatistics.accessLogs.empty') }}
            </p>
          </div>

          <el-pagination
            v-if="accessLogsTotal > 0"
            v-model:current-page="accessLogsPage"
            v-model:page-size="accessLogsPageSize"
            class="art-table-pagination center custom-pagination"
            layout="total, prev, pager, next"
            :total="accessLogsTotal"
            @current-change="loadAccessLogs"
            @size-change="loadAccessLogs"
          />
        </div>
      </el-card>
    </template>

    <template v-else-if="activeTab === 'retention'">
      <el-alert
        v-if="retentionProblem"
        type="error"
        :title="retentionProblem.title"
        :description="retentionProblem.detail ?? retentionProblem.code"
        show-icon
        class="art-page-alert"
      />

      <el-card v-loading="retentionLoading" shadow="never" data-testid="document-version-retention-panel">
        <p class="document-statistics__retention-hint">{{ t('documentStatistics.retention.readOnlyHint') }}</p>
        <template v-if="retentionSettings">
          <el-form
            v-if="canUpdateRetention"
            label-width="200px"
            class="document-statistics__retention-form"
            @submit.prevent
          >
            <el-form-item :label="t('documentStatistics.retention.minimumRetainedVersions')">
              <el-input-number v-model="retentionForm.minimumRetainedVersionsPerItem" :min="1" :max="1000" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.maximumHistoryVersions')">
              <el-input-number v-model="retentionForm.maximumRetainedHistoryVersions" :min="0" :max="10000" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.pollSeconds')">
              <el-input-number v-model="retentionForm.pollSeconds" :min="60" :max="86400" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.batchSize')">
              <el-input-number v-model="retentionForm.batchSize" :min="1" :max="1000" />
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                data-testid="document-version-retention-save"
                :loading="retentionSaving"
                @click="saveRetentionSettings"
              >
                {{ t('documentStatistics.retention.save') }}
              </el-button>
            </el-form-item>
          </el-form>
          <el-row v-else :gutter="16">
            <el-col :xs="24" :sm="12" :md="6">
              <el-statistic
                :title="t('documentStatistics.retention.minimumRetainedVersions')"
                :value="retentionSettings.minimumRetainedVersionsPerItem"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="6">
              <el-statistic
                :title="t('documentStatistics.retention.maximumHistoryVersions')"
                :value="retentionSettings.maximumRetainedHistoryVersions"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="6">
              <el-statistic
                :title="t('documentStatistics.retention.pollSeconds')"
                :value="retentionSettings.pollSeconds"
              />
            </el-col>
            <el-col :xs="24" :sm="12" :md="6">
              <el-statistic
                :title="t('documentStatistics.retention.batchSize')"
                :value="retentionSettings.batchSize"
              />
            </el-col>
          </el-row>
        </template>
      </el-card>
    </template>
  </section>
</template>

<style scoped>
.document-statistics-view {
  flex: 1;
  min-height: 0;
}

.document-statistics-view--logs {
  display: flex;
  flex-direction: column;
}

.document-statistics-view--logs .document-statistics__logs-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-statistics-view--logs :deep(.document-statistics__logs-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-statistics-view--logs :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-statistics__summary {
  margin-bottom: 24px;
}

.document-statistics__section-title {
  margin: 0 0 12px;
  font-size: 16px;
  font-weight: 600;
}

.document-statistics__filters-card {
  margin-bottom: 12px;
}

.document-statistics__retention-hint {
  margin: 0 0 16px;
  color: var(--art-gray-600);
  font-size: 13px;
}
</style>
