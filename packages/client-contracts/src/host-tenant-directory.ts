import type { HttpClient } from './http.js';
import { isHostTenant, type HostTenant } from './host-tenants.js';

/** 租户目录成员项。 */
export interface HostTenantMember {
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly accountType: string;
  readonly isActive: boolean;
}

/** 租户成员分页结果。 */
export interface HostTenantMembersPage {
  readonly tenantId: string;
  readonly items: readonly HostTenantMember[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** 租户管理员分页结果。 */
export interface HostTenantAdministratorsPage {
  readonly tenantId: string;
  readonly items: readonly HostTenantMember[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isHostTenantMember(value: unknown): value is HostTenantMember {
  return isRecord(value)
    && typeof value.userId === 'string'
    && guidPattern.test(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.accountType === 'string'
    && typeof value.isActive === 'boolean';
}

export function isHostTenantMembersPage(value: unknown): value is HostTenantMembersPage {
  return isRecord(value)
    && typeof value.tenantId === 'string'
    && guidPattern.test(value.tenantId)
    && Array.isArray(value.items)
    && value.items.every(isHostTenantMember)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isHostTenantAdministratorsPage(
  value: unknown
): value is HostTenantAdministratorsPage {
  return isHostTenantMembersPage(value);
}

/** 重新启用已禁用的 Host 租户。 */
export async function enableHostTenant(
  http: HttpClient,
  tenantId: string,
  signal?: AbortSignal
): Promise<HostTenant> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/enable`,
    { method: 'POST' },
    signal,
    { retryUnauthorized: false }
  );
  if (!isHostTenant(value)) {
    throw new TypeError('租户启用响应不符合 HostTenant 契约。');
  }

  return value;
}

/** 分页查询指定租户的活动成员目录。 */
export async function listHostTenantMembers(
  http: HttpClient,
  tenantId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostTenantMembersPage> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/members?page=${page}&pageSize=${pageSize}`,
    { method: 'GET' },
    signal
  );
  if (!isHostTenantMembersPage(value)) {
    throw new TypeError('租户成员列表响应不符合 HostTenantMembersPage 契约。');
  }

  return value;
}

/** 分页查询指定租户的活动系统管理员目录。 */
export async function listHostTenantAdministrators(
  http: HttpClient,
  tenantId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostTenantAdministratorsPage> {
  const value = await http.request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/administrators?page=${page}&pageSize=${pageSize}`,
    { method: 'GET' },
    signal
  );
  if (!isHostTenantAdministratorsPage(value)) {
    throw new TypeError('租户管理员列表响应不符合 HostTenantAdministratorsPage 契约。');
  }

  return value;
}
