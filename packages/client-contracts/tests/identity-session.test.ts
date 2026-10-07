import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';
import { createIdentitySession } from '../src/identity-session';

const localeStorageKey = 'fullnet.admin.locale';
const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

beforeEach(() => { vi.stubGlobal('document', { cookie: '' }); });

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('headless 身份会话', () => {
  it.each(['login', 'oidc'] as const)('取消 %s 后迟到快照不能建立会话', async flow => {
    let finish!: (value: Response) => void;
    const fetchMock = vi.fn().mockImplementation(() => new Promise<Response>(resolve => { finish = resolve; }));
    if (flow === 'login') fetchMock.mockResolvedValueOnce(jsonResponse(tokenResponse('pending-access')));
    vi.stubGlobal('fetch', fetchMock);
    const session = createTestSession(); const controller = new AbortController();
    const pending = flow === 'login'
      ? session.login('admin', 'Password!123', controller.signal)
      : session.completeOidcAuthorization(tokenResponse('pending-access'), controller.signal);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(flow === 'login' ? 2 : 1));
    controller.abort(); finish(jsonResponse({ ...currentUser(), passwordChangeRequired: true })); await pending;
    expect(session.snapshot().state).toBe('anonymous'); expect(session.readAccessToken()).toBeUndefined(); session.dispose();
  });

  it('导航尚未通过守卫时不提前暴露用户权限或切换语言', async () => {
    let finish!: (value: Response) => void;
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser(null, 'en-US')))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock); const storage = createMemoryStorage(); const session = createTestSession(storage);
    const pending = session.login('admin', 'Password!123');
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    expect(session.snapshot().currentUser).toBeUndefined(); expect(session.can('tenancy.tenants.read')).toBe(false);
    expect(storage.getItem(localeStorageKey)).not.toBe('en-US');
    finish(jsonResponse(navigation())); await pending; expect(session.snapshot().state).toBe('authenticated'); session.dispose();
  });

  it('成功通知触发页面取消不能撤销已确认会话', async () => {
    vi.stubGlobal('fetch', createLoginFetch()); const session = createTestSession(); const controller = new AbortController();
    session.subscribe(snapshot => { if (snapshot.state === 'authenticated') controller.abort(); });
    await session.login('admin', 'Password!123', controller.signal);
    expect(session.snapshot().state).toBe('authenticated'); expect(session.readAccessToken()).toBe('access-token'); session.dispose();
  });

  it('快照返回与认证提交之间取消仍保持匿名', async () => {
    const controller = new AbortController();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(jsonResponse({ ...currentUser(), passwordChangeRequired: true })));
    const session = createIdentitySession({ http: createHttpClient(), i18n: { getLocale: () => 'zh-CN', setLocale: () => { queueMicrotask(() => controller.abort()); } }, isSupportedNavigationTree: () => true });
    await session.completeOidcAuthorization(tokenResponse('pending-access'), controller.signal);
    expect(session.snapshot().state).toBe('anonymous'); expect(session.readAccessToken()).toBeUndefined(); session.dispose();
  });

  it.each(['restore', 'switch'] as const)('%s 快照提交前注销不能重建假认证', async flow => {
    const fetchMock = createLoginFetch(); vi.stubGlobal('fetch', fetchMock); let invalidate = false;
    const session = createIdentitySession({ http: createHttpClient(), i18n: { getLocale: () => 'zh-CN', setLocale: () => { if (invalidate) queueMicrotask(() => session.invalidateLocalSession()); } }, isSupportedNavigationTree: () => true });
    await session.login('admin', 'Password!123'); invalidate = true;
    fetchMock.mockResolvedValueOnce(jsonResponse({ ...tokenResponse('refreshed-access'), context: { tenantId: null, identifier: 'host', scope: 'host', name: 'Full.NET Host' } }))
      .mockResolvedValueOnce(jsonResponse({ ...currentUser(), passwordChangeRequired: true }));
    if (flow === 'restore') expect(await session.restore()).toBe(false); else await session.switchTenant(null);
    expect(session.snapshot().state).toBe('anonymous'); expect(session.readAccessToken()).toBeUndefined(); session.dispose();
  });

  it('旧认证取消不能清理后来建立的会话', async () => {
    let finish!: (value: Response) => void;
    const fetchMock = vi.fn().mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }));
    vi.stubGlobal('fetch', fetchMock); const session = createTestSession(); const controller = new AbortController();
    const old = session.login('old', 'Password!123', controller.signal);
    fetchMock.mockResolvedValueOnce(jsonResponse(tokenResponse('new-access'))).mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation())).mockResolvedValueOnce(jsonResponse(tenants()));
    await session.login('new', 'Password!123'); controller.abort(); finish(jsonResponse(tokenResponse('old-access'))); await old;
    expect(session.snapshot().state).toBe('authenticated'); expect(session.readAccessToken()).toBe('new-access'); session.dispose();
  });

  it('登录后仅在内存保存令牌并按顺序加载授权快照', async () => {
    const fetchMock = createLoginFetch();
    vi.stubGlobal('fetch', fetchMock);
    const storage = createMemoryStorage();
    const session = createTestSession(storage);

    await session.login('admin', 'FullNet!2026Secure');

    expect(session.snapshot().state).toBe('authenticated');
    expect(session.readAccessToken()).toBe('access-token');
    expect(session.snapshot().navigation).toHaveLength(2);
    expect(session.snapshot().availableTenants).toHaveLength(1);
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/auth/login',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    expect(storage.getItem(localeStorageKey)).toBe('zh-CN');
    session.dispose();
    expect(session.readAccessToken()).toBeUndefined();
  });


  it('OIDC 授权码兑换令牌后可建立认证会话', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = createTestSession();

    await session.completeOidcAuthorization(tokenResponse('oidc-access-token'));

    expect(session.snapshot().state).toBe('authenticated');
    expect(session.readAccessToken()).toBe('oidc-access-token');
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    session.dispose();
  });

  it('外部刷新路径可在 restore 时重建认证会话', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession({
      http: createHttpClient(),
      i18n: {
        getLocale: () => 'zh-CN',
        setLocale: () => undefined
      },
      isSupportedNavigationTree: createAdminNavigationCatalog().isSupportedNavigationTree,
      externalRefreshAccessToken: async () => tokenResponse('refreshed-oidc-token')
    });

    const restored = await session.restore();

    expect(restored).toBe(true);
    expect(session.snapshot().state).toBe('authenticated');
    expect(session.readAccessToken()).toBe('refreshed-oidc-token');
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    session.dispose();
  });

  it('confirms an applied password change only after loading the rotated session', async () => {
    const fetchMock = createLoginFetch(); vi.stubGlobal('fetch', fetchMock); const session = createTestSession(); await session.login('admin', 'FullNet!2026Secure');
    fetchMock.mockResolvedValueOnce(jsonResponse(tokenResponse('rotated-token'))).mockResolvedValueOnce(jsonResponse({ ...currentUser(), sessionId: tenantId })).mockResolvedValueOnce(jsonResponse(navigation())).mockResolvedValueOnce(jsonResponse(tenants()));
    expect(await session.changePassword('Current!Password123', 'Changed!Password123')).toBe(true);
    expect(session.snapshot().currentUser?.sessionId).toBe(tenantId); session.dispose();
  });
  it('does not confirm a password result arriving after session disposal', async () => {
    const fetchMock = createLoginFetch(); vi.stubGlobal('fetch', fetchMock); const session = createTestSession(); await session.login('admin', 'FullNet!2026Secure');
    let resolve!: (value: Response) => void; const pending = new Promise<Response>(r => { resolve = r; }); fetchMock.mockReturnValueOnce(pending);
    const change = session.changePassword('Current!Password123', 'Changed!Password123'); session.dispose(); resolve(jsonResponse(tokenResponse('old-token')));
    expect(await change).toBe(false); expect(session.readAccessToken()).toBeUndefined();
  });
  it('does not confirm a password change when its authenticated context cannot reload', async () => {
    const fetchMock = createLoginFetch(); vi.stubGlobal('fetch', fetchMock); const session = createTestSession(); await session.login('admin', 'FullNet!2026Secure');
    fetchMock.mockResolvedValueOnce(jsonResponse(tokenResponse('rotated-token'))).mockResolvedValueOnce(jsonResponse({ invalid: true }));
    expect(await session.changePassword('Current!Password123', 'Changed!Password123')).toBe(false); expect(session.snapshot().state).toBe('anonymous'); session.dispose();
  });
  it('权限判断精确匹配完整编码', async () => {
    vi.stubGlobal('fetch', createLoginFetch());
    const session = createTestSession();
    await session.login('admin', 'FullNet!2026Secure');

    expect(session.can('tenancy.tenants.switch')).toBe(true);
    expect(session.can('Tenancy.Tenants.Switch')).toBe(false);
    session.dispose();
  });
});

function createTestSession(storage = createMemoryStorage()) {
  const http = createHttpClient();
  const catalog = createAdminNavigationCatalog();
  let locale: 'zh-CN' | 'en-US' = 'zh-CN';
  return createIdentitySession({
    http,
    i18n: {
      getLocale: () => locale,
      setLocale: (value) => {
        locale = value;
        storage.setItem(localeStorageKey, value);
      }
    },
    isSupportedNavigationTree: catalog.isSupportedNavigationTree
  });
}

function createLoginFetch() {
  return vi.fn()
    .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
    .mockResolvedValueOnce(jsonResponse(currentUser()))
    .mockResolvedValueOnce(jsonResponse(navigation()))
    .mockResolvedValueOnce(jsonResponse(tenants()));
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' }
  });
}

function tokenResponse(accessToken: string) {
  return {
    accessToken,
    tokenType: 'Bearer' as const,
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUser(
  tenantIdValue: string | null = null,
  preferredLocale: 'zh-CN' | 'en-US' = 'zh-CN',
  profileVersion = 1
) {
  return {
    id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: tenantIdValue,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
      passwordChangeRequired: false,
    permissions: ['tenancy.tenants.switch', 'tenancy.tenants.read'],
    sessionId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61',
    preferredLocale,
    profileVersion
  };
}

function navigation() {
  return [
    {
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
    },
    {
      id: 'tenant-context',
      parentId: null,
      routeName: 'tenant-context',
      path: '/tenant-context',
      componentKey: 'tenant-context',
      title: '租户上下文',
      caption: '进入租户或返回 Host',
      icon: 'building',
      order: 20,
      requiredPermission: 'tenancy.tenants.read',
      children: []
    }
  ];
}

function tenants() {
  return [{
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}

function createMemoryStorage() {
  const values = new Map<string, string>();
  return {
    getItem(key: string) {
      return values.get(key) ?? null;
    },
    setItem(key: string, value: string) {
      values.set(key, value);
    }
  };
}
