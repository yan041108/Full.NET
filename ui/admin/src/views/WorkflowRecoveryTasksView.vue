<script setup lang="ts">
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { ElButton, ElCard, ElDialog, ElForm, ElFormItem, ElInput, ElPagination, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type WorkflowRecoveryTaskResponse } from '@fullnet/client-contracts';
import { getWorkflowRecoveryTask, listWorkflowRecoveryTasks, reconcileWorkflowRecoveryTask, retryWorkflowRecoveryTask } from '../api/workflow-recovery-tasks';
import { recoveryStatuses, recoveryTrim, validRecoveryPage, validRecoveryTask } from '../api/workflow-recovery-guards';
import { workflowFormIdValid, workflowFormValueKey, workflowFormWriteRevisionValid } from '../api/workflow-form-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import PermissionGate from '../components/PermissionGate.vue';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';

type Kind = 'retry' | 'reconcile';
interface Block { kind: Kind; revision: number; }
const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-recovery-tasks') && session.can('workflow.recovery_tasks.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const items = ref<WorkflowRecoveryTaskResponse[]>([]), selectedId = ref<string>(), selected = ref<WorkflowRecoveryTaskResponse>();
const page = ref(1), pageSize = ref(20), total = ref(0), retryReason = ref(''), loading = ref(false), changing = ref(false), detailLoading = ref(false), catalogReady = ref(false), detailReady = ref(false);
const problem = ref<FullNetProblemDetails>(), problemIsRead = ref(true), owner = ref(0), detailOwner = ref(0), detailWindow = ref(0);
const receipt = ref<{ kind: Kind; id: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
const blocks = ref(new Map<string, Block>()), floors = new Map<string, WorkflowRecoveryTaskResponse>();
const scope = useAuthorizedViewScope(session, reset, async () => { if (canRead.value) await load(); });
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: Lease | undefined, detailRequest: Lease | undefined;
let write: { lease: Lease; target: WorkflowRecoveryTaskResponse; kind: Kind } | undefined;
// 只在当前身份代次内保留任务意图，切换详情不会丢失幂等键，也不落浏览器存储。
const intents = new Map<string, { taskId: string; key: string; revision: number }>();
const recovery = ref<{ lease: Lease; id: string; block: Block; ticket: number }>();
let recoveryTicket = 0;
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
const displayTotal = computed(() => Math.max(total.value, (page.value - 1) * pageSize.value + Math.max(page.value > 1 || loading.value ? 1 : 0, items.value.length)));
const idKey = (id: string) => id.toLowerCase();
const permission = (kind: Kind) => 'workflow.recovery_tasks.' + kind;
const begin = (code: string) => canRead.value ? scope.begin(code) : undefined;
const taskKey = (row: WorkflowRecoveryTaskResponse) => workflowFormValueKey({ ...row, id: idKey(row.id), instanceId: idKey(row.instanceId), stepId: row.stepId?.toLowerCase() ?? null });
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void load(); }, { flush: 'sync' });
function reset(): void {
  listRequest = detailRequest = undefined; write = undefined; recovery.value = undefined; intents.clear();
  items.value = []; selectedId.value = undefined; selected.value = undefined; page.value = 1; total.value = 0; retryReason.value = ''; receipt.value = undefined; problem.value = undefined;
  loading.value = changing.value = detailLoading.value = catalogReady.value = detailReady.value = false; blocks.value.clear(); floors.clear(); owner.value++; detailOwner.value++; detailWindow.value++;
}
function failure(error: unknown, read = true): void { problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_recovery_failed', title: t(read ? 'workflowRecoveryTasks.loadFailed' : 'workflowRecoveryTasks.operationFailed') }; problemIsRead.value = read; if (!read) showProblem(problem.value, t('workflowRecoveryTasks.operationFailed')); }
function assertTask(value: unknown, id?: string): asserts value is WorkflowRecoveryTaskResponse {
  if (!validRecoveryTask(value) || id && idKey(value.id) !== idKey(id)) throw Error('client.invalid_recovery_task');
  const floor = floors.get(idKey(value.id));
  if (floor && (value.revision < floor.revision || value.leaseGeneration < floor.leaseGeneration || idKey(value.instanceId) !== idKey(floor.instanceId) || value.stepId?.toLowerCase() !== floor.stepId?.toLowerCase() || value.kindKey !== floor.kindKey || value.revision === floor.revision && taskKey(value) !== taskKey(floor))) throw Error('client.stale_recovery_task');
}
function observe(row: WorkflowRecoveryTaskResponse): void {
  floors.set(idKey(row.id), { ...row }); const block = blocks.value.get(idKey(row.id));
  if (block && row.revision > block.revision) blocks.value.delete(idKey(row.id));
  for (const [signature, intent] of intents) if (intent.taskId === idKey(row.id) && row.revision > intent.revision) intents.delete(signature);
}
function markUnknown(operation: NonNullable<typeof write>): void {
  blocks.value.set(idKey(operation.target.id), { kind: operation.kind, revision: operation.target.revision });
  receipt.value = { kind: operation.kind, id: operation.target.id, outcome: 'unconfirmed' }; detailReady.value = false; catalogReady.value = false;
}
function cancelWrite(): void { if (!write) return; markUnknown(write); write.lease.cancel(); write = undefined; changing.value = false; owner.value++; detailOwner.value++; }
function finishRecovery(yes: boolean): void {
  const current = recovery.value; if (!current) return;
  if (yes && current.lease.current() && canRead.value && detailReady.value && catalogReady.value && selectedId.value && idKey(selectedId.value) === current.id && blocks.value.get(current.id) === current.block) blocks.value.delete(current.id);
  current.lease.finish(); recovery.value = undefined; detailOwner.value++;
}
function closeDetail(): void {
  cancelWrite(); detailRequest?.cancel(); detailRequest = undefined; detailLoading.value = false; detailReady.value = false; selectedId.value = undefined; selected.value = undefined; retryReason.value = ''; finishRecovery(false); detailOwner.value++; detailWindow.value++;
}
async function load(fallback = true): Promise<void> {
  if (!canRead.value || changing.value || recovery.value) return; listRequest?.cancel(); const request = begin('workflow.recovery_tasks.read'); if (!request) return;
  listRequest = request; const targetPage = page.value, size = pageSize.value; loading.value = true; catalogReady.value = false; problem.value = undefined; owner.value++;
  let fallbackPage: number | undefined;
  try {
    const result = await listWorkflowRecoveryTasks(targetPage, size, request.signal); if (!request.current() || listRequest !== request || page.value !== targetPage || pageSize.value !== size) return;
    if (!validRecoveryPage(result, targetPage, size)) throw Error('client.invalid_recovery_page'); for (const row of result.items) assertTask(row);
    // 完整校验后才推进修订下界，避免半个无效页面污染后续恢复。
    for (const row of result.items) observe(row); items.value = result.items; total.value = result.total; catalogReady.value = true;
    if (selected.value) { const row = items.value.find(value => idKey(value.id) === idKey(selected.value!.id)); if (!row || row.revision !== selected.value.revision) detailReady.value = false; }
    if (fallback && !result.items.length && targetPage > 1) { const last = Math.max(1, Math.ceil(result.total / size)); if (last < targetPage) fallbackPage = last; }
  } catch (error) { if (request.current() && listRequest === request) failure(error); }
  finally { if (listRequest === request) { listRequest = undefined; loading.value = false; owner.value++; } request.finish(); void nextTick(syncTableLayout); }
  if (fallbackPage !== undefined && canRead.value && page.value === targetPage) { closeDetail(); page.value = fallbackPage; items.value = []; await load(false); }
}
async function changePage(value: number): Promise<void> {
  if (!canRead.value || !Number.isInteger(value) || value < 1 || (value - 1) * pageSize.value > 2147483647 || value === page.value || recovery.value) return;
  closeDetail(); page.value = value; items.value = []; catalogReady.value = false; await load();
}
async function selectItem(row: WorkflowRecoveryTaskResponse): Promise<void> {
  if (!canRead.value || !catalogReady.value || loading.value || recovery.value || !items.value.some(item => idKey(item.id) === idKey(row.id) && taskKey(item) === taskKey(row)) || changing.value && selectedId.value === row.id) return;
  if (selectedId.value && idKey(selectedId.value) === idKey(row.id)) { await reloadDetail(); return; }
  closeDetail(); selectedId.value = row.id; detailWindow.value++; await reloadDetail();
}
async function reloadDetail(): Promise<void> {
  if (!selectedId.value || changing.value || recovery.value) return; const id = selectedId.value; detailRequest?.cancel(); const request = begin('workflow.recovery_tasks.read'); if (!request) return;
  detailRequest = request; detailLoading.value = true; detailReady.value = false; detailOwner.value++; problem.value = undefined;
  try {
    const row = await getWorkflowRecoveryTask(id, request.signal); if (!request.current() || detailRequest !== request || selectedId.value !== id) return; assertTask(row, id);
    const keep = selected.value && idKey(selected.value.id) === idKey(row.id) && selected.value.revision === row.revision;
    observe(row); selected.value = row; if (!keep) { retryReason.value = ''; detailWindow.value++; } detailReady.value = true;
  } catch (error) { if (request.current() && detailRequest === request) failure(error); }
  finally { if (detailRequest === request) { detailRequest = undefined; detailLoading.value = false; detailOwner.value++; } request.finish(); }
}
function canMutate(kind: Kind): boolean {
  const row = selected.value;
  return canRead.value && session.can(permission(kind)) && !!row && !!selectedId.value && idKey(selectedId.value) === idKey(row.id) && catalogReady.value && detailReady.value && !loading.value && !detailLoading.value && !changing.value && !recovery.value && !blocks.value.has(idKey(row.id)) && workflowFormWriteRevisionValid(row.revision)
    && (kind === 'retry' ? row.statusKey === 'failed' || row.statusKey === 'dead_lettered' : ['pending', 'failed', 'dead_lettered'].includes(row.statusKey)) && floors.get(idKey(row.id))?.revision === row.revision;
}
async function runWrite(kind: Kind): Promise<void> {
  if (!canMutate(kind)) return; const target = { ...selected.value! }, reason = recoveryTrim(retryReason.value);
  if (reason.length > 512 || kind === 'retry' && !reason) return; const lease = begin(permission(kind)); if (!lease) return;
  const signature = workflowFormValueKey([idKey(target.id), target.revision, kind, reason]);
  let intent = intents.get(signature);
  if (!intent) { intent = { taskId: idKey(target.id), key: kind + '-' + crypto.randomUUID(), revision: target.revision }; intents.set(signature, intent); }
  const body = { expectedRevision: target.revision, reason: reason || null, idempotencyKey: intent.key }, operation = { lease, target, kind };
  write = operation; changing.value = true; owner.value++; detailOwner.value++; receipt.value = { kind, id: target.id, outcome: 'processing' }; problem.value = undefined;
  try {
    const result = kind === 'retry' ? await retryWorkflowRecoveryTask(target.id, { ...body, reason }, lease.signal) : await reconcileWorkflowRecoveryTask(target.id, body, lease.signal);
    if (!lease.current() || write !== operation || !canRead.value) return; assertTask(result, target.id);
    if (result.revision <= target.revision || kind === 'reconcile' && !['succeeded', 'cancelled'].includes(result.statusKey)) throw Error('client.invalid_recovery_receipt');
    // 先确定写入回执再补读；补读失败不重发，也不把已完成操作降级为未知。
    write = undefined; changing.value = false; receipt.value = { kind, id: target.id, outcome: 'succeeded' }; observe(result); selected.value = result; detailReady.value = true;
    items.value = items.value.map(row => idKey(row.id) === idKey(result.id) ? result : row); retryReason.value = ''; owner.value++; detailOwner.value++; detailWindow.value++;
    showSuccess(t(kind === 'retry' ? 'workflowRecoveryTasks.retrySuccess' : 'workflowRecoveryTasks.reconcileSuccess')); await load();
  } catch (error) {
    if (lease.current() && write === operation) {
      const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
      if (unknown) markUnknown(operation); else receipt.value = { kind, id: target.id, outcome: 'failed' };
      catalogReady.value = false; detailReady.value = false; failure(error, false);
    }
  } finally { if (write === operation) { write = undefined; changing.value = false; owner.value++; detailOwner.value++; } lease.finish(); }
}
const retryItem = () => runWrite('retry'), reconcileItem = () => runWrite('reconcile');
function recover(): void {
  const row = selected.value, block = row && blocks.value.get(idKey(row.id)); if (!row || !block || !catalogReady.value || !detailReady.value || changing.value || detailLoading.value || loading.value || recovery.value) return;
  const lease = begin(permission(block.kind)); if (!lease) return; recovery.value = { lease, id: idKey(row.id), block, ticket: ++recoveryTicket }; detailOwner.value++;
}
const listControls = computed(() => { const ticket = owner.value; const current = () => ticket === owner.value && canRead.value; return { refresh: () => { if (current()) void load(); }, page: (value: number) => { if (current()) void changePage(value); }, select: (row: WorkflowRecoveryTaskResponse) => { if (current()) void selectItem(row); } }; });
const rowControls = computed(() => { const ticket = owner.value; return new Map(items.value.map(row => [row.id, { click: (event: Event) => { event.stopPropagation(); if (ticket === owner.value) void selectItem(row); } }])); });
const detailControls = computed(() => {
  const ticket = detailOwner.value, window = detailWindow.value, id = selectedId.value; const current = () => ticket === detailOwner.value && window === detailWindow.value && id === selectedId.value && canRead.value;
  return { retry: () => { if (current()) void retryItem(); }, reconcile: () => { if (current()) void reconcileItem(); }, reload: () => { if (current()) void reloadDetail(); }, close: () => { if (current()) closeDetail(); }, recover: () => { if (current()) recover(); }, reason: (value: string) => { if (current() && detailReady.value && !changing.value && !recovery.value) retryReason.value = value; } };
});
const recoveryControls = computed(() => { const value = recovery.value; const current = () => !!value && recovery.value === value; return { confirm: () => { if (current()) finishRecovery(true); }, cancel: () => { if (current()) finishRecovery(false); }, model: (open: boolean) => { if (!open && current()) finishRecovery(false); } }; });
function statusLabel(key: string): string { return recoveryStatuses.includes(key as typeof recoveryStatuses[number]) ? t(('workflowRecoveryTasks.status.' + key) as 'workflowRecoveryTasks.status.pending') : key; }
function statusTone(key: string): 'success' | 'warning' | 'info' | 'danger' | undefined { return key === 'succeeded' ? 'success' : key === 'pending' ? 'info' : key === 'failed' ? 'warning' : key === 'dead_lettered' ? 'danger' : undefined; }
</script>

<template>
  <section class="workflow-recovery-tasks art-page-stack art-full-height" :aria-busy="loading || detailLoading || changing">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowRecoveryTasks.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowRecoveryTasks.description') }}</p>
    <p v-if="problem" class="art-inline-alert" role="alert">{{ t(problemIsRead ? 'workflowRecoveryTasks.loadFailed' : 'workflowRecoveryTasks.operationFailed') }}</p>
    <p v-if="receipt" role="status" aria-live="polite" data-testid="workflow-recovery-tasks-receipt">{{ t(('workflowRecoveryTasks.recovery.' + receipt.outcome) as 'workflowRecoveryTasks.recovery.processing') }}</p>
    <PermissionGate code="workflow.recovery_tasks.read">
    <div v-if="canRead" class="workflow-recovery-tasks__layout art-split-layout">
      <ElCard class="workflow-recovery-tasks__list art-table-card" shadow="never">
        <template #header><h2>{{ t('workflowRecoveryTasks.listTitle') }}</h2><ElButton :disabled="loading || changing || !!recovery" data-testid="workflow-recovery-tasks-refresh" v-on="{ click: listControls.refresh }">{{ t('workflowRecoveryTasks.recoveryRetry') }}</ElButton></template>
        <div ref="tableMainRef" class="art-crud-table-main">
          <ElTable v-loading="loading" :data="items" :height="tableHeight" row-key="id" highlight-current-row empty-text="" :current-row-key="selectedId" v-on="{ 'row-click': listControls.select }">
            <ElTableColumn label="ID" min-width="200" show-overflow-tooltip><template #default="{ row }"><button type="button" class="workflow-recovery-tasks__id" data-testid="workflow-recovery-tasks-load" :disabled="!catalogReady || loading || !!recovery" translate="no" v-on="rowControls.get(row.id) ?? {}">{{ row.id }}</button></template></ElTableColumn>
            <ElTableColumn :label="t('workflowRecoveryTasks.fieldStatus')" width="120" align="center"><template #default="{ row }"><ElTag size="small" :type="statusTone(row.statusKey)" :data-testid="'workflow-recovery-tasks-status-' + row.statusKey">{{ statusLabel(row.statusKey) }}</ElTag></template></ElTableColumn>
            <ElTableColumn :label="t('workflowRecoveryTasks.fieldAttempts')" width="100" align="center"><template #default="{ row }"><span translate="no">{{ row.attemptCount }}</span></template></ElTableColumn>
            <template #empty><p class="art-empty-state">{{ t('workflowRecoveryTasks.emptyList') }}</p></template>
          </ElTable>
          <ElPagination v-if="displayTotal > 0" class="art-table-pagination" background layout="prev, pager, next, total" :current-page="page" :page-size="pageSize" :total="displayTotal" :disabled="!!recovery" v-on="{ 'update:current-page': listControls.page }" />
        </div>
      </ElCard>
      <ElCard class="workflow-recovery-tasks__detail art-form-card" shadow="never" data-testid="workflow-recovery-tasks-detail" :aria-busy="detailLoading">
        <template #header><h2>{{ t('workflowRecoveryTasks.detailTitle') }}</h2></template>
        <p v-if="!selectedId" class="art-empty-state">{{ t('workflowRecoveryTasks.selectDetail') }}</p>
        <template v-else>
          <ElButton :disabled="detailLoading || changing || !!recovery" data-testid="workflow-recovery-tasks-reload-detail" v-on="{ click: detailControls.reload }">{{ t('workflowRecoveryTasks.recoveryRetry') }}</ElButton>
          <ElButton :disabled="!!recovery" data-testid="workflow-recovery-tasks-close-detail" v-on="{ click: detailControls.close }">{{ t('workflowRecoveryTasks.recoveryClose') }}</ElButton>
          <template v-if="selected">
            <p>{{ t('workflowRecoveryTasks.fieldStatus') }}: <ElTag :type="statusTone(selected.statusKey)" :data-testid="'workflow-recovery-tasks-detail-status-' + selected.statusKey">{{ statusLabel(selected.statusKey) }}</ElTag></p>
            <p>{{ t('workflowRecoveryTasks.fieldAttempts') }}: <strong translate="no">{{ selected.attemptCount }}</strong></p>
            <p>{{ t('workflowRecoveryTasks.fieldRevision') }}: <strong translate="no">{{ selected.revision }}</strong></p>
            <p>{{ t('workflowRecoveryTasks.fieldLease') }}: <code translate="no">{{ selected.leaseOwnerKey ?? t('workflowRecoveryTasks.none') }}</code></p>
            <p>{{ formatAdminDateTime(locale, selected.updatedAtUtc) }}</p>
            <p v-if="selected.lastError">{{ t('workflowRecoveryTasks.fieldLastError') }}: {{ selected.lastError }}</p>
            <ElForm v-if="session.can('workflow.recovery_tasks.retry') || session.can('workflow.recovery_tasks.reconcile')" label-position="top" @submit.prevent>
              <ElFormItem :label="t('workflowRecoveryTasks.fieldReason')" :required="session.can('workflow.recovery_tasks.retry')">
                <ElInput :model-value="retryReason" :disabled="!detailReady || changing || !!recovery" data-testid="workflow-recovery-tasks-retry-reason" maxlength="512" v-on="{ 'update:model-value': detailControls.reason }" />
                <p>{{ t('workflowRecoveryTasks.recoveryReasonHint') }}</p>
              </ElFormItem>
              <PermissionGate code="workflow.recovery_tasks.retry"><ElButton data-testid="workflow-recovery-tasks-retry" type="primary" :disabled="!canMutate('retry') || !recoveryTrim(retryReason)" v-on="{ click: detailControls.retry }">{{ t('workflowRecoveryTasks.retry') }}</ElButton></PermissionGate>
              <PermissionGate code="workflow.recovery_tasks.reconcile"><ElButton data-testid="workflow-recovery-tasks-reconcile" :disabled="!canMutate('reconcile')" v-on="{ click: detailControls.reconcile }">{{ t('workflowRecoveryTasks.reconcile') }}</ElButton></PermissionGate>
              <ElButton v-if="blocks.has(selected.id.toLowerCase())" :disabled="!detailReady || !catalogReady || changing || !!recovery" data-testid="workflow-recovery-tasks-recover" v-on="{ click: detailControls.recover }">{{ t('workflowRecoveryTasks.recoveryUnlock') }}</ElButton>
            </ElForm>
          </template>
        </template>
      </ElCard>
    </div>
    </PermissionGate>
    <ElDialog v-if="recovery" :key="recovery.ticket" :model-value="true" :title="t('workflowRecoveryTasks.recoveryTitle')" :close-on-click-modal="false" destroy-on-close v-on="{ 'update:model-value': recoveryControls.model }">
      <p>{{ t('workflowRecoveryTasks.recoveryHint') }}</p>
      <template #footer><ElButton v-on="{ click: recoveryControls.cancel }">{{ t('workflowRecoveryTasks.recoveryClose') }}</ElButton><ElButton type="warning" v-on="{ click: recoveryControls.confirm }">{{ t('workflowRecoveryTasks.recoveryUnlock') }}</ElButton></template>
    </ElDialog>
  </section>
</template>

<style scoped>
.workflow-recovery-tasks { min-height: 0; }
.workflow-recovery-tasks__layout { display: flex; flex: 1; gap: 12px; min-height: 0; }
.workflow-recovery-tasks__list { flex: 1 1 0; min-width: 0; }
.workflow-recovery-tasks__list :deep(.el-card__body) { padding-top: 0; }
.workflow-recovery-tasks__list :deep(.el-card__header) { padding: 12px 16px; }
.workflow-recovery-tasks__list :deep(.el-card__header) h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-recovery-tasks__detail { flex: 0 0 320px; max-width: 400px; overflow: auto; }
.workflow-recovery-tasks__detail :deep(.el-card__header) { padding: 12px 16px; }
.workflow-recovery-tasks__detail :deep(.el-card__body) { padding: 12px 16px; }
.workflow-recovery-tasks__id { padding: 0; border: none; background: none; color: var(--el-color-primary); font: inherit; cursor: pointer; text-align: left; }
@media (max-width: 960px) {
  .workflow-recovery-tasks__layout { flex-direction: column; }
  .workflow-recovery-tasks__detail { flex: none; max-width: none; width: 100%; }
}
</style>
