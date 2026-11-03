/** 单一来源的有界游标页；不表达总数或跨来源排序。 */
export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

/** 交叉核对后续标记与位置，避免损坏响应使客户端无限重复第一页。 */
export function isCursorPage<T>(value: unknown, item: (entry: unknown) => entry is T): value is CursorPage<T> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
  const page = value as Record<string, unknown>;
  return Array.isArray(page.items) && page.items.length <= 100 && page.items.every(item) && typeof page.hasMore === 'boolean'
    && (page.hasMore ? page.items.length > 0 && typeof page.nextCursor === 'string' && page.nextCursor.length > 0 && page.nextCursor.length <= 1024
      : page.nextCursor === null);
}
