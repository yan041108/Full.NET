<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
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
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { showSuccess } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentAccessLogs, type DocumentAccessLogListFilters } from '../api/document-access-logs';
import { getDocumentStatistics } from '../api/document-statistics';
import {
  getDocumentVersionRetentionSettings,
  isDocumentVersionRetentionInput,
  updateDocumentVersionRetentionSettings
} from '../api/document-version-retention';

defineOptions({ name: 'DocumentStatisticsView' });

const route = inject(routeLocationKey, undefined);
const viewActive = ref(false), owner = ref(0);
onMounted(() => { viewActive.value = true; });
onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; });
onBeforeUnmount(() => { viewActive.value = false; });
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
// 输入框是草稿，翻页与刷新只复用用户显式应用的查询快照。
const appliedAccessLogFilters = ref<DocumentAccessLogListFilters>({});
const retentionSettings = ref<Awaited<ReturnType<typeof getDocumentVersionRetentionSettings>> | null>(null);
const retentionSaving = ref(false);
const defaultRetentionForm = () => ({
  version: 0,
  minimumRetainedVersionsPerItem: 1,
  maximumRetainedHistoryVersions: 0,
  pollSeconds: 300,
  batchSize: 50
});
const retentionForm = ref(defaultRetentionForm());
const retentionReady = ref(false);
type Settings = NonNullable<typeof retentionSettings.value>;
type RetentionReceipt = { sequence: number; version: number; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode?: string };
const retentionReceipt = ref<RetentionReceipt>(); let receiptSequence = 0;
// 版本底线跨页签保留；较旧读取不能重新开放覆盖已确认策略的写入入口。
let authoritySettings: Settings | undefined;
let savingIntent: { receipt: RetentionReceipt } | undefined;
let lastLogQuery: { page: number; pageSize: number; filters: DocumentAccessLogListFilters } | undefined;

const canReadPage = computed(() => viewActive.value && (!route || route.name === 'document-statistics') && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null && session.can('document.host_statistics.read'));
const canReadAccessLogs = computed(() => canReadPage.value && session.can('document.host_access_logs.read'));
const canReadRetention = computed(() => canReadPage.value && session.can('document.host_documents.read'));
const canUpdateRetention = computed(() => canReadRetention.value && session.can('document.host_documents.update'));
// 修订凭据来自最近一次权威读取，表单只负责策略值，不能替换该并发边界。
const validRetentionForm = computed(() => isDocumentVersionRetentionInput({ ...retentionForm.value, version: retentionSettings.value?.version ?? 0 }));

const scope = useAuthorizedViewScope(session, resetPage, () => {
  if (!canReadPage.value) return;
  if ((activeTab.value === 'accessLogs' && !canReadAccessLogs.value) || (activeTab.value === 'retention' && !canReadRetention.value)) {
    activeTab.value = 'statistics';
  } else void loadActiveTab();
});
let statisticsRequest: ReturnType<typeof scope.begin>;
let logsRequest: ReturnType<typeof scope.begin>;
let retentionRequest: ReturnType<typeof scope.begin>;
let saveRequest: ReturnType<typeof scope.begin>;
function cancelSave(): void {
  if (saveRequest?.current() && savingIntent) {
    retentionReceipt.value = { ...savingIntent.receipt, outcome: 'unconfirmed' };
    retentionReady.value = false; retentionSettings.value = null;
  }
  saveRequest?.cancel(); saveRequest = undefined; savingIntent = undefined; retentionSaving.value = false;
}
const policyKey = (value: Settings) => JSON.stringify([value.minimumRetainedVersionsPerItem, value.maximumRetainedHistoryVersions, value.pollSeconds, value.batchSize]);
watch(() => JSON.stringify(retentionForm.value), () => { cancelSave(); owner.value++; }, { flush: 'sync' });
watch(() => route?.name, name => { clearTabData(); if (name === 'document-statistics') void loadActiveTab(); }, { flush: 'sync' });
const controls = computed(() => {
  const ticket = owner.value, tab = activeTab.value, draft = JSON.stringify(accessLogFilters.value), form = JSON.stringify(retentionForm.value), version = retentionSettings.value?.version;
  const owned = () => ticket === owner.value && tab === activeTab.value && canReadPage.value;
  const logOwned = () => owned() && tab === 'accessLogs' && canReadAccessLogs.value && draft === JSON.stringify(accessLogFilters.value);
  const formOwned = () => owned() && tab === 'retention' && canUpdateRetention.value && form === JSON.stringify(retentionForm.value) && version === retentionSettings.value?.version;
  const filterInput = (key: keyof DocumentAccessLogListFilters) => (value: string) => { if (logOwned()) accessLogFilters.value = { ...accessLogFilters.value, [key]: value }; };
  const input = (key: Exclude<keyof Settings, 'version'>) => (value: number | undefined) => { if (formOwned() && !retentionLoading.value && !retentionSaving.value) (retentionForm.value as Record<string, unknown>)[key] = value; };
  return { refresh: () => { if (owned()) void loadActiveTab(); }, tab: (value: string) => { if (owned() && (value === 'statistics' || value === 'accessLogs' && canReadAccessLogs.value || value === 'retention' && canReadRetention.value)) activeTab.value = value as typeof activeTab.value; },
    filter: () => { if (logOwned()) applyAccessLogFilters(); }, submit: (event: Event) => { event.preventDefault(); if (logOwned()) applyAccessLogFilters(); }, reset: () => { if (logOwned()) resetAccessLogFilters(); },
    filters: { documentItemId: filterInput('documentItemId'), accessTypeKey: filterInput('accessTypeKey'), sourceKey: filterInput('sourceKey') },
    currentPage: (value: number) => { if (owned() && tab === 'accessLogs' && canReadAccessLogs.value && validLogPage(value, accessLogsPageSize.value)) accessLogsPage.value = value; },
    pageSize: (value: number) => { if (owned() && tab === 'accessLogs' && canReadAccessLogs.value && validLogPage(accessLogsPage.value, value)) accessLogsPageSize.value = value; },
    page: (nextPage: number, nextSize: number) => { if (!owned() || tab !== 'accessLogs' || !canReadAccessLogs.value || !validLogPage(nextPage, nextSize)) return;
      accessLogsPage.value = lastLogQuery?.pageSize !== nextSize ? 1 : nextPage; accessLogsPageSize.value = nextSize;
      if (lastLogQuery?.page !== accessLogsPage.value || lastLogQuery.pageSize !== nextSize) void loadAccessLogs(); },
    save: () => { if (formOwned()) void saveRetentionSettings(); },
    fields: { minimumRetainedVersionsPerItem: input('minimumRetainedVersionsPerItem'), maximumRetainedHistoryVersions: input('maximumRetainedHistoryVersions'), pollSeconds: input('pollSeconds'), batchSize: input('batchSize') } };
});

/** 页签与账号共用失效边界；离开策略页也清空编辑值，取消不代表服务端写入回滚。 */
function clearTabData() {
  cancelSave(); statisticsRequest?.cancel(); logsRequest?.cancel(); retentionRequest?.cancel(); owner.value++;
  statistics.value = null; accessLogs.value = []; accessLogsTotal.value = 0; retentionSettings.value = null;
  statisticsProblem.value = undefined; accessLogsProblem.value = undefined; retentionProblem.value = undefined;
  statisticsLoading.value = false; accessLogsLoading.value = false; retentionLoading.value = false; retentionSaving.value = false;
  retentionForm.value = defaultRetentionForm();
  retentionReady.value = false; lastLogQuery = undefined;
}

function resetPage() {
  clearTabData(); accessLogsPage.value = 1; accessLogsPageSize.value = 20; appliedAccessLogFilters.value = {};
  accessLogFilters.value = { documentItemId: '', accessTypeKey: '', sourceKey: '' };
  retentionReceipt.value = undefined; authoritySettings = undefined;
}

function loadActiveTab() {
  if (activeTab.value === 'statistics') return loadStatistics();
  if (activeTab.value === 'accessLogs') return loadAccessLogs();
  return loadRetentionSettings();
}

const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(accessLogsLoading);
// COUNT 与日志读取并非快照事务；非空页证明当前页存在，旧总数不能把分页器压回前页。
const logPaginationTotal = computed(() => Math.max(accessLogsTotal.value, accessLogs.value.length ? ((lastLogQuery?.page ?? accessLogsPage.value) - 1) * (lastLogQuery?.pageSize ?? accessLogsPageSize.value) + accessLogs.value.length : 0));

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
  statisticsRequest?.cancel();
  if (!canReadPage.value || activeTab.value !== 'statistics') return;
  const request = scope.begin('document.host_statistics.read'); if (!request) return;
  statisticsRequest = request; statistics.value = null; owner.value++;
  statisticsLoading.value = true;
  statisticsProblem.value = undefined;
  try {
    const result = await getDocumentStatistics(request.signal);
    if (request.current()) statistics.value = result;
  } catch (error) {
    if (!request.current()) return;
    statistics.value = null;
    statisticsProblem.value = toProblem(error, 'documentStatistics.loadFailed');
  } finally {
    if (request.current()) statisticsLoading.value = false;
    request.finish();
  }
}

/** 访问日志按服务端分页拉取，避免把全量日志一次性加载到浏览器。 */
const validLogPage = (page: number, size: number) => Number.isSafeInteger(page) && page > 0 && page <= 2147483647 && [10, 20, 50, 100].includes(size);
async function loadAccessLogs(recoverPage = true): Promise<void> {
  if (!canReadAccessLogs.value || activeTab.value !== 'accessLogs' || !validLogPage(accessLogsPage.value, accessLogsPageSize.value)) {
    return;
  }
  logsRequest?.cancel(); const request = scope.begin('document.host_access_logs.read'); if (!request) return;
  const query = { page: accessLogsPage.value, pageSize: accessLogsPageSize.value, filters: { ...appliedAccessLogFilters.value } };
  const sameQuery = JSON.stringify(query) === JSON.stringify(lastLogQuery); lastLogQuery = query;
  logsRequest = request; owner.value++; if (!sameQuery) { accessLogs.value = []; accessLogsTotal.value = 0; }

  accessLogsLoading.value = true;
  accessLogsProblem.value = undefined;
  try {
    const result = await listDocumentAccessLogs(
      query.page,
      query.pageSize,
      query.filters,
      request.signal
    );
    if (!request.current()) return;
    if (result.page !== query.page || result.pageSize !== query.pageSize || !Number.isSafeInteger(result.total) || result.total < 0 || result.items.length > query.pageSize) throw new Error('client.invalid_document_access_log_page');
    const lastPage = Math.max(1, Math.ceil(result.total / query.pageSize));
    if (recoverPage && result.items.length === 0 && query.page > lastPage) { accessLogsPage.value = lastPage; await loadAccessLogs(false); return; }
    accessLogs.value = result.items;
    accessLogsTotal.value = result.total;
  } catch (error) {
    if (!request.current()) return;
    accessLogsProblem.value = toProblem(error, 'documentStatistics.accessLogs.loadFailed');
  } finally {
    if (request.current()) { accessLogsLoading.value = false; void syncTableLayout(); }
    request.finish();
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
  if (!canReadAccessLogs.value || activeTab.value !== 'accessLogs') return;
  const filters = buildAccessLogFilters();
  if (filters.documentItemId && (!isGuid(filters.documentItemId) || filters.documentItemId === '00000000-0000-0000-0000-000000000000')) {
    accessLogsProblem.value = { status: 400, code: 'client.invalid_document_access_log_query', title: t('documentStatistics.accessLogs.invalidFilters') }; return;
  }
  appliedAccessLogFilters.value = filters;
  accessLogsPage.value = 1;
  void loadAccessLogs();
}

function resetAccessLogFilters(): void {
  if (!canReadAccessLogs.value || activeTab.value !== 'accessLogs') return;
  appliedAccessLogFilters.value = {};
  accessLogFilters.value = { documentItemId: '', accessTypeKey: '', sourceKey: '' };
  accessLogsPage.value = 1;
  void loadAccessLogs();
}

async function loadRetentionSettings() {
  if (!canReadRetention.value || activeTab.value !== 'retention' || retentionSaving.value) return;
  retentionRequest?.cancel();
  const request = scope.begin('document.host_documents.read'); if (!request) return;
  retentionRequest = request; retentionReady.value = false; owner.value++;
  retentionLoading.value = true;
  retentionProblem.value = undefined;
  try {
    const result = await getDocumentVersionRetentionSettings(request.signal);
    if (!request.current()) return;
    if (!Number.isSafeInteger(result.version) || result.version < 0 || authoritySettings && (result.version < authoritySettings.version || result.version === authoritySettings.version && policyKey(result) !== policyKey(authoritySettings))) throw new Error('client.invalid_document_version_retention_settings');
    const preserve = retentionSettings.value?.version === result.version && policyKey(retentionSettings.value) === policyKey(result);
    authoritySettings = { ...result }; retentionSettings.value = result; if (!preserve) retentionForm.value = { ...result }; retentionReady.value = true;
  } catch (error) {
    if (!request.current()) return;
    retentionProblem.value = toProblem(error, 'documentStatistics.retention.loadFailed');
  } finally {
    if (request.current()) retentionLoading.value = false;
    request.finish();
  }
}

async function saveRetentionSettings() {
  if (!canUpdateRetention.value || activeTab.value !== 'retention' || !retentionReady.value || !retentionSettings.value || retentionLoading.value || retentionSaving.value || !validRetentionForm.value) {
    return;
  }
  const snapshot = { ...retentionForm.value, version: retentionSettings.value.version };
  if (!isDocumentVersionRetentionInput(snapshot)) return;
  retentionRequest?.cancel(); const request = scope.begin('document.host_documents.update'); if (!request) return;
  saveRequest = request; owner.value++;
  const attempt = { sequence: ++receiptSequence, version: snapshot.version, outcome: 'processing' as const };
  retentionReceipt.value = attempt; savingIntent = { receipt: attempt };
  retentionSaving.value = true;
  retentionProblem.value = undefined;
  try {
    const result = await updateDocumentVersionRetentionSettings(snapshot, request.signal);
    if (!request.current()) return;
    if (!isDocumentVersionRetentionInput({ ...result, version: snapshot.version }) || result.version !== snapshot.version + 1 || policyKey(result) !== policyKey(snapshot)) throw new Error('client.invalid_document_version_retention_settings');
    retentionReceipt.value = { ...attempt, version: result.version, outcome: 'succeeded' }; authoritySettings = { ...result };
    // 成功先保存写入事实并移交请求归属，随后表单更新不能取消自己的成功处理。
    savingIntent = undefined; saveRequest = undefined; retentionSaving.value = false;
    retentionSettings.value = result; retentionForm.value = { ...result };
    showSuccess(t('documentStatistics.retention.saveSuccess'));
  } catch (error) {
    if (saveRequest !== request || !request.current()) return;
    // 写入可能已提交而回包丢失，旧快照必须失效，重读后再允许整体替换策略。
    retentionSettings.value = null; retentionForm.value = defaultRetentionForm();
    retentionReady.value = false;
    const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
    retentionReceipt.value = { ...attempt, outcome: unknown ? 'unconfirmed' : 'failed', errorCode: toProblem(error, 'documentStatistics.retention.saveFailed').code };
    retentionProblem.value = toProblem(error, 'documentStatistics.retention.saveFailed');
  } finally {
    if (saveRequest === request) { if (request.current()) retentionSaving.value = false; saveRequest = undefined; savingIntent = undefined; }
    request.finish();
  }
}

watch(activeTab, () => { clearTabData(); void loadActiveTab(); }, { flush: 'sync' });
</script>

<template>
  <section
    v-if="canReadPage"
    class="document-statistics-view art-page-stack art-full-height"
    :class="{ 'document-statistics-view--logs': activeTab === 'accessLogs' }"
  >
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentStatistics.title') }}</h1>

    <el-button data-testid="document-statistics-refresh" :disabled="retentionSaving" v-on="{ click: controls.refresh }">{{ t('common.refresh') }}</el-button>
    <div v-if="retentionReceipt" role="status" data-testid="document-retention-receipt">
      <p>{{ t('documentStatistics.retention.receiptVersion', { version: String(retentionReceipt.version) }) }} · {{ t(retentionReceipt.outcome === 'succeeded' ? 'documentStatistics.retention.resultSucceeded' : retentionReceipt.outcome === 'failed' ? 'documentStatistics.retention.resultFailed' : retentionReceipt.outcome === 'processing' ? 'documentStatistics.retention.resultProcessing' : 'documentStatistics.retention.resultUnconfirmed') }}</p>
      <code v-if="retentionReceipt.errorCode" translate="no">{{ retentionReceipt.errorCode }}</code>
      <p v-if="retentionReceipt.outcome === 'unconfirmed'">{{ t('documentStatistics.retention.recoveryHint') }}</p>
    </div>

    <el-tabs :model-value="activeTab" data-testid="document-statistics-tabs" v-on="{ 'update:modelValue': controls.tab }">
      <el-tab-pane :label="t('documentStatistics.tabs.statistics')" name="statistics" />
      <el-tab-pane
        v-if="canReadAccessLogs"
        :label="t('documentStatistics.tabs.accessLogs')"
        name="accessLogs"
      />
      <el-tab-pane v-if="canReadRetention" :label="t('documentStatistics.tabs.retention')" name="retention" />
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
        <el-form inline label-width="96px" v-on="{ submit: controls.submit }">
          <el-form-item :label="t('documentStatistics.accessLogs.documentId')">
            <el-input
              :model-value="accessLogFilters.documentItemId"
              v-on="{ 'update:modelValue': controls.filters.documentItemId }"
              clearable
              :placeholder="t('documentStatistics.accessLogs.documentIdPlaceholder')"
            />
          </el-form-item>
          <el-form-item :label="t('documentStatistics.accessLogs.accessType')">
            <el-select :model-value="accessLogFilters.accessTypeKey" clearable v-on="{ 'update:modelValue': controls.filters.accessTypeKey }">
              <el-option :label="t('documentStatistics.accessLogs.accessType.download')" value="download" />
              <el-option :label="t('documentStatistics.accessLogs.accessType.preview')" value="preview" />
              <el-option
                :label="t('documentStatistics.accessLogs.accessType.share_access')"
                value="share_access"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="t('documentStatistics.accessLogs.source')">
            <el-select :model-value="accessLogFilters.sourceKey" clearable v-on="{ 'update:modelValue': controls.filters.sourceKey }">
              <el-option
                :label="t('documentStatistics.accessLogs.source.authenticated')"
                value="authenticated"
              />
              <el-option :label="t('documentStatistics.accessLogs.source.share')" value="share" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" data-testid="document-access-logs-filter" v-on="{ click: controls.filter }">{{ t('documentStatistics.accessLogs.filter') }}</el-button>
            <el-button v-on="{ click: controls.reset }">{{ t('documentStatistics.accessLogs.resetFilters') }}</el-button>
          </el-form-item>
        </el-form>
      </el-card>

      <el-alert
        v-if="accessLogsProblem"
        data-testid="document-access-logs-problem"
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
            :current-page="accessLogsPage"
            :page-size="accessLogsPageSize"
            :page-sizes="[10, 20, 50, 100]"
            class="art-table-pagination center custom-pagination"
            layout="total, sizes, prev, pager, next"
            :total="logPaginationTotal"
            v-on="{ 'update:current-page': controls.currentPage, 'update:page-size': controls.pageSize, change: controls.page }"
          />
        </div>
      </el-card>
    </template>

    <template v-else-if="activeTab === 'retention' && canReadRetention">
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
            :disabled="retentionSaving || retentionLoading"
            @submit.prevent
          >
            <el-form-item :label="t('documentStatistics.retention.minimumRetainedVersions')" required>
              <el-input-number :model-value="retentionForm.minimumRetainedVersionsPerItem" :min="1" :max="1000" :precision="0" v-on="{ 'update:modelValue': controls.fields.minimumRetainedVersionsPerItem }" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.maximumHistoryVersions')" required>
              <el-input-number :model-value="retentionForm.maximumRetainedHistoryVersions" :min="0" :max="10000" :precision="0" v-on="{ 'update:modelValue': controls.fields.maximumRetainedHistoryVersions }" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.pollSeconds')" required>
              <el-input-number :model-value="retentionForm.pollSeconds" :min="60" :max="86400" :precision="0" v-on="{ 'update:modelValue': controls.fields.pollSeconds }" />
            </el-form-item>
            <el-form-item :label="t('documentStatistics.retention.batchSize')" required>
              <el-input-number :model-value="retentionForm.batchSize" :min="1" :max="1000" :precision="0" v-on="{ 'update:modelValue': controls.fields.batchSize }" />
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                data-testid="document-version-retention-save"
                :loading="retentionSaving"
                :disabled="!validRetentionForm || !retentionReady || retentionLoading"
                v-on="{ click: controls.save }"
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
