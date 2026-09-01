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

/** 查询当前超级管理员列表，并对响应结构做失败关闭校验。 */
export async function getSuperAdministrators(
  signal?: AbortSignal
): Promise<SuperAdministrator[]> {
  const value = await identityListSuperAdministrators(http, {}, signal);
  if (!isSuperAdministratorArray(value)) {
    throw new Error('client.invalid_super_administrator_list');
  }

  return value;
}

/** 查询最近的超级管理员变更审计；页面固定只展示最近 50 条。 */
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

/** 授予超级管理员角色，需携带当前操作者密码与可选 TOTP 二次确认。 */
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

/** 撤销超级管理员角色，需携带当前操作者密码与可选 TOTP 二次确认。 */
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

/** 导出超级管理员列表、审计与变更结果模型，供列表页、审计面板与授予/撤销流程共享同一契约。 */
export type {
  SuperAdministrator,
  SuperAdministratorAudit,
  SuperAdministratorChangeResponse
};
