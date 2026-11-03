export interface ObjectSearchHit { id: string; title: string; number: string | null }
export interface ObjectSearchPage { items: ObjectSearchHit[]; page: number; hasMore: boolean; isTruncated: boolean; windowDays: number }
export interface ObjectSearchSource {
  key: string;
  label: string;
  routeName: string;
  queryKey: string;
  load: (query: string, page: number, signal: AbortSignal) => Promise<ObjectSearchPage>;
}
export const isSearchObjectId = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';

/** 网络摘要必须有界；来源不能提供任意跳转地址或以总数代替授权结果。 */
export function readObjectSearchPage(value: unknown, expectedPage: number): ObjectSearchPage {
  if (!value || typeof value !== 'object') throw new TypeError('client.invalid_object_search_page');
  const page = value as Record<string, unknown>;
  if (page.page !== expectedPage || !Number.isInteger(expectedPage) || expectedPage < 1 || expectedPage > 20
    || page.windowDays !== 90 || typeof page.hasMore !== 'boolean' || typeof page.isTruncated !== 'boolean'
    || page.hasMore && (expectedPage === 20 || page.isTruncated) || page.isTruncated && expectedPage !== 20
    || !Array.isArray(page.items) || page.items.length > 5 || (page.hasMore || page.isTruncated) && page.items.length !== 5) throw new TypeError('client.invalid_object_search_page');
  const ids = new Set<string>();
  for (const item of page.items) {
    if (!item || typeof item !== 'object' || !isSearchObjectId(item.id) || ids.has(item.id.toLowerCase())
      || typeof item.title !== 'string' || !item.title.trim() || item.title.length > 256
      || item.number !== null && (typeof item.number !== 'string' || item.number.length > 128))
      throw new TypeError('client.invalid_object_search_page');
    ids.add(item.id.toLowerCase());
  }
  return value as ObjectSearchPage;
}
