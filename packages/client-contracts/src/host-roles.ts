/** 自定义 Host 角色可分配的权限白名单（与后端校验一致，不含 super_administrators.*）。 */
export const HOST_ROLE_ASSIGNABLE_PERMISSIONS = [
  'platform.dashboard.read',
  'identity.navigation.read',
  'identity.users.read',
  'identity.users.write',
  'identity.roles.read',
  'identity.roles.write',
  'identity.menus.read',
  'identity.menus.write',
  'organization.units.read',
  'organization.units.write',
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
] as const;

export type HostRoleAssignablePermission =
  typeof HOST_ROLE_ASSIGNABLE_PERMISSIONS[number];

export interface HostRole {
  id: string;
  code: string;
  name: string;
  isSystem: boolean;
  isActive: boolean;
  isSuperAdministrator: boolean;
  permissionCodes: string[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostRolePage {
  items: HostRole[];
  page: number;
  pageSize: number;
  total: number;
}

export interface UpdateHostRoleRequest {
  name: string;
  version: number;
}

export interface ReplaceHostRolePermissionsRequest {
  permissionCodes: string[];
  version: number;
}

/** 校验不可信 JSON 是否为 Host 角色更新请求。 */
export function isUpdateHostRoleRequest(
  value: unknown
): value is UpdateHostRoleRequest {
  return isRecord(value)
    && typeof value.name === 'string'
    && value.name.length > 0
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 角色权限替换请求。 */
export function isReplaceHostRolePermissionsRequest(
  value: unknown
): value is ReplaceHostRolePermissionsRequest {
  return isRecord(value)
    && Array.isArray(value.permissionCodes)
    && value.permissionCodes.every(code => typeof code === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 角色分页结果。 */
export function isHostRolePage(value: unknown): value is HostRolePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostRole)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个 Host 角色。 */
export function isHostRole(value: unknown): value is HostRole {
  return isRecord(value)
    && isText(value.id)
    && typeof value.code === 'string'
    && typeof value.name === 'string'
    && typeof value.isSystem === 'boolean'
    && typeof value.isActive === 'boolean'
    && typeof value.isSuperAdministrator === 'boolean'
    && Array.isArray(value.permissionCodes)
    && value.permissionCodes.every(code => typeof code === 'string')
    && isText(value.createdAtUtc)
    && (value.updatedAtUtc === null || isText(value.updatedAtUtc))
    && typeof value.version === 'number';
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
