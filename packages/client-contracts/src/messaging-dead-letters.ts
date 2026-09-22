import { isGuid, isRecord } from './response-shape.js';

export interface MessagingDeadLetter {
  consumerName: string;
  messageId: string;
  messageType: string;
  schemaVersion: number;
  tenantId: string | null;
  attempts: number;
  receivedAtUtc: string;
  lastErrorCode: string | null;
  lastError: string | null;
}

export interface MessagingDeadLetterPage {
  items: MessagingDeadLetter[];
  total: number;
  page: number;
  pageSize: number;
}

function isNullableGuid(value: unknown): value is string | null {
  return value === null || isGuid(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isMessagingDeadLetter(value: unknown): value is MessagingDeadLetter {
  return isRecord(value)
    && typeof value.consumerName === 'string'
    && isGuid(value.messageId)
    && typeof value.messageType === 'string'
    && Number.isInteger(value.schemaVersion)
    && isNullableGuid(value.tenantId)
    && Number.isInteger(value.attempts)
    && typeof value.receivedAtUtc === 'string'
    && isNullableString(value.lastErrorCode)
    && isNullableString(value.lastError);
}

export function isMessagingDeadLetterPage(value: unknown): value is MessagingDeadLetterPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isMessagingDeadLetter)
    && Number.isInteger(value.total)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize);
}
