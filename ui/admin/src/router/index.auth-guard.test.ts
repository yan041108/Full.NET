import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import { createAppRouter } from './index';
import { useSessionStore } from '../auth/session';

const overviewNavigation = [{
  id: 'overview',
  parentId: null,
  routeName: 'overview',
  path: '/',
  componentKey: 'overview',
  title: '工作台',
  caption: '平台运行概览',
  icon: 'dashboard',
  order: 10,
  requiredPermission: 'platform.dashboard.read',
  children: []
}];

function createSessionUser(passwordChangeRequired = false) {
  return {
    id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
    passwordChangeRequired,
    permissions: ['platform.dashboard.read'],
    sessionId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61',
    preferredLocale: 'zh-CN' as const,
    profileVersion: 1
  };
}

describe('Vue 管理端路由守卫', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('未认证用户不受导航目录守卫拦截', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const router = createAppRouter(createMemoryHistory(), pinia);

    await router.push('/identity/users');
    await router.isReady();

    expect(router.currentRoute.value.path).toBe('/identity/users');
  });

  it('已认证用户可进入 OIDC 回调页而不依赖导航目录', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({
      state: 'authenticated',
      currentUser: createSessionUser(),
      navigation: overviewNavigation
    });
    const router = createAppRouter(createMemoryHistory(), pinia);

    await router.push('/identity/oidc/callback');
    await router.isReady();

    expect(router.currentRoute.value.path).toBe('/identity/oidc/callback');
    expect(router.currentRoute.value.name).toBe('oidc-callback');
  });

  it('已认证用户可进入 OAuth 回调页而不依赖导航目录', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({
      state: 'authenticated',
      currentUser: createSessionUser(),
      navigation: overviewNavigation
    });
    const router = createAppRouter(createMemoryHistory(), pinia);

    await router.push('/oauth/callback');
    await router.isReady();

    expect(router.currentRoute.value.path).toBe('/oauth/callback');
  });

  it('已认证用户访问未下发导航路径时重定向到 403', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({
      state: 'authenticated',
      currentUser: createSessionUser(),
      navigation: overviewNavigation
    });
    const router = createAppRouter(createMemoryHistory(), pinia);

    await router.push('/identity/users');
    await router.isReady();

    expect(router.currentRoute.value.path).toBe('/403');
  });

  it('要求改密时强制进入账户安全页', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().$patch({
      state: 'authenticated',
      currentUser: createSessionUser(true),
      navigation: overviewNavigation
    });
    const router = createAppRouter(createMemoryHistory(), pinia);

    await router.push('/');
    await router.isReady();

    expect(router.currentRoute.value.path).toBe('/account/security');
    expect(router.currentRoute.value.query.forced).toBe('1');
  });
});