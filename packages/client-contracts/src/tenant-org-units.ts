export interface OrganizationUnit {
  id: string;
  parentId: string | null;
  code: string;
  name: string;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface OrganizationUnitPage {
  items: OrganizationUnit[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOrganizationUnitRequest {
  parentId: string | null;
  code: string;
  name: string;
  displayOrder: number;
}

export interface UpdateOrganizationUnitRequest {
  parentId: string | null;
  name: string;
  displayOrder: number;
  version: number;
}

/** 校验不可信 JSON 是否为租户机构分页结果。 */
export function isOrganizationUnitPage(
  value: unknown
): value is OrganizationUnitPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationUnit)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个租户机构。 */
export function isOrganizationUnit(value: unknown): value is OrganizationUnit {
  return isRecord(value)
    && isText(value.id)
    && (value.parentId === null || isText(value.parentId))
    && typeof value.code === 'string'
    && typeof value.name === 'string'
    && typeof value.displayOrder === 'number'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

/** 校验不可信 JSON 是否为租户机构更新请求。 */
export function isUpdateOrganizationUnitRequest(
  value: unknown
): value is UpdateOrganizationUnitRequest {
  return isRecord(value)
    && (value.parentId === null || typeof value.parentId === 'string')
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
