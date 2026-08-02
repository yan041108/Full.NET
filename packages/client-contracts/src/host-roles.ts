/** 自定义 Host 角色可分配的权限白名单（与后端校验一致，不含 super_administrators.*）。 */
export const HOST_ROLE_ASSIGNABLE_PERMISSIONS = [
  'platform.dashboard.read',
  'identity.navigation.read',
  'identity.users.read',
  'identity.users.create',
  'identity.users.update',
  'identity.users.assign_roles',
  'identity.users.reset_password',
  'identity.users.disable',
  'identity.users.enable',
  'identity.users.export',
  'identity.roles.read',
  'identity.roles.create',
  'identity.roles.update',
  'identity.roles.assign_permissions',
  'identity.roles.disable',
  'identity.roles.assign_data_scope',
  'identity.role_field_grants.read',
  'identity.role_field_grants.replace',
  'identity.menus.read',
  'identity.menus.create',
  'identity.menus.update',
  'identity.menus.disable',
  'identity.sessions.read',
  'identity.sessions.revoke',
  'identity.api_keys.read',
  'identity.api_keys.create',
  'identity.api_keys.disable',
  'identity.api_keys.rotate',
  'organization.units.read',
  'organization.units.create',
  'organization.units.update',
  'organization.units.disable',
  'organization.positions.read',
  'organization.positions.create',
  'organization.positions.update',
  'organization.positions.disable',
  'organization.positions.assign_unit',
  'organization.positions.assign_position_level',
  'organization.position_levels.read',
  'organization.position_levels.create',
  'organization.position_levels.update',
  'organization.position_levels.disable',
  'organization.user_positions.read',
  'organization.user_positions.create',
  'organization.user_positions.update',
  'organization.user_positions.disable',
  'organization.user_units.read',
  'organization.user_units.create',
  'organization.user_units.update',
  'organization.user_units.disable',
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
] as const;

/** 角色数据范围种类稳定机器码（与后端 RoleDataScopeKinds 一致）。 */
export const ROLE_DATA_SCOPE_KINDS = [
  'identity.data_scope.all',
  'identity.data_scope.org',
  'identity.data_scope.org_subtree',
  'identity.data_scope.self',
  'identity.data_scope.custom'
] as const;

export type RoleDataScopeKind = typeof ROLE_DATA_SCOPE_KINDS[number];

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

export interface HostRoleDataScope {
  roleId: string;
  dataScopeKind: RoleDataScopeKind;
  unitIds: string[];
  version: number;
}

export interface UpdateHostRoleDataScopeRequest {
  dataScopeKind: RoleDataScopeKind;
  unitIds: string[] | null;
  version: number;
  tenantId: string | null;
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

/** 校验不可信 JSON 是否为 Host 角色数据范围响应。 */
export function isHostRoleDataScope(value: unknown): value is HostRoleDataScope {
  return isRecord(value)
    && isText(value.roleId)
    && typeof value.dataScopeKind === 'string'
    && ROLE_DATA_SCOPE_KINDS.includes(value.dataScopeKind as RoleDataScopeKind)
    && Array.isArray(value.unitIds)
    && value.unitIds.every(unitId => isText(unitId))
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 角色数据范围更新请求。 */
export function isUpdateHostRoleDataScopeRequest(
  value: unknown
): value is UpdateHostRoleDataScopeRequest {
  return isRecord(value)
    && typeof value.dataScopeKind === 'string'
    && ROLE_DATA_SCOPE_KINDS.includes(value.dataScopeKind as RoleDataScopeKind)
    && (value.unitIds === null
      || (Array.isArray(value.unitIds)
        && value.unitIds.every(unitId => isText(unitId))))
    && typeof value.version === 'number'
    && (value.tenantId === null || isText(value.tenantId));
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
