import {
  identityGetHostSessionPolicy,
  identityListHostOnlineSessions,
  identityRevokeAllHostUserOnlineSessions,
  identityRevokeHostOnlineSession,
  readIdentitySessionPolicyResponse,
  readRevokeAllHostUserSessionsResponse,
  isHostOnlineSession,
  isHostOnlineSessionPage,
  type HostOnlineSession,
  type HostOnlineSessionPage,
  type IdentitySessionLoginPolicy,
  type IdentitySessionPolicyResponse,
  type RevokeAllHostUserSessionsResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询在线会话列表，并对用户名筛选词做 trim 规范化。 */
export async function listHostOnlineSessions(
  page = 1,
  pageSize = 20,
  usernameContains?: string,
  userId?: string,
  signal?: AbortSignal
): Promise<HostOnlineSessionPage> {
  const trimmedUsername = usernameContains?.trim();
  const value = await identityListHostOnlineSessions(
    http,
    {
      page,
      pageSize,
      ...(trimmedUsername ? { usernameContains: trimmedUsername } : {}),
      ...(userId ? { userId } : {})
    },
    signal
  );
  if (!isHostOnlineSessionPage(value)) {
    throw new Error('client.invalid_host_online_session_page');
  }

  return value;
}

/** 读取当前 Host 登录会话并发策略。 */
export async function getHostSessionPolicy(
  signal?: AbortSignal
): Promise<IdentitySessionPolicyResponse> {
  const value = await identityGetHostSessionPolicy(http, {}, signal);
  return readIdentitySessionPolicyResponse(value);
}

/** 撤销指定用户的全部活跃在线会话。 */
export async function revokeAllHostUserOnlineSessions(
  userId: string,
  signal?: AbortSignal
): Promise<RevokeAllHostUserSessionsResponse> {
  const value = await identityRevokeAllHostUserOnlineSessions(
    http,
    { userId },
    signal
  );
  return readRevokeAllHostUserSessionsResponse(value);
}

/** 撤销指定在线会话。 */
export async function revokeHostOnlineSession(
  id: string,
  signal?: AbortSignal
): Promise<HostOnlineSession> {
  const value = await identityRevokeHostOnlineSession(
    http,
    { sessionId: id },
    signal
  );
  if (!isHostOnlineSession(value)) {
    throw new Error('client.invalid_host_online_session');
  }

  return value;
}

/** 导出在线会话详情与分页模型，供会话列表、筛选器与强制下线流程共享同一契约。 */
export type {
  HostOnlineSession,
  HostOnlineSessionPage,
  IdentitySessionLoginPolicy,
  IdentitySessionPolicyResponse,
  RevokeAllHostUserSessionsResponse
};
