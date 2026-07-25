export interface HostOnlineSession {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  clientId: string;
  activeTenantId: string | null;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface HostOnlineSessionPage {
  items: HostOnlineSession[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostOnlineSession(value: unknown): value is HostOnlineSession {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.clientId === 'string'
    && (value.activeTenantId === null || isGuid(value.activeTenantId))
    && typeof value.createdAtUtc === 'string'
    && typeof value.expiresAtUtc === 'string';
}

export function isHostOnlineSessionPage(value: unknown): value is HostOnlineSessionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostOnlineSession)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
