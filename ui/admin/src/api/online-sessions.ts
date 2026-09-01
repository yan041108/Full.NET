import {
  identityListHostOnlineSessions,
  identityRevokeHostOnlineSession,
  isHostOnlineSession,
  isHostOnlineSessionPage,
  type HostOnlineSession,
  type HostOnlineSessionPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询在线会话列表，并对用户名筛选词做 trim 规范化。 */
export async function listHostOnlineSessions(
  page = 1,
  pageSize = 20,
  usernameContains?: string,
  signal?: AbortSignal
): Promise<HostOnlineSessionPage> {
  const trimmedUsername = usernameContains?.trim();
  const value = await identityListHostOnlineSessions(
    http,
    {
      page,
      pageSize,
      ...(trimmedUsername ? { usernameContains: trimmedUsername } : {})
    },
    signal
  );
  if (!isHostOnlineSessionPage(value)) {
    throw new Error('client.invalid_host_online_session_page');
  }

  return value;
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
export type { HostOnlineSession, HostOnlineSessionPage };
