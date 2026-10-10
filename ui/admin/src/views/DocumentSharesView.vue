<script setup lang="ts">
import { computed, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCheckbox,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus, Refresh } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, HostDocumentShareResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import DocumentShareCreateDialog from '../components/DocumentShareCreateDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useSessionStore } from '../auth/session';
import { showError, showSuccess } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  listDocumentShares,
  updateDocumentShareStatus,
  type DocumentShareListFilters
} from '../api/document-shares';
import { listDocumentItems } from '../api/host-document-items';
import { buildDocumentShareUrl } from '../utils/documentShareUrl';

defineOptions({ name: 'DocumentSharesView' });

interface DocumentLabel {
  title: string;
  documentNo: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentShareResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const editorOpen = ref(false);
const documentLabels = ref<Map<string, DocumentLabel>>(new Map());
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<DocumentShareListFilters>({
  sortBy: 'createdAtUtc',
  sortDir: 'desc'
});
const DOCUMENT_SHARE_BATCH_HINT_KEY = 'documentShares.recentBatch';
const batchShareHint = ref<{ succeeded: number; total: number } | null>(null);

function consumeBatchShareHint(): void {
  try {
    const raw = sessionStorage.getItem(DOCUMENT_SHARE_BATCH_HINT_KEY);
    if (!raw) {
      return;
    }
    sessionStorage.removeItem(DOCUMENT_SHARE_BATCH_HINT_KEY);
    const parsed = JSON.parse(raw) as { succeeded?: number; total?: number };
    if (typeof parsed.succeeded === 'number' && typeof parsed.total === 'number') {
      batchShareHint.value = { succeeded: parsed.succeeded, total: parsed.total };
    }
  } catch {
    sessionStorage.removeItem(DOCUMENT_SHARE_BATCH_HINT_KEY);
  }
}

function dismissBatchShareHint(): void {
  batchShareHint.value = null;
}

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'shareCode', label: t('documentShares.filterShareCode') },
  { key: 'documentId', label: t('documentShares.filterDocumentId') },
  {
    key: 'isEnabled',
    label: t('documentShares.filterEnabled'),
    type: 'select',
    options: [
      { label: t('documentShares.filterEnabledAll'), value: '' },
      { label: t('documentShares.filterEnabledOn'), value: 'true' },
      { label: t('documentShares.filterEnabledOff'), value: 'false' }
    ]
  },
  { key: 'minAccessCount', label: t('documentShares.filterMinAccess') },
  { key: 'maxAccessCount', label: t('documentShares.filterMaxAccess') },
  {
    key: 'sortBy',
    label: t('documentShares.sortBy'),
    type: 'select',
    options: [
      { label: t('documentShares.sortCreatedAt'), value: 'createdAtUtc' },
      { label: t('documentShares.sortAccessCount'), value: 'accessCount' },
      { label: t('documentShares.sortExpireTime'), value: 'expireTime' }
    ]
  },
  {
    key: 'sortDir',
    label: t('documentShares.sortDir'),
    type: 'select',
    options: [
      { label: t('documentShares.sortDesc'), value: 'desc' },
      { label: t('documentShares.sortAsc'), value: 'asc' }
    ]
  }
]);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  syncTableLayout
} = useArtPagedTableInCard(loading);

const canRead = computed(() => session.currentUser?.scope === 'host' && session.can('document.host_shares.read'));
const canCreate = () => canRead.value && session.can('document.host_shares.create');
const canUpdateStatus = () => canRead.value && session.can('document.host_shares.update_status');
const scope = useAuthorizedViewScope(session, () => {
  items.value = []; documentLabels.value = new Map(); problem.value = undefined;
  total.value = 0; page.value = 1; pageSize.value = 20; loading.value = false; changing.value = false;
  editorOpen.value = false; batchShareHint.value = null; searchForm.value = {};
  appliedFilters.value = { sortBy: 'createdAtUtc', sortDir: 'desc' };
}, () => {
  if (!canRead.value) return;
  consumeBatchShareHint();
  void loadDocumentLabels(); void load();
});
let listRequest: ReturnType<typeof scope.begin>;
let labelsRequest: ReturnType<typeof scope.begin>;

function shareUrl(row: HostDocumentShareResponse): string {
  return buildDocumentShareUrl(row.shareCode);
}

function documentTitle(documentId: string): string {
  return documentLabels.value.get(documentId)?.title ?? '—';
}

function documentNo(documentId: string): string {
  return documentLabels.value.get(documentId)?.documentNo ?? '—';
}

async function loadDocumentLabels() {
  labelsRequest?.cancel();
  if (!canRead.value || !session.can('document.host_documents.read')) {
    documentLabels.value = new Map();
    return;
  }
  const request = scope.begin('document.host_documents.read');
  if (!request) return;
  labelsRequest = request;
  try {
    const collected = new Map<string, DocumentLabel>();
    let pageIndex = 1;
    let totalItems = 0;
    do {
      const result = await listDocumentItems(pageIndex, 100, {}, request.signal);
      if (!request.current() || !canRead.value) return;
      for (const item of result.items) {
        collected.set(item.id, { title: item.title, documentNo: item.documentNo });
      }
      totalItems = result.total;
      pageIndex += 1;
    } while (collected.size < totalItems && pageIndex <= 10);
    documentLabels.value = collected;
  } catch {
    if (request.current()) documentLabels.value = new Map();
  } finally { request.finish(); }
}

function buildFiltersFromSearch(params: Record<string, string | undefined>): DocumentShareListFilters {
  const isEnabledRaw = params.isEnabled?.trim();
  const isEnabled =
    isEnabledRaw === 'true' ? true : isEnabledRaw === 'false' ? false : undefined;
  const minAccess = params.minAccessCount?.trim();
  const maxAccess = params.maxAccessCount?.trim();
  return {
    ...appliedFilters.value,
    shareCode: params.shareCode?.trim() || undefined,
    documentId: params.documentId?.trim() || undefined,
    isEnabled,
    minAccessCount: minAccess ? Number(minAccess) : undefined,
    maxAccessCount: maxAccess ? Number(maxAccess) : undefined,
    sortBy: params.sortBy?.trim() || 'createdAtUtc',
    sortDir: (params.sortDir?.trim() === 'asc' ? 'asc' : 'desc') as 'asc' | 'desc'
  };
}

function handleSearch(params: Record<string, string | undefined>) {
  appliedFilters.value = buildFiltersFromSearch(params);
  page.value = 1;
  void load();
}

function resetSearch() {
  appliedFilters.value = { sortBy: 'createdAtUtc', sortDir: 'desc', expiredOnly: false, activeOnly: false };
  searchForm.value = {
    sortBy: 'createdAtUtc',
    sortDir: 'desc'
  };
  page.value = 1;
  void load();
}

async function load() {
  listRequest?.cancel();
  if (!canRead.value) return;
  const request = scope.begin('document.host_shares.read');
  if (!request) return;
  listRequest = request; loading.value = true; problem.value = undefined;
  try {
    const result = await listDocumentShares(page.value, pageSize.value, appliedFilters.value, request.signal);
    if (!request.current() || !canRead.value) return;
    items.value = result.items; page.value = result.page; pageSize.value = result.pageSize; total.value = result.total;
  } catch (error) {
    if (request.current()) problem.value = toProblem(error);
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); }
    request.finish();
  }
}

function openCreate() {
  if (canCreate()) editorOpen.value = true;
}

async function onShareCreated(_share: HostDocumentShareResponse, shareUrlValue: string) {
  if (!canCreate()) return;
  const request = scope.begin('document.host_shares.create');
  if (!request) return;
  try {
    try {
      await navigator.clipboard.writeText(shareUrlValue);
      if (request.current()) showSuccess(t('documentShares.createdWithLink'));
    } catch {
      if (request.current()) showSuccess(t('documentShares.createSuccess'));
    }
    if (request.current()) await Promise.all([load(), loadDocumentLabels()]);
  } finally { request.finish(); }
}

async function copyShareLink(url: string) {
  if (!canRead.value) return;
  const request = scope.begin('document.host_shares.read');
  if (!request) return;
  try {
    await navigator.clipboard.writeText(url);
    if (request.current()) showSuccess(t('documentShares.copyLinkSuccess'));
  } catch {
    if (request.current()) showError(t('documentShares.operationFailed'));
  } finally { request.finish(); }
}

async function toggleStatus(row: HostDocumentShareResponse) {
  if (changing.value || !canUpdateStatus()) return;
  const request = scope.begin('document.host_shares.update_status');
  if (!request) return;
  changing.value = true;
  try {
    await updateDocumentShareStatus(row.id, { isEnabled: !row.isEnabled, version: row.version }, request.signal);
    if (!request.current()) return;
    showSuccess(t('documentShares.updateSuccess'));
    await load();
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'documentShares.operationFailed');
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentShares.loadFailed' | 'documentShares.operationFailed' = 'documentShares.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

</script>

<template>
  <section v-if="canRead" class="document-shares-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentShares.title') }}</h1>

    <el-alert
      v-if="batchShareHint"
      type="success"
      :title="t('documentShares.batchCreateResult', batchShareHint)"
      show-icon
      closable
      class="art-page-alert"
      @close="dismissBatchShareHint"
    />

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card class="document-module-query-card" shadow="never">
      <ArtSearchBar
        v-model="searchForm"
        :items="searchItems"
        :default-visible-count="3"
        :search-label="t('documentShares.query')"
        :reset-label="t('documentShares.reset')"
        @search="handleSearch"
        @reset="resetSearch"
      />
      <div class="document-shares-view__flags">
        <el-checkbox
          v-model="appliedFilters.expiredOnly"
          :disabled="appliedFilters.activeOnly"
          @change="page = 1; load()"
        >
          {{ t('documentShares.filterExpiredOnly') }}
        </el-checkbox>
        <el-checkbox
          v-model="appliedFilters.activeOnly"
          :disabled="appliedFilters.expiredOnly"
          @change="page = 1; load()"
        >
          {{ t('documentShares.filterActiveOnly') }}
        </el-checkbox>
      </div>
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentShares.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" @click="load">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>
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
            <PermissionGate code="document.host_shares.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-share-create"
                @click="openCreate"
              >
                {{ t('documentShares.addShare') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            :loading="loading"
            :data="items"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
            <el-table-column :label="t('documentShares.shareCode')" prop="shareCode" min-width="140" />
            <el-table-column :label="t('documentShares.documentTitle')" min-width="160">
              <template #default="{ row }">
                <span translate="no">{{ documentTitle((row as HostDocumentShareResponse).documentId) }}</span>
              </template>
            </el-table-column>
            <el-table-column :label="t('documentShares.documentNo')" min-width="140">
              <template #default="{ row }">
                <span translate="no">{{ documentNo((row as HostDocumentShareResponse).documentId) }}</span>
              </template>
            </el-table-column>
            <el-table-column :label="t('documentShares.shareLink')" min-width="300">
              <template #default="{ row }">
                <div class="document-shares-view__link-cell">
                  <el-input :model-value="shareUrl(row as HostDocumentShareResponse)" readonly />
                  <el-button
                    data-testid="document-share-copy-link"
                    @click="copyShareLink(shareUrl(row as HostDocumentShareResponse))"
                  >
                    {{ t('documentShares.copyLink') }}
                  </el-button>
                </div>
              </template>
            </el-table-column>
            <el-table-column :label="t('documentShares.hasPassword')" width="100" align="center">
              <template #default="{ row }">
                <el-tag :type="row.hasPassword ? 'warning' : 'success'" size="small">
                  {{ row.hasPassword ? t('documentShares.hasPasswordYes') : t('documentShares.hasPasswordNo') }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('documentShares.accessCount')" prop="accessCount" width="110" />
            <el-table-column :label="t('documentShares.enabled')" width="100">
              <template #default="{ row }">
                <el-switch :model-value="row.isEnabled" disabled />
              </template>
            </el-table-column>
            <el-table-column :label="t('users.columnActions')" width="120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.host_shares.update_status">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-share-toggle"
                      :title="t('documentShares.toggleStatus')"
                      :disabled="changing"
                      @click="toggleStatus(row as HostDocumentShareResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentShares.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>

    <DocumentShareCreateDialog v-model:open="editorOpen" @created="onShareCreated" />
  </section>
</template>

<style scoped>
.document-shares-view {
  flex: 1;
  min-height: 0;
}

.document-shares-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-shares-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-shares-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-shares-view__link-cell {
  display: flex;
  gap: 8px;
  align-items: center;
}

.document-shares-view__flags {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  margin-top: 12px;
}
</style>
