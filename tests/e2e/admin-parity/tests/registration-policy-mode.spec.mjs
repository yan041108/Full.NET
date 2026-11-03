import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01900000-0000-7000-8000-000000000001';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
for (const width of [1440, 390]) {
  test(`注册策略三态与冲突恢复 ${width}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    const errors = [], writes = [];
    page.on('pageerror', error => errors.push(error.message));
    let policy = { id, registrationMode: 1, isPublicRegistrationEnabled: false, updatedAtUtc: '2026-11-04T00:00:00Z', version: 7 };
    await page.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'policy-test', tokenType: 'Bearer', expiresAtUtc: '2030-01-01T00:00:00Z' });
      if (path === '/api/v1/me') return json(route, { id, username: 'policy', displayName: '策略操作员', actorScope: 'host', scope: 'host', tenantId: null,
        isSuperAdministrator: false, permissions: ['identity.navigation.read', 'identity.registration_ways.read', 'identity.registration_policy.read', 'identity.registration_policy.update'],
        sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false });
      if (path === '/api/v1/navigation') return json(route, [{ id: 'registration-ways', parentId: null, routeName: 'registration-ways', path: '/identity/registration-ways',
        componentKey: 'registration-ways', title: '注册方式', caption: '注册方式', icon: 'user', order: 1, requiredPermission: 'identity.registration_ways.read', children: [] }]);
      if (path === '/api/v1/identity/registration-policy') {
        if (route.request().method() === 'GET') return json(route, policy);
        writes.push(route.request().postDataJSON());
        if (writes.length === 1) {
          policy = { ...policy, registrationMode: 0, version: 8 };
          return json(route, { status: 409, title: '注册策略版本冲突', code: 'identity.registration_policy.version_conflict' }, 409);
        }
        policy = { ...policy, ...writes.at(-1), version: 9 };
        return json(route, policy);
      }
      if (path === '/api/v1/identity/registration-ways') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
      if (path === '/api/v1/notifications/my-inbox-messages/unread-count') return json(route, { unreadCount: 0 });
      if (path === '/api/v1/identity/session-policy') return json(route, { loginPolicy: 0 });
      return json(route, []);
    });
    await page.goto('/#/identity/registration-ways');
    const card = page.locator('.registration-policy-card');
    const mode = card.getByRole('combobox', { name: '注册模式', exact: true });
    const save = card.getByRole('button', { name: '保存注册策略', exact: true });
    await expect(card.getByTestId('registration-policy-mode')).toContainText('仅邀请注册');
    await card.getByTestId('registration-policy-mode').click(); await page.getByRole('option', { name: '开放注册', exact: true }).click();
    await save.click();
    await expect(card.getByText('注册策略版本冲突', { exact: true })).toBeVisible();
    await expect(save).toBeDisabled(); await expect(mode).toBeDisabled();
    expect(writes).toEqual([{ registrationMode: 2, isPublicRegistrationEnabled: true, version: 7 }]);
    await card.getByRole('button', { name: '重新读取注册策略', exact: true }).click();
    await expect(card.getByTestId('registration-policy-mode')).toContainText('禁用注册');
    await card.getByTestId('registration-policy-mode').click(); await page.getByRole('option', { name: '仅邀请注册', exact: true }).click();
    await save.click(); await expect(save).toBeDisabled();
    expect(writes.at(-1)).toEqual({ registrationMode: 1, isPublicRegistrationEnabled: false, version: 8 });
    await expect(card.getByTestId('registration-policy-mode')).toContainText('仅邀请注册');
    expect((await new AxeBuilder({ page }).include('.registration-policy-card').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
    expect(await card.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(errors).toEqual([]);
  });
}
