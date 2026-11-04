// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 数据模型。

export interface CompleteWebhookSigningKeyRotationRequest {
  readonly signingKeyId: string;
  readonly version: number;
}

export interface CreateWebhookSubscriptionRequest {
  readonly eventType: string;
  readonly signingSecret: string;
  readonly targetUrl: string;
}

export interface PrepareWebhookSigningKeyRotationRequest {
  readonly operationId: string;
  readonly signingSecret: string;
  readonly version: number;
}

export interface ProblemDetails {
  readonly code?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly title?: string;
}

export interface ReplayWebhookDeliveryRequest {
  readonly operationId: string;
  readonly version: number;
}

export interface SetWebhookSubscriptionStatusRequest {
  readonly isActive: boolean;
  readonly version: number;
}

export interface WebhookDeliveryAttemptPageResponse {
  readonly items: Array<WebhookDeliveryAttemptResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number | string;
}

export interface WebhookDeliveryAttemptResponse {
  readonly appliedToDelivery: boolean | null;
  readonly completedAtUtc: string | null;
  readonly deliveryId: string;
  readonly errorCode: null | "webhooks.delivery.http_error" | "webhooks.delivery.timeout" | "webhooks.delivery.target_invalid" | "webhooks.delivery.target_blocked" | "webhooks.delivery.transport_error" | "webhooks.delivery.failed" | null;
  readonly httpStatus: number | null;
  readonly id: string;
  readonly outcomeKey: "processing" | "unconfirmed" | "delivered" | "failed";
  readonly startedAtUtc: string;
  readonly subscriptionId: string;
  readonly tenantId: string;
}

export interface WebhookDeliveryHistoryPageResponse {
  readonly items: Array<WebhookDeliveryHistoryResponse>;
  readonly page: number;
  readonly pageSize: number;
  readonly total: number | string;
}

export interface WebhookDeliveryHistoryResponse {
  readonly attemptCount: number;
  readonly createdAtUtc: string;
  readonly eventId: string;
  readonly id: string;
  readonly lastErrorCode: null | "webhooks.delivery.http_error" | "webhooks.delivery.timeout" | "webhooks.delivery.failed" | null;
  readonly lastHttpStatus: number | null;
  readonly nextAttemptAtUtc: string | null;
  readonly statusKey: "pending" | "processing" | "delivered" | "failed";
  readonly subscriptionId: string;
  readonly tenantId: string;
  readonly updatedAtUtc: string;
  readonly version: number;
}

export interface WebhookDeliveryReplayReceipt {
  readonly attemptCount: number;
  readonly deliveryId: string;
  readonly eventId: string;
  readonly operationId: string;
  readonly previousVersion: number;
  readonly queuedAtUtc: string;
  readonly subscriptionId: string;
  readonly tenantId: string;
  readonly version: number;
}

export interface WebhookSigningKeyRotationReceipt {
  readonly completedAtUtc: string | null;
  readonly completedVersion: number | null;
  readonly operationId: string;
  readonly preparedAtUtc: string;
  readonly preparedVersion: number;
  readonly previousKeyId: string;
  readonly previousVersion: number;
  readonly signingKeyId: string;
  readonly stateKey: "prepared" | "activated" | "cancelled";
  readonly subscriptionId: string;
  readonly tenantId: string;
}

export interface WebhookSubscriptionResponse {
  readonly eventType: string;
  readonly id: string;
  readonly isActive: boolean;
  readonly signingKeyId: string;
  readonly targetUrl: string;
  readonly tenantId: string;
  readonly version: number;
}

export interface WorkflowCompletedWebhookData {
  readonly BusinessId: string;
  readonly BusinessType: string;
  readonly InstanceId: string;
  readonly RecipientUserId: string;
}

export interface WorkflowCompletedWebhookEvent {
  readonly Data: WorkflowCompletedWebhookData;
  readonly EventId: string;
  readonly EventType: "fullnet.workflow.instance.completed";
  readonly OccurredAtUtc: string;
  readonly TenantId: string;
}
