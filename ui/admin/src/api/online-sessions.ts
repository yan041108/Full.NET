import {
  identityListHostOnlineSessions,
  identityRevokeHostOnlineSession,
  isHostOnlineSession,
  isHostOnlineSessionPage,
  type HostOnlineSession,
  type HostOnlineSessionPage
} from '@fullnet/client-contracts';
import { http } from './http';

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
