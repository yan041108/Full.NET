import { vi } from 'vitest';
export const csrf = 'C'.repeat(43);
export const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
export const userSessionId = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61';
export function browserFixture(override?: (url: string, init: RequestInit) => Response | Promise<Response | undefined> | undefined) {
 let activeTenant: string | null = null;
 const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'content-type': 'application/json' } });
 const fetch = vi.fn(async (input: string | URL | Request, init: RequestInit = {}) => {
  const url = String(input); const custom = await override?.(url, init); if (custom) return custom;
  if (url.endsWith('/identity/browser/session')) return json({ expiresAtUtc: '2030-01-01T00:00:00Z', csrfToken: csrf });
  if (url.endsWith('/api/v1/me')) return json({ id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60', username: 'admin', displayName: 'Admin',
   tenantId: activeTenant, actorScope: 'host', scope: activeTenant ? 'tenant:' + activeTenant.replaceAll('-', '') : 'host',
   isSuperAdministrator: true, passwordChangeRequired: false, permissions: ['tenancy.tenants.read'], sessionId: userSessionId,
   preferredLocale: 'zh-CN', profileVersion: 1 });
  if (url.endsWith('/api/v1/navigation')) return json([{ id: 'overview', parentId: null, routeName: 'overview', path: '/', componentKey: 'overview',
   title: 'Overview', caption: '', icon: 'dashboard', order: 10, requiredPermission: 'platform.dashboard.read', children: [] }]);
  if (url.endsWith('/api/v1/tenancy/available')) return json([{ id: tenantId, identifier: 'acme', name: 'Acme', domain: 'acme.localhost', scope: 'tenant:' + tenantId.replaceAll('-', '') }]);
  if (url.endsWith('/api/v1/tenancy/context')) { activeTenant = JSON.parse(String(init.body)).tenantId; return new Response(null, { status: 204 }); }
  if (url.endsWith('/identity/browser/password') || url.endsWith('/identity/browser/logout')) return new Response(null, { status: 204 });
  throw new Error('Unexpected browser request: ' + url);
 });
 vi.stubGlobal('fetch', fetch); return fetch;
}
