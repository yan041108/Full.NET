<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElRadio,
  ElRadioGroup,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsConfigEntry,
  SettingsConfigValueKind
} from '@fullnet/client-contracts';
import {
  SETTINGS_CONFIG_VALUE_KINDS,
  isFullNetProblemDetails
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
  batchDeleteSettingsConfigEntries,
  createSettingsConfigEntry,
  deleteSettingsConfigEntry,
  disableSettingsConfigEntry,
  listSettingsConfigEntries,
  listSettingsConfigGroups,
  updateSettingsConfigEntry
} from '../api/config-entries';

defineOptions({ name: 'ConfigEntriesView' });

type EditorMode = 'create' | 'edit';
// 对齐 Admin.NET：固定展示 配置名称、配置编码、属性值、内置参数、排序、备注
type ConfigEntryTableColumnKey = 'remark';

interface AppliedFilters {
  configKey: string;
  displayName: string;
  groupName: string;
  status: '' | 'active' | 'inactive';
}

const CONFIG_KEY_PATTERN = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allEntries = ref<SettingsConfigEntry[]>([]);
// 配置分组下拉数据，对齐 Admin.NET 配置分组筛选；空字符串代表"全部分组"。
const groups = ref<string[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ configKey: '', displayName: '', groupName: '', status: '' });
// 多选行集合；批量删除仅针对其中 IsActive=false 的项，对应 Admin.NET 批量删除已禁用配置。
const selectedEntries = ref<SettingsConfigEntry[]>([]);
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingEntry = ref<SettingsConfigEntry | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  configKey: '',
  displayName: '',
  description: '',
  groupName: '',
  valueKind: 'string' as SettingsConfigValueKind,
  value: '',
  displayOrder: '0',
  isActive: true
});
const fieldErrors = reactive({
  configKey: '',
  displayName: '',
  value: '',
  displayOrder: ''
});
// 默认对齐 Admin.NET：备注为可选列，其余固定列
const columnVisibility = ref<Record<ConfigEntryTableColumnKey, boolean>>({
  remark: true
});
const valueKinds = SETTINGS_CONFIG_VALUE_KINDS;

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

const filteredEntries = computed(() => {
  let rows = allEntries.value;
  const filters = appliedFilters.value;

  if (filters.configKey.trim()) {
    const keyword = filters.configKey.trim().toLowerCase();
    rows = rows.filter(entry => entry.configKey.toLowerCase().includes(keyword));
  }

  if (filters.displayName.trim()) {
    const keyword = filters.displayName.trim().toLowerCase();
    rows = rows.filter(entry => entry.displayName.toLowerCase().includes(keyword));
  }

  // 分组筛选：空字符串表示全部分组；null 分组的项归入"无分组"虚拟桶由下拉显式选项表达。
  if (filters.groupName) {
    if (filters.groupName === '__none__') {
      rows = rows.filter(entry => !entry.groupName);
    } else {
      rows = rows.filter(entry => entry.groupName === filters.groupName);
    }
  }

  if (filters.status === 'active') {
    rows = rows.filter(entry => entry.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(entry => !entry.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedEntries, resetPage } = useArtClientPagination(filteredEntries);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'remark', label: t('configEntries.remark'), visible: columnVisibility.value.remark }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as ConfigEntryTableColumnKey] = column.visible !== false;
      }
    }
  }
});

// 分组下拉选项：先列后端去重分组，再补"全部分组"空值选项，便于重置分组筛选。
const groupFilterOptions = computed(() => {
  const options = groups.value.map(group => ({ label: group, value: group }));
  options.push({ label: t('configEntries.allGroups'), value: '' });
  return options;
});

// 对齐 Admin.NET：搜索条件为 配置名称、配置编码、分组、状态
const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'displayName',
    label: t('configEntries.createLabel'),
    placeholder: t('configEntries.searchNamePlaceholder')
  },
  {
    key: 'configKey',
    label: t('configEntries.updateLabel'),
    placeholder: t('configEntries.searchCodePlaceholder')
  },
  {
    key: 'groupName',
    label: t('configEntries.groupName'),
    type: 'select',
    placeholder: t('configEntries.searchGroupPlaceholder'),
    options: groupFilterOptions.value
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('configEntries.searchStatusPlaceholder'),
    options: [
      { label: t('configEntries.active'), value: 'active' },
      { label: t('configEntries.inactive'), value: 'inactive' }
    ]
  }
]);

const canCreate = computed(() => session.can('settings.config.create'));
const canUpdate = computed(() => session.can('settings.config.update'));
const canDisable = computed(() => session.can('settings.config.disable'));
// 硬删除仅对已禁用的配置项开放，对应 Admin.NET DeleteConfig。
const canDelete = computed(() => session.can('settings.config.delete'));

// 选中行中可硬删除的子集：仅 IsActive=false 才允许进入批量删除。
const deletableSelected = computed(() => selectedEntries.value.filter(entry => !entry.isActive));
const canBatchDelete = computed(() => canDelete.value && deletableSelected.value.length > 0 && !changing.value);

watchLoading(loading);

onMounted(() => {
  void load();
  void loadGroups();
});

function isColumnVisible(key: ConfigEntryTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function normalizeConfigKey(value: string): string {
  return value.trim().toLowerCase();
}

function clearFieldErrors(): void {
  fieldErrors.configKey = '';
  fieldErrors.displayName = '';
  fieldErrors.value = '';
  fieldErrors.displayOrder = '';
}

function validateConfigKey(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const configKey = normalizeConfigKey(editorForm.configKey);
  if (!configKey) {
    return t('configEntries.configKeyRequired');
  }
  if (!CONFIG_KEY_PATTERN.test(configKey)) {
    return t('configEntries.configKeyInvalid');
  }
  return '';
}

function validateDisplayName(): string {
  const displayName = editorForm.displayName.trim();
  if (!displayName) {
    return t('configEntries.displayNameRequired');
  }
  if (displayName.length > 128) {
    return t('configEntries.displayNameInvalid');
  }
  return '';
}

function validateValue(): string {
  if (!editorForm.value.trim()) {
    return t('configEntries.valueRequired');
  }
  return '';
}

function validateDisplayOrder(): string {
  const order = Number.parseInt(editorForm.displayOrder, 10);
  if (Number.isNaN(order)) {
    return t('configEntries.displayOrderInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.configKey = validateConfigKey();
  fieldErrors.displayName = validateDisplayName();
  fieldErrors.value = validateValue();
  fieldErrors.displayOrder = validateDisplayOrder();
  return !fieldErrors.configKey
    && !fieldErrors.displayName
    && !fieldErrors.value
    && !fieldErrors.displayOrder;
}

async function fetchAllEntries(): Promise<SettingsConfigEntry[]> {
  const pageLimit = 100;
  const firstPage = await listSettingsConfigEntries(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listSettingsConfigEntries(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allEntries.value = await fetchAllEntries();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'configEntries.loadFailed');
  } finally {
    loading.value = false;
  }
}

// 加载已使用的分组去重列表，供搜索栏分组下拉使用；失败不阻塞主列表。
async function loadGroups(): Promise<void> {
  try {
    groups.value = await listSettingsConfigGroups();
  } catch {
    groups.value = [];
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    configKey: params.configKey ?? '',
    displayName: params.displayName ?? '',
    groupName: params.groupName ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { configKey: '', displayName: '', groupName: '', status: '' };
  resetPage();
}

// 多选回调：保留全量选中行引用，批量删除时再过滤出已禁用子集。
function handleSelectionChange(rows: SettingsConfigEntry[]): void {
  selectedEntries.value = rows;
}

function clearSelection(): void {
  selectedEntries.value = [];
}

// 批量硬删除：仅删除选中项中已禁用的配置；后端会再次校验 IsActive=0，任一未禁用则整体拒绝。
async function batchDelete(): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  const targets = deletableSelected.value;
  if (targets.length === 0) {
    ElMessage.warning(t('configEntries.batchDeleteNoneAvailable'));
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('configEntries.confirmBatchDelete', { count: targets.length }),
      t('configEntries.batchDelete'),
      {
        type: 'warning',
        confirmButtonText: t('configEntries.batchDelete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await batchDeleteSettingsConfigEntries(targets.map(entry => entry.id));
    ElMessage.success(t('configEntries.batchDeleteSuccess', { count: targets.length }));
    selectedEntries.value = [];
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

function openCreate(): void {
  editorMode.value = 'create';
  editingEntry.value = null;
  editorForm.configKey = '';
  editorForm.displayName = '';
  editorForm.description = '';
  editorForm.groupName = '';
  editorForm.valueKind = 'string';
  editorForm.value = '';
  editorForm.displayOrder = '0';
  editorForm.isActive = true;
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(entry: SettingsConfigEntry): void {
  if (changing.value || !entry.isActive) {
    return;
  }
  editorMode.value = 'edit';
  editingEntry.value = entry;
  editorForm.configKey = entry.configKey;
  editorForm.displayName = entry.displayName;
  editorForm.description = entry.description ?? '';
  editorForm.groupName = entry.groupName ?? '';
  editorForm.valueKind = entry.valueKind;
  editorForm.value = entry.value;
  editorForm.displayOrder = String(entry.displayOrder);
  editorForm.isActive = entry.isActive;
  clearFieldErrors();
  editorOpen.value = true;
}

function onConfigKeyBlur(): void {
  if (editorMode.value !== 'create') {
    return;
  }
  editorForm.configKey = normalizeConfigKey(editorForm.configKey);
  fieldErrors.configKey = validateConfigKey();
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  if (editorMode.value === 'create') {
    editorForm.configKey = normalizeConfigKey(editorForm.configKey);
  }
  editorForm.displayName = editorForm.displayName.trim();
  editorForm.value = editorForm.value.trim();
  if (!applyFieldErrors()) {
    return;
  }
  if (editorMode.value === 'create') {
    await create();
    return;
  }
  await saveEdit();
}

async function create(): Promise<void> {
  if (!canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsConfigEntry(
      editorForm.configKey,
      editorForm.displayName,
      editorForm.description.trim() || null,
      editorForm.valueKind,
      editorForm.value,
      Number.parseInt(editorForm.displayOrder, 10) || 0,
      editorForm.groupName.trim() || null
    );
    editorOpen.value = false;
    ElMessage.success(t('configEntries.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const entry = editingEntry.value;
  if (!canUpdate.value || !entry) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateSettingsConfigEntry(
      entry.id,
      editorForm.displayName,
      editorForm.description.trim() || null,
      editorForm.value,
      Number.parseInt(editorForm.displayOrder, 10) || 0,
      entry.version,
      editorForm.groupName.trim() || null
    );
    editorOpen.value = false;
    ElMessage.success(t('configEntries.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(entry: SettingsConfigEntry): Promise<void> {
  if (changing.value || !entry.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('configEntries.confirmDisable', { name: entry.configKey }),
      t('configEntries.disable'),
      {
        type: 'warning',
        confirmButtonText: t('configEntries.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableSettingsConfigEntry(entry.id);
    ElMessage.success(t('configEntries.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

// 硬删除已禁用的配置项，二次确认后调用删除接口。
async function deleteEntry(entry: SettingsConfigEntry): Promise<void> {
  if (changing.value || entry.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('configEntries.confirmDelete', { name: entry.configKey }),
      t('configEntries.delete'),
      {
        type: 'warning',
        confirmButtonText: t('configEntries.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await deleteSettingsConfigEntry(entry.id, entry.version);
    ElMessage.success(t('configEntries.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'configEntries.loadFailed' | 'configEntries.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_config_entry_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="config-entries-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('configEntries.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="4"
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
            <PermissionGate code="settings.config.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="config-entries-action-create"
                @click="openCreate"
              >
                {{ t('configEntries.addEntry') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="settings.config.delete">
              <el-button
                type="danger"
                plain
                :disabled="!canBatchDelete"
                data-testid="config-entries-action-batch-delete"
                @click="batchDelete"
              >
                {{ t('configEntries.batchDelete') }}
                <span v-if="deletableSelected.length > 0" class="config-entries-batch-count">
                  ({{ deletableSelected.length }})
                </span>
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedEntries.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedEntries"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            row-key="id"
            @selection-change="handleSelectionChange"
          >
            <el-table-column
              type="selection"
              width="48"
              fixed="left"
              :selectable="(row: SettingsConfigEntry) => !row.isActive"
            />
            <el-table-column :label="t('users.columnIndex')" width="64" align="center" fixed="left">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <!-- 对齐 Admin.NET：配置名称 独立列 -->
            <el-table-column
              :label="t('configEntries.createLabel')"
              min-width="180"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.displayName }}</span>
              </template>
            </el-table-column>

            <!-- 对齐 Admin.NET：配置编码 独立列 -->
            <el-table-column
              :label="t('configEntries.updateLabel')"
              min-width="180"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.configKey }}</span>
              </template>
            </el-table-column>

            <!-- 对齐 Admin.NET：分组列，便于按分组聚合查看 -->
            <el-table-column
              :label="t('configEntries.groupName')"
              min-width="140"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <el-tag v-if="row.groupName" type="info" effect="plain" translate="no">
                  {{ row.groupName }}
                </el-tag>
                <span v-else class="config-entries-no-group">—</span>
              </template>
            </el-table-column>

            <!-- 对齐 Admin.NET：属性值 -->
            <el-table-column
              :label="t('configEntries.valueLabel')"
              min-width="200"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.value }}</span>
              </template>
            </el-table-column>

            <!-- 对齐 Admin.NET：内置参数（isActive 作为内置标识） -->
            <el-table-column
              :label="t('configEntries.builtIn')"
              width="100"
              align="center"
              header-align="center"
            >
              <template #default="{ row }">
                <el-tag v-if="row.isActive" type="success" effect="light">
                  {{ t('configEntries.builtInYes') }}
                </el-tag>
                <el-tag v-else type="danger" effect="light">
                  {{ t('configEntries.builtInNo') }}
                </el-tag>
              </template>
            </el-table-column>

            <!-- 对齐 Admin.NET：排序 -->
            <el-table-column
              :label="t('configEntries.displayOrder')"
              width="90"
              align="center"
              header-align="center"
              prop="displayOrder"
            />

            <!-- 对齐 Admin.NET：备注（可选列） -->
            <el-table-column
              v-if="isColumnVisible('remark')"
              :label="t('configEntries.remark')"
              min-width="200"
              align="left"
              header-align="center"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.description ?? '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="160"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate v-if="canUpdate" code="settings.config.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="config-entries-action-edit"
                      :title="t('configEntries.edit')"
                      :disabled="changing || !row.isActive"
                  @click="openEdit(row as SettingsConfigEntry)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive && canDisable" code="settings.config.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="config-entries-action-disable"
                      :title="t('configEntries.disable')"
                      :disabled="changing"
                  @click="disable(row as SettingsConfigEntry)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="!row.isActive && canDelete" code="settings.config.delete">
                    <ArtTableActionButton
                      type="delete"
                      test-id="config-entries-action-delete"
                      :title="t('configEntries.delete')"
                      :disabled="changing"
                      @click="deleteEntry(row as SettingsConfigEntry)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('configEntries.emptyDirectory') }}</template>
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

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('configEntries.createDialogTitle') : t('configEntries.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="config-entries-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="config-entries-editor-form"
        :model="editorForm"
        label-width="96px"
        class="config-entries-editor-form"
      >
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('configEntries.configKey')"
          prop="configKey"
          required
          :error="fieldErrors.configKey || undefined"
        >
          <el-input
            v-model="editorForm.configKey"
            :placeholder="t('configEntries.configKeyPlaceholder')"
            @blur="onConfigKeyBlur"
            @update:model-value="fieldErrors.configKey = validateConfigKey()"
          />
        </el-form-item>
        <el-form-item v-else :label="t('configEntries.configKey')">
          <el-input v-model="editorForm.configKey" disabled />
        </el-form-item>

        <!-- 对齐 Admin.NET：配置名称 -->
        <el-form-item
          :label="t('configEntries.createLabel')"
          prop="displayName"
          required
          :error="fieldErrors.displayName || undefined"
        >
          <el-input
            v-model="editorForm.displayName"
            :placeholder="t('configEntries.displayNamePlaceholder')"
            @update:model-value="fieldErrors.displayName = validateDisplayName()"
          />
        </el-form-item>

        <!-- 对齐 Admin.NET：分组（可选，用于配置项分组聚合） -->
        <el-form-item :label="t('configEntries.groupName')">
          <el-input
            v-model="editorForm.groupName"
            :placeholder="t('configEntries.groupNamePlaceholder')"
          />
        </el-form-item>

        <!-- 对齐 Admin.NET：值类型（创建时可选择，编辑时只读） -->
        <el-form-item
          v-if="editorMode === 'create'"
          :label="t('configEntries.valueKind')"
        >
          <el-select v-model="editorForm.valueKind">
            <el-option v-for="kind in valueKinds" :key="kind" :label="kind" :value="kind" />
          </el-select>
        </el-form-item>
        <el-form-item v-else :label="t('configEntries.valueKind')">
          <el-input v-model="editorForm.valueKind" disabled />
        </el-form-item>

        <!-- 对齐 Admin.NET：值（改为 textarea） -->
        <el-form-item
          :label="t('configEntries.valueLabel')"
          prop="value"
          required
          :error="fieldErrors.value || undefined"
        >
          <el-input
            v-model="editorForm.value"
            :placeholder="t('configEntries.valuePlaceholder')"
            type="textarea"
            :rows="3"
            @update:model-value="fieldErrors.value = validateValue()"
          />
        </el-form-item>

        <!-- 对齐 Admin.NET：内置参数 -->
        <el-form-item :label="t('configEntries.builtIn')">
          <el-radio-group v-model="editorForm.isActive">
            <el-radio :value="true">{{ t('configEntries.builtInYes') }}</el-radio>
            <el-radio :value="false">{{ t('configEntries.builtInNo') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <!-- 对齐 Admin.NET：排序 -->
        <el-form-item
          :label="t('configEntries.displayOrder')"
          prop="displayOrder"
          :error="fieldErrors.displayOrder || undefined"
        >
          <el-input
            v-model="editorForm.displayOrder"
            type="number"
            @update:model-value="fieldErrors.displayOrder = validateDisplayOrder()"
          />
        </el-form-item>

        <!-- 对齐 Admin.NET：备注（textarea） -->
        <el-form-item :label="t('configEntries.remark')">
          <el-input
            v-model="editorForm.description"
            :placeholder="t('configEntries.remarkPlaceholder')"
            type="textarea"
            :rows="3"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.config-entries-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.config-entries-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.config-entries-editor-form {
  padding-top: 8px;
}

.config-entries-batch-count {
  margin-left: 4px;
  font-weight: 600;
}

.config-entries-no-group {
  color: var(--el-text-color-placeholder, #909399);
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
