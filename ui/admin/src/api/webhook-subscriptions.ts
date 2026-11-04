import { isGuid, readWebhookSubscription, readWebhookSubscriptions, type WebhookSubscription, type CreateWebhookSubscription, type SetWebhookSubscriptionStatus } from '@fullnet/client-contracts';
import { request } from './http';
import { readWebhookDeliveryHistory, readWebhookDeliveryHistoryPage, type WebhookDeliveryHistory, type WebhookDeliveryHistoryPage, type WebhookDeliveryStatusKey } from '@fullnet/client-contracts';
import { readWebhookDeliveryAttemptPage, type WebhookDeliveryAttemptPage } from '@fullnet/client-contracts';
import { readWebhookDeliveryReplayReceipt, type WebhookDeliveryReplayReceipt, type ReplayWebhookDelivery } from '@fullnet/client-contracts';

/** 读取仅返回当前可信企业的订阅，不缓存或接受客户端指定企业。 */
export async function listWebhookSubscriptions(signal?: AbortSignal): Promise<WebhookSubscription[]> {
  return readWebhookSubscriptions(await request<unknown>('/api/v1/webhooks/subscriptions', { method: 'GET' }, signal));
}
/** 创建无幂等回执，认证失败也不能自动重放；密钥只存在当前确认输入中。 */
export async function createWebhookSubscription(body: CreateWebhookSubscription, signal?: AbortSignal): Promise<WebhookSubscription> {
  const snapshot = { eventType: body.eventType.trim(), targetUrl: body.targetUrl.trim(), signingSecret: body.signingSecret };
  if (!/^[\x21-\x7E]{1,128}$/u.test(snapshot.eventType) || snapshot.targetUrl.length > 2048 || snapshot.signingSecret.length < 16 || snapshot.signingSecret.length > 128) throw Error('client.invalid_webhook_subscription_request');
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

/** 只读取明确订阅的有界页，不允许响应切换到其他资源。 */
export async function listWebhookDeliveryHistory(subscriptionId: string, page = 1, pageSize = 20, statusKey?: WebhookDeliveryStatusKey, signal?: AbortSignal): Promise<WebhookDeliveryHistoryPage> {
  if (!isGuid(subscriptionId) || subscriptionId === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(page) || page < 1
    || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100 || (page - 1) * pageSize > 2_147_483_647
    || statusKey !== undefined && !['pending','processing','delivered','failed'].includes(statusKey)) throw Error('client.invalid_webhook_history_request');
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) }); if (statusKey) query.set('statusKey', statusKey);
  const result = readWebhookDeliveryHistoryPage(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(subscriptionId)}/deliveries?${query}`, { method: 'GET' }, signal));
  if (result.page !== page || result.pageSize !== pageSize || result.items.some(row => row.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase() || statusKey && row.statusKey !== statusKey)) throw Error('client.invalid_webhook_history_page_identity');
  return result;
}
/** 单条详情始终绑定原订阅和明确投递，读取不触发重放。 */
export async function getWebhookDeliveryHistory(subscriptionId: string, deliveryId: string, signal?: AbortSignal): Promise<WebhookDeliveryHistory> {
  if (![subscriptionId, deliveryId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000')) throw Error('client.invalid_webhook_history_identity');
  const result = readWebhookDeliveryHistory(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(subscriptionId)}/deliveries/${encodeURIComponent(deliveryId)}`, { method: 'GET' }, signal));
  if (result.id.toLowerCase() !== deliveryId.toLowerCase() || result.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase()) throw Error('client.invalid_webhook_history_identity');
  return result;
}

/** 尝试记录绑定原企业资源，不把查询当作发送或重放许可。 */
export async function listWebhookDeliveryAttempts(subscriptionId: string, deliveryId: string, page = 1, pageSize = 20, signal?: AbortSignal): Promise<WebhookDeliveryAttemptPage> {
  if (![subscriptionId,deliveryId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000') || !Number.isSafeInteger(page) || page < 1
    || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100 || (page - 1) * pageSize > 2_147_483_647) throw Error('client.invalid_webhook_attempt_request');
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  const result = readWebhookDeliveryAttemptPage(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(subscriptionId)}/deliveries/${encodeURIComponent(deliveryId)}/attempts?${query}`, { method: 'GET' }, signal));
  if (result.page !== page || result.pageSize !== pageSize || result.items.some(row => row.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase() || row.deliveryId.toLowerCase() !== deliveryId.toLowerCase())) throw Error('client.invalid_webhook_attempt_identity');
  return result;
}

/** 固定原操作与修订，认证失败不自动重发；未知后继续使用同一操作标识。 */
export async function replayWebhookDelivery(subscriptionId: string, deliveryId: string, body: ReplayWebhookDelivery, signal?: AbortSignal): Promise<WebhookDeliveryReplayReceipt> {
  if (![subscriptionId,deliveryId,body.operationId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000') || !Number.isSafeInteger(body.version) || body.version < 1 || body.version > 2_147_483_644) throw Error('client.invalid_webhook_replay_request');
  const snapshot = { ...body }, result = readWebhookDeliveryReplayReceipt(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(subscriptionId)}/deliveries/${encodeURIComponent(deliveryId)}/replays`, { method: 'POST', body: JSON.stringify(snapshot) }, signal, { retryUnauthorized: false }));
  if (result.operationId.toLowerCase() !== snapshot.operationId.toLowerCase() || result.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase() || result.deliveryId.toLowerCase() !== deliveryId.toLowerCase() || result.previousVersion !== snapshot.version) throw Error('client.invalid_webhook_replay_identity');
  return result;
}
/** 仅由原操作者手动读取原回执，不借查询重新排队。 */
export async function getWebhookDeliveryReplayReceipt(subscriptionId: string, deliveryId: string, operationId: string, signal?: AbortSignal): Promise<WebhookDeliveryReplayReceipt> {
  if (![subscriptionId,deliveryId,operationId].every(id => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000')) throw Error('client.invalid_webhook_replay_request');
  const result = readWebhookDeliveryReplayReceipt(await request<unknown>(`/api/v1/webhooks/subscriptions/${encodeURIComponent(subscriptionId)}/deliveries/${encodeURIComponent(deliveryId)}/replays/${encodeURIComponent(operationId)}`, { method: 'GET' }, signal));
  if (result.operationId.toLowerCase() !== operationId.toLowerCase() || result.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase() || result.deliveryId.toLowerCase() !== deliveryId.toLowerCase()) throw Error('client.invalid_webhook_replay_identity');
  return result;
}
