export interface HostUser {
  id: string;
  username: string;
  displayName: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostUserPage {
  items: HostUser[];
  page: number;
  pageSize: number;
  total: number;
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
    && typeof value.version === 'number';
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
