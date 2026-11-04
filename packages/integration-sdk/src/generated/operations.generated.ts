// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 低层 HttpClient Operation。

import type { HttpClient, RequestOptions } from '../http.js';
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
import {
  readWebhookDeliveryAttemptPageResponse,
  readWebhookDeliveryHistoryPageResponse,
  readWebhookDeliveryHistoryResponse,
  readWebhookDeliveryReplayReceipt,
  readWebhooksListSubscriptionsResponse,
  readWebhookSubscriptionResponse
} from './guards.generated.js';

export type GeneratedJsonOperation<T> = (
  http: HttpClient,
  parameters: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
  options?: RequestOptions
) => Promise<T>;

function requestJsonOperation(
  http: HttpClient, path: string, init: RequestInit,
  signal?: AbortSignal, options?: RequestOptions
): Promise<unknown> {
  return options === undefined
    ? http.request<unknown>(path, init, signal)
    : http.request<unknown>(path, init, signal, options);
}

export interface WebhooksCreateSubscriptionParameters {
  readonly body: CreateWebhookSubscriptionRequest;
}

export async function webhooksCreateSubscription(
  http: HttpClient,
  parameters: WebhooksCreateSubscriptionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookSubscriptionResponse> {
  const path = `/api/v1/webhooks/subscriptions`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookSubscriptionResponse(value);
}

export interface WebhooksGetDeliveryReplayReceiptParameters {
  readonly subscriptionId: string;
  readonly deliveryId: string;
  readonly operationId: string;
}

export async function webhooksGetDeliveryReplayReceipt(
  http: HttpClient,
  parameters: WebhooksGetDeliveryReplayReceiptParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookDeliveryReplayReceipt> {
  const path = `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries/${encodeURIComponent(String(parameters.deliveryId))}/replays/${encodeURIComponent(String(parameters.operationId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookDeliveryReplayReceipt(value);
}

export interface WebhooksGetSubscriptionDeliveryParameters {
  readonly subscriptionId: string;
  readonly deliveryId: string;
}

export async function webhooksGetSubscriptionDelivery(
  http: HttpClient,
  parameters: WebhooksGetSubscriptionDeliveryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookDeliveryHistoryResponse> {
  const path = `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries/${encodeURIComponent(String(parameters.deliveryId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookDeliveryHistoryResponse(value);
}

export interface WebhooksListDeliveryAttemptsParameters {
  readonly subscriptionId: string;
  readonly deliveryId: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function webhooksListDeliveryAttempts(
  http: HttpClient,
  parameters: WebhooksListDeliveryAttemptsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookDeliveryAttemptPageResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  const path = query.size === 0 ? `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries/${encodeURIComponent(String(parameters.deliveryId))}/attempts` : `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries/${encodeURIComponent(String(parameters.deliveryId))}/attempts?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookDeliveryAttemptPageResponse(value);
}

export interface WebhooksListSubscriptionDeliveriesParameters {
  readonly subscriptionId: string;
  readonly page?: number;
  readonly pageSize?: number;
  readonly statusKey?: "pending" | "processing" | "delivered" | "failed";
}

export async function webhooksListSubscriptionDeliveries(
  http: HttpClient,
  parameters: WebhooksListSubscriptionDeliveriesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookDeliveryHistoryPageResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.statusKey !== undefined) {
    query.set('statusKey', String(parameters.statusKey));
  }
  const path = query.size === 0 ? `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries` : `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookDeliveryHistoryPageResponse(value);
}

export interface WebhooksListSubscriptionsParameters {

}

export async function webhooksListSubscriptions(
  http: HttpClient,
  parameters: WebhooksListSubscriptionsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<Array<WebhookSubscriptionResponse>> {
  const path = `/api/v1/webhooks/subscriptions`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhooksListSubscriptionsResponse(value);
}

export interface WebhooksReplayDeliveryParameters {
  readonly subscriptionId: string;
  readonly deliveryId: string;
  readonly body: ReplayWebhookDeliveryRequest;
}

export async function webhooksReplayDelivery(
  http: HttpClient,
  parameters: WebhooksReplayDeliveryParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookDeliveryReplayReceipt> {
  const path = `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/deliveries/${encodeURIComponent(String(parameters.deliveryId))}/replays`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookDeliveryReplayReceipt(value);
}

export interface WebhooksSetSubscriptionStatusParameters {
  readonly subscriptionId: string;
  readonly body: SetWebhookSubscriptionStatusRequest;
}

export async function webhooksSetSubscriptionStatus(
  http: HttpClient,
  parameters: WebhooksSetSubscriptionStatusParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<WebhookSubscriptionResponse> {
  const path = `/api/v1/webhooks/subscriptions/${encodeURIComponent(String(parameters.subscriptionId))}/status`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readWebhookSubscriptionResponse(value);
}
