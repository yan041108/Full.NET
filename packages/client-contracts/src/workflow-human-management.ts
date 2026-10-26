import { readWorkflowTodoReturnTargetResponse, readWorkflowTodoCountersignChainResponse,
  readWorkflowManualReminderTargetsResponse, readWorkflowManualReminderResponse,
  type WorkflowTodoReturnTargetResponse, type WorkflowTodoCountersignChainResponse,
  type WorkflowManualReminderTargetsResponse, type WorkflowManualReminderResponse } from './generated/index.generated.js';
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(value);
const integer = (value: number, min = 1, max = Number.MAX_SAFE_INTEGER): boolean => Number.isSafeInteger(value) && value >= min && value <= max;
const date = (value: string): boolean => /^\d{4}-\d{2}-\d{2}T/u.test(value) && Number.isFinite(Date.parse(value));
const invalid = (): never => { throw new TypeError('workflow.invalid_human_management'); };

/** 退回分页只接受真实历史步骤，空办理人代表多人快照，不能回退成当前用户。 */
export function readWorkflowReturnTargetPage(value: unknown): WorkflowTodoReturnTargetResponse[] {
  if (!Array.isArray(value) || value.length > 20) return invalid();
  const rows = value.map(readWorkflowTodoReturnTargetResponse);
  const seen = new Set<string>();
  for (const item of rows) {
    if (!uuid(item.stepId) || seen.has(item.stepId) || !/^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(item.nodeKey) ||
      item.assigneeUserId !== null && !uuid(item.assigneeUserId) || !date(item.completedAtUtc)) return invalid();
    seen.add(item.stepId);
  }
  return rows;
}

/** 只读链显示保留原次序和真实待办，未知状态不能开放取消操作。 */
export function readWorkflowActiveCountersignChain(value: unknown): WorkflowTodoCountersignChainResponse {
  const chain = readWorkflowTodoCountersignChainResponse(value);
  if (!uuid(chain.chainId) || chain.statusKey !== 'active' || !['before', 'after'].includes(chain.directionKey) || chain.items.length < 1 || chain.items.length > 10) return invalid();
  const ids = new Set<string>(); const users = new Set<string>(); const todos = new Set<string>();
  for (let index = 0; index < chain.items.length; index++) {
    const item = chain.items[index]!;
    if (!uuid(item.itemId) || ids.has(item.itemId) || !uuid(item.assigneeUserId) || users.has(item.assigneeUserId) || item.sequenceNo !== index + 1 ||
      !['pending', 'active', 'completed', 'cancelled'].includes(item.statusKey) || item.todoId !== null && (!uuid(item.todoId) || todos.has(item.todoId)) ||
      ['active', 'completed'].includes(item.statusKey) && item.todoId === null) return invalid();
    ids.add(item.itemId); users.add(item.assigneeUserId); if (item.todoId) todos.add(item.todoId);
  }
  return chain;
}

/** 催办范围、当前修订与接收人数均来自本人已授权实例，客户端不能指定接收人。 */
export function readWorkflowReminderTargets(value: unknown): WorkflowManualReminderTargetsResponse {
  const result = readWorkflowManualReminderTargetsResponse(value);
  if (!integer(result.instanceRevision) || result.items.length > 512) return invalid();
  const seen = new Set<string>();
  for (const item of result.items) {
    if (!uuid(item.stepId) || seen.has(item.stepId) || !item.nodeKey || item.nodeKey.length > 128 ||
      !['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(item.nodeTypeKey) ||
      !integer(item.stepRevision) || !integer(item.activeTodoCount, 1, 20) || !integer(item.manualCount, 0, 20) ||
      item.lastRequestedAtUtc !== null && !date(item.lastRequestedAtUtc) || (item.manualCount === 0) !== (item.lastRequestedAtUtc === null) ||
      item.manualCount === 20 && item.canRemind) return invalid();
    seen.add(item.stepId);
  }
  return result;
}
export function readWorkflowReminderReceipt(value: unknown, stepId: string, instanceRevision: number, stepRevision: number): WorkflowManualReminderResponse {
  const result = readWorkflowManualReminderResponse(value);
  if (!uuid(result.requestId) || result.stepId !== stepId || result.instanceRevision !== instanceRevision || result.stepRevision !== stepRevision ||
    !integer(result.manualSequence, 1, 20) || !integer(result.recipientCount, 1, 20) || !date(result.acceptedAtUtc)) return invalid();
  return result;
}
