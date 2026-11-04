import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', token = 'controlled-invitation-token-bff-return';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
for (const width of [1440, 390]) test(`受控BFF整页登录回调保留邀请并显式接受 ${width}`, async ({ page }, info) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  let authenticated = false, accepted = false, logins = 0;
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/identity/browser/session') return json(route, authenticated ? { csrfToken: 'c'.repeat(64), expiresAtUtc: '2099-01-01T00:00:00Z' } : { status: 401 }, authenticated ? 200 : 401);
    if (path === '/api/v1/identity/browser/login') { authenticated = true; logins++; return route.fulfill({ status: 302, headers: { location: '/#/identity/oidc/callback' } }); }
    if (path === '/api/v1/auth/invitations/verify') return json(route, { invitationId: id, tenantId: id, email: 'existing@example.test', registrationWayId: id, expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json(route, { id, username: 'existing', displayName: '已有账号', actorScope: 'host', scope: 'host', tenantId: null,
      isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1,
      permissions: ['identity.users.read', 'tenancy.tenants.read', 'tenancy.tenants.switch'] });
    if (path === '/api/v1/navigation') return json(route, [
      { id: 'users', parentId: null, routeName: 'users', path: '/identity/users', componentKey: 'users', title: '用户', caption: '', icon: 'user', order: 0, requiredPermission: 'identity.users.read', children: [] },
      { id: 'tenant-context', parentId: null, routeName: 'tenant-context', path: '/tenant-context', componentKey: 'tenant-context', title: '企业', caption: '', icon: 'user', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }
    ]);
    if (path === '/api/v1/tenancy/available') return json(route, accepted ? [{ id, identifier: 'acme', name: 'Acme', domain: 'acme.localhost' }] : []);
    if (path === '/api/v1/me/tenant-invitations') return json(route, { items: [], page: 1, pageSize: 20, total: 0 });
    if (path === '/api/v1/identity/tenant-invitations/accept') { expect(route.request().postDataJSON().invitationToken).toBe(token); accepted = true;
      return json(route, { memberId: id, tenantId: id, userId: id, memberRole: 'Member', status: 'Active' }); }
    if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
    if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
    return json(route, []);
  });
  await page.goto(`/#/register?invitationId=${id}&invitationToken=${token}`);
  await page.getByTestId('invitation-existing-account').click(); await page.getByTestId('login-oidc-center').click();
  await expect(page).toHaveURL(/#\/tenant-context$/); expect(logins).toBe(1); expect(accepted).toBe(false);
  await expect(page.getByTestId('my-tenant-invitations').locator('input')).toHaveValue(token);
  expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.login.destination'))).toBeNull();
  await page.getByTestId('accept-tenant-invitation-token').click(); await expect.poll(() => accepted).toBe(true);
  await expect(page.locator(`[data-tenant-id="${id}"]`)).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('fullnet.registration.invitation'))).toBeNull();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath(`bff-return-${width}.png`), fullPage: true, animations: 'disabled' });
});
