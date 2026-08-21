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

export async function getTotpEnrollmentStatus(
  signal?: AbortSignal
): Promise<TotpEnrollmentStatus> {
  const value = await identityGetTotpEnrollmentStatus(http, {}, signal);
  if (!isTotpEnrollmentStatus(value)) {
    throw new Error('client.invalid_totp_status');
  }

  return value;
}

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
