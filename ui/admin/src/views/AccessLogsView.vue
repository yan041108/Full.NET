<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { ElCard, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
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
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
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

const filteredItems = computed(() => items.value);
const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

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

/** 页面只保留一份访问日志快照，刷新时重新按游标拉全量数据并重算表格高度。 */
async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    items.value = await fetchAllLogs(activeQuery.value);
    resetPage();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

/** 服务端使用 cursor 翻页；前端需要按顺序拼接全部页，避免本地筛选只看到首屏数据。 */
async function fetchAllLogs(query: AuditingAccessLogQuery): Promise<AuditingAccessLog[]> {
  const collected: AuditingAccessLog[] = [];
  let cursor: string | null | undefined;
  let hasMore = true;

  while (hasMore) {
    const options = {
      ...query,
      ...(cursor ? { cursor } : {})
    };
    const pageResult = Object.keys(options).length === 0
      ? await listAuditingAccessLogsByCursor()
      : await listAuditingAccessLogsByCursor(options);
    collected.push(...pageResult.items);
    cursor = pageResult.nextCursor;
    hasMore = pageResult.hasMore;
  }

  return collected;
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
</script>

<template>
  <section class="access-logs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('accessLogs.title') }}</h1>

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

            <template #empty>{{ t('accessLogs.emptyDirectory') }}</template>
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
.access-logs-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.access-logs-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
</style>
