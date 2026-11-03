import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01900000-0000-7000-8000-000000000001';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
for (const width of [1440, 390]) {
  test(`注册入口链接到公开表单 ${width}`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(() => {
      localStorage.setItem('fullnet.admin.locale', 'zh-CN');
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async value => { window.__registrationLink = value; } } });
    });
    await page.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'entry-test', tokenType: 'Bearer', expiresAtUtc: '2030-01-01T00:00:00Z' });
      if (path === '/api/v1/me') return json(route, { id, username: 'entry', displayName: '入口管理', actorScope: 'host', scope: 'host', tenantId: null,
        isSuperAdministrator: false, permissions: ['identity.navigation.read', 'identity.registration_ways.read'], sessionId: id,
        preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false });
      if (path === '/api/v1/navigation') return json(route, [{ id: 'registration-ways', parentId: null, routeName: 'registration-ways', path: '/identity/registration-ways',
        componentKey: 'registration-ways', title: '注册方式', caption: '注册方式', icon: 'user', order: 1, requiredPermission: 'identity.registration_ways.read', children: [] }]);
      if (path === '/api/v1/identity/registration-ways') return json(route, { items: [{ id, tenantId: id, name: '链接测试入口', code: 'entry-test', isEnabled: true,
        roleId: id, organizationUnitId: id, positionId: null, sortOrder: 0, remark: null, createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: null, version: 1 }], total: 1, page: 1, pageSize: 20 });
      if (path === '/api/v1/notifications/my-inbox-messages/unread-count') return json(route, { unreadCount: 0 });
      if (path === '/api/v1/identity/session-policy') return json(route, { loginPolicy: 0 });
      return json(route, []);
    });
    await page.goto('/#/identity/registration-ways');
    const row = page.getByRole('row').filter({ hasText: '链接测试入口' }).first();
    await expect(row).toBeVisible();
    const inline = row.getByTestId('registration-way-link');
    if (await inline.isVisible()) await inline.click();
    else {
      await row.getByTestId('art-table-action-more').click();
      await page.locator('[role="menu"]:visible').getByTestId('registration-way-link').click();
    }
    const dialog = page.getByRole('dialog', { name: '注册链接', exact: true });
    await expect(dialog).toBeVisible();
    const field = dialog.getByRole('textbox', { name: '注册链接', exact: true });
    const link = await field.inputValue();
    expect(new URL(link).origin).toBe(new URL(page.url()).origin);
    expect(new URL(link).hash).toBe(`#/register?registrationWayId=${id}`);
    await field.focus();
    expect(await field.evaluate(input => input.selectionStart === 0 && input.selectionEnd === input.value.length)).toBe(true);
    await dialog.getByRole('button', { name: '复制注册链接', exact: true }).click();
    await expect.poll(() => page.evaluate(() => window.__registrationLink)).toBe(link);
    expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);

    const signup = await context.newPage();
    await signup.setViewportSize({ width, height: 1000 });
    let submitted;
    await signup.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/register/email-challenge') {
        expect(route.request().postDataJSON().purpose).toBe(1);
        return json(route, { challengeId: id, expiresAtUtc: '2030-01-01T00:00:00Z' });
      }
      if (path === '/api/v1/auth/register') { submitted = route.request().postDataJSON(); return json(route, { userId: id }, 200); }
      return json(route, { status: 401, title: 'Anonymous', code: 'identity.session_not_active' }, 401);
    });
    await signup.goto(link);
    await signup.getByLabel('显示名称', { exact: true }).fill('链接注册');
    await signup.getByLabel('邮箱', { exact: true }).fill('entry@example.test');
    await signup.getByLabel('密码', { exact: true }).fill('FullNet!2026Secure');
    await signup.getByRole('button', { name: '发送验证码', exact: true }).click();
    await signup.getByLabel('邮箱验证码', { exact: true }).fill('123456');
    await signup.getByRole('button', { name: '注册', exact: true }).click();
    await expect(signup).toHaveURL(/#\/login$/);
    expect(submitted.registrationWayId).toBe(id);
    expect(submitted).not.toHaveProperty('tenantId');
    await signup.close();
  });
}
