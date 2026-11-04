import { isGuid, isRecord } from './response-shape.js';

/** 本企业订阅的可见事实；签名材料永不进入读取模型。 */
export interface WebhookSubscription {
  id: string; tenantId: string; eventType: string; targetUrl: string; isActive: boolean; version: number;
}
export interface CreateWebhookSubscription { eventType: string; targetUrl: string; signingSecret: string }
export interface SetWebhookSubscriptionStatus { isActive: boolean; version: number }
export function readWebhookSubscription(value: unknown): WebhookSubscription {
  if (!isRecord(value) || !isGuid(value.id) || !isGuid(value.tenantId)
    || [value.id, value.tenantId].includes('00000000-0000-0000-0000-000000000000')
    || typeof value.eventType !== 'string' || !/^[\x21-\x7E]{1,128}$/u.test(value.eventType)
    || typeof value.targetUrl !== 'string' || value.targetUrl.length > 2048 || typeof value.isActive !== 'boolean') throw Error('client.invalid_webhook_subscription');
  const version = typeof value.version === 'string' && /^[1-9]\d*$/u.test(value.version) ? Number(value.version) : value.version;
  if (!Number.isSafeInteger(version) || (version as number) < 1 || (version as number) > 2_147_483_647) throw Error('client.invalid_webhook_subscription_version');
  try { if (new URL(value.targetUrl).protocol !== 'https:') throw Error(); } catch { throw Error('client.invalid_webhook_subscription_target'); }
  // 仅构造公开字段，异常服务端响应中的签名材料不能被页面或日志沿用。
  return { id: value.id, tenantId: value.tenantId, eventType: value.eventType, targetUrl: value.targetUrl, isActive: value.isActive, version: version as number };
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
