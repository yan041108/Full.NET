import {
  isSuperAdministratorArray,
  isSuperAdministratorAuditArray,
  isSuperAdministratorChangeResponse,
  type SuperAdministrator,
  type SuperAdministratorAudit,
  type SuperAdministratorChangeResponse
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getSuperAdministrators(): Promise<SuperAdministrator[]> {
  const value = await request<unknown>('/api/v1/identity/super-administrators/');
  if (!isSuperAdministratorArray(value)) throw new Error('client.invalid_super_administrator_list');
  return value;
}

export async function getSuperAdministratorAudits(): Promise<SuperAdministratorAudit[]> {
  const value = await request<unknown>('/api/v1/identity/super-administrators/audits?limit=50');
  if (!isSuperAdministratorAuditArray(value)) throw new Error('client.invalid_super_administrator_audits');
  return value;
}

export async function grantSuperAdministrator(
  username: string,
  currentPassword: string,
  totpCode?: string
): Promise<SuperAdministratorChangeResponse> {
  return await change('/api/v1/identity/super-administrators/grant', {
    username,
    currentPassword,
    ...(totpCode ? { totpCode } : {})
  });
}

export async function revokeSuperAdministrator(
  targetUserId: string,
  currentPassword: string,
  totpCode?: string
): Promise<SuperAdministratorChangeResponse> {
  return await change(
    `/api/v1/identity/super-administrators/${encodeURIComponent(targetUserId)}/revoke`,
    {
      currentPassword,
      ...(totpCode ? { totpCode } : {})
    }
  );
}

async function change(
  path: string,
  body: object
): Promise<SuperAdministratorChangeResponse> {
  const value = await request<unknown>(path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!isSuperAdministratorChangeResponse(value)) throw new Error('client.invalid_super_administrator_change');
  return value;
}
