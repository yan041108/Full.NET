export interface OrganizationPositionLevel {
  id: string;
  code: string;
  name: string;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface OrganizationPositionLevelPage {
  items: OrganizationPositionLevel[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOrganizationPositionLevelRequest {
  code: string;
  name: string;
  displayOrder: number;
}

export interface UpdateOrganizationPositionLevelRequest {
  name: string;
  displayOrder: number;
  version: number;
}

/** 校验不可信 JSON 是否为租户职级分页结果。 */
export function isOrganizationPositionLevelPage(
  value: unknown
): value is OrganizationPositionLevelPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationPositionLevel)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

/** 校验不可信 JSON 是否为单个租户职级。 */
export function isOrganizationPositionLevel(
  value: unknown
): value is OrganizationPositionLevel {
  return isRecord(value)
    && isText(value.id)
    && typeof value.code === 'string'
    && typeof value.name === 'string'
    && typeof value.displayOrder === 'number'
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
