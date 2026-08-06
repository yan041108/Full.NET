export interface HostUser {
  id: string;
  username: string;
  displayName: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
  projectedFields?: HostUserProjectedFields | null;
  profile?: HostUserProfile | null;
}

export interface HostUserProfile {
  nickname: string | null;
  phoneNumber: string | null;
  email: string | null;
  employeeNumber: string | null;
  gender: string | null;
  joinDateUtc: string | null;
  sortOrder: number | null;
  idCardType: string | null;
  idCardNumber: string | null;
  birthDate: string | null;
  ethnicity: string | null;
  address: string | null;
  graduatedSchool: string | null;
  educationLevel: string | null;
  politicalStatus: string | null;
  officePhone: string | null;
  emergencyContact: string | null;
  emergencyContactPhone: string | null;
  emergencyContactAddress: string | null;
  remark: string | null;
  version: number;
}

export interface HostUserProfileWrite {
  fieldKeys?: string[] | null;
  nickname?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  employeeNumber?: string | null;
  gender?: string | null;
  joinDateUtc?: string | null;
  sortOrder?: number | null;
  idCardType?: string | null;
  idCardNumber?: string | null;
  birthDate?: string | null;
  ethnicity?: string | null;
  address?: string | null;
  graduatedSchool?: string | null;
  educationLevel?: string | null;
  politicalStatus?: string | null;
  officePhone?: string | null;
  emergencyContact?: string | null;
  emergencyContactPhone?: string | null;
  emergencyContactAddress?: string | null;
  remark?: string | null;
  version?: number | null;
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
  profile?: HostUserProfileWrite | null;
}

export interface CreateHostUserRequest {
  username: string;
  displayName: string;
  password: string;
  profile?: HostUserProfileWrite | null;
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
    'lockout_end_utc',
    'nickname',
    'phone_number',
    'email',
    'employee_number',
    'gender',
    'join_date_utc',
    'sort_order',
    'id_card_type',
    'id_card_number',
    'birth_date',
    'ethnicity',
    'address',
    'graduated_school',
    'education_level',
    'political_status',
    'office_phone',
    'emergency_contact',
    'emergency_contact_phone',
    'emergency_contact_address',
    'remark'
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
      || isHostUserProjectedFields(value.projectedFields))
    && (value.profile === undefined || value.profile === null || isHostUserProfile(value.profile));
}

function isHostUserProfile(value: unknown): value is HostUserProfile {
  return isRecord(value)
    && (value.nickname === null || typeof value.nickname === 'string')
    && (value.phoneNumber === null || typeof value.phoneNumber === 'string')
    && (value.email === null || typeof value.email === 'string')
    && (value.employeeNumber === null || typeof value.employeeNumber === 'string')
    && (value.gender === null || typeof value.gender === 'string')
    && (value.joinDateUtc === null || typeof value.joinDateUtc === 'string')
    && (value.sortOrder === null || typeof value.sortOrder === 'number')
    && (value.idCardType === null || typeof value.idCardType === 'string')
    && (value.idCardNumber === null || typeof value.idCardNumber === 'string')
    && (value.birthDate === null || typeof value.birthDate === 'string')
    && (value.ethnicity === null || typeof value.ethnicity === 'string')
    && (value.address === null || typeof value.address === 'string')
    && (value.graduatedSchool === null || typeof value.graduatedSchool === 'string')
    && (value.educationLevel === null || typeof value.educationLevel === 'string')
    && (value.politicalStatus === null || typeof value.politicalStatus === 'string')
    && (value.officePhone === null || typeof value.officePhone === 'string')
    && (value.emergencyContact === null || typeof value.emergencyContact === 'string')
    && (value.emergencyContactPhone === null || typeof value.emergencyContactPhone === 'string')
    && (value.emergencyContactAddress === null || typeof value.emergencyContactAddress === 'string')
    && (value.remark === null || typeof value.remark === 'string')
    && typeof value.version === 'number';
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
