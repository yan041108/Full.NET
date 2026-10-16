<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, shallowRef, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import { ElButton, ElCard, ElInput, ElMessageBox, ElOption, ElPagination, ElSelect, ElTabPane, ElTabs, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type WorkflowFieldPolicies } from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import { approveWorkflowTodo, rejectWorkflowTodo, returnWorkflowTodo, countersignWorkflowTodo, cancelWorkflowTodoCountersign, getWorkflowTodo, getWorkflowTodoCountersignChain, listWorkflowTodoReturnTargets, listMyWorkflowTodoHistory, listMyWorkflowTodos, type WorkflowSubmission, type WorkflowTodoDetail, type WorkflowTodoListItemResponse, type WorkflowTodoReturnTargetResponse, type WorkflowTodoCountersignChain } from '../api/workflow-todos';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import { todoResultActions, validWorkflowTodo, validWorkflowTodoPage, validWorkflowTodoPatch, validWorkflowTodoTargets, workflowTodoBinding, workflowTodoRequiredReady } from '../api/workflow-todo-guards';
import { instanceTrim, validWorkflowInstance } from '../api/workflow-instance-guards';
import { workflowFormIdValid, workflowFormValueKey, workflowFormWriteRevisionValid } from '../api/workflow-form-guards';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { showSuccess } from '../feedback/fullNetMessage';
import PermissionGate from '../components/PermissionGate.vue';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';
import { findWorkflowBusinessDetailRoute, formatWorkflowBusinessLabel } from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';

type TodoTabKey = 'pending' | 'history';
type Kind = 'approve' | 'reject' | 'return' | 'countersign' | 'cancelCountersign';
const { t, locale } = useAdminI18n(), session = useSessionStore(), route = inject(routeLocationKey, undefined), router = inject(routerKey, undefined), active = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const canRead = computed(() => { const user = session.currentUser; return active.value && (!route || route.name === 'workflow-todos') && session.can('workflow.todos.read') && !!user && (user.scope === 'host' && user.tenantId === null || workflowFormIdValid(user.tenantId) && user.scope === 'tenant:' + user.tenantId.replaceAll('-', '').toLowerCase()); });
const activeTab = ref<TodoTabKey>('pending'), listItems = ref<WorkflowTodoListItemResponse[]>([]), listPage = ref(1), listPageSize = ref(20), listTotal = ref(0), listLoading = ref(false), catalogReady = ref(false);
const definitionKeyFilter = ref(''), businessTypeFilter = ref(''), resultActionFilter = ref<string>(), applied = ref<{ definitionKey?: string; businessType?: string; resultActionKey?: string }>({});
const selected = ref<WorkflowTodoDetail>(), selectedListItem = ref<WorkflowTodoListItemResponse>(), loading = ref(false), detailReady = ref(false), acting = ref(false), fieldPatch = ref<WorkflowSubmission>({}), comment = ref('');
const returnTargets = ref<WorkflowTodoReturnTargetResponse[]>([]), returnTargetStepId = ref(''), targetsReady = ref(false), targetsLoading = ref(false), targetsProblem = ref<FullNetProblemDetails>();
const countersignDirection = ref<'before' | 'after'>('before'), countersignAssigneeIds = ref<string[]>([]), countersignCandidates = ref<Array<{ id: string; label: string }>>([]), countersignChain = ref<WorkflowTodoCountersignChain>();
const chainReady = ref(false), candidatesReady = ref(false), contextLoading = ref(false), contextProblem = ref<FullNetProblemDetails>(), problem = ref<FullNetProblemDetails>();
const receipt = ref<{ kind: Kind; id: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' }>();
const blocks = ref(new Map<string, { kind: Kind; revision: number }>()), floors = new Map<string, WorkflowTodoDetail>(), rowFacts = new Map<string, WorkflowTodoListItemResponse>(), intents = new Map<string, { id: string; revision: number; key: string }>();
const detailOwner = ref(0), detailWindow = ref(0), listOwner = ref(0), navigating = ref(false);
const scope = useAuthorizedViewScope(session, resetScope, async () => { if (canRead.value) await loadList(); });
type Lease = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: Lease | undefined, detailRequest: Lease | undefined, targetsRequest: Lease | undefined, contextRequest: Lease | undefined, navigation: Lease | undefined;
let operation: { lease: Lease; target: WorkflowTodoDetail; kind: Kind; sent: boolean } | undefined, resetting = false, lastContext = authContext();
const recovery = shallowRef<{ lease: Lease; id: string; block: { kind: Kind; revision: number }; window: number }>();
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(listLoading);
const idKey = (id: string) => id.toLowerCase(), permission = (kind: Kind) => 'workflow.todos.' + (kind === 'cancelCountersign' ? 'countersign' : kind), begin = (code: string) => canRead.value ? scope.begin(code) : undefined;
const detailReadOnly = computed(() => activeTab.value === 'history'), canReturn = computed(() => canRead.value && session.can('workflow.todos.return')), canCountersign = computed(() => canRead.value && session.can('workflow.todos.countersign'));
const resultActionFilterOptions = todoResultActions;
const displayTotal = computed(() => Math.max(listTotal.value, (listPage.value - 1) * listPageSize.value + Math.max(listPage.value > 1 || listLoading.value ? 1 : 0, listItems.value.length)));
const requiredFieldsReady = computed(() => !!selected.value && validWorkflowTodoPatch(selected.value, fieldPatch.value) && workflowTodoRequiredReady(selected.value, fieldPatch.value));
const rendererPolicies = computed<WorkflowFieldPolicies>(() => { const detail = selected.value; if (!detail) return {}; return Object.fromEntries(detail.formSchema.sections.flatMap(section => section.fields.map(field => { const policy = detail.fieldPolicies[field.fieldKey] ?? (field.required ? 'required' : 'editable'); return [field.fieldKey, detailReadOnly.value || acting.value || !detailReady.value || blocks.value.has(idKey(detail.id)) ? policy === 'hidden' ? 'hidden' : 'readOnly' : policy]; }))); });
function authContext(): string { const user = session.currentUser; return workflowFormValueKey([session.state, user?.id, user?.sessionId, user?.tenantId, user?.scope, user?.actorScope]); }
function markUnknown(value: NonNullable<typeof operation>): void { blocks.value.set(idKey(value.target.id), { kind: value.kind, revision: value.target.revision }); receipt.value = { kind: value.kind, id: value.target.id, outcome: 'unconfirmed' }; detailReady.value = false; }
function cancelOperation(): void { if (!operation) return; if (operation.sent) markUnknown(operation); operation.lease.cancel(); operation = undefined; acting.value = false; }
function closeDetail(): void {
  cancelOperation(); detailRequest?.cancel(); targetsRequest?.cancel(); contextRequest?.cancel(); recovery.value?.lease.cancel(); detailRequest = targetsRequest = contextRequest = undefined; recovery.value = undefined;
  selected.value = selectedListItem.value = undefined; fieldPatch.value = {}; comment.value = ''; returnTargets.value = []; returnTargetStepId.value = ''; countersignChain.value = undefined; countersignCandidates.value = []; countersignAssigneeIds.value = []; countersignDirection.value = 'before';
  loading.value = detailReady.value = targetsReady.value = targetsLoading.value = chainReady.value = candidatesReady.value = contextLoading.value = false; targetsProblem.value = contextProblem.value = undefined; detailWindow.value++; detailOwner.value++;
}
function resetScope(): void {
  const context = authContext(), preserve = session.state === 'authenticated' && context === lastContext; lastContext = context; resetting = true;
  try { closeDetail(); navigation?.cancel(); navigation = listRequest = undefined; navigating.value = false; activeTab.value = 'pending'; listItems.value = []; listPage.value = 1; listTotal.value = 0; listLoading.value = catalogReady.value = false; definitionKeyFilter.value = businessTypeFilter.value = ''; resultActionFilter.value = undefined; applied.value = {}; problem.value = undefined; listOwner.value++;
    // 同会话停用、路由或权限变化不能证明服务端写入回滚；取消读取与清空界面仍由作用域统一执行。
    if (!preserve) { blocks.value.clear(); floors.clear(); rowFacts.clear(); intents.clear(); receipt.value = undefined; } else if (receipt.value?.outcome !== 'unconfirmed') receipt.value = undefined;
  } finally { resetting = false; }
}
watch(() => JSON.stringify([route?.name, route?.params, route?.query]), () => { scope.invalidate(); if (canRead.value) void loadList(); }, { flush: 'sync' });
watch(activeTab, () => { if (resetting) return; closeDetail(); listRequest?.cancel(); listRequest = undefined; listItems.value = []; listPage.value = 1; listTotal.value = 0; listLoading.value = catalogReady.value = false; listOwner.value++; if (canRead.value) void loadList(); }, { flush: 'sync' });
function toProblem(error: unknown, key: MessageKey = 'workflowTodos.loadFailed'): FullNetProblemDetails { return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_todo_failed', title: t(key) }; }
function rowBinding(row: WorkflowTodoListItemResponse): string { return workflowFormValueKey([idKey(row.instanceId), idKey(row.stepId), row.definitionKey, row.nodeKey, row.businessType, row.businessId, row.businessTitle, row.arrivedAtUtc]); }
function assertRow(row: WorkflowTodoListItemResponse): void { const previous = rowFacts.get(idKey(row.id)); if (previous && (rowBinding(previous) !== rowBinding(row) || row.revision < previous.revision || row.revision === previous.revision && row.statusKey !== previous.statusKey)) throw Error('client.stale_workflow_todo_row'); }
function assertDetail(value: unknown, row: WorkflowTodoListItemResponse): asserts value is WorkflowTodoDetail {
  if (!validWorkflowTodo(value) || idKey(value.id) !== idKey(row.id) || idKey(value.instanceId) !== idKey(row.instanceId) || idKey(value.stepId) !== idKey(row.stepId) || idKey(value.assigneeUserId) !== idKey(session.currentUser!.id) || value.revision < row.revision || value.revision === row.revision && value.statusKey !== row.statusKey) throw Error('client.invalid_workflow_todo_detail');
  const floor = floors.get(idKey(value.id)); if (floor && (workflowTodoBinding(floor) !== workflowTodoBinding(value) || value.revision < floor.revision || value.revision === floor.revision && value.statusKey !== floor.statusKey || value.submissionRevision < floor.submissionRevision)) throw Error('client.stale_workflow_todo_detail');
}
function observe(detail: WorkflowTodoDetail): void {
  floors.set(idKey(detail.id), JSON.parse(JSON.stringify(detail)) as WorkflowTodoDetail); const block = blocks.value.get(idKey(detail.id));
  if (block && detail.revision > block.revision) blocks.value.delete(idKey(detail.id));
  for (const [signature, intent] of intents) if (intent.id === idKey(detail.id) && detail.revision > intent.revision) intents.delete(signature);
}
function listKey(): string { return workflowFormValueKey([activeTab.value, listPage.value, listPageSize.value, applied.value]); }
async function loadList(fallback = true): Promise<void> {
  if (!canRead.value || acting.value || recovery.value || navigating.value || !['pending', 'history'].includes(activeTab.value)) return;
  listRequest?.cancel(); const request = begin('workflow.todos.read'); if (!request) return; listRequest = request;
  const key = listKey(), page = listPage.value, size = listPageSize.value, history = detailReadOnly.value; listLoading.value = true; catalogReady.value = false; listItems.value = []; listOwner.value++; problem.value = undefined; let fallbackPage: number | undefined;
  try { const query = { page, pageSize: size, definitionKey: applied.value.definitionKey, businessType: applied.value.businessType }, result = await (history ? listMyWorkflowTodoHistory({ ...query, resultActionKey: applied.value.resultActionKey }, request.signal) : listMyWorkflowTodos(query, request.signal));
    if (!request.current() || listRequest !== request || key !== listKey()) return; if (!validWorkflowTodoPage(result, page, size, history)) throw Error('client.invalid_workflow_todo_page');
    for (const row of result.items) assertRow(row); for (const row of result.items) rowFacts.set(idKey(row.id), { ...row });
    listItems.value = result.items; listTotal.value = result.total; catalogReady.value = true;
    if (fallback && !result.items.length && page > 1) { const last = Math.max(1, Math.ceil(result.total / size)); if (last < page) fallbackPage = last; }
  } catch (error) { if (request.current() && listRequest === request) problem.value = toProblem(error); }
  finally { if (listRequest === request) { listRequest = undefined; listLoading.value = false; listOwner.value++; } request.finish(); void syncTableLayout(); }
  if (fallbackPage !== undefined && canRead.value && key === listKey()) { listPage.value = fallbackPage; await loadList(false); }
}
async function applyFilters(): Promise<void> { if (!canRead.value || acting.value || recovery.value) return; const result = resultActionFilter.value ? instanceTrim(resultActionFilter.value) || undefined : undefined; if (result && !todoResultActions.includes(result as typeof todoResultActions[number])) return; applied.value = { definitionKey: instanceTrim(definitionKeyFilter.value) || undefined, businessType: instanceTrim(businessTypeFilter.value) || undefined, resultActionKey: result }; listPage.value = 1; closeDetail(); await loadList(); }
async function changePage(value: number): Promise<void> { if (!canRead.value || acting.value || recovery.value || !Number.isInteger(value) || value < 1 || (value - 1) * listPageSize.value > 2147483647 || value === listPage.value) return; closeDetail(); listPage.value = value; await loadList(); }
async function openTodo(row: WorkflowTodoListItemResponse): Promise<void> {
  if (!canRead.value || !catalogReady.value || listLoading.value || acting.value || recovery.value || !listItems.value.some(item => workflowFormValueKey(item) === workflowFormValueKey(row))) return;
  closeDetail(); selectedListItem.value = { ...row }; await reloadDetail();
}
async function reloadDetail(): Promise<void> {
  const row = selectedListItem.value; if (!canRead.value || !row || acting.value || recovery.value) return;
  detailRequest?.cancel(); targetsRequest?.cancel(); contextRequest?.cancel(); targetsRequest = contextRequest = undefined; targetsLoading.value = contextLoading.value = false; targetsReady.value = chainReady.value = candidatesReady.value = false;
  const request = begin('workflow.todos.read'); if (!request) return; detailRequest = request; const window = detailWindow.value; loading.value = true; detailReady.value = false; detailOwner.value++; problem.value = undefined;
  try { const value = await getWorkflowTodo(row.id, request.signal); if (!request.current() || detailRequest !== request || window !== detailWindow.value) return; assertDetail(value, row); observe(value); selected.value = value; detailReady.value = true; fieldPatch.value = {}; comment.value = ''; returnTargetStepId.value = ''; countersignAssigneeIds.value = []; detailWindow.value++;
    // 详情可独立使用；辅助结果只更新各自状态，不覆盖已开始编辑的字段。
    if (!detailReadOnly.value) { void loadReturnTargets(); void loadCountersignContext(); }
  } catch (error) { if (request.current() && detailRequest === request) { problem.value = toProblem(error); if (!selected.value || isFullNetProblemDetails(error) && [401, 403, 404].includes(error.status)) { selected.value = undefined; returnTargets.value = []; countersignChain.value = undefined; countersignCandidates.value = []; } } }
  finally { if (detailRequest === request) { detailRequest = undefined; loading.value = false; detailOwner.value++; } request.finish(); }
}
async function loadReturnTargets(): Promise<void> {
  const detail = selected.value; if (!canReturn.value || detailReadOnly.value || !detail || !detailReady.value || acting.value) return;
  targetsRequest?.cancel(); const request = begin('workflow.todos.return'); if (!request) return; targetsRequest = request; const window = detailWindow.value; targetsLoading.value = true; targetsReady.value = false; targetsProblem.value = undefined;
  try { const values = await listWorkflowTodoReturnTargets(detail.id, request.signal); if (!request.current() || targetsRequest !== request || window !== detailWindow.value) return; if (!validWorkflowTodoTargets(values) || values.some(value => idKey(value.stepId) === idKey(detail.stepId))) throw Error('client.invalid_workflow_todo_targets'); returnTargets.value = values; targetsReady.value = true; if (!values.some(value => value.stepId === returnTargetStepId.value)) returnTargetStepId.value = ''; }
  catch (error) { if (request.current() && targetsRequest === request) targetsProblem.value = toProblem(error); }
  finally { if (targetsRequest === request) { targetsRequest = undefined; targetsLoading.value = false; detailOwner.value++; } request.finish(); }
}
async function loadCountersignContext(): Promise<void> {
  const detail = selected.value; if (!canCountersign.value || detailReadOnly.value || !detail || !detailReady.value || acting.value) return;
  contextRequest?.cancel(); const request = begin('workflow.todos.countersign'); if (!request) return; contextRequest = request; const window = detailWindow.value; contextLoading.value = true; chainReady.value = candidatesReady.value = false; contextProblem.value = undefined;
  const owns = () => request.current() && contextRequest === request && window === detailWindow.value;
  await Promise.all([
    (async () => { try { const chain = await getWorkflowTodoCountersignChain(detail.id, request.signal); if (owns()) { countersignChain.value = chain; chainReady.value = true; } } catch (error) { if (owns()) { if (isFullNetProblemDetails(error) && error.status === 404 && error.code === 'workflow.todo.countersign_chain_not_found') { countersignChain.value = undefined; chainReady.value = true; } else contextProblem.value = toProblem(error); } } })(),
    (async () => { try { const values = await listWorkflowRecipientCandidates(1, 100, request.signal); if (owns()) { if (!Array.isArray(values.items) || values.items.some(value => !workflowFormIdValid(value.id)) || new Set(values.items.map(value => idKey(value.id))).size !== values.items.length) throw Error('client.invalid_workflow_todo_candidates'); countersignCandidates.value = values.items.filter(value => idKey(value.id) !== idKey(detail.assigneeUserId)).map(value => ({ id: value.id, label: value.displayName || value.username })); candidatesReady.value = true; } } catch (error) { if (owns()) contextProblem.value = toProblem(error); } })()
  ]);
  if (contextRequest === request) { contextRequest = undefined; contextLoading.value = false; detailOwner.value++; } request.finish();
}
function allowed(kind: Kind): boolean {
  const detail = selected.value, row = selectedListItem.value;
  if (!canRead.value || detailReadOnly.value || !detailReady.value || !detail || !row || loading.value || acting.value || recovery.value || navigating.value || !session.can(permission(kind)) || blocks.value.has(idKey(detail.id)) || !workflowFormWriteRevisionValid(detail.revision) || idKey(detail.assigneeUserId) !== idKey(session.currentUser!.id) || row.instanceStatusKey !== 'active') return false;
  if (kind === 'cancelCountersign') return chainReady.value && !contextLoading.value && !!countersignChain.value && (countersignChain.value.directionKey === 'before' ? detail.statusKey === 'awaiting_before_countersign' : detail.statusKey === 'active');
  if (detail.statusKey !== 'active') return false;
  if (kind === 'countersign') return chainReady.value && candidatesReady.value && !contextLoading.value && !countersignChain.value && ['before', 'after'].includes(countersignDirection.value) && countersignAssigneeIds.value.length > 0 && countersignAssigneeIds.value.length <= 10 && new Set(countersignAssigneeIds.value.map(idKey)).size === countersignAssigneeIds.value.length && countersignAssigneeIds.value.every(id => countersignCandidates.value.some(candidate => idKey(candidate.id) === idKey(id)));
  if (!requiredFieldsReady.value) return false;
  return kind !== 'return' || targetsReady.value && !targetsLoading.value && !!instanceTrim(comment.value) && returnTargets.value.some(value => value.stepId === returnTargetStepId.value);
}
const canApprove = computed(() => allowed('approve')), canReject = computed(() => allowed('reject')), returnReady = computed(() => allowed('return')), countersignReady = computed(() => allowed('countersign')), cancelCountersignReady = computed(() => allowed('cancelCountersign'));
async function runAction(kind: Kind): Promise<void> {
  if (!allowed(kind)) return; const target = JSON.parse(JSON.stringify(selected.value!)) as WorkflowTodoDetail, row = { ...selectedListItem.value! }, window = detailWindow.value;
  const note = instanceTrim(comment.value) || null; if (note && note.length > 512) return;
  const patch = JSON.parse(JSON.stringify(fieldPatch.value)) as WorkflowSubmission, targetStep = returnTargetStepId.value, direction = countersignDirection.value, assignees = [...countersignAssigneeIds.value];
  const signature = workflowFormValueKey([idKey(target.id), target.revision, kind, note, kind === 'approve' || kind === 'reject' || kind === 'return' ? patch : null, kind === 'return' ? targetStep : null, kind === 'countersign' ? [direction, assignees.map(idKey)] : null]);
  let intent = intents.get(signature); if (!intent) { intent = { id: idKey(target.id), revision: target.revision, key: kind + '-' + crypto.randomUUID() }; intents.set(signature, intent); }
  const request = begin(permission(kind)); if (!request) return; const action = { lease: request, target, kind, sent: true }; operation = action; acting.value = true; detailOwner.value++; listOwner.value++; problem.value = undefined; receipt.value = { kind, id: target.id, outcome: 'processing' };
  const owns = () => request.current() && operation === action && window === detailWindow.value && canRead.value;
  try {
    const result = kind === 'approve' || kind === 'reject' ? await (kind === 'approve' ? approveWorkflowTodo : rejectWorkflowTodo)(target.id, target.revision, patch, note, intent.key, request.signal)
      : kind === 'return' ? await returnWorkflowTodo(target.id, targetStep, target.revision, patch, note!, intent.key, request.signal)
      : kind === 'countersign' ? await countersignWorkflowTodo(target.id, direction, assignees, target.revision, note, intent.key, request.signal)
      : await cancelWorkflowTodoCountersign(target.id, target.revision, note, intent.key, request.signal);
    if (!owns()) return;
    // 请求修订属于待办，回执修订属于实例；只核对真实可验证的身份及表单、业务绑定。
    if (!validWorkflowInstance(result) || idKey(result.id) !== idKey(target.instanceId) || idKey(result.formVersionId) !== idKey(target.formVersionId) || result.businessType !== row.businessType || result.businessId !== row.businessId || result.businessTitle !== row.businessTitle) throw Error('client.invalid_workflow_todo_receipt');
    operation = undefined; acting.value = false; blocks.value.delete(idKey(target.id)); receipt.value = { kind, id: target.id, outcome: 'succeeded' }; closeDetail();
    showSuccess(t(('workflowTodos.' + ({ approve: 'approveSuccess', reject: 'rejectSuccess', return: 'returnSuccess', countersign: 'countersignSuccess', cancelCountersign: 'countersignCancelSuccess' }[kind])) as MessageKey)); await loadList();
  } catch (error) {
    if (owns()) { const failure = toProblem(error, 'workflowTodos.operationFailed'), unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408; if (unknown) markUnknown(action); else { receipt.value = { kind, id: target.id, outcome: 'failed' }; detailReady.value = false; }
      problem.value = failure; if (!unknown && [401, 403, 404, 409].includes(failure.status)) { operation = undefined; acting.value = false; closeDetail(); if (failure.status === 409) await loadList(); problem.value = failure; }
    }
  } finally { if (operation === action) { operation = undefined; acting.value = false; detailOwner.value++; listOwner.value++; } request.finish(); }
}
const act = (kind: 'approve' | 'reject') => runAction(kind), returnSelected = () => runAction('return'), submitCountersign = () => runAction('countersign'), cancelCountersign = () => runAction('cancelCountersign');
async function recoverUnknown(): Promise<void> {
  const detail = selected.value, block = detail && blocks.value.get(idKey(detail.id)); if (!canRead.value || !detail || !block || !detailReady.value || loading.value || acting.value || recovery.value) return;
  const request = begin(permission(block.kind)); if (!request) return; const value = { lease: request, id: idKey(detail.id), block, window: detailWindow.value }; recovery.value = value; detailOwner.value++;
  try { await ElMessageBox.confirm(t('workflowTodos.recoveryUnlockHint'), t('workflowTodos.recoveryUnlockTitle'), { type: 'warning', confirmButtonText: t('workflowTodos.recoveryUnlock'), cancelButtonText: t('status.back') }); if (request.current() && recovery.value === value && value.window === detailWindow.value && detailReady.value && blocks.value.get(value.id) === value.block) blocks.value.delete(value.id); }
  catch (error) { if (request.current() && error !== 'cancel' && error !== 'close') problem.value = toProblem(error); }
  finally { if (recovery.value === value) { recovery.value = undefined; detailOwner.value++; } request.finish(); }
}
async function openBusinessDetail(type: string, id: string): Promise<void> {
  if (!canRead.value || !router || acting.value || recovery.value || navigating.value || !catalogReady.value || !listItems.value.some(row => row.businessType === type && row.businessId === id)) return;
  const destination = findWorkflowBusinessDetailRoute(type, session.can); if (!destination) return; const request = begin('workflow.todos.read'); if (!request) return; navigation = request; navigating.value = true; listOwner.value++; detailOwner.value++;
  try { await router.push({ name: destination.routeName, query: { [destination.idQueryKey]: id } }); } catch (error) { if (request.current() && navigation === request) problem.value = toProblem(error); } finally { if (navigation === request) { navigation = undefined; navigating.value = false; listOwner.value++; detailOwner.value++; } request.finish(); }
}
function resultActionLabel(key: string | null | undefined): string { return !key ? t('workflowInstances.timeoutStatus.not_configured') : todoResultActions.includes(key as typeof todoResultActions[number]) ? t(('workflowTodos.resultAction.' + key) as MessageKey) : key; }
function approvalModeLabel(key: string): string { return t(('workflowDesigner.approval.' + key) as MessageKey); }
function formatDateTime(value: string | null | undefined): string { return formatAdminDateTime(locale.value, value ?? ''); }
const listControls = computed(() => { const ticket = listOwner.value, current = () => ticket === listOwner.value && canRead.value; return {
  submit: (event: Event) => { event.preventDefault(); if (current()) void applyFilters(); }, definition: (value: string) => { if (current()) definitionKeyFilter.value = value; }, business: (value: string) => { if (current()) businessTypeFilter.value = value; }, result: (value: string | undefined) => { if (current()) resultActionFilter.value = value; }, page: (value: number) => { if (current()) void changePage(value); }, tab: (value: string | number) => { if (current() && !acting.value && !recovery.value && (value === 'pending' || value === 'history')) activeTab.value = value; }
}; });
const rowControls = computed(() => { const ticket = listOwner.value; return new Map(listItems.value.map(row => [row.id, { open: () => { if (ticket === listOwner.value) void openTodo(row); }, business: () => { if (ticket === listOwner.value) void openBusinessDetail(row.businessType, row.businessId); } }])); });
const detailControls = computed(() => { const ticket = detailOwner.value, window = detailWindow.value, current = () => ticket === detailOwner.value && window === detailWindow.value && canRead.value; return {
  close: () => { if (current()) closeDetail(); }, reload: () => { if (current()) void reloadDetail(); }, targets: () => { if (current()) void loadReturnTargets(); }, context: () => { if (current()) void loadCountersignContext(); }, approve: () => { if (current()) void act('approve'); }, reject: () => { if (current()) void act('reject'); }, return: () => { if (current()) void returnSelected(); }, countersign: () => { if (current()) void submitCountersign(); }, cancelCountersign: () => { if (current()) void cancelCountersign(); }, unlock: () => { if (current()) void recoverUnknown(); },
  patch: (value: WorkflowSubmission) => { if (current() && detailReady.value && !detailReadOnly.value && !acting.value && selected.value && validWorkflowTodoPatch(selected.value, value)) fieldPatch.value = JSON.parse(JSON.stringify(value)) as WorkflowSubmission; }, comment: (event: Event) => { if (current() && !acting.value) comment.value = (event.target as HTMLTextAreaElement).value; }, target: (event: Event) => { if (current() && !acting.value && targetsReady.value) returnTargetStepId.value = (event.target as HTMLSelectElement).value; }, direction: (event: Event) => { const value = (event.target as HTMLSelectElement).value; if (current() && !acting.value && (value === 'before' || value === 'after')) countersignDirection.value = value; }, assignees: (event: Event) => { if (current() && !acting.value && candidatesReady.value) countersignAssigneeIds.value = Array.from((event.target as HTMLSelectElement).selectedOptions, option => option.value); }
}; });
</script>


<template>
  <section class="workflow-todos art-page-stack art-full-height" :aria-busy="listLoading || loading || acting">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowTodos.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowTodos.caption') }}</p>

    <p v-if="receipt" role="status" aria-live="polite" data-testid="workflow-todo-receipt">{{ t(('workflowTodos.recovery.' + receipt.outcome) as MessageKey) }}</p>
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <template v-if="canRead">
    <el-card shadow="never" class="workflow-todos__list art-table-card" :aria-busy="listLoading">
      <el-tabs :model-value="activeTab" v-on="{ 'update:model-value': listControls.tab }" data-testid="workflow-todo-tabs">
        <el-tab-pane :label="t('workflowTodos.tabs.pending')" name="pending" />
        <el-tab-pane :label="t('workflowTodos.tabs.history')" name="history" />
      </el-tabs>

      <form class="workflow-todos__filters" v-on="{ submit: listControls.submit }">
        <el-input
          :model-value="definitionKeyFilter" v-on="{ 'update:model-value': listControls.definition }"
          clearable
          data-testid="workflow-todo-definition-filter"
          :placeholder="t('workflowTodos.filters.definitionKey')"
        />
        <el-input
          :model-value="businessTypeFilter" v-on="{ 'update:model-value': listControls.business }"
          clearable
          data-testid="workflow-todo-business-type-filter"
          :placeholder="t('workflowTodos.filters.businessType')"
        />
        <el-select
          v-if="activeTab === 'history'"
          :model-value="resultActionFilter" v-on="{ 'update:model-value': listControls.result }"
          clearable
          data-testid="workflow-todo-result-action-filter"
          :placeholder="t('workflowTodos.filters.resultAction')"
        >
          <el-option
            v-for="actionKey in resultActionFilterOptions"
            :key="actionKey"
            :label="resultActionLabel(actionKey)"
            :value="actionKey"
          />
        </el-select>
        <el-button
          type="primary"
          native-type="submit"
          data-testid="workflow-todo-filter-apply"
          :loading="listLoading"

        >
          {{ t('workflowTodos.filters.apply') }}
        </el-button>
      </form>

      <div
        v-if="!listItems.length && !listLoading"
        class="workflow-todos__empty"
      >
        {{ t(activeTab === 'history' ? 'workflowTodos.historyEmpty' : 'workflowTodos.empty') }}
      </div>
      <div v-else ref="tableMainRef" class="art-crud-table-main">
      <el-table v-loading="listLoading" :data="listItems" :height="tableHeight" row-key="id" class="workflow-todos__table">
        <el-table-column :label="t('workflowTodos.business')" min-width="200">
          <template #default="{ row }">
            <div class="workflow-todos__business-cell">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
              <small translate="no">{{ row.definitionKey }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowTodos.node')" min-width="120" show-overflow-tooltip>
          <template #default="{ row }"><span translate="no">{{ row.nodeKey }}</span></template>
        </el-table-column>
        <el-table-column v-if="activeTab === 'history'" :label="t('workflowTodos.resultAction')" width="120">
          <template #default="{ row }">
            <el-tag size="small" translate="no">{{ resultActionLabel(row.resultActionKey) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t(activeTab === 'history' ? 'workflowTodos.completedAt' : 'workflowTodos.arrivedAt')" width="168">
          <template #default="{ row }">
            <time
              translate="no"
              :datetime="activeTab === 'history' ? (row.completedAtUtc ?? '') : row.arrivedAtUtc"
            >
              {{ formatDateTime(activeTab === 'history' ? row.completedAtUtc : row.arrivedAtUtc) }}
            </time>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowTodos.actions')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button size="small" data-testid="workflow-todo-open" :disabled="listLoading || loading || acting" v-on="{ click: rowControls.get(row.id)?.open }">
              {{ t(detailReadOnly ? 'workflowTodos.view' : 'workflowTodos.open') }}
            </el-button>
            <el-button
              v-if="findWorkflowBusinessDetailRoute(row.businessType, session.can)"
              link
              type="primary"
              size="small"
              data-testid="workflow-todo-open-business"
              v-on="{ click: rowControls.get(row.id)?.business }"
            >
              {{ t('workflow.business.viewDocument') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-if="listTotal > 0"
        class="art-table-pagination"
        background
        layout="prev, pager, next, total"
        data-testid="workflow-todo-pagination"
        :current-page="listPage"
        :page-size="listPageSize"
        :total="displayTotal"
        v-on="{ 'current-change': listControls.page }"
      />
      </div>
    </el-card>

    <el-drawer
      :model-value="selectedListItem !== undefined"
      :title="t(detailReadOnly ? 'workflowTodos.historyDetail' : 'workflowTodos.detail')"
      size="min(680px, 94vw)"
      v-on="{ close: detailControls.close }"
    >
      <el-button data-testid="workflow-todo-reload" :disabled="loading || acting" v-on="{ click: detailControls.reload }">{{ t('workflowTodos.reloadDetail') }}</el-button>
      <el-button v-if="selected && detailReady && blocks.has(selected.id.toLowerCase())" data-testid="workflow-todo-unlock" :disabled="acting || !!recovery" v-on="{ click: detailControls.unlock }">{{ t('workflowTodos.recoveryUnlock') }}</el-button>
      <div v-if="targetsProblem" role="alert"><strong>{{ targetsProblem.code }}</strong> {{ targetsProblem.title }}</div>
      <el-button v-if="canReturn && !detailReadOnly && selected" data-testid="workflow-todo-targets-retry" :disabled="targetsLoading || acting" v-on="{ click: detailControls.targets }">{{ t('workflowTodos.reloadTargets') }}</el-button>
      <div v-if="contextProblem" role="alert"><strong>{{ contextProblem.code }}</strong> {{ contextProblem.title }}</div>
      <el-button v-if="canCountersign && !detailReadOnly && selected" data-testid="workflow-todo-context-retry" :disabled="contextLoading || acting" v-on="{ click: detailControls.context }">{{ t('workflowTodos.reloadContext') }}</el-button>
      <template v-if="selected">
        <div
          v-if="detailReadOnly && selectedListItem"
          class="workflow-todos__history-snapshot"
          data-testid="workflow-todo-history-snapshot"
        >
          <el-tag translate="no">{{ resultActionLabel(selectedListItem.resultActionKey) }}</el-tag>
          <time :datetime="selectedListItem.completedAtUtc ?? ''">
            {{ selectedListItem.completedAtUtc }}
          </time>
        </div>
        <div
          v-else-if="selected.approvedCount + selected.rejectedCount + selected.pendingCount > 1"
          class="workflow-todos__approval-progress"
          data-testid="workflow-approval-progress"
        >
          <strong>{{ approvalModeLabel(selected.approvalModeKey) }}</strong>
          <span>{{ t('workflowTodos.approvalProgress', {
            approved: selected.approvedCount,
            rejected: selected.rejectedCount,
            pending: selected.pendingCount,
            required: selected.requiredApprovalCount
          }) }}</span>
        </div>
        <WorkflowFormRenderer :key="detailWindow"
          :schema="selected.formSchema"
          :submission="selected.submission"
          :instance-id="selected.instanceId"
          :field-policies="rendererPolicies"
          v-on="{ 'update:patch': detailControls.patch }"
        />

        <label v-if="!detailReadOnly" class="workflow-todos__comment">
          <span>{{ t('workflowTodos.comment') }}</span>
          <textarea
            :value="comment" :disabled="acting || !detailReady" v-on="{ input: detailControls.comment }"
            data-testid="workflow-todo-comment"
            rows="3"
            maxlength="512"
            :placeholder="t('workflowTodos.commentPlaceholder')"
          ></textarea>
        </label>

        <label v-if="!detailReadOnly && canReturn" class="workflow-todos__return-target">
          <span>{{ t('workflowTodos.returnTarget') }}</span>
          <select
            :value="returnTargetStepId" v-on="{ change: detailControls.target }"
            data-testid="workflow-todo-return-target"
            :disabled="acting || !targetsReady || returnTargets.length === 0"
          >
            <option value="" disabled>{{ t(returnTargets.length === 0
              ? 'workflowTodos.noReturnTargets'
              : 'workflowTodos.returnTargetPlaceholder') }}</option>
            <option
              v-for="target in returnTargets"
              :key="target.stepId"
              :value="target.stepId"
            >
              {{ target.nodeKey }} · {{ target.completedAtUtc }}
            </option>
          </select>
        </label>

        <PermissionGate v-if="!detailReadOnly" code="workflow.todos.countersign">
          <div v-if="countersignChain" class="workflow-todos__countersign-chain">
            <strong>{{ t('workflowTodos.countersignChain') }}</strong>
            <ol>
              <li
                v-for="item in countersignChain.items"
                :key="item.itemId"
                translate="no"
              >
                #{{ item.sequenceNo }} · {{ item.assigneeUserId }} · {{ item.statusKey }}
              </li>
            </ol>
            <el-button
              data-testid="workflow-todo-countersign-cancel"
              :loading="acting"
              :disabled="!cancelCountersignReady" v-on="{ click: detailControls.cancelCountersign }"
            >
              {{ t('workflowTodos.countersignCancel') }}
            </el-button>
          </div>
          <div v-else class="workflow-todos__countersign">
            <label>
              <span>{{ t('workflowTodos.countersignDirection') }}</span>
              <select :value="countersignDirection" :disabled="acting || !chainReady" v-on="{ change: detailControls.direction }" data-testid="workflow-todo-countersign-direction">
                <option value="before">{{ t('workflowTodos.countersignBefore') }}</option>
                <option value="after">{{ t('workflowTodos.countersignAfter') }}</option>
              </select>
            </label>
            <label>
              <span>{{ t('workflowTodos.countersignAssignees') }}</span>
              <select
                :value="countersignAssigneeIds" v-on="{ change: detailControls.assignees }"
                data-testid="workflow-todo-countersign-assignees"
                multiple
                :disabled="acting || !candidatesReady || !chainReady || countersignCandidates.length === 0"
              >
                <option
                  v-for="candidate in countersignCandidates"
                  :key="candidate.id"
                  :value="candidate.id"
                >
                  {{ candidate.label }}
                </option>
              </select>
            </label>
            <el-button
              data-testid="workflow-todo-countersign-submit"
              :loading="acting"
              :disabled="!countersignReady"
              v-on="{ click: detailControls.countersign }"
            >
              {{ t('workflowTodos.countersign') }}
            </el-button>
          </div>
        </PermissionGate>

        <div v-if="!detailReadOnly" class="workflow-todos__decision-bar">
          <el-button :disabled="acting" v-on="{ click: detailControls.close }">
            {{ t('workflowTodos.close') }}
          </el-button>
          <PermissionGate code="workflow.todos.reject">
            <el-button
              type="danger"
              plain
              data-testid="workflow-todo-reject"
              :loading="acting"
              :disabled="!canReject"
              v-on="{ click: detailControls.reject }"
            >
              {{ t('workflowTodos.reject') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="workflow.todos.approve">
            <el-button
              type="primary"
              data-testid="workflow-todo-approve"
              :loading="acting"
              :disabled="!canApprove"
              v-on="{ click: detailControls.approve }"
            >
              {{ t('workflowTodos.approve') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="workflow.todos.return">
            <el-button
              type="warning"
              plain
              data-testid="workflow-todo-return"
              :loading="acting"
              :disabled="!returnReady"
              v-on="{ click: detailControls.return }"
            >
              {{ t('workflowTodos.return') }}
            </el-button>
          </PermissionGate>
        </div>
      </template>
    </el-drawer>
    </template>
  </section>
</template>

<style scoped>
.workflow-todos {
  display: grid;
  gap: 1rem;
  min-height: 0;
}

.workflow-todos__list {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.workflow-todos__list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.workflow-todos__list .art-crud-table-main {
  flex: 1;
  min-height: 200px;
}

.workflow-todos__header h1 {
  margin: 0;
  color: var(--el-text-color-primary);
  font-size: clamp(1.35rem, 2vw, 1.8rem);
}

.workflow-todos__header p {
  margin: 0.35rem 0 0;
  color: var(--el-text-color-secondary);
}

.workflow-todos__filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.workflow-todos__filters .el-input,
.workflow-todos__filters .el-select {
  width: min(220px, 100%);
}

.workflow-todos__history-snapshot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-light);
}

.workflow-todos td small {
  display: block;
  margin-top: 0.25rem;
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-todos__approval-progress {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-light);
  color: var(--el-text-color-regular);
}

.workflow-todos__business-cell {
  display: grid;
  gap: 4px;
}

.workflow-todos__table-wrap {
  overflow-x: auto;
}

.workflow-todos table {
  width: 100%;
  border-collapse: collapse;
}

.workflow-todos th,
.workflow-todos td {
  padding: 0.8rem;
  border-bottom: 1px solid var(--el-border-color-lighter);
  text-align: left;
}

.workflow-todos th {
  color: var(--el-text-color-secondary);
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.workflow-todos code {
  font-size: 0.75rem;
}

.workflow-todos__status {
  display: inline-flex;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  color: var(--el-color-primary-dark-2);
  background: var(--el-color-primary-light-9);
  font-size: 0.78rem;
  font-weight: 700;
}

.workflow-todos__empty {
  padding: 3rem 1rem;
  color: var(--el-text-color-secondary);
  text-align: center;
}

.workflow-todos__comment {
  display: grid;
  gap: 0.45rem;
  margin-top: 1rem;
  color: var(--el-text-color-regular);
  font-weight: 650;
}

.workflow-todos__return-target {
  display: grid;
  gap: 0.45rem;
  margin-top: 1rem;
  color: var(--el-text-color-regular);
  font-weight: 650;
}

.workflow-todos__comment textarea,
.workflow-todos__return-target select {
  box-sizing: border-box;
  width: 100%;
  padding: 0.65rem 0.75rem;
  border: 1px solid var(--el-border-color);
  border-radius: var(--el-border-radius-base);
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-blank);
  font: inherit;
}

.workflow-todos__comment textarea {
  resize: vertical;
}

.workflow-todos__decision-bar {
  display: flex;
  justify-content: flex-end;
  gap: 0.65rem;
  margin-top: 1.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--el-border-color-lighter);
}
</style>
