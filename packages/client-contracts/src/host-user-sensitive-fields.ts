/** 判断 Host 用户手机号是否为服务端掩码占位值。 */
export function isMaskedHostUserPhoneNumber(value: string | null | undefined): boolean {
  return typeof value === 'string' && value.startsWith('****') && value.length >= 8;
}

/** 判断 Host 用户证件号是否为服务端掩码占位值。 */
export function isMaskedHostUserIdCardNumber(value: string | null | undefined): boolean {
  return typeof value === 'string' && value.includes('*');
}
