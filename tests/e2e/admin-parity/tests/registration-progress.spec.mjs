import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01912345-6789-7abc-8def-0123456789ab';
const tenantId = '01912345-6789-7abc-8def-0123456789ac';
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });

// 受控 HTTP 验证真实页面的未完成、重试与权限反馈；双库状态推进由集成场景验证。
for (const width of [1440, 390]) {
  test(`公开注册入驻进度与键盘继续 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    let attempts = 0, phase = 'Cancelled', version = 7;
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const progress = () => ({ id, tenantId, registrationWayName: '公开注册企业', phase, version, canContinue: phase !== 'Activated',
      errorCode: phase === 'Cancelled' ? 'quota.rejected' : null, createdAtUtc: '2026-11-04T00:00:00Z' });
    await page.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'registration-browser', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
      if (path === '/api/v1/me') return json(route, { id, username: 'member', displayName: '注册成员', tenantId: null,
        actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1,
        permissions: ['identity.navigation.read', 'identity.tenant_registrations.read_self', 'identity.tenant_registrations.continue_self', 'tenancy.tenants.read', 'tenancy.tenants.switch'] });
      if (path === '/api/v1/navigation') return json(route, [{ id: 'tenant-context', parentId: null, routeName: 'tenant-context', path: '/tenant-context', componentKey: 'tenant-context', title: '企业切换', caption: '企业切换', icon: 'user', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]);
      if (path === '/api/v1/me/tenant-registrations') return json(route, [progress()]);
      if (path.endsWith('/continue')) {
        expect(route.request().postDataJSON()).toEqual({ version });
        attempts++; version += 3;
        if (attempts === 2) phase = 'Activated';
        return json(route, progress());
      }
      if (path === '/api/v1/me/tenant-invitations') return json(route, { items: [], page: 1, pageSize: 20, total: 0 });
      if (path === '/api/v1/tenancy/available') return json(route, phase === 'Activated' ? [{ id: tenantId, identifier: 'registered', name: '公开注册企业', domain: 'registered.localhost' }] : []);
      if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
      return json(route, []);
    });
    await page.goto('/#/tenant-context');
    const panel = page.getByTestId('registration-progress');
    await expect(panel).toContainText('开通未完成');
    const proceed = panel.getByRole('button', { name: '继续办理', exact: true });
    await proceed.focus(); await page.keyboard.press('Enter');
    await expect.poll(() => attempts).toBe(1);
    await expect(panel).toContainText('开通未完成');
    await expect(proceed).toBeEnabled();
    await proceed.click();
    await expect(panel).toContainText('已加入企业');
    await expect(proceed).toHaveCount(0);
    await expect(page.locator('[data-tenant-id]')).toHaveCount(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).include('[data-testid="registration-progress"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
    expect(errors).toEqual([]);
    await panel.screenshot({ path: info.outputPath('registration-progress-panel.png') });
    await page.screenshot({ path: info.outputPath('registration-progress.png'), fullPage: true });
  });
}
