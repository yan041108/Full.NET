export interface OrganizationPosition {
  id: string;
  code: string;
  name: string;
  unitId: string | null;
  unitCode: string | null;
  unitName: string | null;
  positionLevelId: string | null;
  positionLevelCode: string | null;
  positionLevelName: string | null;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface OrganizationPositionPage {
  items: OrganizationPosition[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOrganizationPositionRequest {
  code: string;
  name: string;
  displayOrder: number;
}

export interface UpdateOrganizationPositionRequest {
  name: string;
  displayOrder: number;
  version: number;
}

export interface AssignOrganizationPositionUnitRequest {
  unitId: string | null;
  version: number;
}

export interface AssignOrganizationPositionLevelRequest {
  positionLevelId: string | null;
  version: number;
}

/** 校验不可信 JSON 是否为租户职位分页结果。 */
export function isOrganizationPositionPage(
  value: unknown
): value is OrganizationPositionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationPosition)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个租户职位。 */
export function isOrganizationPosition(value: unknown): value is OrganizationPosition {
  return isRecord(value)
    && isText(value.id)
    && typeof value.code === 'string'
    && typeof value.name === 'string'
    && Object.hasOwn(value, 'unitId')
    && Object.hasOwn(value, 'unitCode')
    && Object.hasOwn(value, 'unitName')
    && (
      (
        value.unitId === null
        && value.unitCode === null
        && value.unitName === null
      )
      || (
        isText(value.unitId)
        && isText(value.unitCode)
        && isText(value.unitName)
      )
    )
    && Object.hasOwn(value, 'positionLevelId')
    && Object.hasOwn(value, 'positionLevelCode')
    && Object.hasOwn(value, 'positionLevelName')
    && (
      (
        value.positionLevelId === null
        && value.positionLevelCode === null
        && value.positionLevelName === null
      )
      || (
        isText(value.positionLevelId)
        && isText(value.positionLevelCode)
        && isText(value.positionLevelName)
      )
    )
    && typeof value.displayOrder === 'number'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为租户职位更新请求。 */
export function isUpdateOrganizationPositionRequest(
  value: unknown
): value is UpdateOrganizationPositionRequest {
  return isRecord(value)
    && typeof value.name === 'string'
    && value.name.length > 0
    && typeof value.displayOrder === 'number'
    && typeof value.version === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
