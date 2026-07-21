export interface OrganizationUserUnit {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  unitId: string;
  unitCode: string;
  unitName: string;
  isPrimary: boolean;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface OrganizationUserUnitPage {
  items: OrganizationUserUnit[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOrganizationUserUnitRequest {
  userId: string;
  unitId: string;
  isPrimary: boolean;
}

export interface UpdateOrganizationUserUnitRequest {
  isPrimary: boolean;
  version: number;
}

/** 校验不可信 JSON 是否为租户用户-机构隶属分页结果。 */
export function isOrganizationUserUnitPage(
  value: unknown
): value is OrganizationUserUnitPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationUserUnit)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单条用户-机构隶属。 */
export function isOrganizationUserUnit(
  value: unknown
): value is OrganizationUserUnit {
  return isRecord(value)
    && isText(value.id)
    && isText(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && isText(value.unitId)
    && typeof value.unitCode === 'string'
    && typeof value.unitName === 'string'
    && typeof value.isPrimary === 'boolean'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为用户-机构隶属更新请求。 */
export function isUpdateOrganizationUserUnitRequest(
  value: unknown
): value is UpdateOrganizationUserUnitRequest {
  return isRecord(value)
    && typeof value.isPrimary === 'boolean'
    && typeof value.version === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
