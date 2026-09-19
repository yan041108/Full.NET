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

const sessionId = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61';

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

describe('oidc-center remote session revoke', () => {
  it('clears refresh credentials when revoked session matches current user', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser(sessionId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    }));
    const session = useSessionStore();
    await session.completeOidcAuthorization(tokenResponse('oidc-access-token'));
    expect(session.state).toBe('authenticated');

    const handled = session.handleRemoteSessionRevoke(sessionId.toUpperCase());

    expect(handled).toBe(true);
    expect(session.state).toBe('anonymous');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toBeNull();
  });

  it('ignores forged revoke notifications for another session', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(currentUser(sessionId)))
      .mockResolvedValueOnce(jsonResponse(navigation()))
      .mockResolvedValueOnce(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    }));
    const session = useSessionStore();
    await session.completeOidcAuthorization(tokenResponse('oidc-access-token'));

    const handled = session.handleRemoteSessionRevoke('01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f62');

    expect(handled).toBe(false);
    expect(session.state).toBe('authenticated');
    expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toContain('refresh-token');
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
    tokenType: 'Bearer' as const,
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUser(sessionIdValue: string) {
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
    sessionId: sessionIdValue,
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
