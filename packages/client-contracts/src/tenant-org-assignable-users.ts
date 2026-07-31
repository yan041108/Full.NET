export interface OrganizationAssignableUser {
  id: string;
  username: string;
  displayName: string;
}

export interface OrganizationAssignableUserPage {
  items: OrganizationAssignableUser[];
  page: number;
  pageSize: number;
  total: number;
}

/** 校验不可信 JSON 是否为组织关系表单的可分配用户分页结果。 */
export function isOrganizationAssignableUserPage(
  value: unknown
): value is OrganizationAssignableUserPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOrganizationAssignableUser)
    && isIntegerAtLeast(value.page, 1)
    && isIntegerAtLeast(value.pageSize, 1)
    && isIntegerAtLeast(value.total, 0);
}

/** 校验不可信 JSON 是否为可分配用户最小投影。 */
export function isOrganizationAssignableUser(
  value: unknown
): value is OrganizationAssignableUser {
  return isRecord(value)
    && isText(value.id)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isIntegerAtLeast(value: unknown, minimum: number): value is number {
  return typeof value === 'number'
    && Number.isInteger(value)
    && value >= minimum;
}
