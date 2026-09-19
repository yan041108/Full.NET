import type { RegisterAccountRequest, RecoverPasswordConfirmRequest, VerifyRegistrationInvitationResponse } from '@fullnet/client-contracts';
import { resolveFullNetApiUrl } from '@fullnet/client-contracts';
import { isRecord, isGuid, isDate } from '@fullnet/client-contracts';
import { apiBaseUrl } from './http';

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(resolveFullNetApiUrl(apiBaseUrl, path), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': navigator.language || 'zh-CN'
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
  return postJson<{ challengeId: string }>('/api/v1/auth/register/email-challenge', {
    email,
    purpose,
    invitationId,
    invitationToken
  });
}

export function recoverPassword(email: string) {
  return postJson<{ challengeId: string; expiresAtUtc: string }>('/api/v1/auth/recover-password', { email });
}

export function confirmRecoverPassword(request: RecoverPasswordConfirmRequest) {
  return postJson<void>('/api/v1/auth/recover-password/confirm', request);
}

export function verifyInvitation(invitationId: string, invitationToken: string) {
  return postJson<VerifyRegistrationInvitationResponse>('/api/v1/auth/invitations/verify', {
    invitationId,
    invitationToken
  });
}
