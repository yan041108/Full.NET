import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { localeStorageKey } from '@fullnet/admin-i18n';
import { configureAuthentication, request } from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from './session';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

beforeEach(() => {
  setActivePinia(createPinia());
  useAdminI18n().setLocale('zh-CN');
});

afterEach(() => {
  configureAuthentication();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Vue 管理端会话', () => {
  it('登录后仅在内存保存令牌并按顺序加载授权快照', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const localStorageSet = vi.spyOn(Storage.prototype, 'setItem');

    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    expect(session.state).toBe('authenticated');
    expect(session.currentUser?.username).toBe('admin');
    expect(session.navigation).toHaveLength(2);
    expect(session.availableTenants).toHaveLength(1);
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/auth/login',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    expect(localStorageSet).toHaveBeenCalledWith(localeStorageKey, 'zh-CN');
    expect(localStorageSet.mock.calls.flat().join('|')).not.toContain('access-token');
    const [, meInit] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new Headers(meInit.headers).get('authorization')).toBe(
      'Bearer access-token'
    );
  });

  it('权限判断使用精确且区分大小写的权限码', async () => {
    vi.stubGlobal('fetch', createLoginFetch());
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    expect(session.can('tenancy.tenants.switch')).toBe(true);
    expect(session.can('Tenancy.Tenants.Switch')).toBe(false);
    expect(session.can('tenancy.tenants')).toBe(false);
  });

  it('完整认证快照通过守卫后才同步账号保存语言', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser(null, 'en-US', 7)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await session.login('admin', 'FullNet!2026Secure');

    expect(session.currentUser).toMatchObject({
      preferredLocale: 'en-US',
      profileVersion: 7
    });
    expect(useAdminI18n().locale.value).toBe('en-US');
  });

  it('认证用户仅在语言偏好响应通过守卫后提交语言与资料版本', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockResolvedValueOnce(jsonResponse({
      preferredLocale: 'en-US',
      profileVersion: 2
    }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.changeLocale('en-US');

    expect(session.currentUser).toMatchObject({
      tenantId: null,
      preferredLocale: 'en-US',
      profileVersion: 2
    });
    expect(useAdminI18n().locale.value).toBe('en-US');
    const [path, init] = fetchMock.mock.calls[4] as [string, RequestInit];
    expect(path).toBe('/api/v1/me/locale');
    expect(JSON.parse(String(init.body))).toEqual({
      locale: 'en-US',
      profileVersion: 1
    });
  });

  it('语言偏好保存失败时保留会话、租户、资料版本与原语言', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockResolvedValueOnce(jsonResponse({
      status: 409,
      code: 'identity.profile_version_conflict',
      title: '资料版本冲突'
    }, 409, 'application/problem+json'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await expect(session.changeLocale('en-US')).rejects.toMatchObject({
      code: 'identity.profile_version_conflict'
    });

    expect(session.state).toBe('authenticated');
    expect(session.currentUser).toMatchObject({
      tenantId: null,
      preferredLocale: 'zh-CN',
      profileVersion: 1
    });
    expect(useAdminI18n().locale.value).toBe('zh-CN');
  });

  it('损坏语言偏好响应按契约错误处理且不覆盖旧快照', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockResolvedValueOnce(jsonResponse({
      preferredLocale: 'en-US',
      profileVersion: 0
    }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await expect(session.changeLocale('en-US')).rejects.toThrow(
      '语言偏好响应不符合契约'
    );
    expect(session.currentUser).toMatchObject({
      preferredLocale: 'zh-CN', profileVersion: 1
    });
    expect(useAdminI18n().locale.value).toBe('zh-CN');
  });

  it('匿名选择只更新本地语言而不调用偏好接口', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    session.$patch({ state: 'anonymous' });

    await session.changeLocale('en-US');

    expect(useAdminI18n().locale.value).toBe('en-US');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('未知本地组件键使整个授权快照失败并清空令牌', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('orphan-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse([{
        ...navigation()[0],
        componentKey: 'remote-script'
      }]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await expect(session.login('admin', 'FullNet!2026Secure')).rejects.toThrow(
      '导航响应不符合本地组件白名单'
    );
    await session.logout();

    expect(session.state).toBe('anonymous');
    expect(session.navigation).toEqual([]);
    const [, logoutInit] = fetchMock.mock.calls[3] as [string, RequestInit];
    expect(new Headers(logoutInit.headers).has('authorization')).toBe(false);
  });

  it('成功切换先替换令牌，再使用新令牌重载授权快照', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        ...tokenResponse('tenant-token'),
        context: {
          tenantId,
          identifier: 'acme',
          name: 'Acme Corporation',
          scope: `tenant:${tenantId.replaceAll('-', '')}`
        }
      }))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.switchTenant(tenantId);

    expect(session.currentUser?.tenantId).toBe(tenantId);
    expect(session.currentContextName).toBe('Acme Corporation');
    const [, reloadMeInit] = fetchMock.mock.calls[5] as [string, RequestInit];
    expect(new Headers(reloadMeInit.headers).get('authorization')).toBe(
      'Bearer tenant-token'
    );
  });

  it('服务端完成切换但新授权快照加载失败时清空不一致会话', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        ...tokenResponse('tenant-token'),
        context: {
          tenantId,
          identifier: 'acme',
          name: 'Acme Corporation',
          scope: `tenant:${tenantId.replaceAll('-', '')}`
        }
      }))
      .mockResolvedValueOnce(jsonResponse({
        status: 500,
        code: 'server.error',
        title: '服务端错误'
      }, 500, 'application/problem+json'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.switchTenant(tenantId);

    expect(session.state).toBe('anonymous');
    expect(session.currentUser).toBeUndefined();
    expect(session.navigation).toEqual([]);
    expect(session.readAccessToken()).toBeUndefined();
  });

  it('切换失败保留旧令牌与旧上下文', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockResolvedValueOnce(jsonResponse({
      status: 404,
      code: 'tenancy.context_not_found',
      title: '租户上下文不存在'
    }, 404, 'application/problem+json'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await expect(session.switchTenant(tenantId)).rejects.toMatchObject({
      code: 'tenancy.context_not_found'
    });

    expect(session.currentUser?.tenantId).toBeNull();
    expect(session.currentContextName).toBe('Full.NET Host');
  });

  it('并发冲突时刷新最新会话并且只重试一次', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        status: 409,
        code: 'identity.session_context_conflict',
        title: '上下文切换冲突'
      }, 409, 'application/problem+json'))
      .mockResolvedValueOnce(jsonResponse(tokenResponse('refreshed-token')))
      .mockResolvedValueOnce(jsonResponse({
        ...tokenResponse('tenant-token'),
        context: {
          tenantId,
          identifier: 'acme',
          name: 'Acme Corporation',
          scope: `tenant:${tenantId.replaceAll('-', '')}`
        }
      }))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.switchTenant(tenantId);

    expect(fetchMock.mock.calls.slice(4).map(call => call[0])).toEqual([
      '/api/v1/tenancy/context',
      '/api/v1/auth/refresh',
      '/api/v1/tenancy/context',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    const [, retryInit] = fetchMock.mock.calls[6] as [string, RequestInit];
    expect(new Headers(retryInit.headers).get('authorization')).toBe(
      'Bearer refreshed-token'
    );
  });

  it('并发刷新后的唯一重试失败时清空不一致授权快照', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        status: 409,
        code: 'identity.session_context_conflict',
        title: '上下文切换冲突'
      }, 409, 'application/problem+json'))
      .mockResolvedValueOnce(jsonResponse(tokenResponse('refreshed-token')))
      .mockResolvedValueOnce(jsonResponse({
        status: 404,
        code: 'tenancy.context_not_found',
        title: '租户上下文不存在'
      }, 404, 'application/problem+json'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await expect(session.switchTenant(tenantId)).rejects.toMatchObject({
      code: 'tenancy.context_not_found'
    });

    expect(session.state).toBe('anonymous');
    expect(session.currentUser).toBeUndefined();
    expect(session.navigation).toEqual([]);
  });

  it('退出无论服务端结果如何都清理内存授权状态', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockRejectedValueOnce(new TypeError('offline'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.logout();

    expect(session.state).toBe('anonymous');
    expect(session.currentUser).toBeUndefined();
    expect(session.navigation).toEqual([]);
    expect(session.availableTenants).toEqual([]);
  });

  it('退出后拒绝较晚返回的在途刷新结果', async () => {
    let resolveRefresh!: (response: Response) => void;
    const refreshResponse = new Promise<Response>(resolve => {
      resolveRefresh = resolve;
    });
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        status: 401,
        code: 'identity.session_expired',
        title: '访问令牌已过期'
      }, 401, 'application/problem+json'))
      .mockReturnValueOnce(refreshResponse)
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    const protectedOutcome = request<void>('/api/v1/protected')
      .then(() => undefined, error => error);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(6));
    await session.logout();
    resolveRefresh(jsonResponse(tokenResponse('stale-refreshed-token')));

    await expect(protectedOutcome).resolves.toMatchObject({
      code: 'identity.session_expired'
    });
    await request<void>('/api/v1/probe', {}, undefined, {
      retryUnauthorized: false
    });
    const probeCall = fetchMock.mock.calls.find(call => call[0] === '/api/v1/probe');
    expect(probeCall).toBeDefined();
    expect(new Headers(probeCall?.[1]?.headers).has('authorization')).toBe(false);
  });

  it('退出后忽略较晚返回的租户切换结果', async () => {
    let resolveContext!: (response: Response) => void;
    const contextResponse = new Promise<Response>(resolve => {
      resolveContext = resolve;
    });
    const fetchMock = createLoginFetch();
    fetchMock
      .mockReturnValueOnce(contextResponse)
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    const switchPromise = session.switchTenant(tenantId);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(5));
    await session.logout();
    resolveContext(jsonResponse({
      ...tokenResponse('stale-tenant-token'),
      context: {
        tenantId,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: `tenant:${tenantId.replaceAll('-', '')}`
      }
    }));
    await switchPromise;

    expect(session.state).toBe('anonymous');
    expect(fetchMock.mock.calls.filter(call => call[0] === '/api/v1/me')).toHaveLength(1);
  });
});

function createLoginFetch() {
  return vi.fn()
    .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
    .mockResolvedValueOnce(jsonResponse(currentUser()))
    .mockResolvedValueOnce(jsonResponse(navigation()))
    .mockResolvedValueOnce(jsonResponse(tenants()));
}

function tokenResponse(accessToken: string) {
  return {
    accessToken,
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUser(
  activeTenantId: string | null = null,
  preferredLocale: 'zh-CN' | 'en-US' = 'zh-CN',
  profileVersion = 1
) {
  return {
    id: 'user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: activeTenantId,
    actorScope: 'host',
    isSuperAdministrator: true,
    scope: activeTenantId
      ? `tenant:${activeTenantId.replaceAll('-', '')}`
      : 'host',
    permissions: [
      'identity.navigation.read',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch'
    ],
    sessionId: 'session-id',
    preferredLocale,
    profileVersion
  };
}

function navigation() {
  return [
    {
      id: 'overview', parentId: null, routeName: 'overview', path: '/',
      componentKey: 'overview', title: '工作台', caption: '平台运行概览',
      icon: 'dashboard', order: 10,
      requiredPermission: 'platform.dashboard.read', children: []
    },
    {
      id: 'tenant-context', parentId: null, routeName: 'tenant-context',
      path: '/tenant-context', componentKey: 'tenant-context',
      title: '租户上下文', caption: '进入租户或返回 Host',
      icon: 'building', order: 20,
      requiredPermission: 'tenancy.tenants.read', children: []
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

function jsonResponse(
  body: unknown,
  status = 200,
  contentType = 'application/json'
) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': contentType }
  });
}
