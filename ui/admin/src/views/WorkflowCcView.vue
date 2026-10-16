<script setup lang="ts">
import { computed, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import { ElButton, ElCard, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { listMyWorkflowCc, markWorkflowCcRead, type WorkflowCcResponse } from '../api/workflow-cc';
import { validWorkflowCc, validWorkflowCcList, validWorkflowCcReceipt, workflowCcBindingKey, workflowCcTimestampTicks, workflowCcPersistedReadMatches } from '../api/workflow-cc-guards';
import { workflowFormIdValid } from '../api/workflow-form-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import PermissionGate from '../components/PermissionGate.vue';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import { findWorkflowBusinessDetailRoute, formatWorkflowBusinessLabel } from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';

const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), router = inject(routerKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-cc') && session.can('workflow.cc.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const records = ref<WorkflowCcResponse[]>([]), loading = ref(false), catalogReady = ref(false), actingId = ref<string>(), navigating = ref(false), problem = ref<FullNetProblemDetails>(), problemIsRead = ref(true), owner = ref(0);
const receipt = ref<{ id: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
const known = new Map<string, WorkflowCcResponse>(), scope = useAuthorizedViewScope(session, reset, async () => { if (canRead.value) await load(); });
const pendingReadTimes = new Map<string, string>();
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: Lease | undefined, operation: { lease: Lease; target: WorkflowCcResponse } | undefined, navigation: Lease | undefined;
const begin = (permission: string) => canRead.value ? scope.begin(permission) : undefined;
const busy = computed(() => loading.value || !!actingId.value || navigating.value);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({ bottomOffset: 8 }); watchLoading(busy);
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void load(); }, { flush: 'sync' });
function reset(): void { listRequest = undefined; operation = undefined; navigation = undefined; records.value = []; loading.value = navigating.value = catalogReady.value = false; actingId.value = undefined; problem.value = undefined; receipt.value = undefined; known.clear(); pendingReadTimes.clear(); owner.value++; }
function failure(error: unknown, read: boolean): void { problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_cc_failed', title: t(read ? 'workflowCc.loadFailed' : 'workflowCc.operationFailed') }; problemIsRead.value = read; if (!read) showProblem(problem.value, t('workflowCc.operationFailed')); }
function assertRecord(row: unknown, authoritative = false): asserts row is WorkflowCcResponse {
  if (!validWorkflowCc(row)) throw Error('client.invalid_workflow_cc'); const previous = known.get(row.id.toLowerCase());
  if (!previous) return;
  const pending = pendingReadTimes.get(row.id.toLowerCase()), sameReadTime = workflowCcTimestampTicks(row.readAtUtc) === workflowCcTimestampTicks(previous.readAtUtc);
  const firstPrecisionRead = authoritative && pending !== undefined && row.readAtUtc !== null && workflowCcPersistedReadMatches(pending, row.readAtUtc);
  if (workflowCcBindingKey(row) !== workflowCcBindingKey(previous) || previous.readAtUtc !== null && (row.readAtUtc === null || !sameReadTime && !firstPrecisionRead)) throw Error('client.stale_workflow_cc');
}
function observe(row: WorkflowCcResponse): void { known.set(row.id.toLowerCase(), { ...row }); }
function current(row: WorkflowCcResponse): boolean { return validWorkflowCc(row) && records.value.some(item => item.id.toLowerCase() === row.id.toLowerCase() && workflowCcBindingKey(item) === workflowCcBindingKey(row) && item.readAtUtc === row.readAtUtc); }
async function load(): Promise<void> {
  if (!canRead.value || operation || navigating.value) return; listRequest?.cancel(); const request = begin('workflow.cc.read'); if (!request) return;
  listRequest = request; loading.value = true; catalogReady.value = false; problem.value = undefined; owner.value++;
  try {
    const rows = await listMyWorkflowCc(request.signal); if (!request.current() || listRequest !== request) return;
    if (!validWorkflowCcList(rows)) throw Error('client.invalid_workflow_cc_list'); for (const row of rows) assertRecord(row, true);
    // 全部行先验证再接入，不能让无效目录的一部分提前推进已读下界。
    for (const row of rows) { observe(row); pendingReadTimes.delete(row.id.toLowerCase()); } records.value = rows; catalogReady.value = true;
    const pending = receipt.value; if (pending?.outcome === 'unconfirmed' && rows.some(row => row.id.toLowerCase() === pending.id.toLowerCase() && row.readAtUtc !== null)) receipt.value = { id: pending.id, outcome: 'succeeded' };
  } catch (error) { if (request.current() && listRequest === request) failure(error, true); }
  finally { if (listRequest === request) { listRequest = undefined; loading.value = false; owner.value++; } request.finish(); void nextTick(updateTableHeight); }
}
async function markRead(row: WorkflowCcResponse): Promise<void> {
  if (!canRead.value || !session.can('workflow.cc.mark_read') || !catalogReady.value || busy.value || !current(row) || row.readAtUtc !== null) return;
  const request = begin('workflow.cc.mark_read'); if (!request) return; const target = { ...row }, action = { lease: request, target }; operation = action; actingId.value = target.id; problem.value = undefined; owner.value++; receipt.value = { id: target.id, outcome: 'processing' };
  try {
    const result = await markWorkflowCcRead(target.id, request.signal); if (!request.current() || operation !== action || !canRead.value) return;
    if (!validWorkflowCcReceipt(result, target.id)) throw Error('client.invalid_workflow_cc_receipt'); const updated = { ...target, readAtUtc: result.readAtUtc }; assertRecord(updated);
    observe(updated); pendingReadTimes.set(target.id.toLowerCase(), result.readAtUtc); records.value = records.value.map(item => item.id.toLowerCase() === target.id.toLowerCase() ? updated : item); receipt.value = { id: target.id, outcome: 'succeeded' }; showSuccess(t('workflowCc.markReadSuccess'));
  } catch (error) {
    if (request.current() && operation === action) {
      const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
      receipt.value = { id: target.id, outcome: unknown ? 'unconfirmed' : 'failed' }; catalogReady.value = false; failure(error, false);
    }
  } finally { if (operation === action) { operation = undefined; actingId.value = undefined; owner.value++; } request.finish(); }
}
/** 仅当前确认目录的原业务绑定可进入白名单路由，权限检查发生在实际导航前。 */
async function openBusinessDetail(row: WorkflowCcResponse): Promise<void> {
  if (!canRead.value || !catalogReady.value || busy.value || !current(row) || !router) return; const destination = findWorkflowBusinessDetailRoute(row.businessType, session.can); if (!destination) return;
  const request = begin('workflow.cc.read'); if (!request) return; navigation = request; navigating.value = true; owner.value++; problem.value = undefined;
  try { await router.push({ name: destination.routeName, query: { [destination.idQueryKey]: row.businessId } }); }
  catch (error) { if (request.current() && navigation === request) failure(error, false); }
  finally { if (navigation === request) { navigation = undefined; navigating.value = false; owner.value++; } request.finish(); }
}
const controls = computed(() => { const ticket = owner.value; return { refresh: () => { if (ticket === owner.value && canRead.value) void load(); } }; });
const rowControls = computed(() => { const ticket = owner.value; const currentOwner = () => ticket === owner.value && canRead.value; return new Map(records.value.map(row => [row.id, { mark: () => { if (currentOwner()) void markRead(row); }, business: () => { if (currentOwner()) void openBusinessDetail(row); } }])); });
</script>

<template>
  <section class="workflow-cc art-page-stack art-full-height" :aria-busy="busy">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowCc.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowCc.caption') }}</p>
    <p v-if="problem" class="art-inline-alert" role="alert">{{ t(problemIsRead ? 'workflowCc.loadFailed' : 'workflowCc.operationFailed') }}</p>
    <p v-if="receipt" role="status" aria-live="polite" data-testid="workflow-cc-receipt">{{ t(('workflowCc.recovery.' + receipt.outcome) as 'workflowCc.recovery.processing') }}</p>
    <PermissionGate code="workflow.cc.read">
      <ElCard v-if="canRead" class="workflow-cc__card art-table-card" shadow="never">
        <template #header><ElButton data-testid="workflow-cc-reload" :disabled="busy" v-on="{ click: controls.refresh }">{{ t('workflowCc.reload') }}</ElButton></template>
        <div ref="tableMainRef" class="art-crud-table-main">
          <ElTable v-loading="loading" :data="records" :height="tableHeight" row-key="id" class="workflow-cc__table" empty-text="" :row-class-name="({ row }) => row.readAtUtc === null ? 'is-unread' : ''">
            <ElTableColumn :label="t('workflowCc.business')" min-width="200"><template #default="{ row }"><div class="workflow-cc__business">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
              <ElButton v-if="findWorkflowBusinessDetailRoute(row.businessType, session.can)" link type="primary" data-testid="workflow-cc-view-document" :disabled="!catalogReady || busy" v-on="{ click: rowControls.get(row.id)?.business }">{{ t('workflow.business.viewDocument') }}</ElButton>
            </div></template></ElTableColumn>
            <ElTableColumn :label="t('workflowCc.node')" min-width="120" show-overflow-tooltip><template #default="{ row }"><code translate="no">{{ row.nodeKey }}</code></template></ElTableColumn>
            <ElTableColumn :label="t('workflowCc.createdAt')" width="168"><template #default="{ row }"><time translate="no" :datetime="row.createdAtUtc">{{ formatAdminDateTime(locale, row.createdAtUtc) }}</time></template></ElTableColumn>
            <ElTableColumn :label="t('workflowCc.status')" width="96" align="center"><template #default="{ row }"><ElTag size="small" :type="row.readAtUtc === null ? 'warning' : 'info'">{{ t(row.readAtUtc === null ? 'workflowCc.unread' : 'workflowCc.read') }}</ElTag></template></ElTableColumn>
            <ElTableColumn :label="t('workflowCc.actions')" width="120" fixed="right"><template #default="{ row }"><PermissionGate code="workflow.cc.mark_read">
              <ElButton size="small" data-testid="workflow-cc-mark-read" :disabled="row.readAtUtc !== null || !catalogReady || busy" :loading="actingId === row.id" v-on="{ click: rowControls.get(row.id)?.mark }">{{ t('workflowCc.markRead') }}</ElButton>
            </PermissionGate></template></ElTableColumn>
            <template #empty><p v-if="!loading" class="workflow-cc__empty">{{ t('workflowCc.empty') }}</p></template>
          </ElTable>
        </div>
      </ElCard>
    </PermissionGate>
  </section>
</template>

<style scoped>
.workflow-cc { display: grid; gap: 1rem; min-height: 0; }
.workflow-cc__card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
.workflow-cc__card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}
.workflow-cc__business { display: grid; gap: 4px; }
.workflow-cc :deep(tr.is-unread td:first-child) { box-shadow: inset 3px 0 0 var(--el-color-warning); }
.workflow-cc__empty { padding: 2rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
</style>
