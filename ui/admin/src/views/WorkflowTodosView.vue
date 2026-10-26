<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDrawer,
  ElInput,
  ElLoading,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  approveWorkflowTodo,
  completeWorkflowTodo,
  voteWorkflowTodo,
  cancelWorkflowTodoCountersign,
  countersignWorkflowTodo,
  getWorkflowTodo,
  getWorkflowTodoCountersignChain,
  listWorkflowTodoReturnTargets,
  listMyWorkflowTodoHistory,
  listMyWorkflowTodos,
  rejectWorkflowTodo,
  returnWorkflowTodo,
  uploadWorkflowTodoSignature,
  transferWorkflowTodo,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoListItemResponse,
  type WorkflowTodoReturnTargetResponse,
  type WorkflowTodoCountersignChain
} from '../api/workflow-todos';
import { listWorkflowTodoCountersignCandidates } from '../api/workflow-todos';
import { releaseWorkflowTodo } from '../api/workflow-claims';
import { delegateWorkflowTodo, getWorkflowTodoDelegation, recallWorkflowTodoDelegation,
  type WorkflowTodoDelegation } from '../api/workflow-delegations';
import WorkflowTodoHandoffPicker from '../workflow/WorkflowTodoHandoffPicker.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { usePermission } from '../auth/permission';
import { useSessionStore } from '../auth/session';
import { useRouter } from 'vue-router';
import PermissionGate from '../components/PermissionGate.vue';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';
import WorkflowSignatureCapture from '../workflow/WorkflowSignatureCapture.vue';
import VForm3RuntimeRenderer from '../workflow/VForm3RuntimeRenderer.vue';
import {
  findWorkflowBusinessDetailRoute,
  formatWorkflowBusinessLabel
} from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';

const resultActionFilterOptions = [
  'approve',
  'complete',
  'vote',
  'release',
  'reject',
  'return',
  'transfer',
  'delegate',
  'delegate_recall',
  'reassign',
  'cancel',
  'withdraw',
  'cancelled'
] as const;

type TodoTabKey = 'pending' | 'history';

const { t, locale } = useAdminI18n();

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value ?? '');
}
const router = useRouter();
const { can } = usePermission();
const session = useSessionStore();
const vLoading = ElLoading.directive;
const activeTab = ref<TodoTabKey>('pending');
const listItems = ref<WorkflowTodoListItemResponse[]>([]);
const listPage = ref(1);
const listPageSize = ref(20);
const listTotal = ref(0);
const listLoading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(listLoading);
const definitionKeyFilter = ref('');
const businessTypeFilter = ref('');
const resultActionFilter = ref<string>();
const loading = ref(false);
const acting = ref(false);
const selected = ref<WorkflowTodoDetail>();
const rendererReady = ref(false);
watch(() => selected.value?.id, () => { rendererReady.value = false; }, { flush: 'sync' });
const selectedListItem = ref<WorkflowTodoListItemResponse>();
const fieldPatch = ref<WorkflowSubmission>({});
const voteValues = ref<WorkflowSubmission>({});
const voteChoice = ref<'yes' | 'no' | 'abstain'>();
const voteRendererReady = ref(false);
const comment = ref('');
const signatureFileId = ref<string>();
const signatureUploading = ref(false);
let pendingDecision: { semantic: string; key: string } | undefined;
const returnTargets = ref<WorkflowTodoReturnTargetResponse[]>([]);
const returnTargetStepId = ref('');
const countersignDirection = ref<'before' | 'after'>('before');
const countersignAssigneeIds = ref<string[]>([]);
const countersignCandidates = ref<Array<{ id: string; label: string }>>([]);
const countersignCandidatePage = ref(1);
const countersignCandidateTotal = ref(0);
const countersignCandidateLoading = ref(false);
const countersignCandidateLabels = new Map<string, string>();
const countersignOptions = computed(() => [...countersignAssigneeIds.value
  .filter(id => !countersignCandidates.value.some(candidate => candidate.id === id))
  .map(id => ({ id, label: countersignCandidateLabels.get(id) ?? id })), ...countersignCandidates.value]);
const countersignChain = ref<WorkflowTodoCountersignChain>();
const transferAssigneeId = ref('');
const delegateAssigneeId = ref('');
const delegation = ref<WorkflowTodoDelegation | null>(null);
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;
let detailController: AbortController | undefined;
let detailEpoch = 0;
let contextEpoch = 0;
let disposed = false;
const context = computed(() => JSON.stringify([
  session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId,
  session.currentUser?.scope, session.switching,
  can('workflow.todos.read'), can('workflow.todos.approve'), can('workflow.todos.reject'), can('workflow.todos.complete'),
  can('workflow.todos.return'), can('workflow.todos.countersign'), can('workflow.todos.sign'), can('workflow.todos.transfer'),
  can('workflow.todos.vote'), can('workflow.todos.release')
  , can('workflow.todos.delegate'), can('workflow.todos.recall_delegation')
]));

function canRead(): boolean {
  return !disposed && !session.switching && can('workflow.todos.read');
}

function isContextCurrent(epoch: number): boolean {
  return !disposed && epoch === contextEpoch;
}

watch(context, () => {
  // KeepAlive 不随身份切换销毁；先移除旧授权投影，再加载当前上下文。
  contextEpoch++;
  loadController?.abort();
  closeDetail();
  listItems.value = []; listTotal.value = 0; listPage.value = 1;
  listLoading.value = false; problem.value = undefined;
  void reloadContext();
}, { flush: 'sync' });

const waitingBeforeCountersign = computed(() => activeTab.value !== 'history' && selected.value?.statusKey === 'before_countersign');
const detailReadOnly = computed(() => activeTab.value === 'history' || (selected.value !== undefined && selected.value.statusKey !== 'active'));

const requiredFieldsReady = computed(() => {
  const detail = selected.value;
  if (detail === undefined) {
    return false;
  }
  if (detail.webRenderJson && !rendererReady.value) return false;

  const merged = { ...detail.submission, ...fieldPatch.value };
  return Object.entries(detail.fieldPolicies).every(([key, policy]) =>
    policy !== 'required' || hasValue(merged[key]));
});
const canReturn = computed(() => can('workflow.todos.return') && selected.value?.allowedActions.includes('return') === true);
const canCountersign = computed(() => can('workflow.todos.countersign') && selected.value?.allowedActions.includes('countersign') === true);
const canCancelCountersign = computed(() => canCountersign.value && countersignChain.value?.statusKey === 'active' &&
  (!detailReadOnly.value || waitingBeforeCountersign.value ||
    selected.value?.statusKey === 'completed' && countersignChain.value.directionKey === 'after'));
const canTransfer = computed(() => can('workflow.todos.transfer') && selected.value?.allowedActions.includes('transfer') === true);
const canDelegate = computed(() => can('workflow.todos.delegate') && selected.value?.allowedActions.includes('delegate') === true);
const canRecallDelegation = computed(() => can('workflow.todos.recall_delegation') && delegation.value?.canRecall === true &&
  selected.value?.id === delegation.value.originTodoId);
const transferHasUnsavedChanges = computed(() => selected.value !== undefined && Object.entries(fieldPatch.value)
  .some(([key, value]) => JSON.stringify(value) !== JSON.stringify(selected.value!.submission[key])) ||
  selected.value?.voteForm != null && (voteChoice.value !== (selected.value.voteForm.choiceKey ?? undefined) ||
    Object.entries(voteValues.value).some(([key, value]) => JSON.stringify(value) !== JSON.stringify(selected.value!.voteForm!.values[key]))));
const transferReady = computed(() => !signatureUploading.value && !transferHasUnsavedChanges.value && !!transferAssigneeId.value &&
  (!selected.value?.signRequired || !!signatureFileId.value) &&
  (!selected.value?.commentRequired || comment.value.trim().length > 0));
const delegationReady = computed(() => !signatureUploading.value && !transferHasUnsavedChanges.value && !!delegateAssigneeId.value &&
  (!selected.value?.signRequired || !!signatureFileId.value) &&
  (!selected.value?.commentRequired || comment.value.trim().length > 0));
const decisionReady = computed(() => requiredFieldsReady.value && !signatureUploading.value &&
  (!selected.value?.signRequired || !!signatureFileId.value) &&
  (!selected.value?.commentRequired || comment.value.trim().length > 0));
const voteReady = computed(() => {
  const form = selected.value?.voteForm;
  return selected.value?.activityKindKey === 'human.vote' && can('workflow.todos.vote') &&
    selected.value.allowedActions.includes('vote') && !detailReadOnly.value && form !== undefined && form !== null && !form.readOnly &&
    voteChoice.value !== undefined && (voteChoice.value !== 'abstain' || form.allowAbstain) &&
    (!form.webRenderJson || voteRendererReady.value) && !signatureUploading.value &&
    (!selected.value.signRequired || !!signatureFileId.value) && (!selected.value.commentRequired || comment.value.trim().length > 0) &&
    Object.entries(form.fieldPolicies).every(([key, policy]) => policy !== 'required' || hasValue(voteValues.value[key]));
});
async function submitVote(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !voteReady.value || acting.value || !detail?.voteForm || !voteChoice.value) return;
  await executeAction(() => voteWorkflowTodo(detail.id, detail.revision, detail.voteForm!.revision, voteChoice.value!,
    voteValues.value, comment.value.trim() || null, decisionKey('vote'), signatureFileId.value), 'workflowTodos.voteSuccess', true);
}
async function releaseClaim(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !can('workflow.todos.release') || detailReadOnly.value || acting.value ||
    detail?.activityKindKey !== 'human.claim' || !detail.claimProgress?.allowRelease ||
    !detail.allowedActions.includes('release') || transferHasUnsavedChanges.value || !transferReadyForRelease()) return;
  await executeAction(() => releaseWorkflowTodo(detail.id, { expectedRevision: detail.revision,
    expectedPoolRevision: detail.claimProgress!.poolRevision, expectedFormRevision: detail.submissionRevision,
    idempotencyKey: decisionKey('release'), comment: comment.value.trim() || null, signatureFileId: signatureFileId.value }),
    'workflowTodos.releaseSuccess', true);
}
function transferReadyForRelease(): boolean {
  return !signatureUploading.value && (!selected.value?.signRequired || !!signatureFileId.value) &&
    (!selected.value?.commentRequired || comment.value.trim().length > 0);
}
const returnReady = computed(() =>
  decisionReady.value &&
  returnTargetStepId.value.length > 0 &&
  comment.value.trim().length > 0);

async function loadCountersignContext(todoId: string): Promise<void> {
  const epoch = detailEpoch;
  if (!canCountersign.value) {
    countersignChain.value = undefined;
    clearCountersignCandidates();
    return;
  }

  clearCountersignCandidates();
  const [candidates, chain] = await Promise.all([
    (detailReadOnly.value || waitingBeforeCountersign.value ? Promise.resolve({ items: [], page: 1, total: 0 }) : listWorkflowTodoCountersignCandidates(todoId, 1, 20, detailController?.signal)),
    getWorkflowTodoCountersignChain(todoId, detailController?.signal).catch(() => undefined)
  ]);
  if (epoch !== detailEpoch || selected.value?.id !== todoId) return;
  countersignCandidates.value = candidates.items.map(item => ({ id: item.id, label: item.displayName || item.username }));
  countersignCandidateLabels.clear();
  for (const candidate of countersignCandidates.value) countersignCandidateLabels.set(candidate.id, candidate.label);
  countersignCandidatePage.value = candidates.page;
  countersignCandidateTotal.value = candidates.total;
  countersignChain.value = chain;
  countersignAssigneeIds.value = [];
  countersignDirection.value = 'before';
}

async function loadCountersignCandidatePage(page: number): Promise<void> {
  const todo = selected.value; const epoch = detailEpoch;
  if (!todo || countersignCandidateLoading.value || detailReadOnly.value || acting.value || !canCountersign.value) return;
  countersignCandidateLoading.value = true;
  try {
    const result = await listWorkflowTodoCountersignCandidates(todo.id, page, 20, detailController?.signal);
    if (epoch !== detailEpoch || selected.value?.id !== todo.id) return;
    countersignCandidates.value = result.items.map(item => ({ id: item.id, label: item.displayName || item.username }));
    for (const id of countersignCandidateLabels.keys()) if (!countersignAssigneeIds.value.includes(id)) countersignCandidateLabels.delete(id);
    for (const candidate of countersignCandidates.value) countersignCandidateLabels.set(candidate.id, candidate.label);
    countersignCandidatePage.value = result.page; countersignCandidateTotal.value = result.total;
  } catch (error) { if (epoch === detailEpoch) problem.value = toProblem(error, 'workflowTodos.loadFailed'); }
  finally { if (epoch === detailEpoch) countersignCandidateLoading.value = false; }
}

function clearCountersignCandidates(): void {
  countersignCandidates.value = []; countersignAssigneeIds.value = [];
  countersignCandidatePage.value = 1; countersignCandidateTotal.value = 0; countersignCandidateLoading.value = false;
  countersignCandidateLabels.clear();
}

async function submitCountersign(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !canCountersign.value || detailReadOnly.value || acting.value || detail === undefined || countersignAssigneeIds.value.length === 0 || countersignAssigneeIds.value.length > 10) {
    return;
  }

  await executeAction(() => countersignWorkflowTodo(
      detail.id,
      countersignDirection.value,
      countersignAssigneeIds.value,
      detail.revision,
      comment.value.trim() || null,
      createIdempotencyKey()
    ), 'workflowTodos.countersignSuccess');
}

async function cancelCountersign(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !canCancelCountersign.value || acting.value || detail === undefined || countersignChain.value === undefined) {
    return;
  }

  await executeAction(() => cancelWorkflowTodoCountersign(
      detail.id,
      detail.revision,
      comment.value.trim() || null,
      createIdempotencyKey()
    ), 'workflowTodos.countersignCancelSuccess');
}

async function submitTransfer(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !canTransfer.value || detailReadOnly.value || acting.value || !detail || !transferReady.value) return;
  await executeAction(() => transferWorkflowTodo(detail.id, transferAssigneeId.value, detail.revision,
    detail.submissionRevision, comment.value.trim() || null, decisionKey('transfer'), signatureFileId.value), 'workflowTodos.transferSuccess', true);
}

async function submitDelegation(): Promise<void> {
  const detail = selected.value;
  if (!canRead() || !canDelegate.value || detailReadOnly.value || acting.value || !detail || !delegationReady.value) return;
  await executeAction(() => delegateWorkflowTodo(detail.id, delegateAssigneeId.value, detail.revision,
    detail.submissionRevision, comment.value.trim() || null, decisionKey('delegate'), signatureFileId.value), 'workflowTodos.delegateSuccess', true);
}

async function recallDelegation(): Promise<void> {
  const detail = selected.value; const grant = delegation.value;
  if (!canRead() || !canRecallDelegation.value || acting.value || !detail || !grant ||
    detail.commentRequired && !comment.value.trim()) return;
  await executeAction(() => recallWorkflowTodoDelegation(detail.id, grant.proxyRevision, grant.formRevision,
    comment.value.trim() || null, decisionKey(`delegate_recall:${grant.proxyRevision}:${grant.formRevision}`)), 'workflowTodos.delegateRecallSuccess', true);
}

async function reloadContext(): Promise<void> {
  const context = contextEpoch; const detail = detailEpoch;
  if (await loadList() && isContextCurrent(context) && detail === detailEpoch) openRouteResource();
}

onMounted(() => { void reloadContext(); });
onBeforeUnmount(() => { disposed = true; contextEpoch++; loadController?.abort(); closeDetail(); });

// 页面按路径缓存，切换资源及离开后返回都必须重新读取，不能沿用上一次的授权与状态。
watch([() => router.currentRoute.value.path, () => router.currentRoute.value.query.resourceId], ([path], [oldPath]) => {
  if (path === '/workflow/todos' || oldPath === '/workflow/todos') openRouteResource();
});

function openRouteResource(): void {
  closeDetail();
  const route = router.currentRoute.value;
  const id = route.query.resourceId;
  if (route.path === '/workflow/todos' && typeof id === 'string' && /^[0-9a-f-]{36}$/i.test(id) && canRead()) {
    void loadTodoDetail(id);
  }
}

watch(activeTab, () => {
  listPage.value = 1;
  closeDetail();
  void loadList();
});

async function loadList(): Promise<boolean> {
  if (!canRead()) return false;
  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  const signal = controller.signal;
  listLoading.value = true;
  problem.value = undefined;
  try {
    const query = {
      page: listPage.value,
      pageSize: listPageSize.value,
      definitionKey: definitionKeyFilter.value.trim() || undefined,
      businessType: businessTypeFilter.value.trim() || undefined
    };
    const result = activeTab.value === 'history'
      ? await listMyWorkflowTodoHistory({
          ...query,
          resultActionKey: resultActionFilter.value || undefined
        }, signal)
      : await listMyWorkflowTodos(query, signal);
    if (!signal.aborted) {
      listItems.value = result.items;
      listPage.value = result.page;
      listPageSize.value = result.pageSize;
      listTotal.value = result.total;
      return true;
    }
  } catch (error: unknown) {
    if (!signal.aborted) {
      problem.value = toProblem(error, 'workflowTodos.loadFailed');
    }
  } finally {
    if (loadController === controller && !signal.aborted) {
      listLoading.value = false;
      void syncTableLayout();
    }
  }
  return false;
}

async function openTodo(todo: WorkflowTodoListItemResponse): Promise<void> {
  if (listLoading.value || loading.value || acting.value) {
    return;
  }
  await loadTodoDetail(todo.id, todo);
}

async function loadTodoDetail(id: string, listItem?: WorkflowTodoListItemResponse): Promise<void> {
  if (!canRead()) return;
  closeDetail();
  const epoch = detailEpoch;
  detailController = new AbortController();
  const signal = detailController.signal;
  loading.value = true;
  problem.value = undefined;
  selectedListItem.value = listItem;
  try {
    const detail = await getWorkflowTodo(id, signal);
    if (epoch !== detailEpoch) return;
    // 详情一旦展示即可编辑；先清理上一条待办状态，避免后续辅助请求覆盖新输入。
    fieldPatch.value = {};
    comment.value = '';
    returnTargetStepId.value = '';
    selected.value = detail;
    voteValues.value = { ...detail.voteForm?.values };
    voteChoice.value = detail.voteForm?.choiceKey ?? undefined;
    const targets = !detailReadOnly.value && canReturn.value ? await listWorkflowTodoReturnTargets(id, signal) : [];
    if (epoch !== detailEpoch) return;
    returnTargets.value = targets;
    await loadCountersignContext(id);
    const grant = await getWorkflowTodoDelegation(id, signal);
    if (epoch !== detailEpoch) return;
    delegation.value = grant;
  } catch (error: unknown) {
    if (epoch === detailEpoch) {
      problem.value = toProblem(error, 'workflowTodos.loadFailed');
      selectedListItem.value = undefined;
    }
  } finally {
    if (epoch === detailEpoch) loading.value = false;
  }
}

async function returnSelected(): Promise<void> {
  const detail = selected.value;
  const reason = comment.value.trim();
  if (!canRead() || !canReturn.value || detailReadOnly.value || acting.value || detail === undefined || !returnReady.value) {
    return;
  }

  await executeAction(() => returnWorkflowTodo(
      detail.id,
      returnTargetStepId.value,
      detail.revision,
      detail.submissionRevision,
      fieldPatch.value,
      reason,
      decisionKey('return'),
      ...(signatureFileId.value ? [signatureFileId.value] as [string] : [])
    ), 'workflowTodos.returnSuccess', true);
}

async function act(action: 'approve' | 'reject' | 'complete'): Promise<void> {
  const detail = selected.value;
  const permitted = can(`workflow.todos.${action}`) && detail?.allowedActions.includes(action);
  if (!canRead() || !permitted || detailReadOnly.value || acting.value || detail === undefined || !decisionReady.value) {
    return;
  }

  const execute = action === 'complete' ? completeWorkflowTodo : action === 'approve' ? approveWorkflowTodo : rejectWorkflowTodo;
  await executeAction(() => execute(
      detail.id,
      detail.revision,
      detail.submissionRevision,
      fieldPatch.value,
      comment.value.trim() || null,
      decisionKey(action),
      ...(signatureFileId.value ? [signatureFileId.value] as [string] : [])
    ), action === 'complete' ? 'workflowTodos.completeSuccess' : action === 'approve' ? 'workflowTodos.approveSuccess' : 'workflowTodos.rejectSuccess', true);
}

async function executeAction(execute: () => Promise<unknown>, successKey: MessageKey, refreshOnConflict = false): Promise<void> {
  const context = contextEpoch; const detail = detailEpoch;
  const isCurrent = () => isContextCurrent(context) && detail === detailEpoch;
  acting.value = true;
  problem.value = undefined;
  try {
    // 已发出的写入仍由服务端完成；回执只能更新发起操作时的详情，不能污染新上下文。
    await execute();
    if (!isCurrent()) return;
    ElMessage.success(t(successKey));
    closeDetail();
    await loadList();
  } catch (error: unknown) {
    if (!isCurrent()) return;
    const actionProblem = toProblem(error, 'workflowTodos.operationFailed');
    problem.value = actionProblem;
    if (refreshOnConflict && actionProblem.status === 409) {
      closeDetail();
      const refreshDetail = detailEpoch;
      await loadList();
      // 仅在同一上下文保留原始冲突，避免次生错误掩盖并发事实或覆盖新详情。
      if (isContextCurrent(context) && refreshDetail === detailEpoch) problem.value = actionProblem;
    }
  } finally {
    if (isCurrent()) acting.value = false;
  }
}

function closeDetail(): void {
  detailEpoch++;
  detailController?.abort();
  acting.value = false;
  loading.value = false;
  selected.value = undefined;
  selectedListItem.value = undefined;
  fieldPatch.value = {};
  voteValues.value = {}; voteChoice.value = undefined; voteRendererReady.value = false;
  comment.value = '';
  signatureFileId.value = undefined;
  signatureUploading.value = false;
  pendingDecision = undefined;
  returnTargets.value = [];
  returnTargetStepId.value = '';
  countersignChain.value = undefined;
  clearCountersignCandidates();
  transferAssigneeId.value = '';
  delegateAssigneeId.value = ''; delegation.value = null;
}

function openBusinessDetail(businessType: string, businessId: string): void {
  const route = findWorkflowBusinessDetailRoute(businessType);
  if (route === undefined) {
    return;
  }
  void router.push({ name: route.routeName, query: { [route.idQueryKey]: businessId } });
}

function resultActionLabel(actionKey: string | null | undefined): string {
  if (actionKey === null || actionKey === undefined || actionKey.length === 0) {
    return t('workflowInstances.timeoutStatus.not_configured');
  }
  const key = `workflowTodos.resultAction.${actionKey}` as MessageKey;
  return t(key);
}

function hasValue(value: unknown): boolean {
  return value !== null
    && value !== undefined
    && (typeof value !== 'string' || value.trim().length > 0);
}

function createIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }

  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
}

function decisionKey(action: string): string {
  const semantic = JSON.stringify([selected.value?.id, selected.value?.revision, selected.value?.submissionRevision,
    action, fieldPatch.value, voteValues.value, voteChoice.value, selected.value?.voteForm?.revision, selected.value?.claimProgress?.poolRevision,
    comment.value.trim(), signatureFileId.value, returnTargetStepId.value, transferAssigneeId.value, delegateAssigneeId.value]);
  if (pendingDecision?.semantic !== semantic) pendingDecision = { semantic, key: createIdempotencyKey() };
  return pendingDecision.key;
}

async function captureSignature(file: File): Promise<void> {
  const detail = selected.value;
  if (!detail || detailReadOnly.value || !can('workflow.todos.sign') || signatureUploading.value || acting.value) return;
  const epoch = detailEpoch;
  signatureFileId.value = undefined;
  if (file.size > 1_048_576 || !['image/png', 'image/jpeg'].includes(file.type)) {
    problem.value = { status: 400, code: 'client.workflow_signature_invalid', title: t('workflowTodos.signature.invalid') };
    return;
  }
  signatureUploading.value = true;
  try {
    const uploaded = await uploadWorkflowTodoSignature(detail.id, detail.revision, file, detailController?.signal);
    if (epoch === detailEpoch) signatureFileId.value = uploaded.fileId;
  } catch (error) {
    if (epoch === detailEpoch) problem.value = toProblem(error, 'workflowTodos.operationFailed');
  } finally {
    if (epoch === detailEpoch) signatureUploading.value = false;
  }
}

function approvalModeLabel(modeKey: string): string {
  switch (modeKey) {
    case 'system': return t('workflowDesigner.handling.systemGroup');
    case 'group': return t('workflowDesigner.groups.node');
    case 'all': return t('workflowDesigner.approval.all');
    case 'any': return t('workflowDesigner.approval.any');
    case 'nOfM': return t('workflowDesigner.approval.nOfM');
    case 'sequential': return t('workflowDesigner.approval.sequential');
    case 'percent': return t('workflowDesigner.approval.percent');
    default: return t('workflowDesigner.approval.single');
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'workflowTodos.loadFailed' | 'workflowTodos.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.workflow_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="workflow-todos art-page-stack art-full-height" :aria-busy="listLoading || loading || acting">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowTodos.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowTodos.caption') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never" class="workflow-todos__list art-table-card" :aria-busy="listLoading">
      <el-tabs v-model="activeTab" data-testid="workflow-todo-tabs">
        <el-tab-pane :label="t('workflowTodos.tabs.pending')" name="pending" />
        <el-tab-pane :label="t('workflowTodos.tabs.history')" name="history" />
      </el-tabs>

      <form class="workflow-todos__filters" @submit.prevent="loadList">
        <el-input
          v-model="definitionKeyFilter"
          clearable
          data-testid="workflow-todo-definition-filter"
          :placeholder="t('workflowTodos.filters.definitionKey')"
        />
        <el-input
          v-model="businessTypeFilter"
          clearable
          data-testid="workflow-todo-business-type-filter"
          :placeholder="t('workflowTodos.filters.businessType')"
        />
        <el-select
          v-if="activeTab === 'history'"
          v-model="resultActionFilter"
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
          @click="loadList"
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
            <el-button size="small" data-testid="workflow-todo-open" :disabled="listLoading || loading || acting" @click="openTodo(row as WorkflowTodoListItemResponse)">
              {{ t(activeTab === 'history' ? 'workflowTodos.view' : 'workflowTodos.open') }}
            </el-button>
            <el-button
              v-if="findWorkflowBusinessDetailRoute(row.businessType)"
              link
              type="primary"
              size="small"
              data-testid="workflow-todo-open-business"
              @click="openBusinessDetail(row.businessType, row.businessId)"
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
        :total="listTotal"
        @current-change="value => { listPage = value; void loadList(); }"
      />
      </div>
    </el-card>

    <!-- 详情切换时销毁旧抽屉，避免旧关闭动画回调清空刚载入的新任务。 -->
    <el-drawer
      v-if="selected"
      :key="selected.id"
      :model-value="true"
      :title="t(activeTab === 'history' ? 'workflowTodos.historyDetail' : 'workflowTodos.detail')"
      size="min(680px, 94vw)"
      @close="closeDetail"
    >
      <template v-if="selected">
        <p v-if="waitingBeforeCountersign" role="status" data-testid="workflow-todo-waiting-countersign">{{ t('workflowTodos.waitingBeforeCountersign') }}</p>
        <div
          v-if="activeTab === 'history' && selectedListItem"
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
          <span>{{ t(selected.approvalModeKey === 'group' ? 'workflowTodos.groupProgress' : 'workflowTodos.approvalProgress', {
            approved: selected.approvedCount,
            rejected: selected.rejectedCount,
            pending: selected.pendingCount,
            required: selected.requiredApprovalCount
          }) }}</span>
        </div>
        <div v-if="selected.taskProgress" class="workflow-todos__approval-progress" data-testid="workflow-task-progress">
          <strong>{{ t(`workflowDesigner.tasks.${selected.taskProgress.completionModeKey}`) }}</strong>
          <span>{{ t('workflowTodos.taskProgress', { completed: selected.taskProgress.completedCount, pending: selected.taskProgress.pendingCount,
            cancelled: selected.taskProgress.cancelledCount, required: selected.taskProgress.requiredCompletionCount }) }}</span>
        </div>
        <ul v-if="selected.approvalGroups?.length" class="workflow-todos__group-progress" data-testid="workflow-group-progress">
          <li v-for="group in selected.approvalGroups" :key="group.id" :class="{ 'is-current': group.isCurrentGroup }">
            <strong>{{ group.groupName ?? group.groupKey }} <span v-if="group.isCurrentGroup">{{ t('workflowTodos.currentGroup') }}</span></strong>
            <span>{{ approvalModeLabel(group.approvalModeKey) }} · {{ t(`workflowTodos.groupState.${group.statusKey as 'active' | 'approved' | 'rejected' | 'cancelled'}`) }}</span>
            <span v-if="group.approvalModeKey === 'system'">{{ t('workflowDesigner.handling.systemGroupHint') }}</span>
            <span v-else>{{ t('workflowTodos.approvalProgress', { approved: group.approvedCount, rejected: group.rejectedCount, pending: group.pendingCount, required: group.requiredApprovalCount }) }}</span>
          </li>
        </ul>
        <VForm3RuntimeRenderer v-if="selected.webRenderJson" :key="selected.id" :json="selected.webRenderJson" :submission="selected.submission"
          :field-policies="detailReadOnly ? Object.fromEntries(Object.keys(selected.fieldPolicies).map(key => [key, 'readOnly' as const])) : selected.fieldPolicies"
          @update:patch="fieldPatch = $event" @availability="rendererReady = $event" />
        <WorkflowFormRenderer v-else
          :schema="selected.formSchema"
          :submission="selected.submission"
          :instance-id="selected.instanceId"
          :todo-id="selected.id"
          :field-policies="detailReadOnly
            ? Object.fromEntries(Object.keys(selected.fieldPolicies).map(key => [key, 'readOnly']))
            : selected.fieldPolicies"
          @update:patch="fieldPatch = $event"
        />

        <section v-if="selected.voteForm && selected.voteProgress" data-testid="workflow-vote-private-form">
          <h3>{{ t('workflowTodos.votePrivateForm') }}</h3>
          <p>{{ t('workflowTodos.voteProgress', { submitted: selected.voteProgress.submittedCount, pending: selected.voteProgress.pendingCount, total: selected.voteProgress.memberCount }) }}</p>
          <p v-if="selected.voteProgress.closesAtUtc">{{ t('workflowTodos.voteDeadline', { time: formatDateTime(selected.voteProgress.closesAtUtc) }) }}</p>
          <p v-if="selected.voteProgress.yesCount !== null">{{ t('workflowTodos.voteFinal', { yes: selected.voteProgress.yesCount,
            no: selected.voteProgress.noCount ?? 0, abstain: selected.voteProgress.abstainCount ?? 0, unsubmitted: selected.voteProgress.unsubmittedCount ?? 0 }) }}</p>
          <VForm3RuntimeRenderer v-if="selected.voteForm.webRenderJson" :key="`${selected.id}:vote`" :json="selected.voteForm.webRenderJson"
            :submission="selected.voteForm.values" :field-policies="selected.voteForm.fieldPolicies"
            @update:patch="voteValues = { ...selected.voteForm!.values, ...$event }" @availability="voteRendererReady = $event" />
          <WorkflowFormRenderer v-else :schema="selected.voteForm.formSchema" :submission="selected.voteForm.values" :instance-id="selected.instanceId" :todo-id="selected.id"
            :field-policies="selected.voteForm.fieldPolicies" @update:patch="voteValues = { ...selected.voteForm!.values, ...$event }" />
          <label><span>{{ t('workflowTodos.voteChoice') }}</span>
            <select v-model="voteChoice" data-testid="workflow-vote-choice" :disabled="detailReadOnly || selected.voteForm.readOnly || acting">
              <option :value="undefined" disabled>{{ t('workflowTodos.voteChoice') }}</option>
              <option value="yes">{{ t('workflowTodos.voteChoice.yes') }}</option>
              <option value="no">{{ t('workflowTodos.voteChoice.no') }}</option>
              <option v-if="selected.voteForm.allowAbstain" value="abstain">{{ t('workflowTodos.voteChoice.abstain') }}</option>
            </select>
          </label>
        </section>

        <label v-if="!detailReadOnly || canRecallDelegation" class="workflow-todos__comment">
          <span>{{ t('workflowTodos.comment') }}{{ selected.commentRequired ? ' *' : '' }}</span>
          <textarea
            v-model="comment"
            data-testid="workflow-todo-comment"
            rows="3"
            maxlength="512"
            :required="selected.commentRequired"
            :disabled="acting"
            :placeholder="t('workflowTodos.commentPlaceholder')"
          ></textarea>
        </label>

        <WorkflowSignatureCapture v-if="!detailReadOnly && can('workflow.todos.sign')"
          :key="selected.id" :busy="signatureUploading || acting" :captured="!!signatureFileId"
          @capture="captureSignature" @clear="signatureFileId = undefined" />
        <p v-else-if="!detailReadOnly && selected.signRequired" role="alert">{{ t('workflowTodos.signature.permissionRequired') }}</p>

        <label v-if="!detailReadOnly && canReturn" class="workflow-todos__return-target">
          <span>{{ t('workflowTodos.returnTarget') }}</span>
          <select
            v-model="returnTargetStepId"
            data-testid="workflow-todo-return-target"
            :disabled="acting || returnTargets.length === 0"
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

        <PermissionGate v-if="!detailReadOnly && canTransfer" code="workflow.todos.transfer">
          <div class="workflow-todos__transfer">
            <label class="workflow-todos__return-target">
              <span>{{ t('workflowTodos.transferTarget') }}</span>
              <WorkflowTodoHandoffPicker :key="`${selected.id}:transfer`" v-model="transferAssigneeId" :todo-id="selected.id"
                action="transfer" :excluded-user-id="selected.assigneeUserId" :disabled="acting" test-id="workflow-todo-transfer-target" />
            </label>
            <p>{{ t('workflowTodos.transferHelp') }}</p>
            <p v-if="transferHasUnsavedChanges" role="alert">{{ t('workflowTodos.transferUnsaved') }}</p>
            <el-button data-testid="workflow-todo-transfer-submit" :loading="acting" :disabled="!transferReady" @click="submitTransfer">
              {{ t('workflowTodos.transfer') }}
            </el-button>
          </div>
        </PermissionGate>

        <PermissionGate v-if="!detailReadOnly && canDelegate" code="workflow.todos.delegate">
          <div class="workflow-todos__transfer">
            <label class="workflow-todos__return-target">
              <span>{{ t('workflowTodos.delegateTarget') }}</span>
              <WorkflowTodoHandoffPicker :key="`${selected.id}:delegate`" v-model="delegateAssigneeId" :todo-id="selected.id"
                action="delegate" :excluded-user-id="selected.assigneeUserId" :disabled="acting" test-id="workflow-todo-delegate-target" />
            </label>
            <p>{{ t('workflowTodos.delegateHelp') }}</p>
            <p v-if="transferHasUnsavedChanges" role="alert">{{ t('workflowTodos.handoffUnsaved') }}</p>
            <el-button data-testid="workflow-todo-delegate-submit" :loading="acting" :disabled="!delegationReady" @click="submitDelegation">
              {{ t('workflowTodos.delegate') }}
            </el-button>
          </div>
        </PermissionGate>
        <div v-if="delegation" data-testid="workflow-todo-delegation">
          <p>{{ t('workflowTodos.delegateStatus') }} · {{ t(`workflowTodos.delegationStatus.${delegation.statusKey}` as MessageKey) }}</p>
          <p translate="no">{{ delegation.originalAssigneeUserId }} → {{ delegation.proxyUserId }}</p>
          <PermissionGate v-if="canRecallDelegation" code="workflow.todos.recall_delegation">
            <p>{{ t('workflowTodos.delegateRecallHelp') }}</p>
            <el-button data-testid="workflow-todo-delegate-recall" :loading="acting"
              :disabled="acting || selected.commentRequired && !comment.trim()" @click="recallDelegation">{{ t('workflowTodos.delegateRecall') }}</el-button>
          </PermissionGate>
        </div>

        <PermissionGate v-if="(!detailReadOnly || canCancelCountersign) && canCountersign" code="workflow.todos.countersign">
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
              :disabled="!canCancelCountersign"
              @click="cancelCountersign"
            >
              {{ t('workflowTodos.countersignCancel') }}
            </el-button>
          </div>
            <div v-else-if="!detailReadOnly" class="workflow-todos__countersign">
            <label>
              <span>{{ t('workflowTodos.countersignDirection') }}</span>
              <select v-model="countersignDirection" data-testid="workflow-todo-countersign-direction">
                <option value="before">{{ t('workflowTodos.countersignBefore') }}</option>
                <option value="after">{{ t('workflowTodos.countersignAfter') }}</option>
              </select>
            </label>
            <label>
              <span>{{ t('workflowTodos.countersignAssignees') }}</span>
              <select
                v-model="countersignAssigneeIds"
                data-testid="workflow-todo-countersign-assignees"
                multiple
                :disabled="acting || countersignCandidateLoading || countersignOptions.length === 0"
              >
                <option
                  v-for="candidate in countersignOptions"
                  :key="candidate.id"
                  :value="candidate.id"
                >
                  {{ candidate.label }}
                </option>
              </select>
            </label>
            <ElPagination v-if="countersignCandidateTotal > 20" data-testid="workflow-todo-countersign-pages"
              :current-page="countersignCandidatePage" :page-size="20" :total="countersignCandidateTotal"
              :disabled="acting || countersignCandidateLoading" layout="prev, pager, next" @current-change="loadCountersignCandidatePage" />
            <el-button
              data-testid="workflow-todo-countersign-submit"
              :loading="acting"
              :disabled="countersignCandidateLoading || countersignAssigneeIds.length === 0 || countersignAssigneeIds.length > 10"
              @click="submitCountersign"
            >
              {{ t('workflowTodos.countersign') }}
            </el-button>
          </div>
        </PermissionGate>

      </template>
      <template v-if="selected && !detailReadOnly" #footer>
        <!-- 操作区固定在滚动内容之外，避免隔离帧获得焦点后滚动使点击目标移位。 -->
        <div class="workflow-todos__decision-bar">
          <el-button :disabled="acting" @click="closeDetail">
            {{ t('workflowTodos.close') }}
          </el-button>
          <PermissionGate v-if="selected.allowedActions.includes('reject')" code="workflow.todos.reject">
            <el-button
              type="danger"
              plain
              data-testid="workflow-todo-reject"
              :loading="acting"
              :disabled="!decisionReady"
              @click="act('reject')"
            >
              {{ t('workflowTodos.reject') }}
            </el-button>
          </PermissionGate>
          <PermissionGate v-if="selected.allowedActions.includes('complete')" code="workflow.todos.complete">
            <el-button type="primary" data-testid="workflow-todo-complete" :loading="acting" :disabled="!decisionReady" @click="act('complete')">{{ t('workflowTodos.complete') }}</el-button>
          </PermissionGate>
          <PermissionGate v-if="selected.allowedActions.includes('vote')" code="workflow.todos.vote">
            <el-button type="primary" data-testid="workflow-todo-vote" :loading="acting" :disabled="!voteReady" @click="submitVote">{{ t('workflowTodos.vote') }}</el-button>
          </PermissionGate>
          <PermissionGate v-if="selected.activityKindKey === 'human.claim' && selected.claimProgress?.allowRelease && selected.allowedActions.includes('release')" code="workflow.todos.release">
            <el-button data-testid="workflow-todo-release" :loading="acting" :disabled="transferHasUnsavedChanges || !transferReadyForRelease()" @click="releaseClaim">{{ t('workflowTodos.release') }}</el-button>
          </PermissionGate>
          <PermissionGate v-if="selected.allowedActions.includes('approve')" code="workflow.todos.approve">
            <el-button
              type="primary"
              data-testid="workflow-todo-approve"
              :loading="acting"
              :disabled="!decisionReady"
              @click="act('approve')"
            >
              {{ t('workflowTodos.approve') }}
            </el-button>
          </PermissionGate>
          <PermissionGate v-if="canReturn" code="workflow.todos.return">
            <el-button
              type="warning"
              plain
              data-testid="workflow-todo-return"
              :loading="acting"
              :disabled="!returnReady"
              @click="returnSelected"
            >
              {{ t('workflowTodos.return') }}
            </el-button>
          </PermissionGate>
        </div>
      </template>
    </el-drawer>
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

.workflow-todos__group-progress { display: grid; gap: 10px; padding: 0; list-style: none; }
.workflow-todos__group-progress li { display: grid; gap: 4px; padding: 12px; border: 1px solid var(--el-border-color-light); border-radius: 6px; }
.workflow-todos__group-progress .is-current { border-color: var(--el-color-primary); }
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
