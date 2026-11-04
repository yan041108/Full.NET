import { isGuid, isRecord } from './response-shape.js';

/** 本企业订阅的可见事实；签名材料永不进入读取模型。 */
export interface WebhookSubscription {
  id: string; tenantId: string; eventType: string; targetUrl: string; isActive: boolean; version: number; signingKeyId: string;
}
export interface CreateWebhookSubscription { eventType: string; targetUrl: string; signingSecret: string }
export interface SetWebhookSubscriptionStatus { isActive: boolean; version: number }
export function readWebhookSubscription(value: unknown): WebhookSubscription {
  if (!isRecord(value) || !isGuid(value.id) || !isGuid(value.tenantId) || !isGuid(value.signingKeyId)
    || [value.id, value.tenantId, value.signingKeyId].includes('00000000-0000-0000-0000-000000000000')
    || typeof value.eventType !== 'string' || !/^[\x21-\x7E]{1,128}$/u.test(value.eventType)
    || typeof value.targetUrl !== 'string' || value.targetUrl.length > 2048 || typeof value.isActive !== 'boolean') throw Error('client.invalid_webhook_subscription');
  const version = typeof value.version === 'string' && /^[1-9]\d*$/u.test(value.version) ? Number(value.version) : value.version;
  if (!Number.isSafeInteger(version) || (version as number) < 1 || (version as number) > 2_147_483_647) throw Error('client.invalid_webhook_subscription_version');
  try { if (new URL(value.targetUrl).protocol !== 'https:') throw Error(); } catch { throw Error('client.invalid_webhook_subscription_target'); }
  // 仅构造公开字段，异常服务端响应中的签名材料不能被页面或日志沿用。
  return { id: value.id, tenantId: value.tenantId, eventType: value.eventType, targetUrl: value.targetUrl, isActive: value.isActive, version: version as number, signingKeyId: value.signingKeyId };
}
export function readWebhookSubscriptions(value: unknown): WebhookSubscription[] {
  if (!Array.isArray(value)) throw Error('client.invalid_webhook_subscriptions');
  const result = value.map(readWebhookSubscription), ids = new Set(result.map(row => row.id.toLowerCase()));
  if (ids.size !== result.length) throw Error('client.duplicate_webhook_subscription');
  return result;
}

export type WebhookDeliveryStatusKey = 'pending' | 'processing' | 'delivered' | 'failed';
/** 单个事件投递的当前事实，不包含原始错误、正文或签名材料。 */
export interface WebhookDeliveryHistory {
  id: string; tenantId: string; subscriptionId: string; eventId: string; statusKey: WebhookDeliveryStatusKey;
  attemptCount: number; nextAttemptAtUtc: string | null; lastErrorCode: string | null; lastHttpStatus: number | null;
  createdAtUtc: string; updatedAtUtc: string; version: number;
}
export interface WebhookDeliveryHistoryPage { items: WebhookDeliveryHistory[]; page: number; pageSize: number; total: number }
const statuses = ['pending', 'processing', 'delivered', 'failed'];
const utc = (value: unknown): value is string => typeof value === 'string' && /(?:Z|\+00:00)$/u.test(value) && Number.isFinite(Date.parse(value));
function integer(value: unknown, minimum: number, maximum = 2_147_483_647): number {
  const result = typeof value === 'string' && /^\d+$/u.test(value) ? Number(value) : value;
  if (!Number.isSafeInteger(result) || (result as number) < minimum || (result as number) > maximum) throw Error('client.invalid_webhook_history_integer');
  return result as number;
}
export function readWebhookDeliveryHistory(value: unknown): WebhookDeliveryHistory {
  if (!isRecord(value) || ![value.id, value.tenantId, value.subscriptionId, value.eventId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000')
    || typeof value.statusKey !== 'string' || !statuses.includes(value.statusKey) || !utc(value.createdAtUtc) || !utc(value.updatedAtUtc)
    || value.nextAttemptAtUtc !== null && !utc(value.nextAttemptAtUtc)
    || value.lastErrorCode !== null && !['webhooks.delivery.http_error','webhooks.delivery.timeout','webhooks.delivery.failed'].includes(value.lastErrorCode as string)
    || value.lastHttpStatus !== null && (!Number.isInteger(value.lastHttpStatus) || (value.lastHttpStatus as number) < 100 || (value.lastHttpStatus as number) > 599)
    || (value.lastErrorCode === 'webhooks.delivery.http_error') !== (value.lastHttpStatus !== null)) throw Error('client.invalid_webhook_history');
  return { id: value.id as string, tenantId: value.tenantId as string, subscriptionId: value.subscriptionId as string, eventId: value.eventId as string,
    statusKey: value.statusKey as WebhookDeliveryStatusKey, attemptCount: integer(value.attemptCount, 0), version: integer(value.version, 1),
    nextAttemptAtUtc: value.nextAttemptAtUtc as string | null, lastErrorCode: value.lastErrorCode as string | null, lastHttpStatus: value.lastHttpStatus as number | null,
    createdAtUtc: value.createdAtUtc, updatedAtUtc: value.updatedAtUtc };
}
export function readWebhookDeliveryHistoryPage(value: unknown): WebhookDeliveryHistoryPage {
  if (!isRecord(value) || !Array.isArray(value.items)) throw Error('client.invalid_webhook_history_page');
  const page = integer(value.page, 1), pageSize = integer(value.pageSize, 1, 100), total = integer(value.total, 0, Number.MAX_SAFE_INTEGER), items = value.items.map(readWebhookDeliveryHistory);
  if (items.length > pageSize || items.length > total || new Set(items.map(row => row.id.toLowerCase())).size !== items.length) throw Error('client.invalid_webhook_history_page');
  return { page, pageSize, total, items };
}

export interface WebhookDeliveryAttempt {
  id: string; tenantId: string; subscriptionId: string; deliveryId: string;
  outcomeKey: 'processing' | 'unconfirmed' | 'delivered' | 'failed'; startedAtUtc: string; completedAtUtc: string | null;
  httpStatus: number | null; errorCode: string | null; appliedToDelivery: boolean | null;
}
export interface WebhookDeliveryAttemptPage { items: WebhookDeliveryAttempt[]; page: number; pageSize: number; total: number }

/** 原重放排队事实；不表示当前状态或最终交付，累计次数保持不变。 */
export interface WebhookDeliveryReplayReceipt {
  operationId: string; tenantId: string; subscriptionId: string; deliveryId: string; eventId: string;
  previousVersion: number; version: number; attemptCount: number; queuedAtUtc: string;
}
export interface ReplayWebhookDelivery { operationId: string; version: number }
/** 轮换公开事实；准备秘密及其加密指纹均不进入客户端模型。 */
export interface WebhookSigningKeyRotationReceipt {
  operationId: string; tenantId: string; subscriptionId: string; previousKeyId: string; signingKeyId: string;
  previousVersion: number; preparedVersion: number; stateKey: 'prepared' | 'activated' | 'cancelled';
  preparedAtUtc: string; completedAtUtc: string | null; completedVersion: number | null;
}
export interface PrepareWebhookSigningKeyRotation { operationId: string; version: number; signingSecret: string }
export interface CompleteWebhookSigningKeyRotation { version: number; signingKeyId: string }
export function readWebhookSigningKeyRotationReceipt(value: unknown): WebhookSigningKeyRotationReceipt {
  if (!isRecord(value) || ![value.operationId,value.tenantId,value.subscriptionId,value.previousKeyId,value.signingKeyId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000')
    || (value.previousKeyId as string).toLowerCase() === (value.signingKeyId as string).toLowerCase() || !utc(value.preparedAtUtc) || !['prepared','activated','cancelled'].includes(value.stateKey as string)) throw Error('client.invalid_webhook_rotation');
  const previousVersion=integer(value.previousVersion,1,2_147_483_645),preparedVersion=integer(value.preparedVersion,2,2_147_483_646);
  if(preparedVersion!==previousVersion+1)throw Error('client.invalid_webhook_rotation_version');
  let completedVersion:number|null=null;
  if(value.stateKey==='prepared'){if(value.completedAtUtc!==null||value.completedVersion!==null)throw Error('client.invalid_webhook_rotation_state');}
  else {if(!utc(value.completedAtUtc))throw Error('client.invalid_webhook_rotation_time');completedVersion=integer(value.completedVersion,preparedVersion+1);if(Date.parse(value.completedAtUtc)<Date.parse(value.preparedAtUtc))throw Error('client.invalid_webhook_rotation_time');}
  return {operationId:value.operationId as string,tenantId:value.tenantId as string,subscriptionId:value.subscriptionId as string,previousKeyId:value.previousKeyId as string,signingKeyId:value.signingKeyId as string,
    previousVersion,preparedVersion,stateKey:value.stateKey as WebhookSigningKeyRotationReceipt['stateKey'],preparedAtUtc:value.preparedAtUtc,completedAtUtc:value.completedAtUtc as string|null,completedVersion};
}
export function readWebhookDeliveryReplayReceipt(value: unknown): WebhookDeliveryReplayReceipt {
  if (!isRecord(value) || ![value.operationId,value.tenantId,value.subscriptionId,value.deliveryId,value.eventId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000') || !utc(value.queuedAtUtc)) throw Error('client.invalid_webhook_replay_receipt');
  const previousVersion = integer(value.previousVersion, 1, 2_147_483_644), version = integer(value.version, 2);
  if (version !== previousVersion + 1) throw Error('client.invalid_webhook_replay_version');
  return { operationId: value.operationId as string, tenantId: value.tenantId as string, subscriptionId: value.subscriptionId as string, deliveryId: value.deliveryId as string, eventId: value.eventId as string,
    previousVersion, version, attemptCount: integer(value.attemptCount, 0), queuedAtUtc: value.queuedAtUtc };
}
export function readWebhookDeliveryAttemptPage(value: unknown): WebhookDeliveryAttemptPage {
  if (!isRecord(value) || !Array.isArray(value.items)) throw Error('client.invalid_webhook_attempt_page');
  const page = integer(value.page, 1), pageSize = integer(value.pageSize, 1, 100), total = integer(value.total, 0, Number.MAX_SAFE_INTEGER);
  const items: WebhookDeliveryAttempt[] = value.items.map(item => {
    if (!isRecord(item) || ![item.id,item.tenantId,item.subscriptionId,item.deliveryId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000')
      || !['processing','unconfirmed','delivered','failed'].includes(item.outcomeKey as string) || !utc(item.startedAtUtc)
      || item.completedAtUtc !== null && !utc(item.completedAtUtc)
      || item.httpStatus !== null && (!Number.isInteger(item.httpStatus) || (item.httpStatus as number) < 100 || (item.httpStatus as number) > 599)
      || item.errorCode !== null && !['webhooks.delivery.http_error','webhooks.delivery.timeout','webhooks.delivery.failed','webhooks.delivery.target_invalid','webhooks.delivery.target_blocked','webhooks.delivery.transport_error'].includes(item.errorCode as string)
      || item.appliedToDelivery !== null && typeof item.appliedToDelivery !== 'boolean') throw Error('client.invalid_webhook_attempt');
    const completed = item.outcomeKey === 'delivered' || item.outcomeKey === 'failed';
    if (completed !== (item.completedAtUtc !== null) || completed !== (item.appliedToDelivery !== null)
      || !completed && (item.httpStatus !== null || item.errorCode !== null)) throw Error('client.invalid_webhook_attempt_outcome');
    return { id: item.id as string, tenantId: item.tenantId as string, subscriptionId: item.subscriptionId as string, deliveryId: item.deliveryId as string,
      outcomeKey: item.outcomeKey as WebhookDeliveryAttempt['outcomeKey'], startedAtUtc: item.startedAtUtc, completedAtUtc: item.completedAtUtc as string | null,
      httpStatus: item.httpStatus as number | null, errorCode: item.errorCode as string | null, appliedToDelivery: item.appliedToDelivery as boolean | null };
  });
  if (items.length > pageSize || items.length > total || new Set(items.map(row => row.id.toLowerCase())).size !== items.length) throw Error('client.invalid_webhook_attempt_page');
  return { items, page, pageSize, total };
}
