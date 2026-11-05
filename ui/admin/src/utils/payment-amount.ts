const maximumNumericMinor = BigInt(Number.MAX_SAFE_INTEGER);

/** 当前订单 HTTP 契约使用数字；先以整数解析元/分，再检查可精确传输的边界。 */
export function parsePaymentAmount(input: string): number | undefined {
  const text = input.trim();
  if (text.length > 32) return undefined;
  const match = /^(0|[1-9][0-9]*)(?:\.([0-9]{1,2}))?$/.exec(text);
  if (!match) return undefined;
  const minor = BigInt(match[1]!) * 100n + BigInt((match[2] ?? '').padEnd(2, '0'));
  return minor > 0n && minor <= maximumNumericMinor ? Number(minor) : undefined;
}

/** 数字契约以外的值不显示舍入金额；有效最小单位用整数拆分为两位小数。 */
export function formatPaymentAmount(minor: unknown): string | undefined {
  if (typeof minor !== 'number' || !Number.isSafeInteger(minor) || minor < 0) return undefined;
  const value = BigInt(minor);
  return `${value / 100n}.${(value % 100n).toString().padStart(2, '0')}`;
}
