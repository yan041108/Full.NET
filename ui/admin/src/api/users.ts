import {
  isHostUser,
  isHostUserPage,
  isHostUserRoles,
  type HostUser,
  type HostUserPage,
  type HostUserRoles
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

export async function enableHostUser(id: string): Promise<HostUser> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}/enable`,
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

export async function resetHostUserPassword(
  id: string,
  password: string
): Promise<HostUser> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}/reset-password`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ password })
    }
  );
  if (!isHostUser(value)) throw new Error('client.invalid_host_user');
  return value;
}

export async function getHostUserRoles(id: string): Promise<HostUserRoles> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}/roles`
  );
  if (!isHostUserRoles(value)) throw new Error('client.invalid_host_user_roles');
  return value;
}

export async function replaceHostUserRoles(
  id: string,
  roleIds: string[],
  version: number
): Promise<HostUserRoles> {
  const value = await request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(id)}/roles`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ roleIds, version })
    }
  );
  if (!isHostUserRoles(value)) throw new Error('client.invalid_host_user_roles');
  return value;
}
