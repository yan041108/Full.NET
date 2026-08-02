import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { usePermission } from './permission';
import { useSessionStore } from './session';

const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';

describe('Vue 权限门组合函数', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('会话未恢复时拒绝所有权限码', () => {
    const { can } = usePermission();

    expect(can('identity.users.read')).toBe(false);
    expect(can('')).toBe(false);
  });

  it('仅当会话包含精确权限码时返回 true', () => {
    const session = useSessionStore();
    session.currentUser = authenticatedUser(['identity.users.reset_password']);
    const { can } = usePermission();

    expect(can('identity.users.reset_password')).toBe(true);
    expect(can('identity.users.create')).toBe(false);
    expect(can('Identity.Users.Reset_Password')).toBe(false);
  });

  it('权限撤销后会话更新时同步收敛', async () => {
    const session = useSessionStore();
    session.currentUser = authenticatedUser([
      'identity.users.read',
      'identity.users.reset_password'
    ]);
    const { can } = usePermission();

    expect(can('identity.users.reset_password')).toBe(true);

    session.currentUser = authenticatedUser(['identity.users.read']);

    expect(can('identity.users.reset_password')).toBe(false);
  });
});

function authenticatedUser(permissions: string[]) {
  return {
    id: userId,
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host' as const,
    scope: 'host' as const,
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN' as const,
    profileVersion: 1
  };
}