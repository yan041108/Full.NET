import type { HttpClient } from './http.js';

/** Host 角色成员列表项。 */
export interface HostRoleMember {
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly isActive: boolean;
}

/** Host 角色成员分页结果。 */
export interface HostRoleMembersPage {
  readonly roleId: string;
  readonly items: readonly HostRoleMember[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly version: number;
}

/** 替换 Host 角色成员请求。 */
export interface ReplaceHostRoleMembersRequest {
  readonly userIds: readonly string[];
  readonly version: number;
}

/** 替换 Host 角色成员结果。 */
export interface HostRoleMembersAssignment {
  readonly roleId: string;
  readonly userIds: readonly string[];
  readonly version: number;
}

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isHostRoleMember(value: unknown): value is HostRoleMember {
  return isRecord(value)
    && typeof value.userId === 'string'
    && guidPattern.test(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.isActive === 'boolean';
}

export function isHostRoleMembersPage(value: unknown): value is HostRoleMembersPage {
  return isRecord(value)
    && typeof value.roleId === 'string'
    && guidPattern.test(value.roleId)
    && Array.isArray(value.items)
    && value.items.every(isHostRoleMember)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number'
    && typeof value.version === 'number';
}

export function isHostRoleMembersAssignment(value: unknown): value is HostRoleMembersAssignment {
  return isRecord(value)
    && typeof value.roleId === 'string'
    && guidPattern.test(value.roleId)
    && Array.isArray(value.userIds)
    && value.userIds.every(id => typeof id === 'string' && guidPattern.test(id))
    && typeof value.version === 'number';
}

/** 分页查询 Host 角色成员。 */
export async function listHostRoleMembers(
  http: HttpClient,
  roleId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostRoleMembersPage> {
  const value = await http.request<unknown>(
    `/api/v1/identity/roles/${roleId}/members?page=${page}&pageSize=${pageSize}`,
    { method: 'GET' },
    signal
  );
  if (!isHostRoleMembersPage(value)) {
    throw new TypeError('角色成员列表响应不符合 HostRoleMembersPage 契约。');
  }

  return value;
}

/** 整量替换 Host 角色成员。 */
export async function replaceHostRoleMembers(
  http: HttpClient,
  roleId: string,
  request: ReplaceHostRoleMembersRequest,
  signal?: AbortSignal
): Promise<HostRoleMembersAssignment> {
  const value = await http.request<unknown>(
    `/api/v1/identity/roles/${roleId}/members`,
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    signal,
    { retryUnauthorized: false }
  );
  if (!isHostRoleMembersAssignment(value)) {
    throw new TypeError('角色成员替换响应不符合 HostRoleMembersAssignment 契约。');
  }

  return value;
}

/** 启用已禁用的 Host 角色。 */
export async function enableHostRole(
  http: HttpClient,
  roleId: string,
  signal?: AbortSignal
): Promise<import('./host-roles.js').HostRole> {
  const { isHostRole } = await import('./host-roles.js');
  const value = await http.request<unknown>(
    `/api/v1/identity/roles/${roleId}/enable`,
    { method: 'POST' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isHostRole(value)) {
    throw new TypeError('角色启用响应不符合 HostRole 契约。');
  }

  return value;
}

/** 删除无成员引用的 Host 角色。 */
export async function deleteHostRole(
  http: HttpClient,
  roleId: string,
  signal?: AbortSignal
): Promise<void> {
  await http.request<void>(
    `/api/v1/identity/roles/${roleId}`,
    { method: 'DELETE' },
    signal,
    { retryUnauthorized: false }
  );
}
