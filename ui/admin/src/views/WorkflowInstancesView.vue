<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, shallowRef, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import { ElButton, ElCard, ElInput, ElMessageBox, ElOption, ElPagination, ElSelect, ElTabPane, ElTabs, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { cancelWorkflowInstance, getWorkflowInstance, listMyWorkflowInstances, listWorkflowInstanceExecutionLogs, listWorkflowInstances, pauseWorkflowInstance, recoverWorkflowInstance, reassignWorkflowInstance, resumeWorkflowInstance, type WorkflowExecutionLogResponse, type WorkflowInstanceListItemResponse, type WorkflowInstanceResponse } from '../api/workflow-instances';
import { instanceStatuses, instanceTrim, validWorkflowInstance, validWorkflowInstancePage, validWorkflowLogs, workflowInstanceBinding } from '../api/workflow-instance-guards';
import { workflowFormIdValid, workflowFormValueKey, workflowFormWriteRevisionValid } from '../api/workflow-form-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import WorkflowInstanceReassignDialog from './workflow/WorkflowInstanceReassignDialog.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { showSuccess } from '../feedback/fullNetMessage';
import { findWorkflowBusinessDetailRoute, formatWorkflowBusinessLabel } from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
type Kind = 'cancel' | 'pause' | 'resume' | 'recover' | 'reassign';
type ReassignInput = { assigneeUserId: string; reason: string | null };
const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), router = inject(routerKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-instances') && session.can('workflow.instances.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const statusFilterOptions = instanceStatuses;
type InstanceStatusKey = typeof instanceStatuses[number];
const activeTab = ref<'mine' | 'all'>('mine'), listItems = ref<WorkflowInstanceListItemResponse[]>([]), listPage = ref(1), listPageSize = ref(20), listTotal = ref(0), listLoading = ref(false), catalogReady = ref(false);
const statusFilter = ref<string>(), definitionKeyFilter = ref(''), applied = ref<{ statusKey?: string; definitionKey?: string }>({});
const instanceId = ref(''), instance = ref<WorkflowInstanceResponse>(), executionLogs = ref<WorkflowExecutionLogResponse[]>([]), loading = ref(false), logsLoading = ref(false), detailReady = ref(false), logsReady = ref(false);
const problem = ref<FullNetProblemDetails>(), logsProblem = ref<FullNetProblemDetails>(), reassignOpen = ref(false), reassignWindow = ref(0), listOwner = ref(0), detailOwner = ref(0), detailWindow = ref(0), actionKind = ref<Kind>(), navigating = ref(false);
const receipt = ref<{ kind: Kind; id: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
const blocks = ref(new Map<string, { kind: Kind; revision: number }>()), floors = new Map<string, WorkflowInstanceResponse>(), bindings = new Map<string, string>(), logFacts = new Map<string, string>(), intents = new Map<string, { id: string; revision: number; key: string }>();
const scope = useAuthorizedViewScope(session, resetScope, async () => { if (canRead.value) await loadList(); });
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let detailRequest: Lease | undefined, listRequest: Lease | undefined, logsRequest: Lease | undefined, navigation: Lease | undefined;
let operation: { lease: Lease; target: WorkflowInstanceResponse; kind: Kind; sent: boolean; previous: typeof receipt.value } | undefined, resetting = false;
let lastContext = authContext();
const recovery = shallowRef<{ lease: Lease; id: string; block: { kind: Kind; revision: number }; window: number }>();
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(listLoading);
const idKey = (id: string) => id.toLowerCase(), requestedId = () => instanceTrim(instanceId.value), permission = (kind: Kind) => 'workflow.instances.' + (kind === 'reassign' ? 'recover' : kind);
const begin = (code: string) => canRead.value ? scope.begin(code) : undefined;
const canListAll = computed(() => canRead.value && session.can('workflow.instances.list'));
const mutating = computed(() => !!actionKind.value || reassignOpen.value || !!recovery.value || navigating.value);
const cancelling = computed(() => actionKind.value === 'cancel'), pausing = computed(() => actionKind.value === 'pause'), resuming = computed(() => actionKind.value === 'resume'), recovering = computed(() => actionKind.value === 'recover');
const displayTotal = computed(() => Math.max(listTotal.value, (listPage.value - 1) * listPageSize.value + Math.max(listPage.value > 1 || listLoading.value ? 1 : 0, listItems.value.length)));
function markUnknown(value: NonNullable<typeof operation>): void { blocks.value.set(idKey(value.target.id), { kind: value.kind, revision: value.target.revision }); receipt.value = { kind: value.kind, id: value.target.id, outcome: 'unconfirmed' }; detailReady.value = false; }
function cancelAction(): void { if (!operation) return; if (operation.sent) markUnknown(operation); else receipt.value = operation.previous; operation.lease.cancel(); operation = undefined; actionKind.value = undefined; detailOwner.value++; }
function closeReassign(): void { if (operation?.kind === 'reassign') cancelAction(); reassignOpen.value = false; reassignWindow.value++; detailOwner.value++; }
function clearDetail(): void { cancelAction(); detailRequest?.cancel(); logsRequest?.cancel(); navigation?.cancel(); recovery.value?.lease.cancel(); detailRequest = logsRequest = navigation = undefined; recovery.value = undefined; instance.value = undefined; executionLogs.value = []; loading.value = logsLoading.value = detailReady.value = logsReady.value = navigating.value = false; reassignOpen.value = false; reassignWindow.value++; problem.value = logsProblem.value = undefined; detailWindow.value++; detailOwner.value++; }
function authContext(): string { const user = session.currentUser; return workflowFormValueKey([session.state, user?.id, user?.sessionId, user?.tenantId, user?.scope, user?.actorScope]); }
function resetScope(): void {
  const context = authContext(), preserveRecovery = session.state === 'authenticated' && context === lastContext; lastContext = context;
  resetting = true;
  try {
    listRequest = undefined; clearDetail(); instanceId.value = ''; activeTab.value = 'mine'; listItems.value = []; listTotal.value = 0; listPage.value = 1; listLoading.value = catalogReady.value = false; statusFilter.value = undefined; definitionKeyFilter.value = ''; applied.value = {};
    // 同一会话的停用、路由或权限变化不等于服务端回滚；保留未知门槛和幂等意图，界面内容仍全部清空。
    if (!preserveRecovery) { receipt.value = undefined; blocks.value.clear(); floors.clear(); bindings.clear(); logFacts.clear(); intents.clear(); }
    else if (receipt.value?.outcome !== 'unconfirmed') receipt.value = undefined;
    listOwner.value++;
  } finally { resetting = false; }
}
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void loadList(); }, { flush: 'sync' });
watch(() => requestedId().toLowerCase(), () => { if (!resetting) clearDetail(); }, { flush: 'sync' });
watch(activeTab, () => { if (resetting) return; listRequest?.cancel(); listRequest = undefined; listLoading.value = catalogReady.value = false; listItems.value = []; listTotal.value = 0; listPage.value = 1; instanceId.value = ''; clearDetail(); listOwner.value++; if (activeTab.value === 'mine' || activeTab.value === 'all' && canListAll.value) void loadList(); }, { flush: 'sync' });
function toProblem(error: unknown, code: MessageKey = 'workflowInstances.loadFailed'): FullNetProblemDetails { return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_instance_failed', title: t(code) }; }
function assertBinding(row: WorkflowInstanceResponse | WorkflowInstanceListItemResponse): void { const previous = bindings.get(idKey(row.id)); if (previous && previous !== workflowInstanceBinding(row)) throw Error('client.invalid_workflow_instance_binding'); }
function assertInstance(value: unknown, id: string): asserts value is WorkflowInstanceResponse {
  if (!validWorkflowInstance(value) || idKey(value.id) !== idKey(id)) throw Error('client.invalid_workflow_instance'); assertBinding(value); const floor = floors.get(idKey(value.id));
  if (floor && (value.formVersionId.toLowerCase() !== floor.formVersionId.toLowerCase() || value.revision < floor.revision || value.revision === floor.revision && value.statusKey !== floor.statusKey)) throw Error('client.stale_workflow_instance');
}
function observe(row: WorkflowInstanceResponse): void { floors.set(idKey(row.id), { ...row }); bindings.set(idKey(row.id), workflowInstanceBinding(row)); const block = blocks.value.get(idKey(row.id)); if (block && row.revision > block.revision) blocks.value.delete(idKey(row.id)); for (const [key, intent] of intents) if (intent.id === idKey(row.id) && row.revision > intent.revision) intents.delete(key); }
function listKey(): string { return workflowFormValueKey([activeTab.value, listPage.value, listPageSize.value, applied.value]); }
async function loadList(fallback = true): Promise<void> {
  if (!canRead.value || actionKind.value || recovery.value || activeTab.value !== 'mine' && (activeTab.value !== 'all' || !canListAll.value)) return;
  listRequest?.cancel(); const request = begin(activeTab.value === 'all' ? 'workflow.instances.list' : 'workflow.instances.read'); if (!request) return;
  listRequest = request; const key = listKey(), page = listPage.value, size = listPageSize.value, mine = activeTab.value === 'mine', actor = session.currentUser!.id; listLoading.value = true; catalogReady.value = false; problem.value = undefined; listOwner.value++; let fallbackPage: number | undefined;
  try { const query = { page, pageSize: size, ...applied.value }, result = await (mine ? listMyWorkflowInstances : listWorkflowInstances)(query, request.signal); if (!request.current() || listRequest !== request || key !== listKey()) return;
    if (!validWorkflowInstancePage(result, page, size) || mine && result.items.some(row => idKey(row.startedById) !== idKey(actor))) throw Error('client.invalid_workflow_instance_list'); for (const row of result.items) assertBinding(row);
    for (const row of result.items) bindings.set(idKey(row.id), workflowInstanceBinding(row)); listItems.value = result.items; listTotal.value = result.total; catalogReady.value = true;
    if (fallback && !result.items.length && page > 1) { const last = Math.max(1, Math.ceil(result.total / size)); if (last < page) fallbackPage = last; }
  } catch (error) { if (request.current() && listRequest === request) problem.value = toProblem(error, 'workflowInstances.listFailed'); }
  finally { if (listRequest === request) { listRequest = undefined; listLoading.value = false; listOwner.value++; } request.finish(); void syncTableLayout(); }
  if (fallbackPage !== undefined && canRead.value && key === listKey()) { listPage.value = fallbackPage; listItems.value = []; await loadList(false); }
}
async function applyFilters(): Promise<void> { if (!canRead.value || actionKind.value || recovery.value) return; const statusKey = statusFilter.value ? instanceTrim(statusFilter.value) || undefined : undefined, definitionKey = instanceTrim(definitionKeyFilter.value) || undefined; if (statusKey && !instanceStatuses.includes(statusKey as InstanceStatusKey)) return; applied.value = { statusKey, definitionKey }; listPage.value = 1; instanceId.value = ''; clearDetail(); listItems.value = []; await loadList(); }
async function changePage(value: number): Promise<void> { if (!canRead.value || !Number.isInteger(value) || value < 1 || (value - 1) * listPageSize.value > 2147483647 || value === listPage.value) return; instanceId.value = ''; clearDetail(); listPage.value = value; listItems.value = []; await loadList(); }
async function selectListItem(row: WorkflowInstanceListItemResponse): Promise<void> { if (!canRead.value || !catalogReady.value || listLoading.value || actionKind.value || recovery.value || !listItems.value.some(item => workflowFormValueKey(item) === workflowFormValueKey(row))) return; instanceId.value = row.id; await load(); }
async function loadDetail(): Promise<void> {
  const id = requestedId(); if (!canRead.value || !workflowFormIdValid(id) || actionKind.value || reassignOpen.value || recovery.value) return;
  detailRequest?.cancel(); const request = begin('workflow.instances.read'); if (!request) return; detailRequest = request; loading.value = true; detailReady.value = false; problem.value = undefined; detailOwner.value++;
  try { const row = await getWorkflowInstance(id, request.signal); if (!request.current() || detailRequest !== request || idKey(requestedId()) !== idKey(id)) return; assertInstance(row, id); observe(row); instance.value = row; detailReady.value = true; }
  catch (error) { if (request.current() && detailRequest === request) { problem.value = toProblem(error); if (isFullNetProblemDetails(error) && [401,403,404].includes(error.status)) { instance.value = undefined; executionLogs.value = []; logsRequest?.cancel(); logsRequest = undefined; logsLoading.value = logsReady.value = false; } } }
  finally { if (detailRequest === request) { detailRequest = undefined; loading.value = false; detailOwner.value++; } request.finish(); }
}
async function loadLogs(): Promise<void> {
  const id = requestedId(); if (!canRead.value || !workflowFormIdValid(id) || actionKind.value || reassignOpen.value || recovery.value) return; logsRequest?.cancel(); const request = begin('workflow.instances.read'); if (!request) return; logsRequest = request; logsLoading.value = true; logsReady.value = false; logsProblem.value = undefined; detailOwner.value++;
  try { const logs = await listWorkflowInstanceExecutionLogs(id, request.signal); if (!request.current() || logsRequest !== request || idKey(requestedId()) !== idKey(id)) return;
    if (!validWorkflowLogs(logs, id)) throw Error('client.invalid_workflow_instance_logs'); for (const log of logs) { const previous = logFacts.get(idKey(log.id)); if (previous && previous !== workflowFormValueKey(log)) throw Error('client.stale_workflow_instance_log'); } for (const log of logs) logFacts.set(idKey(log.id), workflowFormValueKey(log)); executionLogs.value = logs; logsReady.value = true;
  } catch (error) { if (request.current() && logsRequest === request) logsProblem.value = toProblem(error); }
  finally { if (logsRequest === request) { logsRequest = undefined; logsLoading.value = false; detailOwner.value++; } request.finish(); }
}
async function load(): Promise<void> { if (!canRead.value || !workflowFormIdValid(requestedId()) || mutating.value) return; await Promise.all([loadDetail(), loadLogs()]); }
function allowed(kind: Kind): boolean { const row = instance.value; return canRead.value && session.can(permission(kind)) && !!row && detailReady.value && !loading.value && !actionKind.value && !recovery.value && !navigating.value && (!reassignOpen.value || kind === 'reassign') && idKey(requestedId()) === idKey(row.id) && !blocks.value.has(idKey(row.id)) && workflowFormWriteRevisionValid(row.revision) && (kind === 'cancel' ? ['active','suspended'].includes(row.statusKey) : kind === 'pause' || kind === 'reassign' ? row.statusKey === 'active' && (kind !== 'reassign' || !!row.activeTodoId) : row.statusKey === 'suspended'); }
const canSearch = computed(() => canRead.value && workflowFormIdValid(requestedId()) && !loading.value && !mutating.value), canCancel = computed(() => allowed('cancel')), canPause = computed(() => allowed('pause')), canResume = computed(() => allowed('resume')), canRecover = computed(() => allowed('recover')), canReassign = computed(() => !reassignOpen.value && allowed('reassign'));
function openReassign(): void { if (!canReassign.value) return; reassignOpen.value = true; reassignWindow.value++; detailOwner.value++; }
async function runAction(kind: Kind, input?: ReassignInput): Promise<void> {
  if (!allowed(kind) || kind === 'reassign' && (!reassignOpen.value || !input || !workflowFormIdValid(input.assigneeUserId))) return; const current = instance.value!, target = { ...current }, window = detailWindow.value, request = begin(permission(kind)); if (!request) return;
  const action = { lease: request, target, kind, sent: false, previous: receipt.value }; operation = action; actionKind.value = kind; detailOwner.value++; listOwner.value++;
  const owns = () => request.current() && operation === action && canRead.value && detailWindow.value === window && idKey(requestedId()) === idKey(target.id);
  try {
    let reason: string | null = input?.reason == null ? null : instanceTrim(input.reason) || null; const assigneeUserId = input?.assigneeUserId;
    if (kind === 'recover') { const prompt = await ElMessageBox.prompt(t('workflowInstances.recoverConfirm'), t('workflowInstances.recoverTitle'), { inputType: 'textarea', inputValidator: (value: string) => (!!value && !!instanceTrim(value) && instanceTrim(value).length <= 500 && !/\p{Cc}/u.test(value)) || t('workflowInstances.recoverReasonRequired'), confirmButtonText: t('workflowInstances.recover'), cancelButtonText: t('status.back') }); reason = instanceTrim(prompt.value); if (!reason || reason.length > 500 || /\p{Cc}/u.test(reason)) return; }
    else { const prefix = 'workflowInstances.' + kind; await ElMessageBox.confirm(t((prefix + 'Confirm') as MessageKey), t((kind === 'reassign' ? prefix : prefix + 'Title') as MessageKey), { type: kind === 'resume' ? undefined : 'warning', confirmButtonText: t(prefix as MessageKey), cancelButtonText: t('status.back') }); }
    if (!owns() || instance.value !== current || reason && (reason.length > 512 || kind === 'reassign' && (reason.length > 500 || /\p{Cc}/u.test(reason)))) return;
    const signature = workflowFormValueKey([idKey(target.id), target.revision, kind, reason, assigneeUserId?.toLowerCase()]); let intent = intents.get(signature); if (!intent) { intent = { id: idKey(target.id), revision: target.revision, key: kind + '-' + crypto.randomUUID() }; intents.set(signature, intent); }
    const body = { expectedRevision: target.revision, reason, idempotencyKey: intent.key }; action.sent = true; problem.value = undefined; receipt.value = { kind, id: target.id, outcome: 'processing' };
    const result = kind === 'reassign' ? await reassignWorkflowInstance(target.id, { ...body, assigneeUserId: assigneeUserId! }, request.signal) : kind === 'recover' ? await recoverWorkflowInstance(target.id, { ...body, reason: reason! }, request.signal) : await { cancel: cancelWorkflowInstance, pause: pauseWorkflowInstance, resume: resumeWorkflowInstance }[kind](target.id, body, request.signal);
    if (!owns()) return; assertInstance(result, target.id); if (result.revision <= target.revision) throw Error('client.invalid_workflow_instance_receipt');
    // 固定成功并解除原写入归属，再补读详情、轨迹和目录；后续读取失败不能撤销回执。
    operation = undefined; actionKind.value = undefined; receipt.value = { kind, id: target.id, outcome: 'succeeded' }; observe(result); instance.value = result; detailReady.value = false; reassignOpen.value = false; reassignWindow.value++; detailOwner.value++;
    showSuccess(t(('workflowInstances.' + kind + 'Success') as MessageKey)); await Promise.all([loadDetail(), loadLogs(), loadList()]);
  } catch (error) { if (owns() && error !== 'cancel' && error !== 'close') { if (action.sent) { const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408; if (unknown) markUnknown(action); else receipt.value = { kind, id: target.id, outcome: 'failed' }; detailReady.value = false; } problem.value = toProblem(error); } }
  finally { if (operation === action) { operation = undefined; actionKind.value = undefined; detailOwner.value++; listOwner.value++; } request.finish(); }
}
const cancelInstance = () => runAction('cancel'), pauseInstance = () => runAction('pause'), resumeInstance = () => runAction('resume'), recoverInstance = () => runAction('recover'), submitReassign = (input: ReassignInput) => runAction('reassign', { ...input });
async function recoverUnknown(): Promise<void> { const row = instance.value, block = row && blocks.value.get(idKey(row.id)); if (!row || !block || !detailReady.value || mutating.value || loading.value) return; const request = begin(permission(block.kind)); if (!request) return; const value = { lease: request, id: idKey(row.id), block, window: detailWindow.value }; recovery.value = value; detailOwner.value++;
  try { await ElMessageBox.confirm(t('workflowInstances.recoveryUnlockHint'), t('workflowInstances.recoveryUnlockTitle'), { type: 'warning', confirmButtonText: t('workflowInstances.recoveryUnlock'), cancelButtonText: t('status.back') }); if (request.current() && recovery.value === value && detailWindow.value === value.window && detailReady.value && blocks.value.get(value.id) === value.block) blocks.value.delete(value.id); }
  catch (error) { if (request.current() && error !== 'cancel' && error !== 'close') problem.value = toProblem(error); }
  finally { if (recovery.value === value) { recovery.value = undefined; detailOwner.value++; } request.finish(); }
}
async function openBusinessDetail(businessType: string, businessId: string): Promise<void> { const row = instance.value; if (!row || !detailReady.value || !canRead.value || mutating.value || !router || row.businessType !== businessType || row.businessId !== businessId) return; const destination = findWorkflowBusinessDetailRoute(businessType, session.can); if (!destination) return; const request = begin('workflow.instances.read'); if (!request) return; navigation = request; navigating.value = true; detailOwner.value++; try { await router.push({ name: destination.routeName, query: { [destination.idQueryKey]: businessId } }); } catch (error) { if (request.current() && navigation === request) problem.value = toProblem(error); } finally { if (navigation === request) { navigation = undefined; navigating.value = false; detailOwner.value++; } request.finish(); } }
function instanceStatusLabel(key: string): string { return instanceStatuses.includes(key as InstanceStatusKey) ? t(('workflowInstances.statusKey.' + key) as MessageKey) : key; }
function instanceStatusTone(key: string): 'success' | 'warning' | 'info' | 'danger' | undefined { return key === 'completed' ? 'success' : key === 'active' ? 'info' : key === 'suspended' ? 'warning' : key === 'rejected' || key === 'cancelled' ? 'danger' : undefined; }
function formatDateTime(value: string | null | undefined): string { return formatAdminDateTime(locale.value, value ?? ''); }
const listControls = computed(() => { const ticket = listOwner.value, current = () => ticket === listOwner.value && canRead.value; return { submit: (event: Event) => { event.preventDefault(); if (current()) void applyFilters(); }, status: (value: string | undefined) => { if (current()) statusFilter.value = value; }, definition: (value: string) => { if (current()) definitionKeyFilter.value = value; }, page: (value: number) => { if (current()) void changePage(value); }, tab: (value: string | number) => { if (current() && !mutating.value && (value === 'mine' || value === 'all' && canListAll.value)) activeTab.value = value; }, select: (row: WorkflowInstanceListItemResponse) => { if (current()) void selectListItem(row); } }; });
const rowControls = computed(() => { const ticket = listOwner.value; return new Map(listItems.value.map(row => [row.id, { click: (event: Event) => { event.stopPropagation(); if (ticket === listOwner.value) void selectListItem(row); } }])); });
const detailControls = computed(() => { const ticket = detailOwner.value, window = detailWindow.value, current = () => ticket === detailOwner.value && window === detailWindow.value && canRead.value; return { submit: (event: Event) => { event.preventDefault(); if (current()) void load(); }, search: () => { if (current()) void load(); }, logs: () => { if (current()) void loadLogs(); }, id: (value: string) => { if (current()) instanceId.value = value; }, cancel: () => { if (current()) void cancelInstance(); }, pause: () => { if (current()) void pauseInstance(); }, resume: () => { if (current()) void resumeInstance(); }, recover: () => { if (current()) void recoverInstance(); }, reassign: () => { if (current()) openReassign(); }, unlock: () => { if (current()) void recoverUnknown(); }, business: () => { if (current() && instance.value) void openBusinessDetail(instance.value.businessType, instance.value.businessId); } }; });
const reassignControls = computed(() => { const ticket = reassignWindow.value; const current = () => ticket === reassignWindow.value && reassignOpen.value && canRead.value; return { close: () => { if (current()) closeReassign(); }, submit: (input: ReassignInput) => { if (current()) void submitReassign(input); } }; });
/** 把执行轨迹迁移键映射为可读标签，并行网关使用专用文案。 */
function transitionLabel(transitionKey: string): string {
  if (transitionKey === 'node.gateway.parallel.fork') {
    return t('workflowInstances.parallelFork');
  }
  if (transitionKey === 'node.gateway.parallel.join') {
    return t('workflowInstances.parallelJoin');
  }
  if (transitionKey === 'node.gateway.inclusive.fork') {
    return t('workflowInstances.inclusiveFork');
  }
  if (transitionKey === 'node.gateway.inclusive.join') {
    return t('workflowInstances.inclusiveJoin');
  }
  return transitionKey;
}

type WorkflowGatewayJoinBranch = { branchKey: string; arrivedAtUtc?: string | null };
type WorkflowGatewayJoin = {
  id: string;
  gatewayTypeKey: string;
  forkNodeKey: string;
  joinNodeKey: string;
  requiredBranchCount: number;
  arrivedBranchCount: number;
  statusKey: string;
  branches: WorkflowGatewayJoinBranch[];
};
type WorkflowInstanceDetail = WorkflowInstanceResponse & { gatewayJoins?: WorkflowGatewayJoin[] | null };

const gatewayJoins = computed(() => (instance.value as WorkflowInstanceDetail | undefined)?.gatewayJoins ?? []);
const timeoutStatusKeys = new Set(['scheduled', 'overdue', 'escalated', 'not_configured']);

/** 把服务端稳定机器码映射为当前语言文案，未知值安全回落为未配置。 */
function timeoutStatusLabel(statusKey: string | undefined): string {
  const normalized = statusKey !== undefined && timeoutStatusKeys.has(statusKey)
    ? statusKey
    : 'not_configured';
  return t(`workflowInstances.timeoutStatus.${normalized}` as MessageKey);
}

/** 把多人审批模式键映射为设计器同源文案。 */
function approvalModeLabel(modeKey: string): string {
  switch (modeKey) {
    case 'all': return t('workflowDesigner.approval.all');
    case 'any': return t('workflowDesigner.approval.any');
    case 'nOfM': return t('workflowDesigner.approval.nOfM');
    default: return modeKey;
  }
}

const showApprovalProgress = computed(() => {
  const current = instance.value;
  if (current?.approvalModeKey === undefined || current.approvalModeKey === null) {
    return false;
  }

  const approved = current.approvedCount ?? 0;
  const rejected = current.rejectedCount ?? 0;
  const pending = current.pendingCount ?? 0;
  return approved + rejected + pending > 1;
});

</script>

<template>
  <section class="workflow-instances art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowInstances.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowInstances.caption') }}</p>

    <p v-if="receipt" role="status" aria-live="polite" data-testid="workflow-instance-receipt">{{ t(('workflowInstances.recovery.' + receipt.outcome) as MessageKey) }}</p>
    <template v-if="canRead">
    <div v-if="instance" class="workflow-instances__toolbar">
      <div class="workflow-instances__actions">
        <el-button
          v-if="canReassign"
          type="primary"
          plain
          data-testid="workflow-instance-reassign"
          v-on="{ click: detailControls.reassign }"
        >{{ t('workflowInstances.reassign') }}</el-button>
        <el-button
          v-if="canPause"
          type="warning"
          plain
          data-testid="workflow-instance-pause"
          :loading="pausing"
          v-on="{ click: detailControls.pause }"
        >
          {{ t('workflowInstances.pause') }}
        </el-button>
        <el-button
          v-if="canResume"
          type="primary"
          plain
          data-testid="workflow-instance-resume"
          :loading="resuming"
          v-on="{ click: detailControls.resume }"
        >
          {{ t('workflowInstances.resume') }}
        </el-button>
        <el-button
          v-if="canRecover"
          type="warning"
          data-testid="workflow-instance-recover"
          :loading="recovering"
          v-on="{ click: detailControls.recover }"
        >
          {{ t('workflowInstances.recover') }}
        </el-button>
        <el-button
          v-if="canCancel"
          type="danger"
          plain
          data-testid="workflow-instance-cancel"
          :loading="cancelling"
          v-on="{ click: detailControls.cancel }"
        >
          {{ t('workflowInstances.cancel') }}
        </el-button>
      </div>
    </div>

    <el-card shadow="never" class="workflow-instances__list-card art-table-card" :aria-busy="listLoading">
      <el-tabs :model-value="activeTab" v-on="{ 'update:model-value': listControls.tab }" data-testid="workflow-instance-tabs">
        <el-tab-pane :label="t('workflowInstances.tabs.mine')" name="mine" />
        <el-tab-pane
          v-if="canListAll"
          :label="t('workflowInstances.tabs.all')"
          name="all"
        />
      </el-tabs>

      <form class="workflow-instances__filters" v-on="{ submit: listControls.submit }">
        <el-select
          :model-value="statusFilter" v-on="{ 'update:model-value': listControls.status }"
          clearable
          data-testid="workflow-instance-status-filter"
          :placeholder="t('workflowInstances.filters.status')"
        >
          <el-option
            v-for="statusKey in statusFilterOptions"
            :key="statusKey"
            :label="instanceStatusLabel(statusKey)"
            :value="statusKey"
          />
        </el-select>
        <el-input
          :model-value="definitionKeyFilter" v-on="{ 'update:model-value': listControls.definition }"
          clearable
          data-testid="workflow-instance-definition-filter"
          :placeholder="t('workflowInstances.filters.definitionKey')"
        />
        <el-button
          type="primary"
          native-type="submit"
          data-testid="workflow-instance-filter-apply"
          :loading="listLoading"
        >
          {{ t('workflowInstances.filters.apply') }}
        </el-button>
      </form>

      <p v-if="!listItems.length && !listLoading" class="workflow-instances__empty">
        {{ t('workflowInstances.listEmpty') }}
      </p>
      <div v-else ref="tableMainRef" class="art-crud-table-main">
      <el-table
        v-loading="listLoading"
        :data="listItems"
        :height="tableHeight"
        row-key="id"
        class="workflow-instances__table"
        highlight-current-row
        v-on="{ 'row-click': listControls.select }"
      >
        <el-table-column :label="t('workflowInstances.business')" min-width="200">
          <template #default="{ row }">
            <button type="button" class="workflow-instances__row-btn" data-testid="workflow-instance-list-item" :disabled="!catalogReady || listLoading || !!actionKind" v-on="rowControls.get(row.id) ?? {}">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
            </button>
            <small translate="no">{{ row.definitionKey }}</small>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowInstances.status')" width="110" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="instanceStatusTone(row.statusKey)">{{ instanceStatusLabel(row.statusKey) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowInstances.startedAt')" width="168">
          <template #default="{ row }">
            <time translate="no" :datetime="row.startedAtUtc">{{ formatDateTime(row.startedAtUtc) }}</time>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-if="displayTotal > 0"
        class="art-table-pagination"
        background
        layout="prev, pager, next, total"
        data-testid="workflow-instance-pagination"
        :current-page="listPage"
        :page-size="listPageSize"
        :total="displayTotal"
        v-on="{ 'update:current-page': listControls.page }"
      />
      </div>
    </el-card>

    <el-card shadow="never" class="workflow-instances__search-card">
      <form class="workflow-instances__search" v-on="{ submit: detailControls.submit }">
        <label for="workflow-instance-id">{{ t('workflowInstances.instanceId') }}</label>
        <div class="workflow-instances__search-row">
          <el-input
            id="workflow-instance-id"
            :model-value="instanceId" v-on="{ 'update:model-value': detailControls.id }"
            data-testid="workflow-instance-id"
            clearable
            autocomplete="off"
            :placeholder="t('workflowInstances.instanceIdPlaceholder')"
          />
          <el-button
            type="primary"
            native-type="submit"
            data-testid="workflow-instance-search"
            :loading="loading"
            :disabled="!canSearch"
          >
            {{ t('workflowInstances.search') }}
          </el-button>
        </div>
        <small>{{ t('workflowInstances.searchHint') }}</small>
      </form>
    </el-card>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
      <el-button
        v-if="problem.status === 409"
        text
        type="primary"
        data-testid="workflow-instance-conflict-refresh"
        v-on="{ click: detailControls.search }"
      >
        {{ t('workflowInstances.conflictRefresh') }}
      </el-button>
    </div>

    <ElButton v-if="instance && blocks.has(instance.id.toLowerCase())" data-testid="workflow-instance-unlock" :disabled="!detailReady || mutating || loading" v-on="{ click: detailControls.unlock }">{{ t('workflowInstances.recoveryUnlock') }}</ElButton>
    <template v-if="instance">
      <section
        class="workflow-instances__summary"
        data-testid="workflow-instance-summary"
        :aria-label="t('workflowInstances.summary')"
      >
        <article class="workflow-instances__identity">
          <span>{{ t('workflowInstances.business') }}</span>
          <strong>{{ formatWorkflowBusinessLabel(instance.businessTitle, instance.businessType, instance.businessId) }}</strong>
          <el-button
            v-if="findWorkflowBusinessDetailRoute(instance.businessType, session.can)"
            type="primary"
            link
            data-testid="workflow-instance-open-business"
            :disabled="!detailReady || mutating" v-on="{ click: detailControls.business }"
          >
            {{ t('workflow.business.viewDocument') }}
          </el-button>
          <code translate="no">{{ instance.id }}</code>
        </article>
        <article>
          <span>{{ t('workflowInstances.status') }}</span>
          <strong class="workflow-instances__status" translate="no">{{ instance.statusKey }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.revision') }}</span>
          <strong translate="no">#{{ instance.revision }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.startedAt') }}</span>
          <time :datetime="instance.startedAtUtc">{{ instance.startedAtUtc }}</time>
        </article>
        <article>
          <span>{{ t('workflowInstances.activeTodo') }}</span>
          <code translate="no">{{ instance.activeTodoId ?? t('workflowInstances.none') }}</code>
        </article>
        <article data-testid="workflow-instance-timeout-status">
          <span>{{ t('workflowInstances.timeoutStatus') }}</span>
          <strong>{{ timeoutStatusLabel(instance.timeoutStatusKey) }}</strong>
          <time v-if="instance.dueAtUtc" :datetime="instance.dueAtUtc">{{ instance.dueAtUtc }}</time>
        </article>
        <article>
          <span>{{ t('workflowInstances.reminderCount') }}</span>
          <strong translate="no">{{ instance.reminderCount ?? 0 }}</strong>
        </article>
        <article v-if="instance.escalatedAtUtc">
          <span>{{ t('workflowInstances.escalatedAt') }}</span>
          <time :datetime="instance.escalatedAtUtc">{{ instance.escalatedAtUtc }}</time>
        </article>
        <article
          v-if="showApprovalProgress"
          class="workflow-instances__approval-progress"
          data-testid="workflow-instance-approval-progress"
        >
          <span>{{ t('workflowInstances.approvalProgressTitle') }}</span>
          <strong>{{ approvalModeLabel(instance.approvalModeKey ?? '') }}</strong>
          <span>{{ t('workflowTodos.approvalProgress', {
            approved: instance.approvedCount ?? 0,
            rejected: instance.rejectedCount ?? 0,
            pending: instance.pendingCount ?? 0,
            required: instance.requiredApprovalCount ?? 0
          }) }}</span>
          <code v-if="instance.activeNodeKey" translate="no">{{ instance.activeNodeKey }}</code>
        </article>
      </section>

      <el-card
        v-if="gatewayJoins.length > 0"
        shadow="never"
        class="workflow-instances__timeline-card"
        data-testid="workflow-instance-gateway-joins"
      >
        <template #header>
          <h2>{{ t('workflowInstances.gatewayJoinsTitle') }}</h2>
        </template>
        <ul class="workflow-instances__timeline">
          <li v-for="join in gatewayJoins" :key="join.id">
            <strong translate="no">{{ join.gatewayTypeKey }} · {{ join.forkNodeKey }} → {{ join.joinNodeKey }}</strong>
            <span>{{ t('workflowInstances.gatewayJoinProgress', {
              arrived: join.arrivedBranchCount,
              required: join.requiredBranchCount,
              status: join.statusKey
            }) }}</span>
            <ul v-if="join.branches.length > 0">
              <li v-for="branch in join.branches" :key="branch.branchKey">
                <code translate="no">{{ branch.branchKey }}</code>
                <time v-if="branch.arrivedAtUtc" :datetime="branch.arrivedAtUtc">{{ branch.arrivedAtUtc }}</time>
              </li>
            </ul>
          </li>
        </ul>
      </el-card>

      <el-card shadow="never" class="workflow-instances__timeline-card">
        <template #header>
          <div class="workflow-instances__timeline-heading">
            <div>
              <h2>{{ t('workflowInstances.timeline') }}</h2>
              <p>{{ t('workflowInstances.timelineCaption') }}</p>
            </div>
            <div><ElButton data-testid="workflow-instance-reload-logs" :disabled="logsLoading || mutating" v-on="{ click: detailControls.logs }">{{ t('workflowInstances.recoveryReloadLogs') }}</ElButton><span translate="no">{{ executionLogs.length }}</span></div>
          </div>
        </template>

        <p v-if="logsProblem" role="alert">{{ t('workflowInstances.recoveryLogsFailed') }}</p>
        <div v-if="executionLogs.length === 0" class="workflow-instances__empty">
          {{ t('workflowInstances.noLogs') }}
        </div>
        <ol v-else class="workflow-instances__timeline">
          <li
            v-for="log in executionLogs"
            :key="log.id"
            data-testid="workflow-execution-log"
          >
            <span class="workflow-instances__marker" aria-hidden="true"></span>
            <div class="workflow-instances__event">
              <div class="workflow-instances__event-heading">
                <strong translate="no">{{ transitionLabel(log.transitionKey) }}</strong>
                <time :datetime="log.createdAtUtc">{{ log.createdAtUtc }}</time>
              </div>
              <div class="workflow-instances__transition">
                <code translate="no">{{ log.fromStatusKey ?? '∅' }}</code>
                <span aria-hidden="true">→</span>
                <code translate="no">{{ log.toStatusKey }}</code>
              </div>
              <small v-if="log.stepId">
                {{ t('workflowInstances.step') }}
                <code translate="no">{{ log.stepId }}</code>
              </small>
            </div>
          </li>
        </ol>
      </el-card>
    </template>

    <div v-else-if="!problem && !loading && !instance" class="workflow-instances__landing">
      <span aria-hidden="true">01 — N</span>
      <p>{{ t('workflowInstances.empty') }}</p>
    </div>
    <WorkflowInstanceReassignDialog v-if="reassignOpen && instance" :key="reassignWindow" :instance="instance" :busy="!!actionKind" :disabled="!detailReady || blocks.has(instance.id.toLowerCase())" v-on="reassignControls" />
    </template>
  </section>
</template>

<style scoped>
.workflow-instances {
  display: grid;
  gap: 1rem;
  max-width: 1120px;
  margin: 0 auto;
}

.workflow-instances__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 4px;
}

.workflow-instances__row-btn {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  cursor: pointer;
  text-align: left;
}

.workflow-instances__header h1 {
  margin: 0.2rem 0 0;
  color: var(--el-text-color-primary);
  font-size: clamp(1.5rem, 2.5vw, 2.15rem);
  letter-spacing: -0.035em;
}

.workflow-instances__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.workflow-instances__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.5rem;
}

.workflow-instances__header p,
.workflow-instances__timeline-heading p {
  margin: 0.45rem 0 0;
  color: var(--el-text-color-secondary);
}

.workflow-instances__eyebrow {
  color: var(--el-color-primary);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

.workflow-instances__search {
  display: grid;
  gap: 0.55rem;
}

.workflow-instances__search label {
  color: var(--el-text-color-primary);
  font-weight: 720;
}

.workflow-instances__search small {
  color: var(--el-text-color-secondary);
}

.workflow-instances__search-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 0.75rem;
}

.workflow-instances {
  min-height: 0;
}

.workflow-instances__list-card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.workflow-instances__list-card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.workflow-instances__list-card .art-crud-table-main {
  flex: 1;
  min-height: 200px;
}

.workflow-instances__filters {
  display: grid;
  grid-template-columns: minmax(160px, 220px) minmax(0, 1fr) auto;
  gap: 0.75rem;
}

.workflow-instances__list {
  display: grid;
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.workflow-instances__list button {
  display: grid;
  gap: 0.35rem;
  width: 100%;
  padding: 0.85rem 1rem;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
  text-align: left;
  cursor: pointer;
}

.workflow-instances__list button:hover {
  border-color: var(--el-color-primary-light-5);
  background: var(--el-color-primary-light-9);
}

.workflow-instances__list small,
.workflow-instances__list time {
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-instances__summary {
  display: grid;
  grid-template-columns: minmax(260px, 1.55fr) repeat(4, minmax(130px, 1fr));
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
}

.workflow-instances__summary article {
  display: grid;
  align-content: start;
  gap: 0.4rem;
  min-height: 108px;
  padding: 1rem;
  border-left: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__summary article:first-child {
  border-left: 0;
}

.workflow-instances__summary span {
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.workflow-instances__summary strong,
.workflow-instances__summary time {
  color: var(--el-text-color-primary);
}

.workflow-instances__summary .workflow-instances__identity {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  overflow-wrap: anywhere;
  background: linear-gradient(145deg, var(--el-color-primary-light-9), transparent 72%);
}

.workflow-instances__identity code,
.workflow-instances__summary article > code {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
}

.workflow-instances__status {
  width: fit-content;
  padding: 0.25rem 0.55rem;
  border-radius: 999px;
  color: var(--el-color-primary-dark-2) !important;
  background: var(--el-color-primary-light-9);
}

.workflow-instances__timeline-heading,
.workflow-instances__event-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.workflow-instances__timeline-heading h2 {
  margin: 0;
  font-size: 1.05rem;
}

.workflow-instances__timeline-heading > span {
  display: grid;
  min-width: 2.2rem;
  height: 2.2rem;
  place-items: center;
  border: 1px solid var(--el-border-color);
  border-radius: 50%;
  color: var(--el-text-color-secondary);
  font-weight: 750;
}

.workflow-instances__timeline {
  display: grid;
  gap: 0;
  margin: 0;
  padding: 0;
  list-style: none;
}

.workflow-instances__timeline li {
  position: relative;
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr);
  gap: 0.9rem;
  padding-bottom: 1.25rem;
}

.workflow-instances__timeline li:not(:last-child)::before {
  position: absolute;
  top: 12px;
  bottom: -2px;
  left: 5px;
  width: 1px;
  background: var(--el-border-color);
  content: '';
}

.workflow-instances__marker {
  z-index: 1;
  width: 11px;
  height: 11px;
  margin-top: 0.25rem;
  border: 2px solid var(--el-bg-color);
  border-radius: 50%;
  background: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary);
}

.workflow-instances__event {
  display: grid;
  gap: 0.55rem;
  padding: 0 0 1rem;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__event-heading time,
.workflow-instances__event small {
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-instances__transition {
  display: flex;
  align-items: center;
  gap: 0.55rem;
}

.workflow-instances__transition code {
  padding: 0.2rem 0.45rem;
  border-radius: 4px;
  background: var(--el-fill-color-light);
}

.workflow-instances__landing,
.workflow-instances__empty {
  display: grid;
  min-height: 150px;
  place-items: center;
  color: var(--el-text-color-secondary);
  text-align: center;
}

.workflow-instances__landing span {
  color: var(--el-border-color-darker);
  font-size: clamp(2.5rem, 8vw, 5.5rem);
  font-weight: 800;
  letter-spacing: -0.08em;
}

.workflow-instances__landing p {
  margin: -3rem 1rem 0;
}

@media (max-width: 900px) {
  .workflow-instances__summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .workflow-instances__summary article,
  .workflow-instances__summary article:first-child {
    border-top: 1px solid var(--el-border-color-lighter);
    border-left: 1px solid var(--el-border-color-lighter);
  }

  .workflow-instances__identity {
    grid-column: 1 / -1;
  }
}

@media (max-width: 560px) {
  .workflow-instances__search-row,
  .workflow-instances__summary,
  .workflow-instances__filters {
    grid-template-columns: 1fr;
  }

  .workflow-instances__summary article,
  .workflow-instances__identity {
    grid-column: auto;
  }

  .workflow-instances__event-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
