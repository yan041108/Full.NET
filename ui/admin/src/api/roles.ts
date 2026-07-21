import {
  isHostRole,
  isHostRoleDataScope,
  isHostRolePage,
  type HostRole,
  type HostRoleDataScope,
  type HostRolePage,
  type RoleDataScopeKind
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostRoles(
  page = 1,
  pageSize = 20
): Promise<HostRolePage> {
  const value = await request<unknown>(
    `/api/v1/identity/roles?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostRolePage(value)) throw new Error('client.invalid_host_role_page');
  return value;
}

export async function createHostRole(
  code: string,
  name: string
): Promise<HostRole> {
  const value = await request<unknown>('/api/v1/identity/roles', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ code, name })
  });
  if (!isHostRole(value)) throw new Error('client.invalid_host_role');
  return value;
}

export async function updateHostRole(
  id: string,
  name: string,
  version: number
): Promise<HostRole> {
  const value = await request<unknown>(
    `/api/v1/identity/roles/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, version })
    }
  );
  if (!isHostRole(value)) throw new Error('client.invalid_host_role');
  return value;
}

export async function replaceHostRolePermissions(
  id: string,
  permissionCodes: string[],
  version: number
): Promise<HostRole> {
  const value = await request<unknown>(
    `/api/v1/identity/roles/${encodeURIComponent(id)}/permissions`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ permissionCodes, version })
    }
  );
  if (!isHostRole(value)) throw new Error('client.invalid_host_role');
  return value;
}

export async function disableHostRole(id: string): Promise<HostRole> {
  const value = await request<unknown>(
    `/api/v1/identity/roles/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostRole(value)) throw new Error('client.invalid_host_role');
  return value;
}

export async function getHostRoleDataScope(id: string): Promise<HostRoleDataScope> {
  const value = await request<unknown>(
    `/api/v1/identity/roles/${encodeURIComponent(id)}/data-scope`
  );
  if (!isHostRoleDataScope(value)) throw new Error('client.invalid_host_role_data_scope');
  return value;
}

export async function updateHostRoleDataScope(
  id: string,
  dataScopeKind: RoleDataScopeKind,
  unitIds: string[] | null,
  version: number
): Promise<HostRoleDataScope> {
  const value = await request<unknown>(
    `/api/v1/identity/roles/${encodeURIComponent(id)}/data-scope`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ dataScopeKind, unitIds, version })
    }
  );
  if (!isHostRoleDataScope(value)) throw new Error('client.invalid_host_role_data_scope');
  return value;
}
