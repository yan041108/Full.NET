import type { TenantOwnerTransferRequest } from '@fullnet/client-contracts';

/** 跨窗口实例保留未知操作的非敏感证明；账号/会话变更清除旧凭据范围，不存密码或 TOTP。 */
export interface PendingOwnerTransfer extends Omit<TenantOwnerTransferRequest, 'currentPassword' | 'totpCode'> {
  tenantId: string; actorUserId: string; sourceUserId?: string | null; targetUserId: string;
}
const pending = new Map<string, PendingOwnerTransfer>();
export const ownerPendingKey = (user: string, session: string, tenant: string, target: string) =>
  JSON.stringify([user, session, tenant, target].map(value => value.toLowerCase()));
export const readPendingOwner = (key: string) => { const value = pending.get(key); return value ? { ...value } : undefined; };
export const forgetPendingOwner = (key: string) => { pending.delete(key); };
export function rememberPendingOwner(key: string, value: PendingOwnerTransfer): boolean {
  if (!pending.has(key) && pending.size >= 64) return false;
  // 显式逐项投影，即使调用者传入写请求也不会把凭据留在未知操作表中。
  pending.set(key, { operationId: value.operationId, sourceMemberId: value.sourceMemberId, sourceVersion: value.sourceVersion,
    targetMemberId: value.targetMemberId, targetVersion: value.targetVersion, tenantId: value.tenantId, actorUserId: value.actorUserId,
    sourceUserId: value.sourceUserId, targetUserId: value.targetUserId }); return true;
}
export function retainOwnerSession(user: string, session: string) {
  for (const key of pending.keys()) { const [actor, binding] = JSON.parse(key) as string[];
    if (actor !== user.toLowerCase() || binding !== session.toLowerCase()) pending.delete(key); }
}
