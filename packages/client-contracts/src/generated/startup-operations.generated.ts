// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 低层 HttpClient Operation。

import type { HttpClient, RequestOptions } from '../http.js';
import type {
  ArchiveInboxMessagesRequest,
  CurrentUserResponse,
  InboxMessageResponse,
  InboxUnreadCountResponse,
  LocalePreferenceResponse,
  LoginRequest,
  NotificationDeepLink,
  SendHostInboxMessageRequest,
  TokenResponse,
  UpdateLocaleRequest
} from './models.generated.js';
import {
  readCurrentUserResponse,
  readInboxMessageResponse,
  readInboxUnreadCountResponse,
  readLocalePreferenceResponse,
  readTokenResponse
} from './startup-guards.generated.js';

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

export interface IdentityGetCurrentUserParameters {

}

export async function identityGetCurrentUser(
  http: HttpClient,
  parameters: IdentityGetCurrentUserParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<CurrentUserResponse> {
  const path = `/api/v1/me`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readCurrentUserResponse(value);
}

export interface IdentityLoginParameters {
  readonly body: LoginRequest;
}

export async function identityLogin(
  http: HttpClient,
  parameters: IdentityLoginParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TokenResponse> {
  const path = `/api/v1/auth/login`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readTokenResponse(value);
}

export interface IdentityLogoutParameters {

}

export async function identityLogout(
  http: HttpClient,
  parameters: IdentityLogoutParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<void> {
  const path = `/api/v1/auth/logout`;
  const init: RequestInit = { method: 'POST' };
  await requestJsonOperation(http, path, init, signal, options);
}

export interface IdentityRefreshSessionParameters {

}

export async function identityRefreshSession(
  http: HttpClient,
  parameters: IdentityRefreshSessionParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<TokenResponse> {
  const path = `/api/v1/auth/refresh`;
  const init: RequestInit = { method: 'POST' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readTokenResponse(value);
}

export interface IdentityUpdatePreferredLocaleParameters {
  readonly body: UpdateLocaleRequest;
}

export async function identityUpdatePreferredLocale(
  http: HttpClient,
  parameters: IdentityUpdatePreferredLocaleParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<LocalePreferenceResponse> {
  const path = `/api/v1/me/locale`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readLocalePreferenceResponse(value);
}

export interface NotificationsArchiveMyInboxMessageParameters {
  readonly messageId: string;
}

export async function notificationsArchiveMyInboxMessage(
  http: HttpClient,
  parameters: NotificationsArchiveMyInboxMessageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/${encodeURIComponent(String(parameters.messageId))}/archive`;
  const init: RequestInit = { method: 'POST' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsArchiveMyInboxMessagesParameters {
  readonly body: ArchiveInboxMessagesRequest;
}

export async function notificationsArchiveMyInboxMessages(
  http: HttpClient,
  parameters: NotificationsArchiveMyInboxMessagesParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/archive-batch`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsGetMyInboxUnreadCountParameters {

}

export async function notificationsGetMyInboxUnreadCount(
  http: HttpClient,
  parameters: NotificationsGetMyInboxUnreadCountParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/unread-count`;
  const init: RequestInit = { method: 'GET' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsMarkAllMyInboxMessagesReadParameters {

}

export async function notificationsMarkAllMyInboxMessagesRead(
  http: HttpClient,
  parameters: NotificationsMarkAllMyInboxMessagesReadParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxUnreadCountResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/read-all`;
  const init: RequestInit = { method: 'POST' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxUnreadCountResponse(value);
}

export interface NotificationsMarkMyInboxMessageReadParameters {
  readonly messageId: string;
}

export async function notificationsMarkMyInboxMessageRead(
  http: HttpClient,
  parameters: NotificationsMarkMyInboxMessageReadParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxMessageResponse> {
  const path = `/api/v1/notifications/my-inbox-messages/${encodeURIComponent(String(parameters.messageId))}/read`;
  const init: RequestInit = { method: 'POST' };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxMessageResponse(value);
}

export interface NotificationsSendHostInboxMessageParameters {
  readonly body: SendHostInboxMessageRequest;
}

export async function notificationsSendHostInboxMessage(
  http: HttpClient,
  parameters: NotificationsSendHostInboxMessageParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<InboxMessageResponse> {
  const path = `/api/v1/notifications/host-inbox-messages`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = await requestJsonOperation(http, path, init, signal, options);
  return readInboxMessageResponse(value);
}
