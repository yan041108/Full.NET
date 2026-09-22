import {
  readAcceptTenantInvitationResult,
  readMyTenantInvitationList,
  type AcceptTenantInvitationResult,
  type MyTenantInvitation
} from '@fullnet/client-contracts';
import { http } from './http';

export type { AcceptTenantInvitationResult, MyTenantInvitation };

export async function listMyTenantInvitations(): Promise<MyTenantInvitation[]> {
  const value = await http.request<unknown>('/api/v1/me/tenant-invitations');
  return readMyTenantInvitationList(value);
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
