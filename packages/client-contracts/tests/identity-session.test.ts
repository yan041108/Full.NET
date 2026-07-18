import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';
import { createIdentitySession } from '../src/identity-session';

const localeStorageKey = 'fullnet.admin.locale';
const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('headless 身份会话', () => {
  it('登录后仅在内存保存令牌并按顺序加载授权快照', async () => {
    const fetchMock = createLoginFetch();
    vi.stubGlobal('fetch', fetchMock);
    const storage = createMemoryStorage();
    const session = createTestSession(storage);

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
    expect(storage.getItem(localeStorageKey)).toBe('zh-CN');
    session.dispose();
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
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUser(
  tenantIdValue: string | null = null,
  preferredLocale: 'zh-CN' | 'en-US' = 'zh-CN',
  profileVersion = 1
) {
  return {
    id: 'user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: tenantIdValue,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
    permissions: ['tenancy.tenants.switch', 'tenancy.tenants.read'],
    sessionId: 'session-id',
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
