import { afterEach, describe, expect, it, vi } from 'vitest';
import { createIdentitySession } from '../js/core/session.js';
import { configureAuthentication } from '../js/core/http.js';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  configureAuthentication();
  document.cookie = 'fullnet-csrf=; Max-Age=0; Path=/';
});

describe('Layui 管理端会话', () => {
  it('登录后仅在内存保存令牌并按顺序加载授权快照', async () => {
    const fetchMock = createLoginFetch();
    vi.stubGlobal('fetch', fetchMock);
    const localStorageSet = vi.spyOn(Storage.prototype, 'setItem');
    const session = createIdentitySession();

    await session.login('admin', 'FullNet!2026Secure');

    expect(session.snapshot().state).toBe('authenticated');
    expect(session.snapshot().navigation).toHaveLength(2);
    expect(session.snapshot().availableTenants).toHaveLength(1);
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/auth/login',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    expect(localStorageSet).not.toHaveBeenCalled();
    session.dispose();
  });

  it('权限判断精确匹配完整编码', async () => {
    vi.stubGlobal('fetch', createLoginFetch());
    const session = createIdentitySession();
    await session.login('admin', 'FullNet!2026Secure');

    expect(session.can('tenancy.tenants.switch')).toBe(true);
    expect(session.can('Tenancy.Tenants.Switch')).toBe(false);
    session.dispose();
  });

  it('未知组件键拒绝整个快照并清空令牌', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('orphan-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse([{
        ...navigation()[0], componentKey: 'remote-script'
      }]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession();

    await expect(session.login('admin', 'FullNet!2026Secure')).rejects.toThrow(
      '导航响应不符合本地组件白名单'
    );
    await session.logout();

    expect(session.snapshot().state).toBe('anonymous');
    const [, logoutInit] = fetchMock.mock.calls[3];
    expect(new Headers(logoutInit.headers).has('authorization')).toBe(false);
    session.dispose();
  });

  it('成功切换后使用新令牌重载快照', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse(contextToken('tenant-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession();
    await session.login('admin', 'FullNet!2026Secure');

    await session.switchTenant(tenantId);

    expect(session.snapshot().currentUser.tenantId).toBe(tenantId);
    expect(session.snapshot().currentContextName).toBe('Acme Corporation');
    const [, meInit] = fetchMock.mock.calls[5];
    expect(new Headers(meInit.headers).get('authorization')).toBe(
      'Bearer tenant-token'
    );
    session.dispose();
  });

  it('切换失败保留旧上下文', async () => {
    const fetchMock = createLoginFetch();
    fetchMock.mockResolvedValueOnce(jsonResponse({
      status: 404,
      code: 'tenancy.context_not_found',
      title: '租户不存在'
    }, 404, 'application/problem+json'));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession();
    await session.login('admin', 'FullNet!2026Secure');

    await expect(session.switchTenant(tenantId)).rejects.toMatchObject({
      code: 'tenancy.context_not_found'
    });

    expect(session.snapshot().currentUser.tenantId).toBeNull();
    expect(session.snapshot().currentContextName).toBe('Full.NET Host');
    session.dispose();
  });

  it('并发冲突只在刷新最新会话后重试一次', async () => {
    const fetchMock = createLoginFetch();
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        status: 409,
        code: 'identity.session_context_conflict',
        title: '上下文冲突'
      }, 409, 'application/problem+json'))
      .mockResolvedValueOnce(jsonResponse(tokenResponse('refreshed-token')))
      .mockResolvedValueOnce(jsonResponse(contextToken('tenant-token')))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession();
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
    session.dispose();
  });

  it('恢复失败时进入匿名状态并清空授权快照', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({
      status: 401,
      code: 'identity.refresh_missing',
      title: '刷新会话不存在'
    }, 401, 'application/problem+json')));
    const session = createIdentitySession();

    await session.restore();

    expect(session.snapshot()).toMatchObject({
      state: 'anonymous',
      currentUser: undefined,
      navigation: [],
      availableTenants: []
    });
    session.dispose();
  });
});

function createLoginFetch() {
  return vi.fn()
    .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
    .mockResolvedValueOnce(jsonResponse(currentUser()))
    .mockResolvedValueOnce(jsonResponse(navigation()))
    .mockResolvedValueOnce(jsonResponse(tenants()));
}

function tokenResponse(accessToken) {
  return {
    accessToken, tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function contextToken(accessToken) {
  return {
    ...tokenResponse(accessToken),
    context: {
      tenantId, identifier: 'acme', name: 'Acme Corporation',
      scope: `tenant:${tenantId.replaceAll('-', '')}`
    }
  };
}

function currentUser(activeTenantId = null) {
  return {
    id: 'user-id', username: 'admin', displayName: '系统管理员',
    tenantId: activeTenantId, actorScope: 'host',
    scope: activeTenantId
      ? `tenant:${activeTenantId.replaceAll('-', '')}`
      : 'host',
    permissions: [
      'identity.navigation.read',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch'
    ],
    sessionId: 'session-id'
  };
}

function navigation() {
  return [navigationNode('overview'), navigationNode('tenant-context')];
}

function navigationNode(componentKey) {
  const tenant = componentKey === 'tenant-context';
  return {
    id: componentKey, parentId: null, routeName: componentKey,
    path: tenant ? '/tenant-context' : '/', componentKey,
    title: tenant ? '租户上下文' : '工作台',
    caption: tenant ? '进入租户或返回 Host' : '平台运行概览',
    icon: tenant ? 'building' : 'dashboard', order: tenant ? 20 : 10,
    requiredPermission: tenant
      ? 'tenancy.tenants.read'
      : 'platform.dashboard.read',
    children: []
  };
}

function tenants() {
  return [{
    id: tenantId, identifier: 'acme', name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}

function jsonResponse(value, status = 200, contentType = 'application/json') {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': contentType }
  });
}
