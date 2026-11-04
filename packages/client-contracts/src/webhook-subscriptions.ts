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
    || typeof value.eventType !== 'string' || !value.eventType.trim() || value.eventType.length > 256
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
