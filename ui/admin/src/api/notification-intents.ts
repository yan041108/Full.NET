import {
  isNotificationIntentResponse,
  type CreateNotificationIntentRequest,
  type NotificationIntentResponse
} from '@fullnet/client-contracts';
import { request } from './http';

/** 创建通知意图；邮件渠道可附带 Files 模块已上传文件标识。 */
export async function createNotificationIntent(
  body: CreateNotificationIntentRequest,
  signal?: AbortSignal
): Promise<NotificationIntentResponse> {
  const value = await request<unknown>('/api/v1/notifications/intents', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  }, signal);
  if (!isNotificationIntentResponse(value)) {
    throw new Error('client.invalid_notification_intent_response');
  }

  return value;
}

/** 查询通知意图详情，含附件元数据。 */
export async function getNotificationIntent(
  intentId: string,
  signal?: AbortSignal
): Promise<NotificationIntentResponse> {
  const value = await request<unknown>(`/api/v1/notifications/intents/${intentId}`, {
    method: 'GET'
  }, signal);
  if (!isNotificationIntentResponse(value)) {
    throw new Error('client.invalid_notification_intent_response');
  }

  return value;
}

export type { CreateNotificationIntentRequest, NotificationIntentResponse };
