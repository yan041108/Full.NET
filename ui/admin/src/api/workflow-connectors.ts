import { workflowListConnectorCatalog, workflowListConnectorCalls, workflowGetConnectorCall, workflowReconcileConnectorCall,
  type WorkflowConnectorCatalogResponse, type WorkflowConnectorCatalogItemResponse, type WorkflowConnectorCallResponse,
  type WorkflowConnectorCallPageResponse, type WorkflowConnectorResolutionResponse, type ReconcileWorkflowConnectorRequest } from '@fullnet/client-contracts';
import { http } from './http';
export type WorkflowConnectorCatalogItem = WorkflowConnectorCatalogItemResponse;
export type WorkflowConnectorCall = WorkflowConnectorCallResponse;
export type WorkflowConnectorPage = WorkflowConnectorCallPageResponse;
export type WorkflowConnectorResolution = WorkflowConnectorResolutionResponse;
export type WorkflowConnectorReconcileRequest = ReconcileWorkflowConnectorRequest;
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const integer = (value: unknown, minimum = 1): value is number => Number.isSafeInteger(value) && Number(value) >= minimum;
const uuid = (value: unknown): value is string => typeof value === 'string' && /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u.test(value);
const invalid = (): never => { throw new TypeError('client.invalid_workflow_connector'); };
const statuses = ['pending', 'dispatched', 'retryable', 'result_ready', 'applying', 'applied', 'outcome_unknown', 'failed', 'rejected', 'cancelled'];

/** 目录只包含服务器当前作用域的固定版本；未知参数类型不能被提升为任意请求。 */
export async function listWorkflowConnectors(signal?: AbortSignal): Promise<WorkflowConnectorCatalogResponse> {
  const value: unknown = await workflowListConnectorCatalog(http, {}, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > 64) return invalid();
  const seen = new Set<string>();
  for (const entry of value.items) {
    if (!object(entry) || !key(entry.connectorKey) || !integer(entry.connectorVersion) || typeof entry.supportsIdempotency !== 'boolean') return invalid();
    const identity = `${entry.connectorKey}@${entry.connectorVersion}`; if (seen.has(identity)) return invalid(); seen.add(identity);
    for (const parameters of [entry.inputParameters, entry.outputParameters]) {
      if (!Array.isArray(parameters) || parameters.length > 512) return invalid();
      const names = new Set<string>();
      for (const parameter of parameters) {
        if (!object(parameter) || !key(parameter.parameterKey) || names.has(parameter.parameterKey) || !key(parameter.fieldTypeKey) ||
          typeof parameter.required !== 'boolean' || !object(parameter.constraints)) return invalid();
        names.add(parameter.parameterKey);
      }
    }
  }
  return value as unknown as WorkflowConnectorCatalogResponse;
}
function readCall(value: unknown, instanceId: string): WorkflowConnectorCall {
  if (!object(value) || !uuid(value.id) || value.instanceId !== instanceId || !key(value.nodeKey) || !key(value.connectorKey) || !integer(value.connectorVersion) ||
    !['sync', 'async'].includes(String(value.modeKey)) || !statuses.includes(String(value.statusKey)) || !integer(value.revision) ||
    !integer(value.attemptCount, 0) || Number(value.attemptCount) > 5 || !integer(value.applyAttemptCount, 0) || !integer(value.inputFormRevision) ||
    typeof value.cancellationRequested !== 'boolean' || typeof value.requiresReconciliation !== 'boolean' || !Array.isArray(value.attempts) || value.attempts.length > 5) return invalid();
  for (const attempt of value.attempts) if (!object(attempt) || !uuid(attempt.id) || !integer(attempt.leaseGeneration) || !['inflight', 'received'].includes(String(attempt.statusKey)) ||
    attempt.outcomeKey !== null && !['succeeded', 'failed', 'rejected', 'retryable', 'outcome_unknown'].includes(String(attempt.outcomeKey))) return invalid();
  return value as unknown as WorkflowConnectorCall;
}
export async function listWorkflowConnectorCalls(instanceId: string, page = 1, signal?: AbortSignal): Promise<WorkflowConnectorPage> {
  if (!uuid(instanceId) || !integer(page)) return invalid();
  const value = await workflowListConnectorCalls(http, { instanceId, page, pageSize: 20 }, signal);
  if (!object(value) || !Array.isArray(value.items) || value.items.length > 20 || value.page !== page || value.pageSize !== 20 || !integer(value.total, 0)) return invalid();
  value.items.forEach(item => readCall(item, instanceId)); return value as unknown as WorkflowConnectorPage;
}
export async function getWorkflowConnectorCall(instanceId: string, callId: string, signal?: AbortSignal): Promise<WorkflowConnectorCall> {
  if (!uuid(instanceId) || !uuid(callId)) return invalid();
  const value = readCall(await workflowGetConnectorCall(http, { instanceId, callId }, signal), instanceId); if (value.id !== callId) return invalid(); return value;
}
/** 对账只引用已保存的尝试及双修订；客户端不能发送业务结果或指定操作人。 */
export async function reconcileWorkflowConnector(instanceId: string, callId: string, body: WorkflowConnectorReconcileRequest, signal?: AbortSignal): Promise<WorkflowConnectorResolution> {
  if (!object(body) || Object.keys(body).some(name => !['expectedInstanceRevision', 'expectedRevision', 'actionKey', 'attemptId', 'evidenceReference', 'reason', 'idempotencyKey'].includes(name)) ||
    !uuid(instanceId) || !uuid(callId) || !integer(body.expectedInstanceRevision) || !integer(body.expectedRevision) ||
    !['accept_result', 'retry', 'confirm_no_effect'].includes(body.actionKey) || !body.reason.trim() || body.reason.trim().length > 512 ||
    !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u.test(body.idempotencyKey) ||
    (body.actionKey === 'accept_result' ? !uuid(body.attemptId) : body.attemptId != null) ||
    body.actionKey !== 'accept_result' && !body.evidenceReference?.trim()) return invalid();
  const value = await workflowReconcileConnectorCall(http, { instanceId, callId, body }, signal);
  if (!object(value) || !uuid(value.id) || value.callId !== callId || value.instanceId !== instanceId || value.actionKey !== body.actionKey ||
    !integer(value.revision) || !integer(value.instanceRevision) || !statuses.includes(String(value.statusKey))) return invalid();
  return value as unknown as WorkflowConnectorResolution;
}
