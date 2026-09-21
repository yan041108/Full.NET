import type { TenantMemberListQuery, TenantMember, TenantMemberPage, TenantInvitation, TenantInvitationPage, CreateTenantInvitationRequest } from '@fullnet/client-contracts';
import { request } from './http';
import { isRecord, isGuid, isInteger, isDate, isPage, readResponse } from '@fullnet/client-contracts';
function isMember(v: unknown): v is TenantMember { return isRecord(v) && isGuid(v.id) && isGuid(v.tenantId) && isGuid(v.userId) && typeof v.username === 'string' && typeof v.displayName === 'string' && typeof v.memberRole === 'string' && typeof v.status === 'string' && isDate(v.createdAtUtc) && isDate(v.updatedAtUtc) && isInteger(v.version); }
function isInvitation(v: unknown): v is TenantInvitation { return isRecord(v) && isGuid(v.id) && isGuid(v.tenantId) && typeof v.targetEmail === 'string' && (v.targetUserId === null || v.targetUserId === undefined || isGuid(v.targetUserId)) && isGuid(v.invitedByUserId) && typeof v.memberRole === 'string' && typeof v.status === 'string' && isDate(v.expiresAtUtc) && isDate(v.createdAtUtc) && isDate(v.updatedAtUtc) && isInteger(v.version); }

function buildQuery(query: TenantMemberListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.status?.trim()) {
    params.set('status', query.status.trim());
  }
  return params.toString();
}

export async function listTenantMembers(
  query: TenantMemberListQuery = {},
  signal?: AbortSignal
): Promise<TenantMemberPage> {
  const value = await request<unknown>(
    `/api/v1/identity/tenant-members?${buildQuery(query)}`,
    { method: 'GET' },
    signal
  ); return readResponse(value, v => isPage(v, isMember), 'client.invalid_tenant_member_page') as TenantMemberPage;
}

export async function listTenantInvitations(
  query: TenantMemberListQuery = {},
  signal?: AbortSignal
): Promise<TenantInvitationPage> {
  const value = await request<unknown>(
    `/api/v1/identity/tenant-members/invitations?${buildQuery(query)}`,
    { method: 'GET' },
    signal
  ); return readResponse(value, v => isPage(v, isInvitation), 'client.invalid_tenant_invitation_page') as TenantInvitationPage;
}

export async function provisionTenantMember(
  body: {
    username: string;
    displayName: string;
    password: string;
    memberRole: string;
    email?: string | null;
  },
  signal?: AbortSignal
): Promise<TenantMember> {
  const value = await request<unknown>(
    '/api/v1/identity/tenant-members/provision',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  return readResponse(value, isMember, 'client.invalid_tenant_member');
}

export async function createTenantInvitation(
  body: CreateTenantInvitationRequest,
  signal?: AbortSignal
): Promise<{ invitation: TenantInvitation; invitationToken: string }> {
  const value = await request<unknown>(
    '/api/v1/identity/tenant-members/invitations',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  ); return readResponse(value, (v): v is { invitation: TenantInvitation; invitationToken: string } => isRecord(v) && isInvitation(v.invitation) && typeof v.invitationToken === 'string' && v.invitationToken.length > 0, 'client.invalid_tenant_invitation_result');
}

export async function updateTenantMember(
  memberId: string,
  body: { memberRole: string; version: number },
  signal?: AbortSignal
): Promise<TenantMember> {
  const value = await request<unknown>(
    `/api/v1/identity/tenant-members/${memberId}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  return readResponse(value, isMember, 'client.invalid_tenant_member');
}

export async function removeTenantMember(
  memberId: string,
  version: number,
  signal?: AbortSignal
): Promise<TenantMember> {
  const params = new URLSearchParams();
  params.set('version', String(version));
  const value = await request<unknown>(
    `/api/v1/identity/tenant-members/${memberId}?${params.toString()}`,
    { method: 'DELETE' },
    signal
  );
  return readResponse(value, isMember, 'client.invalid_tenant_member');
}

export async function revokeTenantInvitation(
  invitationId: string,
  signal?: AbortSignal
): Promise<TenantInvitation> {
  const value = await request<unknown>(
    `/api/v1/identity/tenant-members/invitations/${invitationId}/revoke`,
    { method: 'POST' },
    signal
  );
  return readResponse(value, isInvitation, 'client.invalid_tenant_invitation');
}
