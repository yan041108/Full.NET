import {
  workflowCancelInstance as cancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
  workflowListInstances,
  workflowListMyInstances,
  workflowPauseInstance as pauseInstance,
  workflowRecoverInstance as recoverInstance,
  workflowReassignInstance as reassignInstance,
  workflowResumeInstance as resumeInstance,
  type CancelWorkflowInstanceRequest,
  type PauseWorkflowInstanceRequest,
  type RecoverWorkflowInstanceRequest,
  type ReassignWorkflowInstanceRequest,
  type ResumeWorkflowInstanceRequest,
  type WorkflowExecutionLogResponse,
  type WorkflowInstanceListItemResponse,
  type WorkflowInstanceResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { workflowFormIdValid, workflowFormWriteRevisionValid } from '../guards/workflow-form-guards';
import { instanceStatuses, instanceTrim, validWorkflowInstance, validWorkflowInstancePage, validWorkflowLogs } from '../guards/workflow-instance-guards';
type InstanceQuery = NonNullable<Parameters<typeof workflowListInstances>[1]>;
function requireId(id: string): void { if (!workflowFormIdValid(id)) throw Error('client.invalid_workflow_instance_id'); }
function assertInstance(value: unknown, id: string): asserts value is WorkflowInstanceResponse { if (!validWorkflowInstance(value) || value.id.toLowerCase() !== id.toLowerCase()) throw Error('client.invalid_workflow_instance'); }
function mutationResult(value: unknown, id: string, revision: number): WorkflowInstanceResponse { assertInstance(value, id); if (value.revision <= revision) throw Error('client.invalid_workflow_instance_receipt'); return value; }
function frozenBody(body: PauseWorkflowInstanceRequest, requireReason = false): PauseWorkflowInstanceRequest {
  const expectedRevision = body.expectedRevision, reason = typeof body.reason === 'string' ? instanceTrim(body.reason) || null : body.reason, idempotencyKey = typeof body.idempotencyKey === 'string' ? instanceTrim(body.idempotencyKey) : '';
  if (!workflowFormWriteRevisionValid(expectedRevision) || reason !== null && typeof reason !== 'string' || reason && reason.length > 512 || requireReason && !reason || !idempotencyKey || idempotencyKey.length > 128) throw Error('client.invalid_workflow_instance_request');
  return { expectedRevision, reason, idempotencyKey };
}
function normalizeQuery(query: InstanceQuery): InstanceQuery & { page: number; pageSize: number } {
  let page = query.page ?? 1, pageSize = query.pageSize ?? 20; if (!Number.isInteger(page) || !Number.isInteger(pageSize)) throw Error('client.invalid_workflow_instance_page'); page = Math.max(1, page); pageSize = Math.min(100, Math.max(1, pageSize));
  const statusKey = query.statusKey ? instanceTrim(query.statusKey) || undefined : undefined, definitionKey = query.definitionKey ? instanceTrim(query.definitionKey) || undefined : undefined;
  if ((page - 1) * pageSize > 2147483647 || statusKey && !instanceStatuses.includes(statusKey as typeof instanceStatuses[number]) || query.definitionVersionId !== undefined && !workflowFormIdValid(query.definitionVersionId)) throw Error('client.invalid_workflow_instance_query');
  const from = query.startedFromUtc === undefined ? undefined : Date.parse(query.startedFromUtc), to = query.startedToUtc === undefined ? undefined : Date.parse(query.startedToUtc);
  if (from !== undefined && !Number.isFinite(from) || to !== undefined && !Number.isFinite(to) || from !== undefined && to !== undefined && from > to) throw Error('client.invalid_workflow_instance_time_range');
  return { page, pageSize, statusKey, definitionKey, definitionVersionId: query.definitionVersionId, startedFromUtc: query.startedFromUtc, startedToUtc: query.startedToUtc };
}

/** 取消工作流实例。 */
export async function cancelWorkflowInstance(
  instanceId: string,
  body: CancelWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const snapshot = frozenBody(body);
  return mutationResult(await cancelInstance(http, { instanceId, body: snapshot }, signal), instanceId, snapshot.expectedRevision);
}

/** 取消时由调用方显式携带 expectedRevision 与幂等键，避免重复取消覆盖并发状态。 */
export type { CancelWorkflowInstanceRequest };

/** 暂停运行中的工作流实例，保留原活动待办。 */
export async function pauseWorkflowInstance(
  instanceId: string,
  body: PauseWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const snapshot = frozenBody(body);
  return mutationResult(await pauseInstance(http, { instanceId, body: snapshot }, signal), instanceId, snapshot.expectedRevision);
}

/** 暂停请求必须显式携带 expectedRevision 与幂等键。 */
export type { PauseWorkflowInstanceRequest };

/** 普通恢复已暂停的工作流实例，并从原活动节点继续。 */
export async function resumeWorkflowInstance(
  instanceId: string,
  body: ResumeWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const snapshot = frozenBody(body);
  return mutationResult(await resumeInstance(http, { instanceId, body: snapshot }, signal), instanceId, snapshot.expectedRevision);
}

/** 普通恢复请求必须显式携带 expectedRevision 与幂等键。 */
export type { ResumeWorkflowInstanceRequest };

/** 管理员强制恢复已暂停实例，原因规范化后不得为空。 */
export async function recoverWorkflowInstance(
  instanceId: string,
  body: RecoverWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const snapshot = frozenBody(body, true) as RecoverWorkflowInstanceRequest;
  return mutationResult(await recoverInstance(http, { instanceId, body: snapshot }, signal), instanceId, snapshot.expectedRevision);
}

/** 强制恢复请求必须显式携带原因、expectedRevision 与幂等键。 */
export type { RecoverWorkflowInstanceRequest };

/** 使用恢复控制面把当前活动待办改派给同一作用域内的活动用户。 */
export async function reassignWorkflowInstance(
  instanceId: string,
  body: ReassignWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const snapshot = { ...frozenBody(body), assigneeUserId: body.assigneeUserId }; requireId(snapshot.assigneeUserId);
  return mutationResult(await reassignInstance(http, { instanceId, body: snapshot }, signal), instanceId, snapshot.expectedRevision);
}

/** 改派请求必须显式携带目标用户、expectedRevision 与幂等键。 */
export type { ReassignWorkflowInstanceRequest };

/** 分页查询当前作用域内全部工作流实例。 */
export async function listWorkflowInstances(
  query: {
    page?: number;
    pageSize?: number;
    statusKey?: string;
    definitionKey?: string;
    definitionVersionId?: string;
    startedFromUtc?: string;
    startedToUtc?: string;
  } = {},
  signal?: AbortSignal
) {
  const snapshot = normalizeQuery(query), result = await workflowListInstances(http, snapshot, signal);
  if (!validWorkflowInstancePage(result, snapshot.page, snapshot.pageSize)) throw Error('client.invalid_workflow_instance_page'); return result;
}

/** 分页查询当前用户发起的工作流实例。 */
export async function listMyWorkflowInstances(
  query: {
    page?: number;
    pageSize?: number;
    statusKey?: string;
    definitionKey?: string;
    definitionVersionId?: string;
    startedFromUtc?: string;
    startedToUtc?: string;
  } = {},
  signal?: AbortSignal
) {
  const snapshot = normalizeQuery(query), result = await workflowListMyInstances(http, snapshot, signal);
  if (!validWorkflowInstancePage(result, snapshot.page, snapshot.pageSize)) throw Error('client.invalid_workflow_instance_page'); return result;
}

/** 读取工作流实例详情，供待办页与详情页回到同一权威实例快照。 */
export async function getWorkflowInstance(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  requireId(instanceId); const result = await workflowGetInstance(http, { instanceId }, signal); assertInstance(result, instanceId); return result;
}

/** 查询工作流实例执行日志，供前端按实例时间线回显节点推进过程。 */
export async function listWorkflowInstanceExecutionLogs(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionLogResponse[]> {
  requireId(instanceId); const result = await workflowListInstanceExecutionLogs(http, { instanceId }, signal); if (!validWorkflowLogs(result, instanceId)) throw Error('client.invalid_workflow_instance_logs'); return result;
}

/** 导出工作流实例详情与执行日志模型，供实例详情页和时间线面板共享同一契约。 */
export type {
  WorkflowExecutionLogResponse,
  WorkflowInstanceListItemResponse,
  WorkflowInstanceResponse
};
