export interface HostFile {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  contentHash: string | null;
  createdAtUtc: string;
  createdByUserId: string;
}

export interface HostFilePage {
  items: HostFile[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostFile(value: unknown): value is HostFile {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.originalFileName === 'string'
    && typeof value.contentType === 'string'
    && Number.isInteger(value.sizeBytes)
    && (value.contentHash === null || typeof value.contentHash === 'string')
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId);
}

export function isHostFilePage(value: unknown): value is HostFilePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostFile)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
