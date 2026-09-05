import {
  workflowCancelInstance as cancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
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
  type WorkflowInstanceResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 取消工作流实例。 */
export async function cancelWorkflowInstance(
  instanceId: string,
  body: CancelWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return cancelInstance(http, { instanceId, body }, signal);
}

/** 取消时由调用方显式携带 expectedRevision 与幂等键，避免重复取消覆盖并发状态。 */
export type { CancelWorkflowInstanceRequest };

/** 暂停运行中的工作流实例，保留原活动待办。 */
export async function pauseWorkflowInstance(
  instanceId: string,
  body: PauseWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return pauseInstance(http, { instanceId, body }, signal);
}

/** 暂停请求必须显式携带 expectedRevision 与幂等键。 */
export type { PauseWorkflowInstanceRequest };

/** 普通恢复已暂停的工作流实例，并从原活动节点继续。 */
export async function resumeWorkflowInstance(
  instanceId: string,
  body: ResumeWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return resumeInstance(http, { instanceId, body }, signal);
}

/** 普通恢复请求必须显式携带 expectedRevision 与幂等键。 */
export type { ResumeWorkflowInstanceRequest };

/** 管理员强制恢复已暂停实例，原因规范化后不得为空。 */
export async function recoverWorkflowInstance(
  instanceId: string,
  body: RecoverWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return recoverInstance(http, { instanceId, body }, signal);
}

/** 强制恢复请求必须显式携带原因、expectedRevision 与幂等键。 */
export type { RecoverWorkflowInstanceRequest };

/** 使用恢复控制面把当前活动待办改派给同一作用域内的活动用户。 */
export async function reassignWorkflowInstance(
  instanceId: string,
  body: ReassignWorkflowInstanceRequest,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return reassignInstance(http, { instanceId, body }, signal);
}

/** 改派请求必须显式携带目标用户、expectedRevision 与幂等键。 */
export type { ReassignWorkflowInstanceRequest };

/** 读取工作流实例详情，供待办页与详情页回到同一权威实例快照。 */
export async function getWorkflowInstance(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowGetInstance(http, { instanceId }, signal);
}

/** 查询工作流实例执行日志，供前端按实例时间线回显节点推进过程。 */
export async function listWorkflowInstanceExecutionLogs(
  instanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionLogResponse[]> {
  return workflowListInstanceExecutionLogs(http, { instanceId }, signal);
}

/** 导出工作流实例详情与执行日志模型，供实例详情页和时间线面板共享同一契约。 */
export type { WorkflowExecutionLogResponse, WorkflowInstanceResponse };
