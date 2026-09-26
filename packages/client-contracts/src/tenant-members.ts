export interface TenantMemberListQuery {
  page?: number;
  pageSize?: number;
  status?: string;
}

export interface TenantMember {
  id: string;
  tenantId: string;
  userId: string;
  username: string;
  displayName: string;
  memberRole: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface TenantMemberPage {
  items: TenantMember[];
  page: number;
  pageSize: number;
  total: number;
}

export interface TenantInvitation {
  id: string;
  tenantId: string;
  targetEmail: string;
  targetUserId?: string | null;
  invitedByUserId: string;
  memberRole: string;
  status: string;
  expiresAtUtc: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface TenantInvitationPage {
  items: TenantInvitation[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateTenantInvitationRequest {
  targetEmail: string;
  memberRole: string;
  expiresInHours?: number;
}

export interface LeaveTenantMembershipRequest {
  version: number;
}

export function isTenantMember(value: unknown): value is TenantMember {
  return (
    isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && isGuid(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.memberRole === 'string'
    && typeof value.status === 'string'
    && isDate(value.createdAtUtc)
    && isDate(value.updatedAtUtc)
    && isInteger(value.version)
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function isDate(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && !Number.isNaN(Date.parse(value));
}

function isInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value);
}
