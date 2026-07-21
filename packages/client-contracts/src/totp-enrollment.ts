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
    && typeof value.isEnabled === 'boolean';
}

/** 校验 begin 登记响应。 */
export function isBeginTotpEnrollmentResponse(
  value: unknown
): value is BeginTotpEnrollmentResponse {
  return isRecord(value)
    && isText(value.sharedSecretBase32)
    && isText(value.otpAuthUri);
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
