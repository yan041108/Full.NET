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
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createSettingsConfigEntry,
  disableSettingsConfigEntry,
  listSettingsConfigEntries,
  updateSettingsConfigEntry
} from '../api/config-entries';

defineOptions({ name: 'ConfigEntriesView' });

type EditorMode = 'create' | 'edit';
type ConfigEntryTableColumnKey = 'valueKind' | 'value' | 'displayOrder' | 'status' | 'description';

interface AppliedFilters {
  configKey: string;
  displayName: string;
  status: '' | 'active' | 'inactive';
}

const CONFIG_KEY_PATTERN = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/;

const session = useSessionStore();
const { t } = useAdminI18n();
const allEntries = ref<SettingsConfigEntry[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ configKey: '', displayName: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingEntry = ref<SettingsConfigEntry | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  configKey: '',
  displayName: '',
  description: '',
  valueKind: 'string' as SettingsConfigValueKind,
  value: '',
  displayOrder: '0'
});
const fieldErrors = reactive({
  configKey: '',
  displayName: '',
  value: '',
  displayOrder: ''
});
const columnVisibility = ref<Record<ConfigEntryTableColumnKey, boolean>>({
  valueKind: true,
  value: true,
  displayOrder: true,
  status: true,
  description: true
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
    { key: 'valueKind', label: t('configEntries.valueKind'), visible: columnVisibility.value.valueKind },
    { key: 'value', label: t('configEntries.value'), visible: columnVisibility.value.value },
    {
      key: 'displayOrder',
      label: t('configEntries.displayOrder'),
      visible: columnVisibility.value.displayOrder
    },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    {
      key: 'description',
      label: t('configEntries.descriptionLabel'),
      visible: columnVisibility.value.description
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as ConfigEntryTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'configKey',
    label: t('configEntries.configKey'),
    placeholder: t('configEntries.searchConfigKeyPlaceholder')
  },
  {
    key: 'displayName',
    label: t('configEntries.displayName'),
    placeholder: t('configEntries.searchDisplayNamePlaceholder')
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

watchLoading(loading);

onMounted(() => {
  void load();
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

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    configKey: params.configKey ?? '',
    displayName: params.displayName ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { configKey: '', displayName: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingEntry.value = null;
  editorForm.configKey = '';
  editorForm.displayName = '';
  editorForm.description = '';
  editorForm.valueKind = 'string';
  editorForm.value = '';
  editorForm.displayOrder = '0';
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
  editorForm.valueKind = entry.valueKind;
  editorForm.value = entry.value;
  editorForm.displayOrder = String(entry.displayOrder);
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
      Number.parseInt(editorForm.displayOrder, 10) || 0
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
      entry.version
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
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('configEntries.configKey')" min-width="220">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.configKey.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.configKey }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.displayName }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('valueKind')"
              :label="t('configEntries.valueKind')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <span translate="no">{{ row.valueKind }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('value')"
              :label="t('configEntries.value')"
              min-width="160"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.value }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('displayOrder')"
              :label="t('configEntries.displayOrder')"
              width="100"
              align="center"
              prop="displayOrder"
            />

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'configEntries.active' : 'configEntries.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('description')"
              :label="t('configEntries.descriptionLabel')"
              min-width="160"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.description ?? '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate v-if="canUpdate" code="settings.config.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="config-entries-action-edit"
                      :title="t('configEntries.edit')"
                      :disabled="changing || !row.isActive"
                      @click="openEdit(row)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive && canDisable" code="settings.config.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="config-entries-action-disable"
                      :title="t('configEntries.disable')"
                      :disabled="changing"
                      @click="disable(row)"
                    />
                  </PermissionGate>
                </div>
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
        <el-form-item
          :label="t('configEntries.displayName')"
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
        <el-form-item :label="t('configEntries.descriptionLabel')">
          <el-input
            v-model="editorForm.description"
            :placeholder="t('configEntries.descriptionPlaceholder')"
          />
        </el-form-item>
        <el-form-item v-if="editorMode === 'create'" :label="t('configEntries.valueKind')">
          <el-select v-model="editorForm.valueKind">
            <el-option v-for="kind in valueKinds" :key="kind" :label="kind" :value="kind" />
          </el-select>
        </el-form-item>
        <el-form-item v-else :label="t('configEntries.valueKind')">
          <el-input v-model="editorForm.valueKind" disabled />
        </el-form-item>
        <el-form-item
          :label="t('configEntries.value')"
          prop="value"
          required
          :error="fieldErrors.value || undefined"
        >
          <el-input
            v-model="editorForm.value"
            :placeholder="t('configEntries.valuePlaceholder')"
            @update:model-value="fieldErrors.value = validateValue()"
          />
        </el-form-item>
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
