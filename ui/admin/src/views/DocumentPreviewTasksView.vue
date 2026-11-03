<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { onActivated, onDeactivated, onMounted, onUnmounted, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus, Refresh, Search } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useBoundedPolling } from '../composables/useBoundedPolling';
import {
  getDocumentPreviewCapabilities, cancelDocumentPreviewTask, retryDocumentPreviewTask,
  createDocumentPreviewTask,
  listDocumentPreviewTasks,
  openDocumentPreviewTaskContent
} from '../api/document-preview-tasks';

defineOptions({ name: 'DocumentPreviewTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentPreviewTaskResponse[]>([]);
const loading = ref(false);
const changing = ref(false);
const active = ref(true), mutationUncertain = ref(false), cancelTarget = ref<HostDocumentPreviewTaskResponse>();
const cancelOpen = ref(false);
let generation = 0, listVersion = 0, listRequest: AbortController | undefined, requests = new AbortController();
const capabilityEnabled = ref(false);
const compactLayout = ref(window.innerWidth < 768);
const syncViewport = () => { compactLayout.value = window.innerWidth < 768; };
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const filterDocumentItemId = ref('');
const editorOpen = ref(false);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  documentItemId: '',
  versionId: ''
});

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

const available = () => active.value && !session.switching && !!session.currentUser && !session.currentUser.tenantId && session.currentUser.actorScope === 'host';
const canRead = () => available() && session.can('document.host_preview_tasks.read');
const canCreate = () => canRead() && capabilityEnabled.value && !mutationUncertain.value && session.can('document.host_preview_tasks.create');
const canCancel = (row: HostDocumentPreviewTaskResponse) => canRead() && session.can('document.host_preview_tasks.cancel') && ['preparing', 'pending', 'processing'].includes(row.statusKey);
const canRetry = (row: HostDocumentPreviewTaskResponse) => canRead() && capabilityEnabled.value && session.can('document.host_preview_tasks.retry') && ['failed', 'cancelled'].includes(row.statusKey);
const scopeKey = () => [session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.id].join('|');
const progress = useBoundedPolling({
  enabled: () => canRead() && !loading.value && !changing.value && !mutationUncertain.value && !cancelOpen.value,
  pending: () => items.value.some(row => ['preparing', 'pending', 'processing'].includes(row.statusKey)),
  key: () => [scopeKey(), page.value, pageSize.value, filterDocumentItemId.value.trim()].join('|'),
  intervalMs: 5000,
  read: signal => listDocumentPreviewTasks(page.value, pageSize.value, filterDocumentItemId.value.trim() || undefined, signal),
  accept: result => { items.value = result.items; total.value = result.total; },
  failed: error => { problem.value = toProblem(error); }
});
const progressPaused = progress.paused;

function statusTagType(statusKey: string): 'success' | 'warning' | 'danger' | 'info' {
  if (statusKey === 'succeeded') {
    return 'success';
  }
  if (statusKey === 'failed' || statusKey === 'cancelled') {
    return 'danger';
  }
  if (statusKey === 'processing') {
    return 'warning';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  const key = `documentPreviewTasks.status.${statusKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}
function errorLabel(code: string): string { return code === 'document.preview.cancelled' ? t('documentPreviewTasks.cancelledMessage') : code; }

async function load() {
  if (!canRead() || changing.value) return;
  const current = generation, version = ++listVersion, controller = new AbortController(); listRequest?.abort(); listRequest = controller;
  progress.stop(); cancelOpen.value = false; cancelTarget.value = undefined;
  loading.value = true;
  problem.value = undefined;
  try {
    const documentItemId = filterDocumentItemId.value.trim() || undefined;
    const [capability, result] = await Promise.all([
      getDocumentPreviewCapabilities(controller.signal).catch(() => ({ enabled: false })),
      listDocumentPreviewTasks(page.value, pageSize.value, documentItemId, controller.signal)
    ]);
    if (current !== generation || version !== listVersion || controller.signal.aborted || !canRead()) return;
    capabilityEnabled.value = capability.enabled;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    mutationUncertain.value = false;
  } catch (error) {
    if (current === generation && version === listVersion && !controller.signal.aborted) problem.value = toProblem(error);
  } finally {
    if (current === generation && version === listVersion) { loading.value = false; void syncTableLayout(); }
  }
}

function openCreate() {
  if (!canCreate()) return;
  editorForm.documentItemId = filterDocumentItemId.value.trim();
  editorForm.versionId = '';
  editorOpen.value = true;
}

async function submitCreate() {
  if (!canCreate()) return;
  const documentItemId = editorForm.documentItemId.trim();
  const versionId = editorForm.versionId.trim();
  if (!documentItemId) {
    return;
  }

  if (changing.value) return;
  const current = generation; changing.value = true; progress.stop(); listVersion++; listRequest?.abort(); loading.value = false;
  try {
    await createDocumentPreviewTask({
      documentItemId,
      versionId: versionId || null
    }, requests.signal);
    if (current !== generation || !canRead()) return;
    editorOpen.value = false;
    ElMessage.success(t('documentPreviewTasks.createSuccess'));
    changing.value = false; await load();
  } catch (error) {
    if (current === generation) { mutationUncertain.value = uncertain(error); problem.value = toProblem(error, 'documentPreviewTasks.operationFailed'); }
  } finally {
    if (current === generation) changing.value = false;
  }
}

/** 确认绑定点击时的任务版本，期间暂停自动刷新；服务端仍裁定 ACL、版本与完成竞争。 */
function confirmCancel(row: HostDocumentPreviewTaskResponse) {
  if (!canCancel(row) || changing.value || mutationUncertain.value) return;
  cancelTarget.value = { ...row }; cancelOpen.value = true;
}
function uncertain(error: unknown) { return !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
async function changeTask(row: HostDocumentPreviewTaskResponse, action: 'cancel' | 'retry') {
  if (changing.value || mutationUncertain.value || !(action === 'cancel' ? canCancel(row) : canRetry(row))) return;
  const current = generation; changing.value = true; progress.stop(); listVersion++; listRequest?.abort(); loading.value = false;
  try {
    if (action === 'cancel') await cancelDocumentPreviewTask(row, requests.signal); else await retryDocumentPreviewTask(row.id, requests.signal);
    if (current !== generation || !canRead()) return;
    cancelOpen.value = false; changing.value = false; await load();
  } catch (error) {
    if (current === generation) { cancelOpen.value = false; mutationUncertain.value = uncertain(error); problem.value = toProblem(error, 'documentPreviewTasks.operationFailed'); }
  } finally { if (current === generation) changing.value = false; }
}

async function openPdf(row: HostDocumentPreviewTaskResponse) {
  if (row.statusKey !== 'succeeded' || changing.value || !canRead()) {
    return;
  }

  const current = generation; changing.value = true;
  try {
    await openDocumentPreviewTaskContent(row.id, requests.signal);
  } catch (error) {
    if (current === generation) problem.value = toProblem(error, 'documentPreviewTasks.operationFailed');
  } finally {
    if (current === generation) changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentPreviewTasks.loadFailed' | 'documentPreviewTasks.operationFailed' = 'documentPreviewTasks.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

/** 任何身份、权限或保活状态变化均作废旧数据及迟到响应，不能把 Host 任务带入租户页面。 */
function reset() {
  generation++; listVersion++; listRequest?.abort(); requests.abort(); requests = new AbortController(); progress.stop();
  items.value = []; total.value = 0; page.value = 1; filterDocumentItemId.value = ''; capabilityEnabled.value = false;
  problem.value = undefined; loading.value = false; changing.value = false; mutationUncertain.value = false;
  cancelOpen.value = false; cancelTarget.value = undefined; editorOpen.value = false; editorForm.documentItemId = ''; editorForm.versionId = '';
}
watch(() => [scopeKey(), session.currentUser?.permissions.join('|'), session.switching, active.value], () => { reset(); if (canRead()) void load(); }, { immediate: true, flush: 'sync' });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; });
onMounted(() => window.addEventListener('resize', syncViewport));
onUnmounted(() => { active.value = false; reset(); window.removeEventListener('resize', syncViewport); });
</script>

<template>
  <section class="document-preview-tasks-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPreviewTasks.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-alert v-if="!capabilityEnabled && canRead()" type="info" :closable="false" :title="t('documentPreviewTasks.conversionDisabled')" data-testid="document-preview-disabled" />
    <ElAlert v-if="mutationUncertain" type="warning" :closable="false" :title="t('documentPreviewTasks.writeUnconfirmed')" data-testid="document-preview-unconfirmed" />
    <ElAlert v-if="progressPaused" type="info" :closable="false" :title="t('documentPreviewTasks.refreshPaused')" />
    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPreviewTasks.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" :disabled="changing || !canRead()" data-testid="document-preview-refresh" @click="load">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>
      <div ref="tableMainRef" class="art-crud-table-main">
        <el-form class="document-module-inline-query" :inline="true" @submit.prevent="load">
          <el-form-item :label="t('documentPreviewTasks.documentItemId')">
            <el-input
              v-model="filterDocumentItemId"
              class="document-preview-tasks-view__filter"
              :placeholder="t('documentPreviewTasks.filterDocumentItemId')"
              clearable
              data-testid="document-preview-task-filter"
              @keyup.enter="load"
              @clear="load"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" @click="load">{{ t('documentPermissions.query') }}</el-button>
          </el-form-item>
        </el-form>
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
            <PermissionGate v-if="canCreate()" code="document.host_preview_tasks.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-preview-task-create"
                @click="openCreate"
              >
                {{ t('documentPreviewTasks.addTask') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <el-table
            v-loading="loading"
            :data="items"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
            <el-table-column :label="t('documentPreviewTasks.documentTitle')" min-width="180">
              <template #default="{ row }">
                <div>{{ row.documentTitle }}</div>
                <div v-if="compactLayout" class="document-preview-tasks-view__mobile-state">
                  <el-tag :type="statusTagType(row.statusKey)" size="small" class="preview-state-tag">{{ statusLabel(row.statusKey) }}</el-tag>
                  <span>{{ t('documentPreviewTasks.attempts') }}：{{ row.attemptCount ?? 0 }}</span>
                  <span v-if="row.errorCode">{{ errorLabel(row.errorCode) }}</span>
                  <span v-if="row.nextAttemptAtUtc">{{ t('documentPreviewTasks.nextAttemptAtUtc') }}：{{ row.nextAttemptAtUtc }}</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.documentItemId')" prop="documentItemId" min-width="280" />
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.status')" width="140">
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.statusKey)" size="small" class="preview-state-tag">
                  {{ statusLabel(row.statusKey) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.attempts')" prop="attemptCount" width="100" />
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.nextAttemptAtUtc')" prop="nextAttemptAtUtc" min-width="200" />
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.provider')" prop="providerKey" width="160" />
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.errorCode')" min-width="180">
              <template #default="{ row }"><span :title="row.errorCode ?? undefined">{{ row.errorCode ? errorLabel(row.errorCode) : '' }}</span></template>
            </el-table-column>
            <el-table-column v-if="!compactLayout" :label="t('documentPreviewTasks.createdAtUtc')" prop="createdAtUtc" min-width="200" />
            <el-table-column :label="t('users.columnActions')" :width="compactLayout ? 96 : 120" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <ArtTableActionButton v-if="canCancel(row as HostDocumentPreviewTaskResponse)" type="delete" :title="t('documentPreviewTasks.cancelTask')" test-id="document-preview-task-cancel" :disabled="changing || mutationUncertain" @click="confirmCancel(row as HostDocumentPreviewTaskResponse)" />
                  <ArtTableActionButton v-if="canRetry(row as HostDocumentPreviewTaskResponse)" type="edit" :title="t('documentPreviewTasks.retryTask')" test-id="document-preview-task-retry" :disabled="changing || mutationUncertain" @click="changeTask(row as HostDocumentPreviewTaskResponse, 'retry')" />
                  <PermissionGate v-if="canRead() && row.statusKey === 'succeeded'" code="document.host_preview_tasks.read">
                    <ArtTableActionButton
                      type="view"
                      test-id="document-preview-task-open-pdf"
                      :title="t('documentPreviewTasks.openPdf')"
                      :disabled="changing"
                      @click="openPdf(row as HostDocumentPreviewTaskResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPreviewTasks.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          :layout="compactLayout ? 'total, prev, pager, next' : 'total, sizes, prev, pager, next'"
          :small="compactLayout"
          :pager-count="5"
          :page-sizes="[10, 20, 50]"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>

    <ElDialog v-model="cancelOpen" :title="t('documentPreviewTasks.cancelConfirmTitle')" width="min(480px, 100vw)" :close-on-click-modal="!changing" :close-on-press-escape="!changing" :show-close="!changing">
      <p>{{ cancelTarget?.documentTitle }}</p>
      <p>{{ t('documentPreviewTasks.cancelConfirmDescription') }}</p>
      <template #footer>
        <ElButton :disabled="changing" @click="cancelOpen = false">{{ t('common.cancel') }}</ElButton>
        <ElButton type="danger" :loading="changing" :disabled="changing || mutationUncertain || !cancelTarget || !canCancel(cancelTarget)" data-testid="document-preview-confirm-cancel"
          @click="cancelTarget && changeTask(cancelTarget, 'cancel')">{{ t('documentPreviewTasks.cancelTask') }}</ElButton>
      </template>
    </ElDialog>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="t('documentPreviewTasks.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-preview-task-editor-submit"
      :show-confirm="canCreate()"
      @confirm="submitCreate"
    >
      <el-form ref="editorFormRef" data-testid="document-preview-task-editor-form" :model="editorForm" label-width="140px">
        <el-form-item :label="t('documentPreviewTasks.documentItemId')">
          <el-input v-model="editorForm.documentItemId" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentPreviewTasks.versionIdOptional')">
          <el-input v-model="editorForm.versionId" autocomplete="off" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.preview-state-tag { color: var(--el-text-color-primary); }
.document-preview-tasks-view {
  flex: 1;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-preview-tasks-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-preview-tasks-view__filter {
  width: min(280px, 100%);
  margin-right: 12px;
}
.document-preview-tasks-view__mobile-state {
  display: grid;
  gap: 6px;
  margin-top: 6px;
  font-size: 12px;
  overflow-wrap: anywhere;
  white-space: normal;
}
.document-preview-tasks-view__mobile-state .el-tag { width: max-content; }
</style>
