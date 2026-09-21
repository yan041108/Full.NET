<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElPagination,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus, Refresh } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, HostDocumentShareResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import DocumentShareCreateDialog from '../components/DocumentShareCreateDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentShares, updateDocumentShareStatus } from '../api/document-shares';
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

const canCreate = () => session.can('document.host_shares.create');
const canUpdateStatus = () => session.can('document.host_shares.update_status');

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
  if (!session.can('document.host_documents.read')) {
    documentLabels.value = new Map();
    return;
  }
  try {
    const collected = new Map<string, DocumentLabel>();
    let pageIndex = 1;
    let totalItems = 0;
    do {
      const result = await listDocumentItems(pageIndex, 100);
      for (const item of result.items) {
        collected.set(item.id, { title: item.title, documentNo: item.documentNo });
      }
      totalItems = result.total;
      pageIndex += 1;
    } while (collected.size < totalItems && pageIndex <= 10);
    documentLabels.value = collected;
  } catch {
    documentLabels.value = new Map();
  }
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listDocumentShares(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

function openCreate() {
  editorOpen.value = true;
}

async function onShareCreated(_share: HostDocumentShareResponse, shareUrlValue: string) {
  try {
    await navigator.clipboard.writeText(shareUrlValue);
    ElMessage.success(t('documentShares.createdWithLink'));
  } catch {
    ElMessage.success(t('documentShares.createSuccess'));
  }
  await load();
  await loadDocumentLabels();
}

async function copyShareLink(url: string) {
  try {
    await navigator.clipboard.writeText(url);
    ElMessage.success(t('documentShares.copyLinkSuccess'));
  } catch {
    ElMessage.error(t('documentShares.operationFailed'));
  }
}

async function toggleStatus(row: HostDocumentShareResponse) {
  changing.value = true;
  try {
    await updateDocumentShareStatus(row.id, {
      isEnabled: !row.isEnabled,
      version: row.version
    });
    ElMessage.success(t('documentShares.updateSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error, 'documentShares.operationFailed');
  } finally {
    changing.value = false;
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

onMounted(async () => {
  await loadDocumentLabels();
  await load();
});
</script>

<template>
  <section class="document-shares-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentShares.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

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
</style>
