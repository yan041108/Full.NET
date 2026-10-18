import type { CurrentUserResponse } from '@fullnet/client-contracts';

/** 按正式会话协议匹配当前租户；actorScope 保留账号归属，不能代表切换后的有效作用域。 */
export function isTenantSessionContext(user: Pick<CurrentUserResponse, 'tenantId' | 'scope'> | undefined): boolean {
  if (!user?.tenantId) return false;
  return user.scope === `tenant:${user.tenantId.replaceAll('-', '').toLowerCase()}`;
}
