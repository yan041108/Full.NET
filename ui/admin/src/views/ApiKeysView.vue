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
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostApiKey } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
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
  createHostApiKey,
  disableHostApiKey,
  listHostApiKeys,
  rotateHostApiKey
} from '../api/api-keys';

defineOptions({ name: 'ApiKeysView' });

type EditorMode = 'create';
type ApiKeyTableColumnKey = 'username' | 'keyPrefix' | 'permissions' | 'expiresAt' | 'lastUsedAt' | 'status';

interface AppliedFilters {
  displayName: string;
  userId: string;
  status: '' | 'active' | 'disabled';
}

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const allItems = ref<HostApiKey[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ displayName: '', userId: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  userId: '',
  displayName: '',
  permissionsText: '',
  expiresAt: ''
});
const fieldErrors = reactive({
  userId: '',
  displayName: ''
});
const secret = ref('');
const columnVisibility = ref<Record<ApiKeyTableColumnKey, boolean>>({
  username: true,
  keyPrefix: true,
  permissions: true,
  expiresAt: true,
  lastUsedAt: true,
  status: true
});

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

const filteredItems = computed(() => {
  let rows = allItems.value;
  const filters = appliedFilters.value;

  if (filters.displayName.trim()) {
    const keyword = filters.displayName.trim().toLowerCase();
    rows = rows.filter(item => item.displayName.toLowerCase().includes(keyword));
  }

  if (filters.userId.trim()) {
    const keyword = filters.userId.trim().toLowerCase();
    rows = rows.filter(item => item.userId.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(item => item.isActive);
  } else if (filters.status === 'disabled') {
    rows = rows.filter(item => !item.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'username', label: t('apiKeys.username'), visible: columnVisibility.value.username },
    { key: 'keyPrefix', label: t('apiKeys.prefix'), visible: columnVisibility.value.keyPrefix },
    {
      key: 'permissions',
      label: t('apiKeys.permissions'),
      visible: columnVisibility.value.permissions
    },
    { key: 'expiresAt', label: t('apiKeys.expiresAt'), visible: columnVisibility.value.expiresAt },
    {
      key: 'lastUsedAt',
      label: t('apiKeys.lastUsedAt'),
      visible: columnVisibility.value.lastUsedAt
    },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as ApiKeyTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'displayName',
    label: t('apiKeys.fieldDisplayName'),
    placeholder: t('apiKeys.searchDisplayNamePlaceholder')
  },
  {
    key: 'userId',
    label: t('apiKeys.fieldUserId'),
    placeholder: t('apiKeys.searchUserIdPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('apiKeys.searchStatusPlaceholder'),
    options: [
      { label: t('apiKeys.statusActive'), value: 'active' },
      { label: t('apiKeys.statusDisabled'), value: 'disabled' }
    ]
  }
]);

const canCreate = computed(() => session.can('identity.api_keys.create'));

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: ApiKeyTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatLastUsed(value: string | null | undefined): string {
  if (!value) {
    return t('apiKeys.never');
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function parsePermissions(text: string): string[] {
  return [...new Set(
    text
      .split(/[\n,]+/)
      .map(value => value.trim())
      .filter(Boolean)
  )];
}

function clearFieldErrors(): void {
  fieldErrors.userId = '';
  fieldErrors.displayName = '';
}

function validateUserId(): string {
  if (editorMode.value !== 'create') {
    return '';
  }
  const userId = editorForm.userId.trim();
  if (!userId) {
    return t('apiKeys.userIdRequired');
  }
  if (!GUID_PATTERN.test(userId)) {
    return t('apiKeys.userIdInvalid');
  }
  return '';
}

function validateDisplayName(): string {
  const displayName = editorForm.displayName.trim();
  if (!displayName) {
    return t('apiKeys.displayNameRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.userId = validateUserId();
  fieldErrors.displayName = validateDisplayName();
  return !fieldErrors.userId && !fieldErrors.displayName;
}

async function fetchAllApiKeys(): Promise<HostApiKey[]> {
  const pageLimit = 100;
  const firstPage = await listHostApiKeys(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostApiKeys(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allItems.value = await fetchAllApiKeys();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'apiKeys.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    displayName: params.displayName ?? '',
    userId: params.userId ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { displayName: '', userId: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editorForm.userId = '';
  editorForm.displayName = '';
  editorForm.permissionsText = '';
  editorForm.expiresAt = '';
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value || !canCreate.value) {
    return;
  }
  editorForm.displayName = editorForm.displayName.trim();
  editorForm.userId = editorForm.userId.trim();
  if (!applyFieldErrors()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  secret.value = '';
  try {
    const result = await createHostApiKey({
      userId: editorForm.userId,
      displayName: editorForm.displayName,
      permissions: parsePermissions(editorForm.permissionsText),
      expiresAtUtc: editorForm.expiresAt ? new Date(editorForm.expiresAt).toISOString() : null
    });
    secret.value = result.secret;
    editorOpen.value = false;
    ElMessage.success(t('apiKeys.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function copySecret(): Promise<void> {
  if (!secret.value) {
    return;
  }
  try {
    await navigator.clipboard.writeText(secret.value);
    ElMessage.success(t('apiKeys.copySuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

async function rotate(item: HostApiKey): Promise<void> {
  if (changing.value || !item.isActive || !session.can('identity.api_keys.rotate')) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('apiKeys.confirmRotate', { name: item.displayName }),
      t('apiKeys.rotate'),
      {
        type: 'warning',
        confirmButtonText: t('apiKeys.rotate'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    problem.value = undefined;
    secret.value = '';
    const result = await rotateHostApiKey(item.id);
    secret.value = result.secret;
    ElMessage.success(t('apiKeys.rotateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function disable(item: HostApiKey): Promise<void> {
  if (changing.value || !item.isActive || !session.can('identity.api_keys.disable')) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('apiKeys.confirmDisable', { name: item.displayName }),
      t('apiKeys.disable'),
      {
        type: 'warning',
        confirmButtonText: t('apiKeys.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await disableHostApiKey(item.id);
    ElMessage.success(t('apiKeys.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'apiKeys.loadFailed' | 'apiKeys.operationFailed' = 'apiKeys.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_api_key_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="api-keys-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('apiKeys.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="secret" class="art-form-card api-keys-secret-card" shadow="never" data-testid="api-key-secret">
      <h2>{{ t('apiKeys.secretTitle') }}</h2>
      <p role="alert">{{ t('apiKeys.secretWarning') }}</p>
      <code translate="no">{{ secret }}</code>
      <el-button type="primary" plain @click="copySecret">{{ t('apiKeys.copy') }}</el-button>
    </el-card>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('apiKeys.query')"
      :reset-label="t('apiKeys.reset')"
      :expand-label="t('apiKeys.expand')"
      :collapse-label="t('apiKeys.collapse')"
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
            <PermissionGate code="identity.api_keys.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="api-keys-action-create"
                @click="openCreate"
              >
                {{ t('apiKeys.addKey') }}
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
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column :label="t('apiKeys.fieldDisplayName')" min-width="200">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.displayName.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name">{{ row.displayName }}</div>
                    <div v-if="isColumnVisible('username')" class="art-crud-table-row__sub" translate="no">
                      {{ row.username }}
                    </div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('keyPrefix')"
              :label="t('apiKeys.prefix')"
              min-width="140"
              prop="keyPrefix"
            />

            <el-table-column
              v-if="isColumnVisible('permissions')"
              :label="t('apiKeys.permissions')"
              min-width="220"
            >
              <template #default="{ row }">{{ row.permissions.join(', ') }}</template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('expiresAt')"
              :label="t('apiKeys.expiresAt')"
              min-width="160"
            >
              <template #default="{ row }">
                {{ row.expiresAtUtc ?? t('apiKeys.noExpiration') }}
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('lastUsedAt')"
              :label="t('apiKeys.lastUsedAt')"
              min-width="160"
            >
              <template #default="{ row }">{{ formatLastUsed(row.lastUsedAtUtc) }}</template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ row.isActive ? t('apiKeys.statusActive') : t('apiKeys.statusDisabled') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div v-if="row.isActive" class="art-crud-table-actions">
                  <PermissionGate code="identity.api_keys.rotate">
                    <ArtTableActionButton
                      type="password"
                      test-id="api-keys-action-rotate"
                      :title="t('apiKeys.rotate')"
                      :disabled="changing"
                  @click="rotate(row as HostApiKey)"
                    />
                  </PermissionGate>
                  <PermissionGate code="identity.api_keys.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="api-keys-action-disable"
                      :title="t('apiKeys.disable')"
                      :disabled="changing"
                  @click="disable(row as HostApiKey)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('apiKeys.emptyList') }}</template>
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
      :title="t('apiKeys.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="api-keys-editor-submit"
      :show-confirm="canCreate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="api-key-create-form"
        :model="editorForm"
        label-width="108px"
        class="api-keys-editor-form"
      >
        <el-form-item
          :label="t('apiKeys.fieldUserId')"
          prop="userId"
          required
          :error="fieldErrors.userId || undefined"
        >
          <el-input
            v-model="editorForm.userId"
            data-testid="api-key-user-id"
            autocomplete="off"
            spellcheck="false"
            @update:model-value="fieldErrors.userId = validateUserId()"
          />
        </el-form-item>
        <el-form-item
          :label="t('apiKeys.fieldDisplayName')"
          prop="displayName"
          required
          :error="fieldErrors.displayName || undefined"
        >
          <el-input
            v-model="editorForm.displayName"
            data-testid="api-key-display-name"
            @update:model-value="fieldErrors.displayName = validateDisplayName()"
          />
        </el-form-item>
        <el-form-item :label="t('apiKeys.fieldPermissions')">
          <el-input
            v-model="editorForm.permissionsText"
            data-testid="api-key-permissions"
            type="textarea"
            :rows="3"
            spellcheck="false"
            :placeholder="t('apiKeys.permissionsHint')"
          />
        </el-form-item>
        <el-form-item :label="t('apiKeys.fieldExpiresAt')">
          <el-input v-model="editorForm.expiresAt" type="datetime-local" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.api-keys-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.api-keys-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.api-keys-secret-card code {
  display: block;
  margin: 12px 0;
  word-break: break-all;
}

.api-keys-editor-form {
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
