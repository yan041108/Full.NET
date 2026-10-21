import {
  readAcceptTenantInvitationResult,
  readMyTenantInvitationList,
  type AcceptTenantInvitationResult,
  type MyTenantInvitation
} from '@fullnet/client-contracts';
import { isRecord, isInteger, type PagedResult } from '@fullnet/client-contracts';
import { http } from './http';

export type { AcceptTenantInvitationResult, MyTenantInvitation };

export async function listMyTenantInvitations(page = 1, pageSize = 20): Promise<PagedResult<MyTenantInvitation>> {
  const value = await http.request<unknown>(`/api/v1/me/tenant-invitations?page=${page}&pageSize=${pageSize}`);
  if (!isRecord(value) || !isInteger(value.page) || !isInteger(value.pageSize) || typeof value.total !== 'number')
    throw new TypeError('client.invalid_tenant_invitation_page');
  return { items: readMyTenantInvitationList(value.items), page: value.page, pageSize: value.pageSize, total: value.total };
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
  return readAcceptTenantInvitationResult(value);
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
  return readAcceptTenantInvitationResult(value);
}
