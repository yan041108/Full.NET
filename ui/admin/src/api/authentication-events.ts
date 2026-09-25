import {
  identityListAuthenticationEvents,
  identityGetAuthenticationEvent,
  identityExportAuthenticationEvents,
  type AuthenticationEventResponse,
  type IdentityExportAuthenticationEventsParameters,
  type AuthenticationEventCursorPage
} from '@fullnet/client-contracts';
import { http } from './http';

export type AuthenticationEvent = AuthenticationEventResponse;
export type AuthenticationEventPage = AuthenticationEventCursorPage;
export type AuthenticationEventExportQuery = IdentityExportAuthenticationEventsParameters;

export interface AuthenticationEventQuery {
  page: number;
  pageSize: number;
  cursor?: string;
  userId?: string;
  eventType?: string;
  succeeded?: boolean;
  fromUtc?: string;
  toUtc?: string;
}

/** 使用生成的 OpenAPI 客户端和结构校验查询认证事件。 */
export async function listAuthenticationEvents(
  query: AuthenticationEventQuery,
  signal?: AbortSignal
): Promise<AuthenticationEventPage> {
  return identityListAuthenticationEvents(http, query, signal);
}

/** 详情复用相同的服务端权限与安全投影。 */
export async function getAuthenticationEvent(
  id: string,
  signal?: AbortSignal
): Promise<AuthenticationEvent> {
  return identityGetAuthenticationEvent(http, { id }, signal);
}

/** 受限范围内下载认证事件安全投影 CSV。 */
export async function exportAuthenticationEvents(
  query: AuthenticationEventExportQuery,
  signal?: AbortSignal
): Promise<Blob> {
  return identityExportAuthenticationEvents(http, query, signal);
}
