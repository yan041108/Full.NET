// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

import type {
  CreateWebhookSubscriptionRequest,
  ProblemDetails,
  ReplayWebhookDeliveryRequest,
  SetWebhookSubscriptionStatusRequest,
  WebhookDeliveryAttemptPageResponse,
  WebhookDeliveryAttemptResponse,
  WebhookDeliveryHistoryPageResponse,
  WebhookDeliveryHistoryResponse,
  WebhookDeliveryReplayReceipt,
  WebhookSubscriptionResponse,
  WorkflowCompletedWebhookData,
  WorkflowCompletedWebhookEvent
} from './models.generated.js';

export function readCreateWebhookSubscriptionRequest(value: unknown): CreateWebhookSubscriptionRequest {
  return requireValidResponse(value, isCreateWebhookSubscriptionRequest(value), 'create_webhook_subscription_request') as CreateWebhookSubscriptionRequest;
}

function isCreateWebhookSubscriptionRequest(value: unknown): value is CreateWebhookSubscriptionRequest {
  return isRecord(value) && (typeof value["eventType"] === 'string') && (typeof value["signingSecret"] === 'string') && (typeof value["targetUrl"] === 'string');
}

export function readProblemDetails(value: unknown): ProblemDetails {
  return requireValidResponse(value, isProblemDetails(value), 'problem_details') as ProblemDetails;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return isRecord(value) && (value["code"] === undefined || (typeof value["code"] === 'string')) && (value["detail"] === undefined || (typeof value["detail"] === 'string')) && (value["status"] === undefined || (Number.isSafeInteger(value["status"]))) && (value["title"] === undefined || (typeof value["title"] === 'string'));
}

export function readReplayWebhookDeliveryRequest(value: unknown): ReplayWebhookDeliveryRequest {
  return requireValidResponse(value, isReplayWebhookDeliveryRequest(value), 'replay_webhook_delivery_request') as ReplayWebhookDeliveryRequest;
}

function isReplayWebhookDeliveryRequest(value: unknown): value is ReplayWebhookDeliveryRequest {
  return isRecord(value) && (typeof value["operationId"] === 'string' && guidPattern.test(value["operationId"])) && (Number.isSafeInteger(value["version"]));
}

export function readSetWebhookSubscriptionStatusRequest(value: unknown): SetWebhookSubscriptionStatusRequest {
  return requireValidResponse(value, isSetWebhookSubscriptionStatusRequest(value), 'set_webhook_subscription_status_request') as SetWebhookSubscriptionStatusRequest;
}

function isSetWebhookSubscriptionStatusRequest(value: unknown): value is SetWebhookSubscriptionStatusRequest {
  return isRecord(value) && (typeof value["isActive"] === 'boolean') && (Number.isSafeInteger(value["version"]));
}

export function readWebhookDeliveryAttemptPageResponse(value: unknown): WebhookDeliveryAttemptPageResponse {
  return requireValidResponse(value, isWebhookDeliveryAttemptPageResponse(value), 'webhook_delivery_attempt_page_response') as WebhookDeliveryAttemptPageResponse;
}

function isWebhookDeliveryAttemptPageResponse(value: unknown): value is WebhookDeliveryAttemptPageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWebhookDeliveryAttemptResponse(item14))) && (Number.isSafeInteger(value["page"])) && (Number.isSafeInteger(value["pageSize"])) && ((Number.isSafeInteger(value["total"])) || (typeof value["total"] === 'string'));
}

export function readWebhookDeliveryAttemptResponse(value: unknown): WebhookDeliveryAttemptResponse {
  return requireValidResponse(value, isWebhookDeliveryAttemptResponse(value), 'webhook_delivery_attempt_response') as WebhookDeliveryAttemptResponse;
}

function isWebhookDeliveryAttemptResponse(value: unknown): value is WebhookDeliveryAttemptResponse {
  return isRecord(value) && ((typeof value["appliedToDelivery"] === 'boolean') || (value["appliedToDelivery"] === null)) && ((typeof value["completedAtUtc"] === 'string') || (value["completedAtUtc"] === null)) && (typeof value["deliveryId"] === 'string' && guidPattern.test(value["deliveryId"])) && ((typeof value["errorCode"] === 'string' && [null, "webhooks.delivery.http_error", "webhooks.delivery.timeout", "webhooks.delivery.target_invalid", "webhooks.delivery.target_blocked", "webhooks.delivery.transport_error", "webhooks.delivery.failed"].includes(value["errorCode"])) || (value["errorCode"] === null)) && ((Number.isSafeInteger(value["httpStatus"])) || (value["httpStatus"] === null)) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["outcomeKey"] === 'string' && ["processing", "unconfirmed", "delivered", "failed"].includes(value["outcomeKey"])) && (typeof value["startedAtUtc"] === 'string') && (typeof value["subscriptionId"] === 'string' && guidPattern.test(value["subscriptionId"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]));
}

export function readWebhookDeliveryHistoryPageResponse(value: unknown): WebhookDeliveryHistoryPageResponse {
  return requireValidResponse(value, isWebhookDeliveryHistoryPageResponse(value), 'webhook_delivery_history_page_response') as WebhookDeliveryHistoryPageResponse;
}

function isWebhookDeliveryHistoryPageResponse(value: unknown): value is WebhookDeliveryHistoryPageResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isWebhookDeliveryHistoryResponse(item14))) && (Number.isSafeInteger(value["page"])) && (Number.isSafeInteger(value["pageSize"])) && ((Number.isSafeInteger(value["total"])) || (typeof value["total"] === 'string'));
}

export function readWebhookDeliveryHistoryResponse(value: unknown): WebhookDeliveryHistoryResponse {
  return requireValidResponse(value, isWebhookDeliveryHistoryResponse(value), 'webhook_delivery_history_response') as WebhookDeliveryHistoryResponse;
}

function isWebhookDeliveryHistoryResponse(value: unknown): value is WebhookDeliveryHistoryResponse {
  return isRecord(value) && (Number.isSafeInteger(value["attemptCount"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["eventId"] === 'string' && guidPattern.test(value["eventId"])) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && ((typeof value["lastErrorCode"] === 'string' && [null, "webhooks.delivery.http_error", "webhooks.delivery.timeout", "webhooks.delivery.failed"].includes(value["lastErrorCode"])) || (value["lastErrorCode"] === null)) && ((Number.isSafeInteger(value["lastHttpStatus"])) || (value["lastHttpStatus"] === null)) && ((typeof value["nextAttemptAtUtc"] === 'string') || (value["nextAttemptAtUtc"] === null)) && (typeof value["statusKey"] === 'string' && ["pending", "processing", "delivered", "failed"].includes(value["statusKey"])) && (typeof value["subscriptionId"] === 'string' && guidPattern.test(value["subscriptionId"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["updatedAtUtc"] === 'string') && (Number.isSafeInteger(value["version"]));
}

export function readWebhookDeliveryReplayReceipt(value: unknown): WebhookDeliveryReplayReceipt {
  return requireValidResponse(value, isWebhookDeliveryReplayReceipt(value), 'webhook_delivery_replay_receipt') as WebhookDeliveryReplayReceipt;
}

function isWebhookDeliveryReplayReceipt(value: unknown): value is WebhookDeliveryReplayReceipt {
  return isRecord(value) && (Number.isSafeInteger(value["attemptCount"])) && (typeof value["deliveryId"] === 'string' && guidPattern.test(value["deliveryId"])) && (typeof value["eventId"] === 'string' && guidPattern.test(value["eventId"])) && (typeof value["operationId"] === 'string' && guidPattern.test(value["operationId"])) && (Number.isSafeInteger(value["previousVersion"])) && (typeof value["queuedAtUtc"] === 'string') && (typeof value["subscriptionId"] === 'string' && guidPattern.test(value["subscriptionId"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (Number.isSafeInteger(value["version"]));
}

export function readWebhookSubscriptionResponse(value: unknown): WebhookSubscriptionResponse {
  return requireValidResponse(value, isWebhookSubscriptionResponse(value), 'webhook_subscription_response') as WebhookSubscriptionResponse;
}

function isWebhookSubscriptionResponse(value: unknown): value is WebhookSubscriptionResponse {
  return isRecord(value) && (typeof value["eventType"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isActive"] === 'boolean') && (typeof value["targetUrl"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (Number.isSafeInteger(value["version"]));
}

export function readWorkflowCompletedWebhookData(value: unknown): WorkflowCompletedWebhookData {
  return requireValidResponse(value, isWorkflowCompletedWebhookData(value), 'workflow_completed_webhook_data') as WorkflowCompletedWebhookData;
}

function isWorkflowCompletedWebhookData(value: unknown): value is WorkflowCompletedWebhookData {
  return isRecord(value) && (typeof value["BusinessId"] === 'string') && (typeof value["BusinessType"] === 'string') && (typeof value["InstanceId"] === 'string' && guidPattern.test(value["InstanceId"])) && (typeof value["RecipientUserId"] === 'string' && guidPattern.test(value["RecipientUserId"]));
}

export function readWorkflowCompletedWebhookEvent(value: unknown): WorkflowCompletedWebhookEvent {
  return requireValidResponse(value, isWorkflowCompletedWebhookEvent(value), 'workflow_completed_webhook_event') as WorkflowCompletedWebhookEvent;
}

function isWorkflowCompletedWebhookEvent(value: unknown): value is WorkflowCompletedWebhookEvent {
  return isRecord(value) && (isWorkflowCompletedWebhookData(value["Data"])) && (typeof value["EventId"] === 'string' && guidPattern.test(value["EventId"])) && (typeof value["EventType"] === 'string' && ["fullnet.workflow.instance.completed"].includes(value["EventType"])) && (typeof value["OccurredAtUtc"] === 'string') && (typeof value["TenantId"] === 'string' && guidPattern.test(value["TenantId"]));
}

export function readWebhooksListSubscriptionsResponse(value: unknown): Array<WebhookSubscriptionResponse> {
  return requireValidResponse(value, Array.isArray(value) && value.every(item5 => isWebhookSubscriptionResponse(item5)), 'webhooks_list_subscriptions_response') as Array<WebhookSubscriptionResponse>;
}

// 统一失败关闭与错误码，避免每个生成读取器重复携带异常和返回分支。
function requireValidResponse(value: unknown, valid: boolean, responseKind: string): unknown {
  if (!valid) throw new Error('client.invalid_' + responseKind);
  return value;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 服务端 Int64 可按 JSON 字符串输出；客户端仅在无精度损失时归一为既有 number 契约。
function normalizeWireInteger(value: unknown): unknown {
  if (typeof value !== 'string' || !/^-?(?:0|[1-9]\d*)$/.test(value)) return value;
  const number = Number(value);
  return Number.isSafeInteger(number) ? number : value;
}

function normalizeIntegerIntersection(value: unknown, normalizers: ReadonlyArray<(value: unknown) => unknown>): unknown {
  for (const normalize of normalizers) value = normalize(value);
  return value;
}

function normalizeIntegerUnion(value: unknown, branches: ReadonlyArray<{ matches: (value: unknown) => boolean; normalize: (value: unknown) => unknown }>): unknown {
  if (branches.some(branch => branch.matches(value))) return value;
  for (const branch of branches) {
    const candidate = branch.normalize(value);
    if (branch.matches(candidate)) return candidate;
  }
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isJsonValue(value: unknown): boolean {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') {
    return true;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value);
  }
  if (Array.isArray(value)) {
    return value.every(isJsonValue);
  }
  return isRecord(value) && Object.values(value).every(isJsonValue);
}
