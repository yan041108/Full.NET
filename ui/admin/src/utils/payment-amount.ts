import { isNonnegativeInt64String } from './exact-integer';
const maximumMinor = 9223372036854775807n;

/** 元/分全程使用整数计算，HTTP 保留规范字符串，不经过浏览器 Number。 */
export function parsePaymentAmount(input: string): string | undefined {
  const text = input.trim();
  if (text.length > 32) return undefined;
  const match = /^(0|[1-9][0-9]*)(?:\.([0-9]{1,2}))?$/.exec(text);
  if (!match) return undefined;
  const minor = BigInt(match[1]!) * 100n + BigInt((match[2] ?? '').padEnd(2, '0'));
  return minor > 0n && minor <= maximumMinor ? minor.toString() : undefined;
}

/** 拒绝错误线格式；完整 Int64 最小单位按整数拆分为两位小数。 */
export function formatPaymentAmount(minor: unknown): string | undefined {
  if (!isNonnegativeInt64String(minor)) return undefined;
  const value = BigInt(minor);
  return `${value / 100n}.${(value % 100n).toString().padStart(2, '0')}`;
}
