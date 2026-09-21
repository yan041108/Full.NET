import { isGuid, isRecord } from '@fullnet/client-contracts';
import { http } from './http';

export interface MyTenantInvitation {
  readonly id: string;
  readonly tenantId: string;
  readonly tenantName: string;
  readonly targetEmail: string;
  readonly memberRole: string;
  readonly expiresAtUtc: string;
  readonly createdAtUtc: string;
}

function isMyTenantInvitation(value: unknown): value is MyTenantInvitation {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && typeof value.tenantName === 'string'
    && typeof value.targetEmail === 'string'
    && typeof value.memberRole === 'string'
    && typeof value.expiresAtUtc === 'string'
    && typeof value.createdAtUtc === 'string';
}

function isAcceptTenantInvitationResponse(
  value: unknown
): value is AcceptTenantInvitationResult {
  return isRecord(value)
    && isGuid(value.memberId)
    && isGuid(value.tenantId)
    && isGuid(value.userId)
    && typeof value.memberRole === 'string'
    && typeof value.status === 'string';
}

export async function listMyTenantInvitations(): Promise<MyTenantInvitation[]> {
  const value = await http.request<unknown>('/api/v1/me/tenant-invitations');
  if (!Array.isArray(value) || !value.every(isMyTenantInvitation)) {
    throw new TypeError('我的租户邀请响应不符合契约。');
  }

  return value;
}

export interface AcceptTenantInvitationResult {
  readonly memberId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly memberRole: string;
  readonly status: string;
}

export async function acceptMyTenantInvitation(
  invitationId: string
): Promise<AcceptTenantInvitationResult> {
  const value = await http.request<unknown>(
    `/api/v1/me/tenant-invitations/${invitationId}/accept`,
    { method: 'POST' },
    undefined,
    { retryUnauthorized: false }
  );
  if (!isAcceptTenantInvitationResponse(value)) {
    throw new TypeError('接受租户邀请响应不符合契约。');
  }

  return value;
}

export async function acceptTenantInvitationByToken(
  invitationToken: string
): Promise<AcceptTenantInvitationResult> {
  const value = await http.request<unknown>(
    '/api/v1/identity/tenant-invitations/accept',
    {
      method: 'POST',
      body: JSON.stringify({ invitationToken })
    },
    undefined,
    { retryUnauthorized: false }
  );
  if (!isAcceptTenantInvitationResponse(value)) {
    throw new TypeError('接受租户邀请响应不符合契约。');
  }

  return value;
}
