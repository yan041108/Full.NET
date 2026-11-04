import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01900000-0000-7000-8000-000000000001';
const token = 'invitation-token-for-browser-proof';
const now = '2026-11-04T00:00:00Z';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
const invitation = { id, tenantId: id, targetEmail: 'invitee@example.test', targetUserId: null,
  invitedByUserId: id, memberRole: 'Member', status: 'Pending', expiresAtUtc: '2030-01-01T00:00:00Z', createdAtUtc: now, updatedAtUtc: now, version: 1 };

for (const width of [1440, 390]) {
  test(`邀请注册链接与登录后显式接受 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(() => { if (location.protocol === 'http:') localStorage.setItem('fullnet.admin.locale', 'zh-CN'); });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let authenticated = true, manager = true, accepted = false, activated = false, writes = 0;
    await page.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/invitations/verify') return json(route, { invitationId: id, tenantId: id, email: invitation.targetEmail, registrationWayId: id, expiresAtUtc: invitation.expiresAtUtc });
      if (path === '/api/v1/auth/refresh' || path === '/api/v1/auth/login') {
        if (!authenticated && path.endsWith('/refresh')) return json(route, { status: 401, title: 'Anonymous', code: 'identity.session_not_active' }, 401);
        if (path.endsWith('/login')) expect(route.request().postDataJSON()).toEqual({ username: 'invitee', password: 'Original!2026Password' });
        authenticated = true;
        return json(route, { accessToken: 'invitation-browser', tokenType: 'Bearer', expiresAtUtc: invitation.expiresAtUtc });
      }
      if (path === '/api/v1/me') return json(route, { id, username: 'invitee', displayName: '邀请用户', actorScope: 'host', scope: manager ? `tenant:${id.replaceAll('-', '')}` : 'host', tenantId: manager ? id : null,
        isSuperAdministrator: false, permissions: ['identity.navigation.read', 'identity.users.read', 'tenancy.tenants.read', 'tenancy.tenants.switch', ...(manager ? ['identity.tenant_members.read', 'identity.tenant_members.invite'] : [])],
        sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false });
      if (path === '/api/v1/navigation') {
        const key = manager ? 'tenant-members' : 'tenant-context';
        return json(route, [...(manager ? [] : [{ id: 'users', parentId: null, routeName: 'users', path: '/identity/users', componentKey: 'users', title: '用户', caption: '', icon: 'user', order: 0, requiredPermission: 'identity.users.read', children: [] }]), { id: key, parentId: null, routeName: key, path: manager ? '/identity/tenant-members' : '/tenant-context', componentKey: key,
          title: '邀请', caption: '邀请', icon: 'user', order: 1, requiredPermission: manager ? 'identity.tenant_members.read' : 'tenancy.tenants.read', children: [] }]);
      }
      if (path === '/api/v1/tenancy/available') return json(route, activated ? [{ id, identifier: 'acme', name: 'Acme', domain: 'acme.localhost' }] : []);
      if (path === '/api/v1/identity/tenant-members/invitations/registration-ways') return json(route, [{ id, name: '企业邮箱注册' }]);
      if (path === '/api/v1/identity/tenant-members/invitations' && route.request().method() === 'POST') {
        expect(route.request().postDataJSON().registrationWayId).toBe(id);
        writes++;
        return json(route, { invitation, invitationToken: token, registrationInvitationId: id });
      }
      if (path === '/api/v1/identity/tenant-members' || path === '/api/v1/identity/tenant-members/invitations') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
      if (path === '/api/v1/me/tenant-invitations') return json(route, { items: accepted && !activated ? [{ ...invitation, tenantName: 'Acme', status: 'Accepted' }] : [], total: accepted && !activated ? 1 : 0, page: 1, pageSize: 20 });
      if (path === '/api/v1/identity/tenant-invitations/accept') {
        expect(route.request().postDataJSON().invitationToken).toBe(token);
        accepted = true;
        return json(route, { memberId: id, tenantId: id, userId: id, memberRole: 'Member', status: 'Pending' });
      }
      if (path === `/api/v1/me/tenant-invitations/${id}/accept`) {
        activated = true;
        return json(route, { memberId: id, tenantId: id, userId: id, memberRole: 'Member', status: 'Active' });
      }
      if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
      if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
      return json(route, []);
    });
    await page.goto('/#/identity/tenant-members');
    await page.getByTestId('tenant-members-action-invite').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('invitation-registration-way').click();
    await page.getByRole('option', { name: '企业邮箱注册', exact: true }).click();
    await dialog.locator('input[autocomplete="email"]').fill(invitation.targetEmail);
    await dialog.locator('.art-form-dialog__footer button').last().click();
    await expect.poll(() => writes).toBe(1);
    const link = await page.getByRole('textbox', { name: '注册邀请链接', exact: true }).inputValue();
    expect(new URL(link).hash).toContain(`invitationId=${id}`);
    manager = false;
    authenticated = false;
    await page.goto('about:blank');
    await page.goto(link);
    await expect(page.getByTestId('invitation-existing-account')).toBeVisible();
    expect(page.url()).not.toContain(token);
    await page.getByTestId('invitation-existing-account').click();
    await page.locator('input[name="username"]').fill('invitee');
    await page.locator('input[name="password"]').fill('Original!2026Password');
    await page.locator('form').getByRole('button', { name: '进入控制台', exact: true }).click();
    await expect(page).toHaveURL(/#\/tenant-context$/);
    const card = page.getByTestId('my-tenant-invitations');
    await expect(card.locator('input')).toHaveValue(token);
    await expect(card.locator('input')).toBeInViewport();
    expect(await card.evaluate(element => element.scrollHeight <= element.clientHeight)).toBe(true);
    expect(await page.locator('.art-context-banner').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(accepted).toBe(false);
    await page.getByTestId('accept-tenant-invitation-token').click();
    await expect(page.getByRole('alert').filter({ hasText: '等待成员激活' })).toBeVisible();
    await expect(card.getByRole('button', { name: '重试激活' })).toBeVisible();
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.registration.invitation'))).toBeNull();
    await card.getByRole('button', { name: '重试激活' }).click();
    await expect.poll(() => activated).toBe(true);
    await expect(page.locator(`[data-tenant-id="${id}"]`)).toBeVisible();
    await expect(page.getByRole('alert').filter({ hasText: '等待成员激活' })).not.toBeVisible();
    expect((await new AxeBuilder({ page }).include('[data-testid="my-tenant-invitations"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`onboarding-${width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}
