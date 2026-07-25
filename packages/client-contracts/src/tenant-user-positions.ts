export interface OrganizationUserPosition {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  positionId: string;
  positionCode: string;
  positionName: string;
  isPrimary: boolean;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface OrganizationUserPositionPage {
  items: OrganizationUserPosition[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOrganizationUserPositionRequest {
  userId: string;
  positionId: string;
  isPrimary: boolean;
}

export interface UpdateOrganizationUserPositionRequest {
  isPrimary: boolean;
  version: number;
}

/** 校验不可信 JSON 是否为租户用户-职位隶属分页结果。 */
export function isOrganizationUserPositionPage(
  value: unknown
): value is OrganizationUserPositionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationUserPosition)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单条用户-职位隶属。 */
export function isOrganizationUserPosition(
  value: unknown
): value is OrganizationUserPosition {
  return isRecord(value)
    && isText(value.id)
    && isText(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && isText(value.positionId)
    && typeof value.positionCode === 'string'
    && typeof value.positionName === 'string'
    && typeof value.isPrimary === 'boolean'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为用户-职位隶属更新请求。 */
export function isUpdateOrganizationUserPositionRequest(
  value: unknown
): value is UpdateOrganizationUserPositionRequest {
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
