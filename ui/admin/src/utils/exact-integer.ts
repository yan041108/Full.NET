/** 非负 Int64 使用规范十进制字符串，拒绝数字、指数、前导零和越界值。 */
export function isNonnegativeInt64String(value: unknown): value is string {
  return typeof value === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(value) && BigInt(value) <= 9223372036854775807n;
}
