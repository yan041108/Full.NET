export interface MqttBrokerStatus {
  isEnabled: boolean;
  host: string;
  port: number;
  useTls: boolean;
  maximumPayloadBytes: number;
  maximumPublishRatePerMinute: number;
  allowedPublishTopicPrefixes: string[];
  deploymentNotice: string;
}

export interface MqttClient {
  id: string;
  clientKey: string;
  displayName: string;
  description: string | null;
  tenantId: string | null;
  isEnabled: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface MqttMessage {
  id: string;
  tenantId: string | null;
  clientId: string | null;
  clientKey: string | null;
  topic: string;
  payloadSizeBytes: number;
  qos: number;
  status: string;
  idempotencyKey: string | null;
  summaryMessage: string | null;
  publishedAtUtc: string | null;
  createdAtUtc: string;
  createdByUserId: string;
}

export interface MqttMessagePage {
  items: MqttMessage[];
  page: number;
  pageSize: number;
  total: number;
}

export interface MqttMessageListQuery {
  page?: number;
  pageSize?: number;
  clientId?: string;
  status?: string;
  topic?: string;
  fromUtc?: string;
  toUtc?: string;
}

export interface PublishMqttMessageRequest {
  topic: string;
  payload: string;
  qos: number;
  clientId?: string | null;
  idempotencyKey?: string | null;
}

export function isMqttBrokerStatus(value: unknown): value is MqttBrokerStatus {
  return isRecord(value)
    && typeof value.isEnabled === 'boolean'
    && typeof value.host === 'string'
    && typeof value.port === 'number'
    && typeof value.useTls === 'boolean'
    && typeof value.maximumPayloadBytes === 'number'
    && typeof value.maximumPublishRatePerMinute === 'number'
    && Array.isArray(value.allowedPublishTopicPrefixes)
    && value.allowedPublishTopicPrefixes.every((item) => typeof item === 'string')
    && typeof value.deploymentNotice === 'string';
}

export function isMqttClient(value: unknown): value is MqttClient {
  return isRecord(value)
    && isNonEmptyString(value.id)
    && isNonEmptyString(value.clientKey)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && (value.tenantId === null || typeof value.tenantId === 'string')
    && typeof value.isEnabled === 'boolean'
    && typeof value.sortOrder === 'number'
    && isNonEmptyString(value.createdAtUtc)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}

export function isMqttMessage(value: unknown): value is MqttMessage {
  return isRecord(value)
    && isNonEmptyString(value.id)
    && (value.tenantId === null || typeof value.tenantId === 'string')
    && (value.clientId === null || typeof value.clientId === 'string')
    && (value.clientKey === null || typeof value.clientKey === 'string')
    && isNonEmptyString(value.topic)
    && typeof value.payloadSizeBytes === 'number'
    && typeof value.qos === 'number'
    && isNonEmptyString(value.status)
    && (value.idempotencyKey === null || typeof value.idempotencyKey === 'string')
    && (value.summaryMessage === null || typeof value.summaryMessage === 'string')
    && (value.publishedAtUtc === null || typeof value.publishedAtUtc === 'string')
    && isNonEmptyString(value.createdAtUtc)
    && isNonEmptyString(value.createdByUserId);
}

export function isMqttMessagePage(value: unknown): value is MqttMessagePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isMqttMessage)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
