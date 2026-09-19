import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from './session';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

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

describe('oidc-center session tenant switch', () => {
  it('switches tenant context and reloads authorization snapshot with new token', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()))
      .mockResolvedValueOnce(jsonResponse({
        ...tokenResponse('tenant-oidc-token'),
        refreshToken: 'rotated-refresh-token',
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
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    }));
    const session = useSessionStore();
    await session.completeOidcAuthorization(tokenResponse('oidc-host-token'));
    expect(session.currentUser?.tenantId).toBeNull();

    await session.switchTenant(tenantId);

    expect(session.state).toBe('authenticated');
    expect(session.currentUser?.tenantId).toBe(tenantId);
    expect(session.currentContextName).toBe('Acme Corporation');
    expect(session.readAccessToken()).toBe('tenant-oidc-token');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toContain('rotated-refresh-token');
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available',
      '/api/v1/tenancy/context',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    const [, switchInit] = fetchMock.mock.calls[3] as [string, RequestInit];
    expect(new Headers(switchInit.headers).get('authorization')).toBe(
      'Bearer oidc-host-token'
    );
    const [, reloadMeInit] = fetchMock.mock.calls[4] as [string, RequestInit];
    expect(new Headers(reloadMeInit.headers).get('authorization')).toBe(
      'Bearer tenant-oidc-token'
    );
  });

  it('refreshes and retries once when tenant switch conflicts', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()))
      .mockResolvedValueOnce(jsonResponse({
        status: 409,
        code: 'identity.session_context_conflict',
        title: '会话上下文已变化'
      }, 409, 'application/problem+json'))
      .mockResolvedValueOnce(jsonResponse({
        access_token: 'refreshed-host-token', token_type: 'Bearer', expires_in: 300,
        refresh_token: 'refreshed-credential'
      }))
      .mockResolvedValueOnce(jsonResponse({
        ...tokenResponse('retried-tenant-token'),
        refreshToken: 'retried-refresh-token',
        context: { tenantId, identifier: 'acme', name: 'Acme Corporation',
          scope: `tenant:${tenantId.replaceAll('-', '')}` }
      }))
      .mockResolvedValueOnce(jsonResponse(currentUser(tenantId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse(tenants()));
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    }));
    const session = useSessionStore();
    await session.completeOidcAuthorization(tokenResponse('oidc-host-token'));

    await session.switchTenant(tenantId);

    expect(session.state).toBe('authenticated');
    expect(session.currentUser?.tenantId).toBe(tenantId);
    expect(session.readAccessToken()).toBe('retried-tenant-token');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toContain('retried-refresh-token');
    expect(fetchMock.mock.calls.filter(call => call[0] === '/api/v1/tenancy/context')).toHaveLength(2);
    const [, retryInit] = fetchMock.mock.calls[5] as [string, RequestInit];
    expect(new Headers(retryInit.headers).get('authorization')).toBe('Bearer refreshed-host-token');
  });
});

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

function tokenResponse(accessToken: string) {
  return {
    accessToken,
    tokenType: 'Bearer' as const,
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUser(activeTenantId: string | null = null) {
  return {
    id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: activeTenantId,
    actorScope: 'host',
    scope: activeTenantId
      ? `tenant:${activeTenantId.replaceAll('-', '')}`
      : 'host',
    isSuperAdministrator: true,
    passwordChangeRequired: false,
    permissions: ['tenancy.tenants.read', 'tenancy.tenants.switch'],
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

function tenants() {
  return [{
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}
