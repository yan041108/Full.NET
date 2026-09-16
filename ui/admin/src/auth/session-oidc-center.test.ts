import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from './session';

vi.mock('../api/http', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/http')>();
  return {
    ...actual,
    apiBaseUrl: 'http://localhost:5149'
  };
});

vi.mock('../config/identity-auth', () => ({
  adminIdentityAuthMode: 'oidc-center',
  resolveAdminOidcClientId: () => 'admin-spa'
}));

beforeEach(() => {
  setActivePinia(createPinia());
  useAdminI18n().setLocale('zh-CN');
  sessionStorage.clear();
});

afterEach(() => {
  configureAuthentication();
  sessionStorage.clear();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('oidc-center session logout', () => {
  it('revokes application session and skips legacy logout endpoint', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    }));
    const session = useSessionStore();
    await session.completeOidcAuthorization(tokenResponse('oidc-access-token'));
    expect(session.state).toBe('authenticated');

    await session.logout();

    expect(session.state).toBe('anonymous');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toBeNull();
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available',
      'http://localhost:5149/api/v1/identity/oidc/logout/application',
      'http://localhost:5149/api/v1/identity/oidc/logout'
    ]);
    const [, applicationLogoutInit] = fetchMock.mock.calls[3] as [string, RequestInit];
    expect(applicationLogoutInit.credentials).toBe('include');
    expect(applicationLogoutInit.method).toBe('POST');
    expect(applicationLogoutInit.body).toBe(JSON.stringify({ clientId: 'admin-spa' }));
    const [, centerLogoutInit] = fetchMock.mock.calls[4] as [string, RequestInit];
    expect(centerLogoutInit.credentials).toBe('include');
    expect(centerLogoutInit.method).toBe('POST');
  });
});

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

function currentUser() {
  return {
    id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
    passwordChangeRequired: false,
    permissions: ['tenancy.tenants.read'],
    sessionId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
}

function navigation() {
  return [{
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
}