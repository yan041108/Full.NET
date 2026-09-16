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

describe('oidc-center session restore', () => {
  it('restores authenticated session from persisted refresh credential', async () => {
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'stored-refresh-token',
      clientId: 'admin-spa'
    }));
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenEndpointResponse({
        access_token: 'restored-access-token',
        token_type: 'Bearer',
        expires_in: 120,
        refresh_token: 'rotated-refresh-token'
      }))
      .mockResolvedValueOnce(jsonResponse(currentUser()))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await session.restore();

    expect(session.state).toBe('authenticated');
    expect(session.currentUser?.username).toBe('admin');
    expect(fetchMock.mock.calls.map(call => call[0])).toEqual([
      'http://localhost:5149/connect/token',
      '/api/v1/me',
      '/api/v1/navigation',
      '/api/v1/tenancy/available'
    ]);
    const [, meInit] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new Headers(meInit.headers).get('authorization')).toBe(
      'Bearer restored-access-token'
    );
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toContain('rotated-refresh-token');
  });

  it('serializes concurrent restore refresh so token exchange never overlaps', async () => {
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'stored-refresh-token',
      clientId: 'admin-spa'
    }));
    let inFlight = 0;
    let maxInFlight = 0;
    const fetchMock = vi.fn(async (url: string | URL) => {
      const href = String(url);
      if (href.includes('/connect/token')) {
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);
        await new Promise(resolve => setTimeout(resolve, 20));
        inFlight -= 1;
        return tokenEndpointResponse({
          access_token: 'restored-access-token',
          token_type: 'Bearer',
          expires_in: 120,
          refresh_token: 'rotated-refresh-token'
        });
      }

      if (href.includes('/api/v1/me')) {
        return jsonResponse(currentUser());
      }

      if (href.includes('/api/v1/navigation')) {
        return jsonResponse(navigation());
      }

      if (href.includes('/api/v1/tenancy/available')) {
        return jsonResponse([]);
      }

      throw new Error(`unexpected fetch ${href}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await Promise.all([session.restore(), session.restore()]);

    expect(session.state).toBe('authenticated');
    expect(maxInFlight).toBe(1);
    expect(
      fetchMock.mock.calls.filter(call => String(call[0]).includes('/connect/token')).length
    ).toBeGreaterThanOrEqual(1);
  });

  it('stays anonymous and clears credentials when refresh fails', async () => {
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'expired-refresh-token',
      clientId: 'admin-spa'
    }));
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({
      error: 'invalid_grant'
    }), {
      status: 400,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await session.restore();

    expect(session.state).toBe('anonymous');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toBeNull();
    expect(fetchMock).toHaveBeenCalledOnce();
  });
});

function tokenEndpointResponse(body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' }
  });
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' }
  });
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