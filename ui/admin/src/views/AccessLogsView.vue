<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type {
  AuditingAccessLog,
  AuditingAccessLogQuery,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  applyAuditingAccessLogContainsDefaults,
  isFullNetProblemDetails
} from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import AuditLogDetailDrawer, {
  type AuditLogDetailRecord
} from './components/AuditLogDetailDrawer.vue';
import AuditLogExportDialog from './components/AuditLogExportDialog.vue';
import AuditLogTrendPanel from './components/AuditLogTrendPanel.vue';
import {
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { listAuditingAccessLogsByCursor } from '../api/access-logs';

defineOptions({ name: 'AccessLogsView' });

const { t } = useAdminI18n();
const items = ref<AuditingAccessLog[]>([]);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const activeQuery = ref<AuditingAccessLogQuery>({});
const containsDefaultRangeApplied = ref(false);
const applyingVisibleDefaults = ref(false);
const detailOpen = ref(false);
const selectedRecord = ref<AuditLogDetailRecord | null>(null);
const exportOpen = ref(false);

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

const page = ref(1);
const pageSize = ref(20);
const pagedItems = computed(() => items.value);
const currentCursor = ref<string>();
const nextCursor = ref<string>();
const previousCursors: Array<{ cursor: string | undefined; page: number }> = [];
const canPrevious = ref(false);
let loadVersion = 0;
let readController: AbortController | undefined;
onUnmounted(() => { loadVersion += 1; readController?.abort(); });


const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'pathContains',
    label: t('accessLogs.pathContains'),
    placeholder: t('accessLogs.pathContains')
  },
  {
    key: 'fromUtc',
    label: t('accessLogs.fromUtc'),
    placeholder: t('accessLogs.fromUtc')
  },
  {
    key: 'toUtc',
    label: t('accessLogs.toUtc'),
    placeholder: t('accessLogs.toUtc')
  }
]);

watchLoading(loading);

watch(
  searchForm,
  form => {
    handlePathContainsInput(form.pathContains ?? '');
  },
  { deep: true }
);

onMounted(() => {
  void load();
});

/** 让客户端分页序号与当前页绑定，避免切页后序号重新从 1 开始。 */
function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

/** 每次只读取用户请求的一页；最多保留 100 个不透明回退游标，不保留历史行。 */
async function load(): Promise<void> {
  currentCursor.value = undefined; nextCursor.value = undefined;
  previousCursors.length = 0; canPrevious.value = false; page.value = 1;
  await loadCursor();
}

async function loadCursor(): Promise<void> {
  const version = ++loadVersion;
  readController?.abort();
  const controller = new AbortController(); readController = controller;
  loading.value = true; problem.value = undefined;
  try {
    const result = await listAuditingAccessLogsByCursor({
      ...activeQuery.value, cursor: currentCursor.value, limit: pageSize.value
    }, controller.signal);
    if (version !== loadVersion) return;
    items.value = result.items; nextCursor.value = result.hasMore ? result.nextCursor ?? undefined : undefined;
    canPrevious.value = previousCursors.length > 0;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    if (version === loadVersion && !controller.signal.aborted) problem.value = toProblem(error);
  } finally { if (version === loadVersion) loading.value = false; }
}
async function nextPage(): Promise<void> {
  if (loading.value || !nextCursor.value) return;
  previousCursors.push({ cursor: currentCursor.value, page: page.value });
  if (previousCursors.length > 100) previousCursors.shift();
  currentCursor.value = nextCursor.value; page.value += 1; await loadCursor();
}
async function previousPage(): Promise<void> {
  if (loading.value) return;
  const previous = previousCursors.pop(); if (!previous) return;
  currentCursor.value = previous.cursor; page.value = previous.page; await loadCursor();
}

/** 搜索动作先把表单值冻结为当前查询快照，再统一触发重新加载。 */
function handleSearch(params: Record<string, string | undefined>): void {
  pathContains.value = params.pathContains ?? '';
  activeQuery.value = buildQuery();
  void load();
}

/** 重置不仅清空输入框，也要撤销 contains 默认时间窗的可见状态。 */
function resetSearch(): void {
  pathContains.value = '';
  fromUtcInput.value = '';
  toUtcInput.value = '';
  containsDefaultRangeApplied.value = false;
  activeQuery.value = {};
  void load();
}

const pathContains = computed({
  get: () => searchForm.value.pathContains ?? '',
  set: value => {
    searchForm.value.pathContains = value || undefined;
  }
});

const fromUtcInput = computed({
  get: () => searchForm.value.fromUtc ?? '',
  set: value => {
    searchForm.value.fromUtc = value || undefined;
  }
});

const toUtcInput = computed({
  get: () => searchForm.value.toUtc ?? '',
  set: value => {
    searchForm.value.toUtc = value || undefined;
  }
});

/** contains 搜索默认补 24 小时时窗，并把隐式默认值同步回可见输入框，避免筛选条件对用户不可见。 */
function handlePathContainsInput(value: string): void {
  if (!value.trim()) {
    if (containsDefaultRangeApplied.value) {
      fromUtcInput.value = '';
      toUtcInput.value = '';
    }
    containsDefaultRangeApplied.value = false;
    return;
  }

  const hadNoTimeRange = !fromUtcInput.value && !toUtcInput.value;
  const query = applyAuditingAccessLogContainsDefaults({
    pathContains: value,
    fromUtc: toUtcIso(fromUtcInput.value),
    toUtc: toUtcIso(toUtcInput.value)
  });
  applyVisibleDefaults(query);
  if (hadNoTimeRange && query.fromUtc && query.toUtc) {
    containsDefaultRangeApplied.value = true;
  }
}

/** 只要用户手动改过时间范围，就撤销 contains 默认时间窗的自动管理。 */
function markTimeRangeEdited(): void {
  containsDefaultRangeApplied.value = false;
}

watch([fromUtcInput, toUtcInput], () => {
  if (!applyingVisibleDefaults.value) {
    markTimeRangeEdited();
  }
});

/** 查询构建统一走生成契约默认值逻辑，保证手动搜索与自动补默认值的语义一致。 */
function buildQuery(): AuditingAccessLogQuery {
  const query = applyAuditingAccessLogContainsDefaults({
    pathContains: pathContains.value,
    fromUtc: toUtcIso(fromUtcInput.value),
    toUtc: toUtcIso(toUtcInput.value)
  });
  applyVisibleDefaults(query);
  return query;
}

/** 把内部 UTC 默认值投影回 datetime-local 输入框，防止界面显示与真实查询参数漂移。 */
function applyVisibleDefaults(query: AuditingAccessLogQuery): void {
  applyingVisibleDefaults.value = true;
  if (query.fromUtc && !fromUtcInput.value) {
    fromUtcInput.value = toDateTimeLocal(query.fromUtc);
  }
  if (query.toUtc && !toUtcInput.value) {
    toUtcInput.value = toDateTimeLocal(query.toUtc);
  }
  applyingVisibleDefaults.value = false;
}

/** datetime-local 输入按本地时区解析，提交前统一折叠为 ISO UTC 字符串。 */
function toUtcIso(value: string): string | undefined {
  if (!value) {
    return undefined;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

/** 将服务端 UTC 字符串转换回本地 datetime-local 值，供默认时间窗可视化。 */
function toDateTimeLocal(value: string): string {
  const parsed = new Date(value);
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

/** 未知异常统一折叠为访问日志页自己的稳定错误码，避免空态和告警分支各自兜底。 */
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.auditing_access_log_failed',
        title: t('accessLogs.loadFailed')
      };
}

function openDetail(row: AuditingAccessLog): void {
  selectedRecord.value = {
    id: row.id,
    occurredAtUtc: row.occurredAtUtc,
    traceId: row.traceId,
    title: `${row.httpMethod} ${row.requestPath}`,
    subtitle: row.occurredAtUtc,
    fields: [
      { label: t('accessLogs.httpMethod'), value: row.httpMethod },
      { label: t('accessLogs.requestPath'), value: row.requestPath },
      { label: t('accessLogs.statusCode'), value: row.statusCode },
      { label: t('accessLogs.durationMs'), value: row.durationMs },
      { label: t('accessLogs.occurredAt'), value: row.occurredAtUtc },
      {
        label: t('users.status'),
        value: t(row.isAuthenticated ? 'accessLogs.authenticated' : 'accessLogs.anonymous')
      },
      { label: 'TraceId', value: row.traceId }
    ]
  };
  detailOpen.value = true;
}
</script>

<template>
  <section class="access-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('accessLogs.title') }}</h1>

    <AuditLogTrendPanel kind="access" />

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('accessLogs.query')"
      :reset-label="t('accessLogs.reset')"
      :expand-label="t('accessLogs.expand')"
      :collapse-label="t('accessLogs.collapse')"
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
        >
          <template #left>
            <PermissionGate code="auditing.access.export">
              <el-button data-testid="access-logs-action-export" @click="exportOpen = true">
                {{ t('auditExport.action') }}
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
            @row-click="openDetail"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('accessLogs.requestPath')" min-width="240">
              <template #default="{ row }">
                <div>
                  <div translate="no">{{ row.httpMethod }} {{ row.requestPath }}</div>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('accessLogs.statusCode')" width="100" align="center" prop="statusCode" />

            <el-table-column :label="t('accessLogs.durationMs')" width="120" align="center" prop="durationMs" />

            <el-table-column :label="t('accessLogs.occurredAt')" min-width="180" prop="occurredAtUtc" />

            <el-table-column :label="t('users.status')" width="100" align="center">
              <template #default="{ row }">
                <el-tag effect="plain">
                  {{ t(row.isAuthenticated ? 'accessLogs.authenticated' : 'accessLogs.anonymous') }}
                </el-tag>
              </template>
            </el-table-column>

            <!-- @vue-generic {AuditingAccessLog} -->
          <el-table-column :label="t('auditAnalytics.viewDetail')" width="120" align="center">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row)">
                  {{ t('auditAnalytics.viewDetail') }}
                </el-button>
              </template>
            </el-table-column>

            <template #empty>{{ t('accessLogs.emptyDirectory') }}</template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <ElButton :disabled="loading || !canPrevious" @click="previousPage">{{ t('common.previousPage') }}</ElButton>
            <ElButton :disabled="loading || !nextCursor" @click="nextPage">{{ t('common.nextPage') }}</ElButton>
          </div>
        </div>
      </div>
    </el-card>

    <AuditLogDetailDrawer v-model="detailOpen" :record="selectedRecord" />
    <AuditLogExportDialog v-model:open="exportOpen" kind="access" />
  </section>
</template>

<style scoped>
.access-logs-view {
  /* 趋势与筛选挤满小视口时整页滚动，表格仍保留可操作高度。 */
  overflow-y: auto;
}

.access-logs-view > :deep(.audit-log-trend-panel),
.access-logs-view > :deep(.art-search-bar) {
  flex-shrink: 0;
}

.access-logs-view :deep(.art-table-card) {
  flex: 1 0 360px;
  display: flex;
  flex-direction: column;
  min-height: 360px;
}

.access-logs-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
