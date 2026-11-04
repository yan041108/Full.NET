// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

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

export function readArchiveInboxMessagesRequest(value: unknown): ArchiveInboxMessagesRequest {
  return requireValidResponse(value, isArchiveInboxMessagesRequest(value), 'archive_inbox_messages_request') as ArchiveInboxMessagesRequest;
}

function isArchiveInboxMessagesRequest(value: unknown): value is ArchiveInboxMessagesRequest {
  return isRecord(value) && (Array.isArray(value["messageIds"]) && value["messageIds"].every(item19 => typeof item19 === 'string' && guidPattern.test(item19)));
}

export function readCurrentUserResponse(value: unknown): CurrentUserResponse {
  const normalizedValue = normalizeCurrentUserResponseIntegerJson(value);
  return requireValidResponse(normalizedValue, isCurrentUserResponse(normalizedValue), 'current_user_response') as CurrentUserResponse;
}

function isCurrentUserResponse(value: unknown): value is CurrentUserResponse {
  return isRecord(value) && (typeof value["actorScope"] === 'string') && (typeof value["displayName"] === 'string') && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isSuperAdministrator"] === 'boolean') && (typeof value["passwordChangeRequired"] === 'boolean') && (Array.isArray(value["permissions"]) && value["permissions"].every(item20 => typeof item20 === 'string')) && (typeof value["preferredLocale"] === 'string') && (Number.isSafeInteger(value["profileVersion"])) && (typeof value["scope"] === 'string') && (typeof value["sessionId"] === 'string' && guidPattern.test(value["sessionId"])) && ((value["tenantId"] === null) || (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"]))) && (typeof value["username"] === 'string');
}

export function readInboxMessageResponse(value: unknown): InboxMessageResponse {
  return requireValidResponse(value, isInboxMessageResponse(value), 'inbox_message_response') as InboxMessageResponse;
}

function isInboxMessageResponse(value: unknown): value is InboxMessageResponse {
  return isRecord(value) && (value["archivedAtUtc"] === undefined || ((value["archivedAtUtc"] === null) || (typeof value["archivedAtUtc"] === 'string'))) && (typeof value["content"] === 'string') && (typeof value["createdAtUtc"] === 'string') && ((value["createdByUserId"] === null) || (typeof value["createdByUserId"] === 'string' && guidPattern.test(value["createdByUserId"]))) && (value["deepLink"] === undefined || ((value["deepLink"] === null) || (isNotificationDeepLink(value["deepLink"])))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (value["producerKey"] === undefined || ((value["producerKey"] === null) || (typeof value["producerKey"] === 'string'))) && ((value["readAtUtc"] === null) || (typeof value["readAtUtc"] === 'string')) && (value["revocationReason"] === undefined || ((value["revocationReason"] === null) || (typeof value["revocationReason"] === 'string'))) && (value["revokedAtUtc"] === undefined || ((value["revokedAtUtc"] === null) || (typeof value["revokedAtUtc"] === 'string'))) && (value["sceneKey"] === undefined || ((value["sceneKey"] === null) || (typeof value["sceneKey"] === 'string'))) && (typeof value["status"] === 'string') && (typeof value["title"] === 'string');
}

export function readInboxUnreadCountResponse(value: unknown): InboxUnreadCountResponse {
  const normalizedValue = normalizeInboxUnreadCountResponseIntegerJson(value);
  return requireValidResponse(normalizedValue, isInboxUnreadCountResponse(normalizedValue), 'inbox_unread_count_response') as InboxUnreadCountResponse;
}

function isInboxUnreadCountResponse(value: unknown): value is InboxUnreadCountResponse {
  return isRecord(value) && (Number.isSafeInteger(value["unreadCount"]));
}

export function readLocalePreferenceResponse(value: unknown): LocalePreferenceResponse {
  const normalizedValue = normalizeLocalePreferenceResponseIntegerJson(value);
  return requireValidResponse(normalizedValue, isLocalePreferenceResponse(normalizedValue), 'locale_preference_response') as LocalePreferenceResponse;
}

function isLocalePreferenceResponse(value: unknown): value is LocalePreferenceResponse {
  return isRecord(value) && (typeof value["preferredLocale"] === 'string') && (Number.isSafeInteger(value["profileVersion"]));
}

export function readLoginRequest(value: unknown): LoginRequest {
  return requireValidResponse(value, isLoginRequest(value), 'login_request') as LoginRequest;
}

function isLoginRequest(value: unknown): value is LoginRequest {
  return isRecord(value) && (typeof value["password"] === 'string') && (typeof value["username"] === 'string');
}

export function readNotificationDeepLink(value: unknown): NotificationDeepLink {
  return requireValidResponse(value, isNotificationDeepLink(value), 'notification_deep_link') as NotificationDeepLink;
}

function isNotificationDeepLink(value: unknown): value is NotificationDeepLink {
  return isRecord(value) && (typeof value["resourceId"] === 'string' && guidPattern.test(value["resourceId"])) && (typeof value["resourceTypeKey"] === 'string');
}

export function readSendHostInboxMessageRequest(value: unknown): SendHostInboxMessageRequest {
  return requireValidResponse(value, isSendHostInboxMessageRequest(value), 'send_host_inbox_message_request') as SendHostInboxMessageRequest;
}

function isSendHostInboxMessageRequest(value: unknown): value is SendHostInboxMessageRequest {
  return isRecord(value) && (typeof value["content"] === 'string') && (typeof value["recipientUserId"] === 'string' && guidPattern.test(value["recipientUserId"])) && (typeof value["title"] === 'string');
}

export function readTokenResponse(value: unknown): TokenResponse {
  return requireValidResponse(value, isTokenResponse(value), 'token_response') as TokenResponse;
}

function isTokenResponse(value: unknown): value is TokenResponse {
  return isRecord(value) && (typeof value["accessToken"] === 'string') && (typeof value["expiresAtUtc"] === 'string') && (typeof value["tokenType"] === 'string');
}

export function readUpdateLocaleRequest(value: unknown): UpdateLocaleRequest {
  const normalizedValue = normalizeUpdateLocaleRequestIntegerJson(value);
  return requireValidResponse(normalizedValue, isUpdateLocaleRequest(normalizedValue), 'update_locale_request') as UpdateLocaleRequest;
}

function isUpdateLocaleRequest(value: unknown): value is UpdateLocaleRequest {
  return isRecord(value) && (typeof value["locale"] === 'string') && (Number.isSafeInteger(value["profileVersion"]));
}

function normalizeCurrentUserResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
}

function normalizeInboxUnreadCountResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "unreadCount") ? { ["unreadCount"]: normalizeWireInteger(value["unreadCount"]) } : {}) } : value);
}

function normalizeLocalePreferenceResponseIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
}

function normalizeUpdateLocaleRequestIntegerJson(value: unknown): unknown {
  return (isRecord(value) ? { ...value, ...(Object.hasOwn(value, "profileVersion") ? { ["profileVersion"]: normalizeWireInteger(value["profileVersion"]) } : {}) } : value);
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
