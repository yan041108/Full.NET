import type { RegisterAccountRequest, RecoverPasswordConfirmRequest, VerifyRegistrationInvitationResponse } from '@fullnet/client-contracts';
import { resolveFullNetApiUrl } from '@fullnet/client-contracts';
import { isRecord, isGuid, isDate } from '@fullnet/client-contracts';
import { apiBaseUrl } from './http';
import { useAdminI18n } from '../i18n/adminI18n';

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(resolveFullNetApiUrl(apiBaseUrl, path), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': useAdminI18n().locale.value
    },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw await response.json();
  }
  if (response.status === 204) {
    return undefined as T;
  }
  const text = await response.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

export function registerAccount(request: RegisterAccountRequest) {
  return postJson<unknown>('/api/v1/auth/register', request).then(value => {
    if (!isRecord(value) || !isGuid(value.userId)) throw new Error('client.invalid_register_account_response');
    return { userId: value.userId };
  });
}

export function requestEmailChallenge(
  email: string,
  purpose: number,
  invitationId?: string,
  invitationToken?: string
) {
  return postJson<unknown>('/api/v1/auth/register/email-challenge', {
    email,
    purpose,
    invitationId,
    invitationToken
  }).then(validateAccountChallengeResponse);
}

export function recoverPassword(email: string) {
  return postJson<unknown>('/api/v1/auth/recover-password', { email }).then(validateAccountChallengeResponse);
}

export function confirmRecoverPassword(request: RecoverPasswordConfirmRequest) {
  return postJson<void>('/api/v1/auth/recover-password/confirm', request);
}

export function verifyInvitation(invitationId: string, invitationToken: string) {
  return postJson<unknown>('/api/v1/auth/invitations/verify', {
    invitationId,
    invitationToken
  }).then(value => {
    // 校验线协议与请求绑定，拒绝将另一邀请或畸形数据带入注册表单。
    if (!isRecord(value) || !isGuid(value.invitationId) || !isGuid(value.tenantId)
      || !isGuid(value.registrationWayId) || typeof value.email !== 'string' || !value.email.trim()
      || !isDate(value.expiresAtUtc) || !isGuid(invitationId)
      || value.invitationId.toLowerCase() !== invitationId.toLowerCase()) {
      throw new Error('client.invalid_registration_invitation_response');
    }
    return { invitationId: value.invitationId, tenantId: value.tenantId, email: value.email,
      registrationWayId: value.registrationWayId, expiresAtUtc: value.expiresAtUtc } satisfies VerifyRegistrationInvitationResponse;
  });
}

/** 验证匿名挑战响应的标识和有效期；类型断言不能把畸形成功响应提升为可提交凭据。 */
function validateAccountChallengeResponse(value: unknown): { challengeId: string; expiresAtUtc: string } {
  if (!isRecord(value) || !isGuid(value.challengeId) || !isDate(value.expiresAtUtc)) {
    throw new Error('client.invalid_account_challenge_response');
  }
  return { challengeId: value.challengeId, expiresAtUtc: value.expiresAtUtc };
}
