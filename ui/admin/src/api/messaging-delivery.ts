import {
  isMessagingDeliveryOverview, isMessagingReplayOperation, isMessagingReplayOperationPage,
  isMessagingRollbackOperation, isMessagingCutoverEvidence, isMessagingShadowCapture,
  type MessagingShadowCapture, type MessagingShadowCaptureStart, type MessagingShadowCapturePolicy,
  type MessagingDeliveryOverview, type MessagingReplayOperation, type MessagingReplayOperationPage,
  type MessagingReplayCreateRequest, type MessagingDeliveryStream, type MessagingRollbackOperation,
  type MessagingCutoverEvidence
} from '@fullnet/client-contracts';
import { request } from './http';

export type { MessagingDeliveryOverview, MessagingReplayOperation, MessagingReplayOperationPage, MessagingReplayCreateRequest,
  MessagingDeliveryStream, MessagingRollbackOperation, MessagingCutoverEvidence, MessagingShadowCapture, MessagingShadowCaptureStart, MessagingShadowCapturePolicy };
const operations = '/api/v1/messaging/kafka/replay-operations';

async function validated<T>(path: string, guard: (value: unknown) => value is T,
  options: Parameters<typeof request>[1] = {}): Promise<T> {
  const value = await request<unknown>(path, options);
  if (!guard(value)) throw new Error('client.invalid_messaging_operation_response');
  return value;
}

/** 未观测运行态与实际零计数保持不同，不在客户端补默认健康值。 */
export function getMessagingDeliveryOverview(signal?: AbortSignal): Promise<MessagingDeliveryOverview> {
  return validated('/api/v1/messaging/delivery-status', isMessagingDeliveryOverview, { signal });
}
export function listMessagingReplayOperations(page: number, pageSize: number, signal?: AbortSignal): Promise<MessagingReplayOperationPage> {
  return validated(`${operations}?${new URLSearchParams({ page: String(page), pageSize: String(pageSize) })}`, isMessagingReplayOperationPage, { signal });
}
export function getMessagingReplayOperation(id: string, signal?: AbortSignal): Promise<MessagingReplayOperation> {
  return validated(`${operations}/${encodeURIComponent(id)}`, operationFor(id), { signal });
}
const operationFor = (id: string) => (value: unknown): value is MessagingReplayOperation =>
  isMessagingReplayOperation(value) && value.operationId.toLowerCase() === id.toLowerCase();
/** 创建键由页面在一次创建意图内保留，网络失败重试仍返回同一操作。 */
export function createMessagingReplayOperation(body: MessagingReplayCreateRequest): Promise<MessagingReplayOperation> {
  return validated(operations, (value): value is MessagingReplayOperation => isMessagingReplayOperation(value)
    && value.topicCode === body.topicCode && value.replayConsumerName === body.replayConsumerName && value.maxMessages === body.maxMessages,
    { method: 'POST', body: JSON.stringify(body) });
}
export function executeMessagingReplayOperation(id: string, reason: string, signal?: AbortSignal): Promise<MessagingReplayOperation> {
  return validated(`${operations}/${encodeURIComponent(id)}/execute`, operationFor(id),
    { method: 'POST', body: JSON.stringify({ reason: reason.trim() }), signal });
}
export function cancelMessagingReplayOperation(id: string, reason: string): Promise<MessagingReplayOperation> {
  return validated(`${operations}/${encodeURIComponent(id)}/cancel`, operationFor(id),
    { method: 'POST', body: JSON.stringify({ reason: reason.trim() }) });
}
export function verifyMessagingCutoverEvidence(stream: MessagingDeliveryStream, reason: string): Promise<MessagingCutoverEvidence> {
  return validated('/api/v1/messaging/delivery/cutover-evidence', (value): value is MessagingCutoverEvidence =>
    isMessagingCutoverEvidence(value) && value.eventType === stream.eventType && value.schemaVersion === stream.schemaVersion,
    { method: 'POST', body: JSON.stringify({ eventType: stream.eventType, schemaVersion: stream.schemaVersion, reason: reason.trim() }) });
}
export async function changeMessagingDeliveryOwner(kind: 'cutover' | 'rollback', stream: MessagingDeliveryStream, reason: string): Promise<void> {
  const targetOwner = kind === 'cutover' ? 2 : 0;
  const value = await request<unknown>(`/api/v1/messaging/delivery/${kind}`, { method: 'POST',
    body: JSON.stringify({ eventType: stream.eventType, schemaVersion: stream.schemaVersion, targetOwner, reason: reason.trim() }) });
  if (typeof value !== 'object' || value === null || !('eventType' in value) || value.eventType !== stream.eventType
    || !('schemaVersion' in value) || value.schemaVersion !== stream.schemaVersion || !('targetOwner' in value) || value.targetOwner !== targetOwner
    || !('ownershipPersisted' in value) || value.ownershipPersisted !== true)
    throw new Error('client.invalid_messaging_owner_change');
}
export function getLatestMessagingRollback(stream: MessagingDeliveryStream): Promise<MessagingRollbackOperation> {
  const query = new URLSearchParams({ eventType: stream.eventType, schemaVersion: String(stream.schemaVersion) });
  return validated(`/api/v1/messaging/delivery/rollback-operations?${query}`, (value): value is MessagingRollbackOperation =>
    isMessagingRollbackOperation(value) && value.eventType === stream.eventType && value.schemaVersion === stream.schemaVersion);
}
export function recoverMessagingRollback(generation: string, reason: string): Promise<MessagingRollbackOperation> {
  return validated(`/api/v1/messaging/delivery/rollback-operations/${encodeURIComponent(generation)}/recover`, (value): value is MessagingRollbackOperation =>
    isMessagingRollbackOperation(value) && value.generation.toLowerCase() === generation.toLowerCase(),
    { method: 'POST', body: JSON.stringify({ reason: reason.trim() }) });
}

const captures = '/api/v1/messaging/delivery/shadow-captures';
export function getMessagingShadowCapture(stream: MessagingDeliveryStream, signal?: AbortSignal): Promise<MessagingShadowCapture> {
  const query = new URLSearchParams({ eventType: stream.eventType, schemaVersion: String(stream.schemaVersion) });
  return validated(`${captures}/current?${query}`, (value): value is MessagingShadowCapture => isMessagingShadowCapture(value)
    && value.eventType === stream.eventType && value.schemaVersion === stream.schemaVersion, { signal });
}
export function startMessagingShadowCapture(body: MessagingShadowCaptureStart): Promise<MessagingShadowCapture> {
  return validated(`${captures}/`, (value): value is MessagingShadowCapture => isMessagingShadowCapture(value)
    && value.eventType === body.eventType && value.schemaVersion === body.schemaVersion,
    { method: 'POST', body: JSON.stringify(body) });
}
export function closeMessagingShadowCapture(generation: string, reason: string): Promise<MessagingShadowCapture> {
  return validated(`${captures}/${encodeURIComponent(generation)}/close`, (value): value is MessagingShadowCapture => isMessagingShadowCapture(value)
    && value.generation.toLowerCase() === generation.toLowerCase(), { method: 'POST', body: JSON.stringify({ reason: reason.trim() }) });
}
