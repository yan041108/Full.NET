import {
  isHostOnlineSession,
  isHostOnlineSessionPage,
  type HostOnlineSession,
  type HostOnlineSessionPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostOnlineSessions(
  page = 1,
  pageSize = 20,
  usernameContains?: string
): Promise<HostOnlineSessionPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  if (usernameContains?.trim()) {
    query.set('usernameContains', usernameContains.trim());
  }
  const value = await request<unknown>(
    `/api/v1/identity/online-sessions?${query.toString()}`
  );
  if (!isHostOnlineSessionPage(value)) {
    throw new Error('client.invalid_host_online_session_page');
  }
  return value;
}

export async function revokeHostOnlineSession(id: string): Promise<HostOnlineSession> {
  const value = await request<unknown>(
    `/api/v1/identity/online-sessions/${encodeURIComponent(id)}/revoke`,
    { method: 'POST' }
  );
  if (!isHostOnlineSession(value)) {
    throw new Error('client.invalid_host_online_session');
  }
  return value;
}
