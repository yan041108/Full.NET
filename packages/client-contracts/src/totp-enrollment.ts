/** TOTP 登记状态。 */
export interface TotpEnrollmentStatus {
  isEnrolled: boolean;
  isEnabled: boolean;
}

/** 开始登记返回的共享密钥与 otpauth URI（仅本次响应明文）。 */
export interface BeginTotpEnrollmentResponse {
  sharedSecretBase32: string;
  otpAuthUri: string;
}

/** 校验 TOTP 登记状态响应。 */
export function isTotpEnrollmentStatus(
  value: unknown
): value is TotpEnrollmentStatus {
  return isRecord(value)
    && typeof value.isEnrolled === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && (!value.isEnabled || value.isEnrolled);
}

/** 校验 begin 登记响应。 */
export function isBeginTotpEnrollmentResponse(
  value: unknown
): value is BeginTotpEnrollmentResponse {
  if (!isRecord(value) || !isText(value.sharedSecretBase32)
    || !/^[A-Z2-7]+$/.test(value.sharedSecretBase32) || !isText(value.otpAuthUri)) return false;
  try {
    const uri = new URL(value.otpAuthUri);
    // 登记材料仅接受当前服务端支持的算法和与明文密钥一致的本地配置 URI。
    return uri.protocol === 'otpauth:' && uri.hostname === 'totp'
      && !uri.username && !uri.password && !uri.port && !uri.hash && uri.pathname.length > 1
      && uri.searchParams.getAll('secret').length === 1
      && uri.searchParams.get('secret') === value.sharedSecretBase32
      && ['algorithm', 'digits', 'period'].every(key => uri.searchParams.getAll(key).length <= 1)
      && (!uri.searchParams.has('algorithm') || uri.searchParams.get('algorithm') === 'SHA1')
      && (!uri.searchParams.has('digits') || uri.searchParams.get('digits') === '6')
      && (!uri.searchParams.has('period') || uri.searchParams.get('period') === '30');
  } catch { return false; }
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
