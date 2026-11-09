import { beforeEach, describe, expect, it, vi } from 'vitest';
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

  it.each([
    ['edit', ['goview.projects.read', 'goview.projects.update'], true, true],
    ['preview', ['goview.projects.read', 'goview.projects.preview'], true, true],
    ['edit', ['goview.projects.read'], true, false],
    ['preview', ['goview.projects.read', 'goview.projects.update'], true, false],
    ['edit', ['goview.projects.update'], true, false],
    ['edit', ['goview.projects.read', 'goview.projects.update'], false, false],
    ['preview', ['goview.projects.read', 'goview.projects.preview'], false, false]
  ] as const)('大屏 %s 必须同时满足父菜单、读取及操作权限：%j / %s', async (action, permissions, hasMenu, allowed) => {
    const pinia = createPinia(); setActivePinia(pinia);
    const parent = { ...overviewNavigation[0]!, componentKey: 'goview-projects', routeName: 'goview-projects',
      path: '/goview/projects', requiredPermission: 'goview.projects.read' };
    useSessionStore().$patch({ state: 'authenticated', currentUser: { ...createSessionUser(), isSuperAdministrator: false,
      permissions: [...permissions] }, navigation: hasMenu ? [parent] : [] });
    const router = createAppRouter(createMemoryHistory(), pinia);
    const target = `/goview/projects/01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60/${action}`;
    await router.push(target); await router.isReady();
    expect(router.currentRoute.value.path).toBe(allowed ? target : '/403');
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

  it.each([
    { role: 'Host 只读', tenant: false, read: true, manage: false, switching: false, allowed: true },
    { role: 'Host 运营', tenant: false, read: true, manage: true, switching: false, allowed: true },
    { role: 'Host 缺少订阅读取', tenant: false, read: false, manage: false, switching: false, allowed: false },
    { role: '企业上下文即使残留 Host 导航', tenant: true, read: true, manage: false, switching: false, allowed: false },
    { role: '正在切换上下文', tenant: false, read: true, manage: false, switching: true, allowed: false }
  ])('旧订阅地址按当前上下文与精确权限交给企业管理 $role', async scenario => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const tenantId = scenario.tenant ? '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f62' : null;
    const permissions = ['tenancy.tenants.read'];
    if (scenario.read) permissions.push('tenancy.tenant_subscriptions.read');
    if (scenario.manage) permissions.push('tenancy.tenant_subscriptions.manage');
    useSessionStore().$patch({
      state: 'authenticated',
      switching: scenario.switching,
      currentUser: { ...createSessionUser(), isSuperAdministrator: false, tenantId, permissions },
      navigation: [{
        ...overviewNavigation[0]!,
        id: 'tenants',
        routeName: 'tenant-management',
        path: '/tenants',
        componentKey: 'tenants',
        requiredPermission: 'tenancy.tenants.read'
      }]
    });
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/tenancy/tenant-subscriptions');
    await router.isReady();
    expect(router.currentRoute.value.path).toBe(scenario.allowed ? '/tenants' : '/403');
  });

  it('冷启动直达旧订阅地址先恢复会话，再拒绝没有订阅读取权限的 Host', async () => {
    const pinia = createPinia();
    const session = useSessionStore(pinia);
    const restore = vi.spyOn(session, 'restore').mockImplementation(async () => {
      session.$patch({
        state: 'authenticated',
        currentUser: { ...createSessionUser(), permissions: ['tenancy.tenants.read'] },
        navigation: [{ ...overviewNavigation[0]!, path: '/tenants', routeName: 'tenant-management' }]
      });
      return true;
    });
    const router = createAppRouter(createMemoryHistory(), pinia);
    await router.push('/tenancy/tenant-subscriptions');
    await router.isReady();
    expect(restore).toHaveBeenCalledOnce();
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

  it('没有工作台权限的普通成员回到根路径时进入已授权页面', async () => {
    const pinia = createPinia(); setActivePinia(pinia);
    const report = { ...overviewNavigation[0]!, id: 'reporting-export-tasks', routeName: 'reporting-export-tasks',
      path: '/reporting/export-tasks', componentKey: 'reporting-export-tasks', requiredPermission: 'reporting.export_tasks.read' };
    useSessionStore().$patch({ state: 'authenticated', currentUser: { ...createSessionUser(), isSuperAdministrator: false,
      tenantId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f62', permissions: ['reporting.export_tasks.read'] }, navigation: [report] });
    const router = createAppRouter(createMemoryHistory(), pinia); await router.push('/'); await router.isReady();
    expect(router.currentRoute.value.path).toBe('/reporting/export-tasks');
  });

  it('默认落点跳过隐藏、外链及不在本地白名单的导航', async () => {
    const pinia = createPinia(); setActivePinia(pinia);
    const report = { ...overviewNavigation[0]!, id: 'reporting-export-tasks', routeName: 'reporting-export-tasks',
      path: '/reporting/export-tasks', componentKey: 'reporting-export-tasks', requiredPermission: 'reporting.export_tasks.read' };
    const navigation = [{ ...report, id: 'hidden', isHidden: true }, { ...report, id: 'external', linkUrl: 'https://example.com' },
      { ...report, id: 'unknown', routeName: 'unknown', path: '/unknown' }, report];
    useSessionStore().$patch({ state: 'authenticated', currentUser: { ...createSessionUser(), isSuperAdministrator: false,
      permissions: ['reporting.export_tasks.read'] }, navigation });
    const router = createAppRouter(createMemoryHistory(), pinia); await router.push('/'); await router.isReady();
    expect(router.currentRoute.value.path).toBe('/reporting/export-tasks');
  });

  it('没有任何已授权本地页面时保留 403，不产生重定向循环', async () => {
    const pinia = createPinia(); setActivePinia(pinia);
    useSessionStore().$patch({ state: 'authenticated', currentUser: createSessionUser(), navigation: [] });
    const router = createAppRouter(createMemoryHistory(), pinia); await router.push('/'); await router.isReady();
    expect(router.currentRoute.value.path).toBe('/403');
  });
});
