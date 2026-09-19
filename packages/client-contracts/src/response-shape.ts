/** 共享响应校验只读取明确字段，不将未知 JSON 强制转换为业务对象。 */
export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
export const isString = (value: unknown): value is string => typeof value === 'string';
export const isNullableString = (value: unknown): value is string | null => value === null || isString(value);
export const isGuid = (value: unknown): value is string => isString(value)
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value);
export const isInteger = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value);
export const isDate = (value: unknown): value is string => isString(value)
  && /^\d{4}-\d{2}-\d{2}T/u.test(value) && Number.isFinite(Date.parse(value));
export const isNullableDate = (value: unknown): value is string | null => value === null || isDate(value);
export function isPage<T>(value: unknown, isItem: (item: unknown) => item is T): value is { items: T[]; page: number; pageSize: number; total: number } {
  return isRecord(value) && Array.isArray(value.items) && value.items.every(isItem)
    && isInteger(value.page) && value.page > 0 && isInteger(value.pageSize) && value.pageSize > 0
    && isInteger(value.total) && value.total >= 0;
}
export function readResponse<T>(value: unknown, guard: (value: unknown) => value is T, code: string): T {
  if (!guard(value)) throw new Error(code);
  return value;
}
