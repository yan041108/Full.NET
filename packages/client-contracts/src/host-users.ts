export interface HostUser {
  id: string;
  username: string;
  displayName: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
  projectedFields?: HostUserProjectedFields | null;
}

export interface HostUserProjectedFields {
  effectiveFieldKeys: string[];
  preferredLocale: string | null;
  failedLoginCount: number | null;
  lockoutEndUtc: string | null;
}

export interface HostUserPage {
  items: HostUser[];
  page: number;
  pageSize: number;
  total: number;
}

export interface UpdateHostUserRequest {
  displayName: string;
  version: number;
}

export interface ResetHostUserPasswordRequest {
  password: string;
}

export interface HostUserRoles {
  userId: string;
  roleIds: string[];
  version: number;
}

export interface ReplaceHostUserRolesRequest {
  roleIds: string[];
  version: number;
}

/** 校验不可信 JSON 是否为 Host 用户密码重置请求。 */
export function isResetHostUserPasswordRequest(
  value: unknown
): value is ResetHostUserPasswordRequest {
  return isRecord(value) && typeof value.password === 'string' && value.password.length > 0;
}

/** 校验不可信 JSON 是否为 Host 用户更新请求。 */
export function isUpdateHostUserRequest(value: unknown): value is UpdateHostUserRequest {
  return isRecord(value)
    && typeof value.displayName === 'string'
    && value.displayName.length > 0
    && typeof value.version === 'number';
}

function isHostUserProjectedFields(value: unknown): value is HostUserProjectedFields {
  const knownFieldKeys = new Set([
    'id',
    'username',
    'display_name',
    'is_active',
    'created_at_utc',
    'updated_at_utc',
    'version',
    'preferred_locale',
    'failed_login_count',
    'lockout_end_utc'
  ]);
  if (!isRecord(value) || !Array.isArray(value.effectiveFieldKeys)) {
    return false;
  }

  const fieldKeys = value.effectiveFieldKeys;
  return isRecord(value)
    && fieldKeys.every(fieldKey => isText(fieldKey) && knownFieldKeys.has(fieldKey))
    && new Set(fieldKeys).size === fieldKeys.length
    && (value.preferredLocale === null || isText(value.preferredLocale))
    && (value.failedLoginCount === null
      || (typeof value.failedLoginCount === 'number'
        && Number.isInteger(value.failedLoginCount)
        && value.failedLoginCount >= 0))
    && (value.lockoutEndUtc === null || isText(value.lockoutEndUtc));
}

/** 校验不可信 JSON 是否为 Host 用户角色分配响应。 */
export function isHostUserRoles(value: unknown): value is HostUserRoles {
  return isRecord(value)
    && isText(value.userId)
    && Array.isArray(value.roleIds)
    && value.roleIds.every(roleId => isText(roleId))
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 用户角色替换请求。 */
export function isReplaceHostUserRolesRequest(
  value: unknown
): value is ReplaceHostUserRolesRequest {
  return isRecord(value)
    && Array.isArray(value.roleIds)
    && value.roleIds.every(roleId => isText(roleId))
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为 Host 用户分页结果。 */
export function isHostUserPage(value: unknown): value is HostUserPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostUser)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个 Host 用户。 */
export function isHostUser(value: unknown): value is HostUser {
  return isRecord(value)
    && isText(value.id)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.isActive === 'boolean'
    && isText(value.createdAtUtc)
    && (value.updatedAtUtc === null || isText(value.updatedAtUtc))
    && typeof value.version === 'number'
    && (value.projectedFields === undefined
      || value.projectedFields === null
      || isHostUserProjectedFields(value.projectedFields));
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
