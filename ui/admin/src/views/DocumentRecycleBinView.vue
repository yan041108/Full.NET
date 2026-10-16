<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { RefreshLeft, Search } from '@element-plus/icons-vue';
import { ElAlert, ElButton, ElCard, ElInput, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails, HostRecycleBinItemResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { showSuccess, showProblem } from '../feedback/fullNetMessage';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listRecycleBinItems, purgeRecycleBinItem, restoreRecycleBinItem } from '../api/document-recycle-bin';
import { validRecyclePage, validRestoredRecycleItem } from '../api/document-recycle-guards';

defineOptions({ name: 'DocumentRecycleBinView' });
const { t } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined);
const items = ref<HostRecycleBinItemResponse[]>([]), selectedRows = ref<HostRecycleBinItemResponse[]>([]);
const loading = ref(false), changing = ref(false), recovering = ref(false), catalogReady = ref(false);
const problem = ref<FullNetProblemDetails>(), catalogProblem = ref<FullNetProblemDetails>();
const page = ref(1), pageSize = ref(20), total = ref(0), searchKeyword = ref(''), appliedKeyword = ref(''), catalogOwner = ref(0);
type Action = 'restore' | 'purge';
type BatchOutcome = 'processing' | 'succeeded' | 'failed' | 'unconfirmed' | 'not_sent';
const batchReceipt = ref<{ action: Action; succeeded: number; total: number; items: { id: string; title: string; outcome: BatchOutcome; errorCode?: string }[] }>();
// 恢复有 CAS，清除没有调用方修订；两类未知结果不能使用同一个自动解锁条件。
const blocks = ref(new Map<string, { action: Action; version: number }>());
const departed = new Map<string, { version: number; permanent: boolean }>(), revisionFloors = new Map<string, number>();
const confirmation = ref<{ kind: 'purge' | 'recover'; message: string; resolve: (value: boolean) => void }>();
function finishConfirmation(value: boolean): void { const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(value); }
const confirmationHandlers = computed(() => {
  const pending = confirmation.value, finish = (value: boolean) => { if (pending && confirmation.value === pending) finishConfirmation(value); };
  return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } };
});
const { tableMainRef, tableHeight, tableSize, tableZebra, tableBorder, tableHeaderBackground, tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const canRead = computed(() => viewActive.value && (!route || route.name === 'document-recycle-bin') && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null && session.can('document.host_recycle_bin.read'));
const scope = useAuthorizedViewScope(session, resetScope, () => { if (canRead.value) void load(); });
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: ViewRequest | undefined, operationRequest: ViewRequest | undefined, recoveryRequest: ViewRequest | undefined, listKey = '', readKey = '';
function resetScope(): void {
  finishConfirmation(false); items.value = []; selectedRows.value = []; total.value = 0; page.value = 1; pageSize.value = 20;
  searchKeyword.value = ''; appliedKeyword.value = ''; loading.value = false; changing.value = false; recovering.value = false; catalogReady.value = false;
  problem.value = undefined; catalogProblem.value = undefined; batchReceipt.value = undefined; blocks.value = new Map(); departed.clear(); revisionFloors.clear();
  listRequest = undefined; operationRequest = undefined; recoveryRequest = undefined; readKey = ''; listKey = ''; catalogOwner.value++;
}
watch(() => route?.name, () => { scope.invalidate(); if (canRead.value) void load(); }, { flush: 'sync' });
watch(items, () => { catalogOwner.value++; finishConfirmation(false); }, { flush: 'sync', deep: true });
watch(selectedRows, () => { catalogOwner.value++; finishConfirmation(false); }, { flush: 'sync', deep: true });
function begin(permission: string, owner?: ViewRequest): ViewRequest | undefined {
  if (!canRead.value || owner && !owner.current()) return;
  const request = scope.begin(permission); if (!request || !owner) return request;
  const cancel = () => request.cancel(); owner.signal.addEventListener('abort', cancel, { once: true });
  return { ...request, finish: () => { owner.signal.removeEventListener('abort', cancel); request.finish(); } };
}
const displayItems = computed(() => { const term = appliedKeyword.value.trim().toLowerCase(); return term ? items.value.filter(row => row.title.toLowerCase().includes(term)) : items.value; });
// 新页等待响应时仍保留目标页下界，避免分页器因临时空目录把受控页码压回第一页。
const displayTotal = computed(() => Math.max(total.value, page.value > 1 ? (page.value - 1) * pageSize.value + Math.max(1, items.value.length) : items.value.length));
const rowIndex = computed(() => (index: number) => (page.value - 1) * pageSize.value + index + 1);
function currentTarget(target: HostRecycleBinItemResponse): boolean {
  return displayItems.value.some(row => row.id.toLowerCase() === target.id.toLowerCase() && row.version === target.version && row.title === target.title && row.deletedAtUtc === target.deletedAtUtc && row.deletedByUserId === target.deletedByUserId);
}
function canAct(row: HostRecycleBinItemResponse): boolean { return catalogReady.value && !loading.value && !changing.value && !recovering.value && currentTarget(row) && !blocks.value.has(row.id.toLowerCase()); }
function toProblem(error: unknown, key: 'documentRecycleBin.loadFailed' | 'documentRecycleBin.operationFailed' = 'documentRecycleBin.loadFailed'): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { title: t(key), status: 500, code: 'client.request_failed' };
}
const unknown = (error: unknown) => !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
async function load(owner?: ViewRequest): Promise<void> {
  if (!canRead.value || owner && !owner.current() || !owner && (changing.value || recovering.value)) return;
  listRequest?.cancel(); const request = begin('document.host_recycle_bin.read', owner); if (!request) return;
  let requestedPage = Math.min(2147483647, Math.max(1, Number.isSafeInteger(page.value) ? page.value : 1));
  const requestedSize = Math.min(100, Math.max(1, Number.isSafeInteger(pageSize.value) ? pageSize.value : 20));
  page.value = requestedPage; pageSize.value = requestedSize; listKey = requestedPage + ':' + requestedSize;
  listRequest = request; loading.value = true; catalogReady.value = false; catalogProblem.value = undefined; selectedRows.value = []; catalogOwner.value++; finishConfirmation(false);
  if (listKey !== readKey) { items.value = []; total.value = 0; }
  try {
    for (let attempt = 0; attempt < 2; attempt++) {
      const result = await listRecycleBinItems(requestedPage, requestedSize, request.signal);
      if (!request.current() || listRequest !== request) return;
      if (!validRecyclePage(result, requestedPage, requestedSize) || result.items.some(row => {
        const id = row.id.toLowerCase(), previous = departed.get(id);
        return row.version < (revisionFloors.get(id) ?? 0) || previous && (previous.permanent || row.version <= previous.version);
      })) throw Error('client.invalid_recycle_bin_page');
      // 只对空页回退一次，计数与行读取之间的并发变化不能导致无限追页。
      if (attempt === 0 && result.items.length === 0 && requestedPage > 1) {
        requestedPage = Math.max(1, Math.min(requestedPage - 1, Math.ceil(result.total / requestedSize))); page.value = requestedPage; listKey = requestedPage + ':' + requestedSize; continue;
      }
      const next = new Map(blocks.value);
      for (const row of result.items) { const id = row.id.toLowerCase(), block = next.get(id); revisionFloors.set(id, row.version); if (block?.action === 'restore' && row.version > block.version) next.delete(id); }
      blocks.value = next; items.value = result.items; total.value = result.total; readKey = listKey; catalogReady.value = true; return;
    }
  } catch (error) { if (request.current() && listRequest === request) catalogProblem.value = toProblem(error); }
  finally { if (listRequest === request) { listRequest = undefined; listKey = ''; loading.value = false; catalogOwner.value++; void syncTableLayout(); } request.finish(); }
}
function changePage(nextPage: number, nextSize: number): void {
  if (!canRead.value || changing.value || recovering.value || !Number.isSafeInteger(nextPage) || nextPage < 1 || !Number.isSafeInteger(nextSize) || nextSize < 1 || nextSize > 100) return;
  const previousSize = Number((listKey || readKey).split(':')[1] || pageSize.value);
  const sizeChanged = nextSize !== previousSize, destination = sizeChanged ? 1 : nextPage, key = destination + ':' + nextSize;
  if (key === listKey || key === readKey && catalogReady.value) return;
  page.value = destination; pageSize.value = nextSize; void load();
}
function handleSearch(): void {
  if (!canRead.value || changing.value || recovering.value) return;
  appliedKeyword.value = searchKeyword.value; selectedRows.value = []; catalogOwner.value++; finishConfirmation(false); void syncTableLayout();
}
function onSelectionChange(rows: HostRecycleBinItemResponse[]): void {
  if (!catalogReady.value || loading.value || changing.value || recovering.value || !rows.every(currentTarget)) return;
  selectedRows.value = [...new Map(rows.map(row => [row.id.toLowerCase(), { ...items.value.find(item => item.id.toLowerCase() === row.id.toLowerCase())! }])).values()];
}
function outcomeLabel(outcome: BatchOutcome): string {
  return t(outcome === 'processing' ? 'documentRecycleBin.itemProcessing' : outcome === 'succeeded' ? 'documentRecycleBin.itemSucceeded' : outcome === 'failed' ? 'documentRecycleBin.itemFailed' : outcome === 'unconfirmed' ? 'documentRecycleBin.itemUnconfirmed' : 'documentRecycleBin.itemNotSent');
}
/** 冻结逐项输入，首个失败即停止；确认回执先落本地，再补读，永不自动重放写入。 */
async function runBatch(rows: readonly HostRecycleBinItemResponse[], action: Action, batch = false): Promise<void> {
  if (!canRead.value || rows.length === 0 || !rows.every(canAct)) return;
  const request = begin('document.host_recycle_bin.' + action); if (!request) return;
  const targets = [...new Map(rows.map(row => [row.id.toLowerCase(), { ...row }])).values()], intent = catalogOwner.value;
  operationRequest = request; changing.value = true; problem.value = undefined; let index = 0, succeeded = 0, sent = false, refresh = false;
  try {
    if (action === 'purge') {
      const confirmed = await new Promise<boolean>(resolve => { confirmation.value = { kind: 'purge', message: t('documentRecycleBin.confirmPurge', { name: batch ? String(targets.length) : targets[0]!.title }), resolve }; });
      if (!confirmed || !request.current() || intent !== catalogOwner.value) return;
    }
    batchReceipt.value = { action, succeeded: 0, total: targets.length, items: targets.map(row => ({ id: row.id, title: row.title, outcome: 'not_sent' })) }; selectedRows.value = [];
    for (; index < targets.length; index++) {
      const row = targets[index]!, id = row.id.toLowerCase(); sent = false;
      if (!request.current()) return;
      if (!currentTarget(row) || blocks.value.has(id)) throw { status: 409, code: 'client.recycle_target_changed', title: t('documentRecycleBin.targetChanged') };
      sent = true; batchReceipt.value.items[index]!.outcome = 'processing'; let restored: HostRecycleBinItemResponse | undefined;
      if (action === 'restore') { restored = await restoreRecycleBinItem(row.id, { version: row.version }, request.signal); if (!validRestoredRecycleItem(restored, row.id, row.version)) throw Error('client.invalid_recycle_bin_item'); }
      else { const result = await purgeRecycleBinItem(row.id, request.signal); if (result === false) throw { status: 409, code: 'client.document_purge_failed', title: t('documentRecycleBin.operationFailed') }; if (result !== true) throw Error('client.invalid_recycle_bin_purge'); }
      if (!request.current()) return;
      succeeded++; batchReceipt.value.succeeded = succeeded; batchReceipt.value.items[index]!.outcome = 'succeeded';
      departed.set(id, { version: restored?.version ?? row.version, permanent: action === 'purge' });
      const previousCount = items.value.length; items.value = items.value.filter(item => item.id.toLowerCase() !== id || action === 'restore' && item.version > restored!.version);
      total.value = Math.max(0, total.value - (previousCount - items.value.length)); refresh = true;
    }
    showSuccess(t(action === 'restore' ? 'documentRecycleBin.restoreSuccess' : 'documentRecycleBin.purgeSuccess'));
  } catch (error) {
    if (request.current()) {
      const failure = toProblem(error, 'documentRecycleBin.operationFailed'), entry = batchReceipt.value?.items[index], unconfirmed = sent && unknown(error);
      if (entry) { entry.outcome = !sent ? 'not_sent' : unconfirmed ? 'unconfirmed' : 'failed'; entry.errorCode = failure.code; }
      if (unconfirmed) { const row = targets[index]!; blocks.value = new Map(blocks.value).set(row.id.toLowerCase(), { action, version: row.version }); }
      problem.value = failure; selectedRows.value = []; showProblem(failure, t('documentRecycleBin.operationFailed')); refresh ||= unconfirmed;
    }
  } finally {
    if (operationRequest === request && refresh && request.current()) await load(request);
    if (operationRequest === request) { operationRequest = undefined; changing.value = false; catalogOwner.value++; }
    request.finish();
  }
}
async function restore(row: HostRecycleBinItemResponse): Promise<void> { await runBatch([row], 'restore'); }
async function purge(row: HostRecycleBinItemResponse): Promise<void> { await runBatch([row], 'purge'); }
async function batchRestore(): Promise<void> { await runBatch(selectedRows.value, 'restore', true); }
async function batchPurge(): Promise<void> { await runBatch(selectedRows.value, 'purge', true); }
async function recover(row: HostRecycleBinItemResponse): Promise<void> {
  const id = row.id.toLowerCase(), block = blocks.value.get(id), owner = catalogOwner.value;
  if (!canRead.value || !catalogReady.value || !currentTarget(row) || !block || changing.value || loading.value || recovering.value) return;
  const request = begin('document.host_recycle_bin.' + block.action); if (!request) return; recoveryRequest = request; recovering.value = true;
  try {
    const confirmed = await new Promise<boolean>(resolve => { confirmation.value = { kind: 'recover', message: t('documentRecycleBin.recoveryHint'), resolve }; });
    if (confirmed && request.current() && owner === catalogOwner.value && currentTarget(row) && blocks.value.get(id) === block) { const next = new Map(blocks.value); next.delete(id); blocks.value = next; }
  } finally { if (recoveryRequest === request) { recoveryRequest = undefined; recovering.value = false; catalogOwner.value++; } request.finish(); }
}
const controls = computed(() => {
  const owner = catalogOwner.value, selection = selectedRows.value.map(row => ({ ...row })), owned = () => canRead.value && owner === catalogOwner.value;
  const search = () => { if (owned()) handleSearch(); };
  return { refresh: () => { if (owned()) void load(); }, search, enter: (event: KeyboardEvent) => { if (event.key === 'Enter') search(); }, input: (value: string) => { if (owned() && !changing.value && !recovering.value) searchKeyword.value = value; }, clear: () => { if (owned()) { searchKeyword.value = ''; handleSearch(); } },
    select: (rows: HostRecycleBinItemResponse[]) => { if (owned()) onSelectionChange(rows); }, restore: () => { if (owned()) void runBatch(selection, 'restore', true); }, purge: () => { if (owned()) void runBatch(selection, 'purge', true); },
    pageModel: (value: number) => { if (owned() && !changing.value && !recovering.value && Number.isSafeInteger(value) && value > 0) page.value = value; },
    sizeModel: (value: number) => { if (owned() && !changing.value && !recovering.value && Number.isSafeInteger(value) && value > 0 && value <= 100) pageSize.value = value; },
    page: (value: number, size: number) => { if (owned()) changePage(value, size); } };
});
const rowControls = computed(() => { const owner = catalogOwner.value; return new Map(displayItems.value.map(row => { const target = { ...row }, owned = () => canRead.value && owner === catalogOwner.value && currentTarget(target); return [row.id.toLowerCase(), {
  restore: () => { if (owned()) void restore(target); }, purge: () => { if (owned()) void purge(target); }, recover: () => { if (owned()) void recover(target); }
}]; })); });
</script>

<template>
  <section v-if="canRead" class="document-recycle-bin-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentRecycleBin.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-alert v-if="catalogProblem" type="error" :title="catalogProblem.title" :description="catalogProblem.detail ?? catalogProblem.code" :closable="false" />

    <el-card v-if="batchReceipt" data-testid="document-recycle-receipt" shadow="never">
      <template #header>{{ t(batchReceipt.action === 'restore' ? 'documentRecycleBin.batchRestore' : 'documentRecycleBin.batchPurge') }}</template>
      <el-alert :title="t('documentRecycleBin.batchSummary', { succeeded: batchReceipt.succeeded, total: batchReceipt.total })"
        :description="t('documentRecycleBin.receiptHint')" :type="batchReceipt.succeeded === batchReceipt.total ? 'success' : 'info'" :closable="false" />
      <el-table :data="batchReceipt.items" max-height="240">
        <el-table-column prop="title" :label="t('documentRecycleBin.titleColumn')" min-width="180" />
        <el-table-column :label="t('documentRecycleBin.itemResult')" min-width="180">
          <template #default="{ row }">{{ outcomeLabel(row.outcome) }} <code v-if="row.errorCode" translate="no">{{ row.errorCode }}</code></template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card class="document-module-toolbar-card" shadow="never">
      <div class="document-module-toolbar">
        <div class="document-module-toolbar__left">
          <PermissionGate code="document.host_recycle_bin.restore">
            <el-button
              type="success"
              :icon="RefreshLeft"
              :disabled="!catalogReady || selectedRows.length === 0 || changing || loading || recovering"
              data-testid="document-recycle-batch-restore"
              v-on="{ click: controls.restore }"
            >
              {{ t('documentRecycleBin.batchRestore') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="document.host_recycle_bin.purge">
            <el-button
              type="danger"
              plain
              :disabled="!catalogReady || selectedRows.length === 0 || changing || loading || recovering"
              data-testid="document-recycle-batch-purge"
              v-on="{ click: controls.purge }"
            >
              {{ t('documentRecycleBin.batchPurge') }}
            </el-button>
          </PermissionGate>
        </div>
        <div class="document-module-toolbar__right">
          <el-input
            :model-value="searchKeyword" :disabled="changing || recovering" v-on="{ 'update:modelValue': controls.input, keyup: controls.enter, clear: controls.clear }"
            :placeholder="t('documentRecycleBin.searchPlaceholder')"
            clearable
            style="width: 280px"
          />
          <el-button :icon="Search" :disabled="changing || recovering" v-on="{ click: controls.search }">{{ t('documentRecycleBin.search') }}</el-button>
        </div>
      </div>
      <p>{{ t('documentRecycleBin.pageSearchHint') }}</p>
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <el-alert type="warning" :closable="false" show-icon class="document-recycle-bin-view__hint">
          {{ t('documentRecycleBin.alertHint') }}
        </el-alert>

        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen"
          v-on="{ refresh: controls.refresh }"
        />

        <div class="art-table" :class="{ 'is-empty': displayItems.length === 0 }">
          <el-table
            v-loading="loading"
            :data="displayItems"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
            row-key="id"
            v-on="{ 'selection-change': controls.select }"
          >
            <el-table-column type="selection" width="48" />
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>
            <el-table-column :label="t('documentRecycleBin.titleColumn')" min-width="240" prop="title" />
            <el-table-column :label="t('documentRecycleBin.deletedAt')" min-width="180" prop="deletedAtUtc" />
            <el-table-column :label="t('users.columnActions')" width="140" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.host_recycle_bin.restore">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-recycle-restore"
                      :title="t('documentRecycleBin.restore')"
                      :disabled="!canAct(row)"
                      v-on="{ click: rowControls.get(row.id.toLowerCase())?.restore }"
                    />
                  </PermissionGate>
                  <PermissionGate code="document.host_recycle_bin.purge">
                    <ArtTableActionButton
                      type="delete"
                      test-id="document-recycle-purge"
                      :title="t('documentRecycleBin.purge')"
                      :disabled="!canAct(row)"
                      v-on="{ click: rowControls.get(row.id.toLowerCase())?.purge }"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="blocks.has(row.id.toLowerCase())" :code="'document.host_recycle_bin.' + blocks.get(row.id.toLowerCase())!.action">
                    <ArtTableActionButton type="edit" test-id="document-recycle-recover" :title="t('documentRecycleBin.recoveryTitle')" :disabled="!catalogReady || changing || loading || recovering" v-on="{ click: rowControls.get(row.id.toLowerCase())?.recover }" />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentRecycleBin.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          :current-page="page"
          :page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="displayTotal" :disabled="changing || recovering"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50]"
          v-on="{ 'update:current-page': controls.pageModel, 'update:page-size': controls.sizeModel, change: controls.page }"
        />
      </div>
    </el-card>
    <ArtFormDialog v-if="confirmation" :open="true" :title="t(confirmation.kind === 'purge' ? 'documentRecycleBin.purge' : 'documentRecycleBin.recoveryTitle')"
      :confirm-label="t(confirmation.kind === 'purge' ? 'documentRecycleBin.purge' : 'users.confirm')" :cancel-label="t('users.cancel')"
      confirm-test-id="document-recycle-confirm" v-on="{ confirm: confirmationHandlers.confirm, 'update:open': confirmationHandlers.update }">
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.document-recycle-bin-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-recycle-bin-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-recycle-bin-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-recycle-bin-view__hint {
  margin-bottom: 12px;
}
</style>
