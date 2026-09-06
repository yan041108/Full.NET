export interface MyReleaseNote {
  id: string;
  versionLabel: string;
  versionSortKey: number;
  title: string;
  content: string;
  publishedAtUtc: string;
  isRead: boolean;
  readAtUtc: string | null;
}

export interface MyReleaseNotePage {
  items: MyReleaseNote[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isMyReleaseNote(value: unknown): value is MyReleaseNote {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.versionLabel)
    && typeof value.versionSortKey === 'number'
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && typeof value.publishedAtUtc === 'string'
    && typeof value.isRead === 'boolean'
    && (value.readAtUtc === null || typeof value.readAtUtc === 'string');
}

export function isMyReleaseNotePage(value: unknown): value is MyReleaseNotePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isMyReleaseNote)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
