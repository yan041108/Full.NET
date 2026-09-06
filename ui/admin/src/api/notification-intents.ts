import { request } from './http';

export interface NotificationIntentAttachment {
  fileId: string;
  sortOrder: number;
}

export interface NotificationIntentResponse {
  id: string;
  producerKey: string;
  sceneKey: string;
  idempotencyKey: string;
  templateVersionId: string;
  bindingVersionId: string | null;
  policyCategoryKey: string;
  dispatchModeKey: string;
  statusKey: string;
  routeSnapshotJson: string;
  parameterSnapshotJson: string;
  recipients: Array<{
    id: string;
    recipientTypeKey: string;
    recipientKey: string;
    userId: string | null;
    resolutionStatusKey: string;
  }>;
  attachments: NotificationIntentAttachment[];
  createdAtUtc: string;
}

export interface CreateNotificationIntentRequest {
  producerKey: string;
  sceneKey: string;
  templateKey: string;
  recipients: Array<{ recipientTypeKey: string; recipientKey: string }>;
  parameters: Record<string, string | number | boolean>;
  idempotencyKey: string;
  attachmentFileIds?: string[];
}

function isNotificationIntentResponse(value: unknown): value is NotificationIntentResponse {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as NotificationIntentResponse;
  return typeof candidate.id === 'string'
    && Array.isArray(candidate.attachments)
    && Array.isArray(candidate.recipients);
}

/** 创建通知意图；邮件渠道可附带 Files 模块已上传文件标识。</ */
export async function createNotificationIntent(
  body: CreateNotificationIntentRequest,
  signal?: AbortSignal
): Promise<NotificationIntentResponse> {
  const value = await request<unknown>('/api/v1/notifications/intents', {
    method: 'POST',
    body: JSON.stringify(body)
  }, signal);
  if (!isNotificationIntentResponse(value)) {
    throw new Error('client.invalid_notification_intent_response');
  }

  return value;
}

/** 查询通知意图详情，含附件元数据。</ */
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
