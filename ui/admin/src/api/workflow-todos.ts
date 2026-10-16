import {
  readWorkflowInstanceResponse,
  workflowApproveTodo,
  workflowGetTodoRuntime,
  workflowListMyTodoHistory,
  workflowListTodoReturnTargets,
  workflowListMyTodos,
  workflowRejectTodo,
  workflowReturnTodo,
  type WorkflowInstanceResponse,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoListItemResponse,
  type WorkflowTodoReturnTargetResponse
} from '@fullnet/client-contracts';
import { http, request } from './http';
import { workflowFormIdValid, workflowFormWriteRevisionValid } from './workflow-form-guards';
import { instanceTrim, validWorkflowInstance } from './workflow-instance-guards';
import { todoResultActions, validWorkflowTodo, validWorkflowTodoPage, validWorkflowTodoTargets } from './workflow-todo-guards';

function requireId(value: string): void { if (!workflowFormIdValid(value)) throw Error('client.invalid_workflow_todo_id'); }
function mutationBody(expectedRevision: number, comment: string | null, idempotencyKey: string) {
  const note = typeof comment === 'string' ? instanceTrim(comment) || null : comment, key = typeof idempotencyKey === 'string' ? instanceTrim(idempotencyKey) : '';
  if (!workflowFormWriteRevisionValid(expectedRevision) || note !== null && typeof note !== 'string' || note && note.length > 512 || !key || key.length > 128) throw Error('client.invalid_workflow_todo_request');
  return { expectedRevision, comment: note, idempotencyKey: key };
}
function freezePatch(patch: WorkflowSubmission): WorkflowSubmission { if (!patch || typeof patch !== 'object' || Array.isArray(patch)) throw Error('client.invalid_workflow_todo_patch'); return JSON.parse(JSON.stringify(patch)) as WorkflowSubmission; }
function mutationResult(value: unknown): WorkflowInstanceResponse { if (!validWorkflowInstance(value)) throw Error('client.invalid_workflow_todo_receipt'); return value; }
function normalizeQuery<T extends WorkflowTodoListQuery & { resultActionKey?: string; arrivedFromUtc?: string; arrivedToUtc?: string; completedFromUtc?: string; completedToUtc?: string }>(query: T) {
  let page = query.page ?? 1, pageSize = query.pageSize ?? 20;
  if (!Number.isInteger(page) || !Number.isInteger(pageSize)) throw Error('client.invalid_workflow_todo_page'); page = Math.max(1, page); pageSize = Math.min(100, Math.max(1, pageSize));
  if ((page - 1) * pageSize > 2147483647) throw Error('client.invalid_workflow_todo_page');
  const resultActionKey = query.resultActionKey ? instanceTrim(query.resultActionKey) || undefined : undefined;
  if (resultActionKey && !todoResultActions.includes(resultActionKey as typeof todoResultActions[number])) throw Error('client.invalid_workflow_todo_query');
  for (const [from, to] of [[query.arrivedFromUtc, query.arrivedToUtc], [query.completedFromUtc, query.completedToUtc]]) {
    if (from !== undefined && !Number.isFinite(Date.parse(from)) || to !== undefined && !Number.isFinite(Date.parse(to)) || from !== undefined && to !== undefined && Date.parse(from) > Date.parse(to)) throw Error('client.invalid_workflow_todo_time_range');
  }
  return { ...query, page, pageSize, definitionKey: query.definitionKey ? instanceTrim(query.definitionKey) || undefined : undefined, businessType: query.businessType ? instanceTrim(query.businessType) || undefined : undefined, resultActionKey };
}

type WorkflowTodoListQuery = {
  page?: number;
  pageSize?: number;
  definitionKey?: string;
  businessType?: string;
};

/** 分页查询当前用户待办列表。 */
export async function listMyWorkflowTodos(
  query: WorkflowTodoListQuery & {
    arrivedFromUtc?: string;
    arrivedToUtc?: string;
  } = {},
  signal?: AbortSignal
) {
  const snapshot = normalizeQuery(query), result = await workflowListMyTodos(http, snapshot, signal);
  if (!validWorkflowTodoPage(result, snapshot.page, snapshot.pageSize, false)) throw Error('client.invalid_workflow_todo_page'); return result;
}

/** 分页查询当前用户已办历史，结果动作与完成时间来自行内快照。 */
export async function listMyWorkflowTodoHistory(
  query: WorkflowTodoListQuery & {
    resultActionKey?: string;
    completedFromUtc?: string;
    completedToUtc?: string;
  } = {},
  signal?: AbortSignal
) {
  const snapshot = normalizeQuery(query), result = await workflowListMyTodoHistory(http, snapshot, signal);
  if (!validWorkflowTodoPage(result, snapshot.page, snapshot.pageSize, true)) throw Error('client.invalid_workflow_todo_page'); return result;
}

/** 读取单个待办运行时详情，并对返回结构做失败关闭校验。 */
export async function getWorkflowTodo(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoDetail> {
  requireId(todoId); const value = await workflowGetTodoRuntime(http, { todoId }, signal);
  if (!validWorkflowTodo(value) || value.id.toLowerCase() !== todoId.toLowerCase()) {
    throw new Error('client.invalid_workflow_todo_detail');
  }

  return value;
}

/** 查询服务端复核后的合法历史人工审批退回目标。 */
export async function listWorkflowTodoReturnTargets(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoReturnTargetResponse[]> {
  requireId(todoId); const pageSize = 100;
  const targets: WorkflowTodoReturnTargetResponse[] = [];
  for (let page = 1; ; page += 1) {
    signal?.throwIfAborted();
    const batch = await workflowListTodoReturnTargets(
      http,
      { todoId, page, pageSize },
      signal
    );
    if (!validWorkflowTodoTargets(batch) || batch.length > pageSize) throw Error('client.invalid_workflow_todo_targets');
    const seen = new Set(targets.map(target => target.stepId.toLowerCase()));
    if (batch.some(target => seen.has(target.stepId.toLowerCase()))) throw Error('client.invalid_workflow_todo_targets');
    targets.push(...batch);
    if (batch.length < pageSize) {
      return targets;
    }
  }
}

/** 审批通过待办，提交字段补丁、审批意见与幂等键。 */
export async function approveWorkflowTodo(
  todoId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(todoId); const body = { ...mutationBody(expectedRevision, comment, idempotencyKey), fieldPatch: freezePatch(fieldPatch) };
  return mutationResult(await workflowApproveTodo(http, {
    todoId,
    body
  }, signal));
}

/** 驳回待办，提交字段补丁、审批意见与幂等键。 */
export async function rejectWorkflowTodo(
  todoId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(todoId); const body = { ...mutationBody(expectedRevision, comment, idempotencyKey), fieldPatch: freezePatch(fieldPatch) };
  return mutationResult(await workflowRejectTodo(http, {
    todoId,
    body
  }, signal));
}

/** 把当前待办退回到指定合法历史人工审批步骤。 */
export async function returnWorkflowTodo(
  todoId: string,
  targetStepId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(todoId); requireId(targetStepId); const common = mutationBody(expectedRevision, comment, idempotencyKey);
  if (!common.comment) throw Error('client.invalid_workflow_todo_return_reason');
  const body = { targetStepId, expectedRevision: common.expectedRevision, fieldPatch: freezePatch(fieldPatch), comment: common.comment, idempotencyKey: common.idempotencyKey };
  return mutationResult(await workflowReturnTodo(http, {
    todoId,
    body
  }, signal));
}

export type WorkflowTodoCountersignItem = {
  itemId: string;
  sequenceNo: number;
  assigneeUserId: string;
  statusKey: string;
  todoId: string | null;
};

export type WorkflowTodoCountersignChain = {
  chainId: string;
  directionKey: 'before' | 'after';
  statusKey: string;
  items: WorkflowTodoCountersignItem[];
};

/** 校验活动加签链响应，未知或残缺结构必须失败关闭。 */
function readWorkflowTodoCountersignChain(value: unknown): WorkflowTodoCountersignChain {
  if (!value || typeof value !== 'object') {
    throw new Error('client.invalid_workflow_todo_countersign_chain');
  }

  const chain = value as Record<string, unknown>;
  const validItems = Array.isArray(chain.items) && chain.items.every(item => {
    if (!item || typeof item !== 'object') return false;
    const candidate = item as Record<string, unknown>;
    return workflowFormIdValid(candidate.itemId) &&
      Number.isSafeInteger(candidate.sequenceNo) && Number(candidate.sequenceNo) >= 1 && Number(candidate.sequenceNo) <= 10 &&
      workflowFormIdValid(candidate.assigneeUserId) &&
      ['pending', 'active', 'completed', 'cancelled'].includes(String(candidate.statusKey)) &&
      (candidate.todoId === null || workflowFormIdValid(candidate.todoId));
  });
  if (!workflowFormIdValid(chain.chainId) ||
      (chain.directionKey !== 'before' && chain.directionKey !== 'after') ||
      chain.statusKey !== 'active' ||
      !validItems || !Array.isArray(chain.items) || chain.items.length < 1 || chain.items.length > 10 ||
      new Set(chain.items.map(item => item.itemId.toLowerCase())).size !== chain.items.length ||
      new Set(chain.items.map(item => item.sequenceNo)).size !== chain.items.length ||
      new Set(chain.items.map(item => item.assigneeUserId.toLowerCase())).size !== chain.items.length) {
    throw new Error('client.invalid_workflow_todo_countersign_chain');
  }

  return value as WorkflowTodoCountersignChain;
}

/** 读取当前待办的活动加签链。 */
export async function getWorkflowTodoCountersignChain(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoCountersignChain> {
  requireId(todoId); const value = await request<unknown>(
    `/api/v1/workflow/todos/${todoId}/countersign-chain`,
    { method: 'GET', signal }
  );
  return readWorkflowTodoCountersignChain(value);
}

/** 对活动待办发起前加签或后加签。 */
export async function countersignWorkflowTodo(
  todoId: string,
  directionKey: 'before' | 'after',
  assigneeUserIds: string[],
  expectedRevision: number,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(todoId); const common = mutationBody(expectedRevision, comment, idempotencyKey);
  if (!['before', 'after'].includes(directionKey) || assigneeUserIds.length < 1 || assigneeUserIds.length > 10 || !assigneeUserIds.every(workflowFormIdValid) || new Set(assigneeUserIds.map(id => id.toLowerCase())).size !== assigneeUserIds.length) throw Error('client.invalid_workflow_todo_countersign');
  const body = { ...common, directionKey, assigneeUserIds: [...assigneeUserIds] };
  const value = await request<unknown>(
    `/api/v1/workflow/todos/${todoId}/countersign`,
    {
      method: 'POST',
      signal,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    }
  );
  return mutationResult(readWorkflowInstanceResponse(value));
}

/** 取消尚未完成的活动加签链。 */
export async function cancelWorkflowTodoCountersign(
  todoId: string,
  expectedRevision: number,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(todoId); const body = mutationBody(expectedRevision, comment, idempotencyKey);
  const value = await request<unknown>(
    `/api/v1/workflow/todos/${todoId}/countersign/cancel`,
    {
      method: 'POST',
      signal,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    }
  );
  return mutationResult(readWorkflowInstanceResponse(value));
}

/** 导出待办列表、详情、审批结果与字段补丁模型，供待办页和审批弹窗共享同一契约。 */
export type {
  WorkflowInstanceResponse,
  WorkflowSubmission,
  WorkflowTodoDetail,
  WorkflowTodoListItemResponse,
  WorkflowTodoReturnTargetResponse
};
