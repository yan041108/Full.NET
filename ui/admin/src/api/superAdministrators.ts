import {
  identityGrantSuperAdministrator,
  identityListSuperAdministratorAudits,
  identityListSuperAdministrators,
  identityRevokeSuperAdministrator,
  isSuperAdministratorArray,
  isSuperAdministratorAuditArray,
  isSuperAdministratorChangeResponse,
  type SuperAdministrator,
  type SuperAdministratorAudit,
  type SuperAdministratorChangeResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getSuperAdministrators(
  signal?: AbortSignal
): Promise<SuperAdministrator[]> {
  const value = await identityListSuperAdministrators(http, {}, signal);
  if (!isSuperAdministratorArray(value)) {
    throw new Error('client.invalid_super_administrator_list');
  }

  return value;
}

export async function getSuperAdministratorAudits(
  signal?: AbortSignal
): Promise<SuperAdministratorAudit[]> {
  // 页面固定拉取最近 50 条；保留与手写实现相同的查询语义。
  const value = await identityListSuperAdministratorAudits(
    http,
    { limit: 50 },
    signal
  );
  if (!isSuperAdministratorAuditArray(value)) {
    throw new Error('client.invalid_super_administrator_audits');
  }

  return value;
}

export async function grantSuperAdministrator(
  username: string,
  currentPassword: string,
  totpCode?: string,
  signal?: AbortSignal
): Promise<SuperAdministratorChangeResponse> {
  const value = await identityGrantSuperAdministrator(
    http,
    {
      body: {
        username,
        currentPassword,
        ...(totpCode ? { totpCode } : {})
      }
    },
    signal
  );
  if (!isSuperAdministratorChangeResponse(value)) {
    throw new Error('client.invalid_super_administrator_change');
  }

  return value;
}

export async function revokeSuperAdministrator(
  targetUserId: string,
  currentPassword: string,
  totpCode?: string,
  signal?: AbortSignal
): Promise<SuperAdministratorChangeResponse> {
  const value = await identityRevokeSuperAdministrator(
    http,
    {
      targetUserId,
      body: {
        currentPassword,
        ...(totpCode ? { totpCode } : {})
      }
    },
    signal
  );
  if (!isSuperAdministratorChangeResponse(value)) {
    throw new Error('client.invalid_super_administrator_change');
  }

  return value;
}
