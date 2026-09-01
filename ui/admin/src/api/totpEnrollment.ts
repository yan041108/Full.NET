import {
  identityBeginTotpEnrollment,
  identityConfirmTotpEnrollment,
  identityGetTotpEnrollmentStatus,
  isBeginTotpEnrollmentResponse,
  isTotpEnrollmentStatus,
  type BeginTotpEnrollmentResponse,
  type TotpEnrollmentStatus
} from '@fullnet/client-contracts';
import { http } from './http';

/** 读取当前账号的 TOTP 注册状态，并对返回结构做失败关闭校验。 */
export async function getTotpEnrollmentStatus(
  signal?: AbortSignal
): Promise<TotpEnrollmentStatus> {
  const value = await identityGetTotpEnrollmentStatus(http, {}, signal);
  if (!isTotpEnrollmentStatus(value)) {
    throw new Error('client.invalid_totp_status');
  }

  return value;
}

/** 开始 TOTP 注册流程，返回二维码/密钥所需的临时登记材料。 */
export async function beginTotpEnrollment(
  signal?: AbortSignal
): Promise<BeginTotpEnrollmentResponse> {
  const value = await identityBeginTotpEnrollment(http, {}, signal);
  // 生成守卫不强制非空 secret/URI；登记页仍要求手写契约。
  if (!isBeginTotpEnrollmentResponse(value)) {
    throw new Error('client.invalid_totp_begin');
  }

  return value;
}

/** 提交一次性验证码确认 TOTP 注册。 */
export async function confirmTotpEnrollment(
  totpCode: string,
  signal?: AbortSignal
): Promise<TotpEnrollmentStatus> {
  const value = await identityConfirmTotpEnrollment(
    http,
    { body: { totpCode } },
    signal
  );
  if (!isTotpEnrollmentStatus(value)) {
    throw new Error('client.invalid_totp_confirm');
  }

  return value;
}

/** 导出 TOTP 注册状态与开始注册响应模型，供安全设置页、二维码面板与确认流程共享同一契约。 */
export type { BeginTotpEnrollmentResponse, TotpEnrollmentStatus };
