import { isGuid, isRecord } from './response-shape.js';

export interface MyTenantInvitation {
  id: string;
  tenantId: string;
  tenantName: string;
  targetEmail: string;
  memberRole: string;
  expiresAtUtc: string;
  createdAtUtc: string;
}

export interface AcceptTenantInvitationResult {
  memberId: string;
  tenantId: string;
  userId: string;
  memberRole: string;
  status: string;
}

export function isMyTenantInvitation(value: unknown): value is MyTenantInvitation {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && typeof value.tenantName === 'string'
    && typeof value.targetEmail === 'string'
    && typeof value.memberRole === 'string'
    && typeof value.expiresAtUtc === 'string'
    && typeof value.createdAtUtc === 'string';
}

export function isAcceptTenantInvitationResult(
  value: unknown
): value is AcceptTenantInvitationResult {
  return isRecord(value)
    && isGuid(value.memberId)
    && isGuid(value.tenantId)
    && isGuid(value.userId)
    && typeof value.memberRole === 'string'
    && typeof value.status === 'string';
}

export function readMyTenantInvitationList(value: unknown): MyTenantInvitation[] {
  if (!Array.isArray(value) || !value.every(isMyTenantInvitation)) {
    throw new TypeError('我的租户邀请响应不符合契约。');
  }
  return value;
}

export function readAcceptTenantInvitationResult(value: unknown): AcceptTenantInvitationResult {
  if (!isAcceptTenantInvitationResult(value)) {
    throw new TypeError('接受租户邀请响应不符合契约。');
  }
  return value;
}
