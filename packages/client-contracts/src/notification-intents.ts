export interface NotificationIntentAttachment {
  fileId: string;
  sortOrder: number;
}

export interface NotificationIntentRecipient {
  id: string;
  recipientTypeKey: string;
  recipientKey: string;
  userId: string | null;
  resolutionStatusKey: string;
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
  recipients: NotificationIntentRecipient[];
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

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isNotificationIntentResponse(
  value: unknown
): value is NotificationIntentResponse {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.producerKey === 'string'
    && typeof value.sceneKey === 'string'
    && typeof value.idempotencyKey === 'string'
    && isGuid(value.templateVersionId)
    && (value.bindingVersionId === null || isGuid(value.bindingVersionId))
    && typeof value.policyCategoryKey === 'string'
    && typeof value.dispatchModeKey === 'string'
    && typeof value.statusKey === 'string'
    && typeof value.routeSnapshotJson === 'string'
    && typeof value.parameterSnapshotJson === 'string'
    && Array.isArray(value.recipients)
    && value.recipients.every(isNotificationIntentRecipient)
    && Array.isArray(value.attachments)
    && value.attachments.every(isNotificationIntentAttachment)
    && typeof value.createdAtUtc === 'string';
}

function isNotificationIntentRecipient(
  value: unknown
): value is NotificationIntentRecipient {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.recipientTypeKey === 'string'
    && typeof value.recipientKey === 'string'
    && (value.userId === null || isGuid(value.userId))
    && typeof value.resolutionStatusKey === 'string';
}

function isNotificationIntentAttachment(
  value: unknown
): value is NotificationIntentAttachment {
  return isRecord(value)
    && isGuid(value.fileId)
    && Number.isInteger(value.sortOrder);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
