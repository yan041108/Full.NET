import { isGuid, readWebhookSubscription, readWebhookSubscriptions, type WebhookSubscription, type CreateWebhookSubscription, type SetWebhookSubscriptionStatus } from '@fullnet/client-contracts';
import { request } from './http';

/** 读取仅返回当前可信企业的订阅，不缓存或接受客户端指定企业。 */
export async function listWebhookSubscriptions(signal?: AbortSignal): Promise<WebhookSubscription[]> {
  return readWebhookSubscriptions(await request<unknown>('/api/v1/webhooks/subscriptions', { method: 'GET' }, signal));
}
/** 创建无幂等回执，认证失败也不能自动重放；密钥只存在当前确认输入中。 */
export async function createWebhookSubscription(body: CreateWebhookSubscription, signal?: AbortSignal): Promise<WebhookSubscription> {
  const snapshot = { eventType: body.eventType.trim(), targetUrl: body.targetUrl.trim(), signingSecret: body.signingSecret };
  if (!snapshot.eventType || snapshot.eventType.length > 256 || snapshot.targetUrl.length > 2048 || snapshot.signingSecret.length < 16 || snapshot.signingSecret.length > 1024) throw Error('client.invalid_webhook_subscription_request');
  const result = readWebhookSubscription(await request<unknown>('/api/v1/webhooks/subscriptions', { method: 'POST', body: JSON.stringify(snapshot) }, signal, { retryUnauthorized: false }));
  if (result.eventType !== snapshot.eventType || result.targetUrl !== snapshot.targetUrl || !result.isActive) throw Error('client.invalid_webhook_subscription_identity');
  return result;
}
/** 状态写入携带当前版本，响应必须属于原订阅和目标状态。 */
export async function setWebhookSubscriptionStatus(id: string, body: SetWebhookSubscriptionStatus, signal?: AbortSignal): Promise<WebhookSubscription> {
  if (!isGuid(id) || id === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(body.version) || body.version < 1 || body.version > 2_147_483_647 || typeof body.isActive !== 'boolean') throw Error('client.invalid_webhook_subscription_status_request');
  const snapshot = { ...body };
  const result = readWebhookSubscription(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(id)}/status`, { method: 'POST', body: JSON.stringify(snapshot) }, signal, { retryUnauthorized: false }));
  if (result.id.toLowerCase() !== id.toLowerCase() || result.isActive !== snapshot.isActive || result.version < snapshot.version) throw Error('client.invalid_webhook_subscription_status_response');
  return result;
}
