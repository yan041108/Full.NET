import {
  isBeginTotpEnrollmentResponse,
  isTotpEnrollmentStatus,
  type BeginTotpEnrollmentResponse,
  type TotpEnrollmentStatus
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getTotpEnrollmentStatus(): Promise<TotpEnrollmentStatus> {
  const value = await request<unknown>('/api/v1/identity/me/mfa/totp/');
  if (!isTotpEnrollmentStatus(value)) throw new Error('client.invalid_totp_status');
  return value;
}

export async function beginTotpEnrollment(): Promise<BeginTotpEnrollmentResponse> {
  const value = await request<unknown>('/api/v1/identity/me/mfa/totp/begin', {
    method: 'POST'
  });
  if (!isBeginTotpEnrollmentResponse(value)) throw new Error('client.invalid_totp_begin');
  return value;
}

export async function confirmTotpEnrollment(
  totpCode: string
): Promise<TotpEnrollmentStatus> {
  const value = await request<unknown>('/api/v1/identity/me/mfa/totp/confirm', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ totpCode })
  });
  if (!isTotpEnrollmentStatus(value)) throw new Error('client.invalid_totp_confirm');
  return value;
}
