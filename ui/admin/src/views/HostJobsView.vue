<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDrawer,
  // 中文注释：Element Plus 2.14.3 公开的抽屉组件 props 类型名为 DrawerProps（不带 El 前缀）
  type DrawerProps,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  HostJobDefinition,
  HostJobExecution,
  HostJobGroup
} from '@fullnet/client-contracts';
import {
  isFullNetProblemDetails,
  JOB_HANDLER_KINDS,
  type HttpJobArgs
} from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  clearHostJobExecutions,
  createHostJobDefinition,
  deleteHostJobDefinition,
  disableHostJobDefinition,
  listHostJobDefinitions,
  listHostJobExecutions,
  listHostJobGroups,
  triggerHostJobDefinition,
  updateHostJobDefinition
} from '../api/host-jobs';

defineOptions({ name: 'HostJobsView' });

type ColumnKey = 'description' | 'createdAt' | 'groupName';

interface AppliedFilters {
  jobKey: string;
  displayName: string;
  status: '' | 'enabled' | 'disabled';
  groupName: string;
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();

// 主列表数据
const allDefinitions = ref<HostJobDefinition[]>([]);
const jobGroups = ref<HostJobGroup[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();

// 搜索与列可见性
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ jobKey: '', displayName: '', status: '', groupName: '' });
const columnVisibility = ref<Record<ColumnKey, boolean>>({
  description: true,
  createdAt: true,
  groupName: true
});

// 编辑器状态
const editorOpen = ref(false);
const editorMode = ref<'create' | 'edit'>('create');
const editingDefinition = ref<HostJobDefinition | null>(null);
const editorFormRef = ref<FormInstance>();
// 中文注释：显式声明 editorForm.jobKey 为 string，避免因直接赋值 JOBS_WELL_KNOWN_KEYS.ping 被收窄为字面量 'jobs.ping'
// 导致 openEdit 中 item.jobKey（string）无法赋值
const editorForm = reactive<{
  jobKey: string;
  handlerKind: string;
  displayName: string;
  description: string;
  groupName: string;
  allowConcurrentExecutions: boolean;
  httpUrl: string;
  httpMethod: string;
  httpHeaders: Array<{ name: string; value: string }>;
  secretHeaders: Array<{ name: string; configKey: string }>;
}>({
  jobKey: '',
  handlerKind: JOB_HANDLER_KINDS.ping,
  displayName: '',
  description: '',
  groupName: '',
  allowConcurrentExecutions: false,
  httpUrl: '',
  httpMethod: 'GET',
  httpHeaders: [],
  secretHeaders: []
});
const fieldErrors = reactive({
  displayName: '',
  description: ''
});

// 执行记录抽屉
const recordsDrawerOpen = ref(false);
const recordsLoading = ref(false);
const recordsDefinitionId = ref('');
const recordsJobKey = ref('');
const recordsJobDisplayName = ref('');
const executions = ref<HostJobExecution[]>([]);
const executionsPage = ref(1);
const executionsPageSize = ref(20);
const executionsTotal = ref(0);
const recordsProblem = ref<FullNetProblemDetails>();

const canCreate = computed(() => session.can('jobs.definitions.create'));
const canUpdate = computed(() => session.can('jobs.definitions.update'));
const canDisable = computed(() => session.can('jobs.definitions.disable'));
const canDelete = computed(() => session.can('jobs.definitions.delete'));
const canTrigger = computed(() => session.can('jobs.definitions.trigger'));
const canReadExecutions = computed(() => session.can('jobs.executions.read'));
const canClearExecutions = computed(() => session.can('jobs.executions.clear'));

// 布局
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

// 过滤后的数据
const filteredDefinitions = computed(() => {
  let rows = allDefinitions.value;
  const filters = appliedFilters.value;

  if (filters.jobKey.trim()) {
    const keyword = filters.jobKey.trim().toLowerCase();
    rows = rows.filter(d => d.jobKey.toLowerCase().includes(keyword));
  }

  if (filters.displayName.trim()) {
    const keyword = filters.displayName.trim().toLowerCase();
    rows = rows.filter(d => d.displayName.toLowerCase().includes(keyword));
  }

  if (filters.status === 'enabled') {
    rows = rows.filter(d => d.isEnabled);
  } else if (filters.status === 'disabled') {
    rows = rows.filter(d => !d.isEnabled);
  }

  if (filters.groupName.trim()) {
    const group = filters.groupName.trim();
    rows = rows.filter(d => d.groupName === group);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedDefinitions, resetPage } = useArtClientPagination(filteredDefinitions);

// 表格列
const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'groupName', label: t('hostJobs.columnGroupName'), visible: columnVisibility.value.groupName },
    { key: 'description', label: t('hostJobs.columnDescription'), visible: columnVisibility.value.description },
    { key: 'createdAt', label: t('hostJobs.columnCreatedAt'), visible: columnVisibility.value.createdAt }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as ColumnKey] = column.visible !== false;
      }
    }
  }
});

// 搜索项
const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'displayName',
    label: t('hostJobs.columnDisplayName'),
    placeholder: t('hostJobs.searchDisplayNamePlaceholder')
  },
  {
    key: 'jobKey',
    label: t('hostJobs.columnJobKey'),
    placeholder: t('hostJobs.searchJobKeyPlaceholder')
  },
  {
    key: 'groupName',
    label: t('hostJobs.columnGroupName'),
    type: 'select',
    placeholder: t('hostJobs.searchGroupNamePlaceholder'),
    options: jobGroups.value.map(g => ({ label: g.groupName, value: g.groupName }))
  },
  {
    key: 'status',
    label: t('hostJobs.columnStatus'),
    type: 'select',
    placeholder: t('hostJobs.searchStatusPlaceholder'),
    options: [
      { label: t('hostJobs.statusEnabled'), value: 'enabled' },
      { label: t('hostJobs.statusDisabled'), value: 'disabled' }
    ]
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: ColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function statusTagType(status: HostJobExecution['status']): 'info' | 'warning' | 'success' | 'danger' {
  switch (status) {
    case 'pending': return 'info';
    case 'running': return 'warning';
    case 'succeeded': return 'success';
    case 'failed': return 'danger';
  }
}

function statusLabel(status: HostJobExecution['status']): string {
  switch (status) {
    case 'pending': return t('hostJobs.columnStatusPending');
    case 'running': return t('hostJobs.columnStatusRunning');
    case 'succeeded': return t('hostJobs.columnStatusSucceeded');
    case 'failed': return t('hostJobs.columnStatusFailed');
  }
}

function calculateDuration(start: string | null | undefined, end: string | null | undefined): string {
  if (!start || !end) {
    return '—';
  }
  const ms = new Date(end).getTime() - new Date(start).getTime();
  if (ms < 0) return '—';
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function clearFieldErrors(): void {
  fieldErrors.displayName = '';
  fieldErrors.description = '';
}

function resetHttpFields(): void {
  editorForm.httpUrl = '';
  editorForm.httpMethod = 'GET';
  editorForm.httpHeaders = [];
  editorForm.secretHeaders = [];
}

function addHttpHeader(): void {
  editorForm.httpHeaders.push({ name: '', value: '' });
}

function removeHttpHeader(index: number): void {
  editorForm.httpHeaders.splice(index, 1);
}

function addSecretHeader(): void {
  editorForm.secretHeaders.push({ name: '', configKey: '' });
}

function removeSecretHeader(index: number): void {
  editorForm.secretHeaders.splice(index, 1);
}

function buildHttpArgs(): HttpJobArgs | null {
  if (editorForm.handlerKind !== JOB_HANDLER_KINDS.http) {
    return null;
  }
  const headers = Object.fromEntries(
    editorForm.httpHeaders
      .filter(row => row.name.trim() && row.value.trim())
      .map(row => [row.name.trim(), row.value.trim()])
  );
  const secretHeaders = Object.fromEntries(
    editorForm.secretHeaders
      .filter(row => row.name.trim() && row.configKey.trim())
      .map(row => [row.name.trim(), { configKey: row.configKey.trim().toLowerCase() }])
  );
  return {
    url: editorForm.httpUrl.trim(),
    method: editorForm.httpMethod,
    headers: Object.keys(headers).length > 0 ? headers : null,
    secretHeaders: Object.keys(secretHeaders).length > 0 ? secretHeaders : null
  };
}

function loadHttpArgs(args: HttpJobArgs | null | undefined): void {
  resetHttpFields();
  if (!args) {
    return;
  }
  editorForm.httpUrl = args.url ?? '';
  editorForm.httpMethod = args.method ?? 'GET';
  editorForm.httpHeaders = Object.entries(args.headers ?? {}).map(([name, value]) => ({
    name,
    value
  }));
  editorForm.secretHeaders = Object.entries(args.secretHeaders ?? {}).map(([name, ref]) => ({
    name,
    configKey: ref.configKey
  }));
}

function validateDisplayName(): string {
  const name = editorForm.displayName.trim();
  if (!name) {
    return t('configEntries.displayNameRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.displayName = validateDisplayName();
  return !fieldErrors.displayName;
}

async function fetchAllDefinitions(): Promise<HostJobDefinition[]> {
  const pageLimit = 100;
  const firstPage = await listHostJobDefinitions(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostJobDefinitions(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [definitions, groups] = await Promise.all([
      fetchAllDefinitions(),
      listHostJobGroups().catch(() => [] as HostJobGroup[])
    ]);
    allDefinitions.value = definitions;
    jobGroups.value = groups;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    jobKey: params.jobKey ?? '',
    displayName: params.displayName ?? '',
    status: (params.status as AppliedFilters['status']) ?? '',
    groupName: params.groupName ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { jobKey: '', displayName: '', status: '', groupName: '' };
  resetPage();
}

// 打开创建表单
function openCreate(): void {
  editorMode.value = 'create';
  editingDefinition.value = null;
  editorForm.jobKey = '';
  editorForm.handlerKind = JOB_HANDLER_KINDS.ping;
  editorForm.displayName = '';
  editorForm.description = '';
  editorForm.groupName = '';
  editorForm.allowConcurrentExecutions = false;
  resetHttpFields();
  clearFieldErrors();
  editorOpen.value = true;
}

// 打开编辑表单
function openEdit(item: HostJobDefinition): void {
  if (changing.value || !item.isEnabled) {
    return;
  }
  editorMode.value = 'edit';
  editingDefinition.value = item;
  editorForm.jobKey = item.jobKey;
  editorForm.handlerKind = item.handlerKind;
  editorForm.displayName = item.displayName;
  editorForm.description = item.description ?? '';
  editorForm.groupName = item.groupName ?? '';
  editorForm.allowConcurrentExecutions = item.allowConcurrentExecutions;
  loadHttpArgs(item.args);
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  editorForm.displayName = editorForm.displayName.trim();
  if (!applyFieldErrors()) {
    return;
  }
  if (editorMode.value === 'create') {
    await create();
  } else {
    await saveEdit();
  }
}

async function create(): Promise<void> {
  if (!canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostJobDefinition(
      editorForm.jobKey.trim(),
      editorForm.handlerKind,
      editorForm.displayName,
      buildHttpArgs(),
      editorForm.description.trim() || undefined,
      editorForm.groupName.trim() || null,
      editorForm.allowConcurrentExecutions
    );
    editorOpen.value = false;
    ElMessage.success(t('hostJobs.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
    // 任务键冲突时刷新列表，避免库中已有定义但页面仍显示空表。
    if (isFullNetProblemDetails(error) && error.code === 'jobs.definition_job_key_exists') {
      await load();
    }
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const item = editingDefinition.value;
  if (!canUpdate.value || !item) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostJobDefinition(
      item.id,
      editorForm.displayName.trim(),
      editorForm.description.trim() || null,
      editorForm.handlerKind,
      buildHttpArgs(),
      item.version,
      editorForm.groupName.trim() || null,
      editorForm.allowConcurrentExecutions
    );
    editorOpen.value = false;
    ElMessage.success(t('hostJobs.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function deleteDefinition(item: HostJobDefinition): Promise<void> {
  if (changing.value || item.isEnabled || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostJobs.confirmDelete', { name: item.displayName }),
      t('hostJobs.delete'),
      {
        type: 'warning',
        confirmButtonText: t('hostJobs.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await deleteHostJobDefinition(item.id, item.version);
    ElMessage.success(t('hostJobs.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error !== 'cancel' && error !== 'close') {
      problem.value = toProblem(error, 'hostJobs.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

async function trigger(item: HostJobDefinition): Promise<void> {
  if (changing.value || !item.isEnabled || !canTrigger.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const execution = await triggerHostJobDefinition(item.id);
    ElMessage.success(t('hostJobs.triggerSuccess'));
    if (execution.status !== 'succeeded') {
      ElMessage.warning(t('hostJobs.triggerFinishedWithStatus', { status: execution.status }));
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(item: HostJobDefinition): Promise<void> {
  if (changing.value || !item.isEnabled || !canDisable.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostJobs.confirmDisable', { name: item.displayName }),
      t('hostJobs.disable'),
      {
        type: 'warning',
        confirmButtonText: t('hostJobs.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await disableHostJobDefinition(item.id, item.version);
    ElMessage.success(t('hostJobs.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error !== 'cancel' && error !== 'close') {
      problem.value = toProblem(error, 'hostJobs.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

// 执行记录抽屉
async function openExecutions(item: HostJobDefinition): Promise<void> {
  recordsDefinitionId.value = item.id;
  recordsJobKey.value = item.jobKey;
  recordsJobDisplayName.value = item.displayName;
  executionsPage.value = 1;
  recordsDrawerOpen.value = true;
  await loadExecutions(item.id);
}

async function loadExecutions(definitionId: string): Promise<void> {
  if (!canReadExecutions.value) {
    return;
  }
  recordsLoading.value = true;
  recordsProblem.value = undefined;
  try {
    const result = await listHostJobExecutions({
      jobDefinitionId: definitionId,
      page: executionsPage.value,
      pageSize: executionsPageSize.value
    });
    executions.value = result.items;
    executionsTotal.value = result.total;
  } catch (error: unknown) {
    recordsProblem.value = toProblem(error, 'hostJobs.loadFailed');
  } finally {
    recordsLoading.value = false;
  }
}

async function onExecutionsPageChange(page: number): Promise<void> {
  executionsPage.value = page;
  if (recordsDefinitionId.value) {
    await loadExecutions(recordsDefinitionId.value);
  }
}

async function clearExecutions(): Promise<void> {
  if (!canClearExecutions.value || !recordsDefinitionId.value || changing.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostJobs.confirmClearExecutions'),
      t('hostJobs.clearExecutions'),
      {
        type: 'warning',
        confirmButtonText: t('hostJobs.clearExecutions'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    recordsProblem.value = undefined;
    await clearHostJobExecutions(recordsDefinitionId.value);
    ElMessage.success(t('hostJobs.clearExecutionsSuccess'));
    await loadExecutions(recordsDefinitionId.value);
  } catch (error: unknown) {
    if (error !== 'cancel' && error !== 'close') {
      recordsProblem.value = toProblem(error, 'hostJobs.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostJobs.loadFailed' | 'hostJobs.operationFailed' = 'hostJobs.operationFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return {
    type: 'about:blank',
    title: t(fallbackKey),
    status: 500,
    code: 'client.unexpected_error'
  };
}
</script>

<template>
  <section class="host-jobs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostJobs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('configEntries.query')"
      :reset-label="t('configEntries.reset')"
      :expand-label="t('configEntries.expand')"
      :collapse-label="t('configEntries.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:columns="tableColumns"
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,columns,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="jobs.definitions.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="host-jobs-action-create"
                @click="openCreate"
              >
                {{ t('hostJobs.create') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedDefinitions.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedDefinitions"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <!-- 序号 -->
            <el-table-column :label="t('hostJobs.columnIndex')" width="64" align="center" fixed="left">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <!-- 任务键 -->
            <el-table-column
              :label="t('hostJobs.columnJobKey')"
              min-width="180"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.jobKey }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.columnHandlerKind')"
              width="100"
              align="center"
              header-align="center"
            >
              <template #default="{ row }">
                <el-tag size="small" effect="plain" translate="no">{{ row.handlerKind }}</el-tag>
              </template>
            </el-table-column>

            <!-- 显示名称 -->
            <el-table-column
              :label="t('hostJobs.columnDisplayName')"
              min-width="160"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.displayName }}</span>
              </template>
            </el-table-column>

            <!-- 作业分组 -->
            <el-table-column
              v-if="isColumnVisible('groupName')"
              :label="t('hostJobs.columnGroupName')"
              min-width="120"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <el-tag v-if="row.groupName" type="info" effect="plain" translate="no">
                  {{ row.groupName }}
                </el-tag>
                <span v-else>—</span>
              </template>
            </el-table-column>

            <!-- 描述 -->
            <el-table-column
              v-if="isColumnVisible('description')"
              :label="t('hostJobs.columnDescription')"
              min-width="200"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.description ?? '—' }}</span>
              </template>
            </el-table-column>

            <!-- 状态 -->
            <el-table-column
              :label="t('hostJobs.columnStatus')"
              width="100"
              align="center"
              header-align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isEnabled ? 'success' : 'info'" effect="light">
                  {{ t(row.isEnabled ? 'hostJobs.statusEnabled' : 'hostJobs.statusDisabled') }}
                </el-tag>
              </template>
            </el-table-column>

            <!-- 创建时间 -->
            <el-table-column
              v-if="isColumnVisible('createdAt')"
              :label="t('hostJobs.columnCreatedAt')"
              min-width="160"
              align="center"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.createdAtUtc) }}</span>
              </template>
            </el-table-column>

            <!-- 操作 -->
            <el-table-column
              :label="t('users.columnActions')"
              width="200"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate v-if="canReadExecutions" code="jobs.executions.read">
                    <ArtTableActionButton
                      type="view"
                      test-id="host-jobs-action-records"
                      :title="t('hostJobs.viewRecords')"
                      :disabled="changing"
                      @click="openExecutions(row as HostJobDefinition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="canTrigger && row.isEnabled" code="jobs.definitions.trigger">
                    <ArtTableActionButton
                      type="edit"
                      test-id="host-jobs-action-trigger"
                      :title="t('hostJobs.trigger')"
                      :disabled="changing"
                      @click="trigger(row as HostJobDefinition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="canUpdate" code="jobs.definitions.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="host-jobs-action-edit"
                      :title="t('hostJobs.edit')"
                      :disabled="changing || !row.isEnabled"
                      @click="openEdit(row as HostJobDefinition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isEnabled && canDisable" code="jobs.definitions.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="host-jobs-action-disable"
                      :title="t('hostJobs.disable')"
                      :disabled="changing"
                      @click="disable(row as HostJobDefinition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="!row.isEnabled && canDelete" code="jobs.definitions.delete">
                    <ArtTableActionButton
                      type="delete"
                      test-id="host-jobs-action-delete"
                      :title="t('hostJobs.delete')"
                      :disabled="changing"
                      @click="deleteDefinition(row as HostJobDefinition)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('hostJobs.emptyList') }}</template>
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

    <!-- 编辑器弹窗 -->
    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('hostJobs.createTitle') : t('hostJobs.editTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="host-jobs-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="host-jobs-editor-form"
        :model="editorForm"
        label-width="120px"
        class="host-jobs-editor-form"
      >
        <!-- 任务键（创建时可输入） -->
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('hostJobs.fieldJobKey')"
        >
          <el-input
            v-model="editorForm.jobKey"
            :placeholder="t('hostJobs.fieldJobKeyPlaceholder')"
            :disabled="changing"
          />
        </el-form-item>
        <el-form-item v-else :label="t('hostJobs.fieldJobKey')">
          <el-input v-model="editorForm.jobKey" disabled />
        </el-form-item>

        <el-form-item :label="t('hostJobs.fieldHandlerKind')">
          <el-select v-model="editorForm.handlerKind" :disabled="changing" style="width: 100%">
            <el-option :label="t('hostJobs.handlerKindPing')" :value="JOB_HANDLER_KINDS.ping" />
            <el-option :label="t('hostJobs.handlerKindHttp')" :value="JOB_HANDLER_KINDS.http" />
          </el-select>
        </el-form-item>

        <template v-if="editorForm.handlerKind === JOB_HANDLER_KINDS.http">
          <el-form-item :label="t('hostJobs.fieldHttpUrl')" required>
            <el-input v-model="editorForm.httpUrl" :disabled="changing" />
          </el-form-item>
          <el-form-item :label="t('hostJobs.fieldHttpMethod')" required>
            <el-select v-model="editorForm.httpMethod" :disabled="changing" style="width: 100%">
              <el-option v-for="method in ['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE']" :key="method" :label="method" :value="method" />
            </el-select>
          </el-form-item>

          <el-form-item :label="t('hostJobs.fieldHttpHeaders')">
            <div class="host-jobs-header-editor">
              <div
                v-for="(row, index) in editorForm.httpHeaders"
                :key="`http-header-${index}`"
                class="host-jobs-header-row"
              >
                <el-input
                  v-model="row.name"
                  :placeholder="t('hostJobs.fieldHeaderName')"
                  :disabled="changing"
                />
                <el-input
                  v-model="row.value"
                  :placeholder="t('hostJobs.fieldHeaderValue')"
                  :disabled="changing"
                />
                <el-button :disabled="changing" @click="removeHttpHeader(index)">
                  {{ t('hostJobs.removeHeader') }}
                </el-button>
              </div>
              <el-button type="primary" plain :disabled="changing" @click="addHttpHeader">
                {{ t('hostJobs.addHeader') }}
              </el-button>
            </div>
          </el-form-item>

          <el-form-item :label="t('hostJobs.fieldSecretHeaders')">
            <p class="host-jobs-field-hint">{{ t('hostJobs.fieldSecretHeadersHint') }}</p>
            <div class="host-jobs-header-editor">
              <div
                v-for="(row, index) in editorForm.secretHeaders"
                :key="`secret-header-${index}`"
                class="host-jobs-header-row"
              >
                <el-input
                  v-model="row.name"
                  :placeholder="t('hostJobs.fieldHeaderName')"
                  :disabled="changing"
                />
                <el-input
                  v-model="row.configKey"
                  :placeholder="t('hostJobs.fieldSecretConfigKey')"
                  :disabled="changing"
                />
                <el-button :disabled="changing" @click="removeSecretHeader(index)">
                  {{ t('hostJobs.removeHeader') }}
                </el-button>
              </div>
              <el-button type="primary" plain :disabled="changing" @click="addSecretHeader">
                {{ t('hostJobs.addSecretHeader') }}
              </el-button>
            </div>
          </el-form-item>
        </template>

        <!-- 显示名称 -->
        <el-form-item
          :label="t('hostJobs.fieldDisplayName')"
          prop="displayName"
          required
          :error="fieldErrors.displayName || undefined"
        >
          <el-input
            v-model="editorForm.displayName"
            :placeholder="t('hostJobs.fieldDisplayName')"
            :disabled="changing"
            @update:model-value="fieldErrors.displayName = validateDisplayName()"
          />
        </el-form-item>

        <!-- 描述 -->
        <el-form-item :label="t('hostJobs.fieldDescription')">
          <el-input
            v-model="editorForm.description"
            :placeholder="t('hostJobs.fieldDescription')"
            type="textarea"
            :rows="3"
            :disabled="changing"
          />
        </el-form-item>

        <!-- 作业分组 -->
        <el-form-item :label="t('hostJobs.fieldGroupName')">
          <el-input
            v-model="editorForm.groupName"
            :placeholder="t('hostJobs.fieldGroupNamePlaceholder')"
            :disabled="changing"
            maxlength="64"
          />
        </el-form-item>

        <!-- 允许重叠执行：默认关闭，对标 Admin.NET Concurrent=false 更安全默认值 -->
        <el-form-item
          v-if="editorMode === 'create' ? canCreate : canUpdate"
          :label="t('hostJobs.fieldAllowConcurrentExecutions')"
        >
          <el-switch
            v-model="editorForm.allowConcurrentExecutions"
            :disabled="changing"
            data-testid="host-jobs-allow-concurrent"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <!-- 执行记录抽屉 -->
    <el-drawer
      v-model="recordsDrawerOpen"
      :title="t('hostJobs.recordsTitle') + ' - ' + recordsJobDisplayName"
      :size="'60%'"
      :append-to-body="true"
    >
      <div v-if="recordsProblem" class="art-inline-alert" role="alert">
        <strong translate="no">{{ recordsProblem.code }}</strong>
        <span>{{ recordsProblem.title }}</span>
      </div>

      <div class="host-jobs-records-toolbar">
        <PermissionGate v-if="canClearExecutions" code="jobs.executions.clear">
          <el-button
            type="danger"
            plain
            :disabled="changing || recordsLoading || executions.length === 0"
            data-testid="host-jobs-action-clear-executions"
            @click="clearExecutions"
          >
            {{ t('hostJobs.clearExecutions') }}
          </el-button>
        </PermissionGate>
      </div>

      <div v-loading="recordsLoading">
        <div class="art-table" :class="{ 'is-empty': executions.length === 0 }">
          <el-table
            :data="executions"
            stripe
            border
            style="width: 100%"
          >
            <el-table-column :label="t('hostJobs.columnIndex')" width="64" align="center">
              <template #default="{ $index }">{{ (executionsPage - 1) * executionsPageSize + $index + 1 }}</template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsStatus')"
              width="110"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.status)" effect="light">
                  {{ statusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsTriggerKind')"
              min-width="100"
              align="center"
            >
              <template #default="{ row }">
                <span translate="no">{{ row.triggerKind }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsStartedAt')"
              min-width="160"
              align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.startedAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsFinishedAt')"
              min-width="160"
              align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.finishedAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsDuration')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <span>{{ calculateDuration(row.startedAtUtc, row.finishedAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsAttemptCount')"
              width="90"
              align="center"
            >
              <template #default="{ row }">
                <span>{{ row.attemptCount }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('hostJobs.recordsError')"
              min-width="200"
              align="left"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span v-if="row.errorMessage" class="host-jobs-error-message" translate="no">
                  {{ row.errorMessage }}
                </span>
                <span v-else>—</span>
              </template>
            </el-table-column>

            <template #empty>{{ t('hostJobs.emptyRecords') }}</template>
          </el-table>

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="executionsPage"
              v-model:page-size="executionsPageSize"
              :total="executionsTotal"
              background
              layout="total, sizes, prev, pager, next, jumper"
              :page-sizes="[10, 20, 50, 100]"
              @current-change="onExecutionsPageChange"
            />
          </div>
        </div>
      </div>
    </el-drawer>
  </section>
</template>

<style scoped>
.host-jobs-editor-form {
  padding-top: 8px;
}

.host-jobs-header-editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}

.host-jobs-header-row {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 8px;
  align-items: center;
}

.host-jobs-field-hint {
  margin: 0 0 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.4;
}

.host-jobs-error-message {
  color: var(--el-color-danger);
  font-size: 12px;
}

.host-jobs-records-toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 12px;
}

.art-sr-heading {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
