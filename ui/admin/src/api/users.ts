import {
  isHostUser,
  isHostUserPage,
  type HostUser,
  type HostUserPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostUsers(
  page = 1,
  pageSize = 20
): Promise<HostUserPage> {
  const value = await request<unknown>(
    `/api/v1/identity/users?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostUserPage(value)) throw new Error('client.invalid_host_user_page');
  return value;
}

export async function createHostUser(
  username: string,
  displayName: string,
  password: string
): Promise<HostUser> {
  const value = await request<unknown>('/api/v1/identity/users', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ username, displayName, password })
  });
  if (!isHostUser(value)) throw new Error('client.invalid_host_user');
  return value;
}

export async function disableHostUser(id: string): Promise<HostUser> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostUser(value)) throw new Error('client.invalid_host_user');
  return value;
}

export async function updateHostUser(
  id: string,
  displayName: string,
  version: number
): Promise<HostUser> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ displayName, version })
    }
  );
  if (!isHostUser(value)) throw new Error('client.invalid_host_user');
  return value;
}
