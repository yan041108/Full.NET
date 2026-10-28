import { workflowGetRouteDecision, workflowReconcileRouteDecision, type WorkflowRouteDecisionResponse,
  type WorkflowRouteResolutionResponse, type ReconcileWorkflowRouteRequest } from '@fullnet/client-contracts';
import { http } from './http';

export type WorkflowRouteDecision = WorkflowRouteDecisionResponse;
export type WorkflowRouteResolution = WorkflowRouteResolutionResponse;
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const uuid = (value: unknown): value is string => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(value);
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
const invalid = (): never => { throw new TypeError('client.invalid_workflow_route'); };

/** 原输入不进入运维客户端；目标、状态与身份均按闭合协议读取。 */
export async function getWorkflowRouteDecision(instanceId: string, routeId: string, signal?: AbortSignal): Promise<WorkflowRouteDecision> {
  if (!uuid(instanceId) || !uuid(routeId)) return invalid();
  const value: unknown = await workflowGetRouteDecision(http, { instanceId, routeId }, signal);
  if (!object(value) || value.id !== routeId || value.instanceId !== instanceId || !key(value.nodeKey) || !key(value.normalNextNodeKey)
    || !['decision_unknown', 'ready', 'applied', 'cancelled'].includes(String(value.statusKey))
    || !['active', 'suspended', 'completed', 'rejected', 'cancelled'].includes(String(value.instanceStatusKey))
    || !revision(value.revision) || !revision(value.instanceRevision) || !uuid(value.sourceExecutionEpochId)
    || value.targetExecutionEpochId !== null && !uuid(value.targetExecutionEpochId)
    || value.continuationWorkId !== null && !uuid(value.continuationWorkId)
    || value.selectedTargetKey !== null && !key(value.selectedTargetKey) || value.nextNodeKey !== null && !key(value.nextNodeKey)
    || !Array.isArray(value.targets) || value.targets.length < 1 || value.targets.length > 8) return invalid();
  for (const target of value.targets) if (!object(target) || !key(target.targetKey) || !key(target.nodeKey) || !key(target.regionKey)
    || typeof target.reenters !== 'boolean' || !revision(target.maximumIterations) || Number(target.maximumIterations) > 32) return invalid();
  if (new Set(value.targets.map(target => target.targetKey)).size !== value.targets.length ||
    value.statusKey === 'applied' && (!uuid(value.targetExecutionEpochId) || !uuid(value.continuationWorkId) || !key(value.nextNodeKey))) return invalid();
  return value as unknown as WorkflowRouteDecision;
}

/** 只发送机器目标键、原修订和处理原因，无法由客户端注入节点、代次或租户。 */
export async function reconcileWorkflowRoute(instanceId: string, routeId: string, body: ReconcileWorkflowRouteRequest, signal?: AbortSignal): Promise<WorkflowRouteResolution> {
  if (!uuid(instanceId) || !uuid(routeId) || !revision(body.expectedInstanceRevision) || !revision(body.expectedRevision)
    || body.selectedTargetKey !== null && (!key(body.selectedTargetKey) || body.selectedTargetKey.length > 64)
    || typeof body.reason !== 'string' || !body.reason.trim() || body.reason.trim().length > 512 || /[\u0000-\u001f\u007f]/u.test(body.reason)
    || typeof body.idempotencyKey !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u.test(body.idempotencyKey)
    || Object.keys(body).some(name => !['expectedInstanceRevision', 'expectedRevision', 'selectedTargetKey', 'reason', 'idempotencyKey'].includes(name))) return invalid();
  const value: unknown = await workflowReconcileRouteDecision(http, { instanceId, routeId, body }, signal);
  if (!object(value) || value.routeId !== routeId || !uuid(value.continuationWorkId) || !uuid(value.executionEpochId) || !key(value.nextNodeKey)) return invalid();
  return value as unknown as WorkflowRouteResolution;
}
