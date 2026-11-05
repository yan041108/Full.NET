import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const merchant = { id: id(1), tenantId: id(2), name: 'Current merchant', channelKey: 'wechat_native', appId: 'current-app', merchantId: 'current-merchant', certificateSerialNo: 'current-certificate',
  notifyUrl: 'https://merchant.example/notify', returnUrl: 'https://merchant.example/return', hasApiV3Key: true, hasPrivateKey: false, isDefault: false, isEnabled: true,
  createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, version: 7 };
const row = { id: merchant.id, tenantId: merchant.tenantId, name: merchant.name, channelKey: merchant.channelKey, maskedAppId: 'app***', maskedMerchantId: 'merchant***',
  maskedCertificateSerialNo: 'cert***', maskedNotifyUrl: 'https://masked/***', maskedReturnUrl: 'https://masked/***', hasApiV3Key: true, hasPrivateKey: false,
  isDefault: false, isEnabled: true, createdAtUtc: merchant.createdAtUtc, updatedAtUtc: null, version: 7 };
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('商户只读精确定位及当前配置核对 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); let writes = 0, links = 0, reads = 0, showRow = false, mode = 'ok';
    await page.route('https://merchant.example/**', route => { links++; return route.fulfill({ status: 404 }); });
    await page.route('**/api/v1/payments/**', route => { if (route.request().method() !== 'GET') writes++; return route.fulfill({ status: 404 }); });
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'readonly', displayName: 'Read only', actorScope: 'host', scope: 'host', tenantId: null,
      isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false, permissions: ['identity.navigation.read', 'payments.merchants.read'] }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'payments-merchant-configs', parentId: null, routeName: 'payments-merchant-configs', componentKey: 'payments-merchant-configs',
      path: '/payments/merchant-configs', title: 'Merchants', caption: '', icon: 'setting', order: 1, requiredPermission: 'payments.merchants.read', children: [] }]));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/payments/merchant-configs?*', route => json(route, { items: showRow ? [row] : [], page: 1, pageSize: 20, total: showRow ? 1 : 0 }));
    await page.route('**/api/v1/payments/merchant-configs/*', route => {
      expect(route.request().method()).toBe('GET'); reads++; const requested = new URL(route.request().url()).pathname.split('/').at(-1);
      if (mode === '503' || mode === '403') return route.fulfill({ status: Number(mode), contentType: 'application/problem+json', body: JSON.stringify({ type: 'about:blank', code: 'test.unavailable', status: Number(mode), title: 'Temporarily unavailable' }) });
      return json(route, { ...merchant, id: requested, tenantId: mode === 'mismatch' ? id(6) : merchant.tenantId });
    });
    await page.goto('/#/payments/merchant-configs'); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.payment-merchant-configs-view');
    await expect(page.getByTestId('payment-merchant-config-create')).toHaveCount(0); await page.getByTestId('merchant-exact-id').fill('bad'); await page.getByTestId('merchant-read-id').click(); expect(reads).toBe(0);
    await page.getByTestId('merchant-exact-id').fill(id(4)); await page.getByTestId('merchant-exact-id').press('Enter');
    await expect(page.getByTestId('merchant-detail-snapshot')).toContainText(merchant.merchantId); await expect(page.getByTestId('merchant-detail-id')).toHaveText(id(4)); expect(reads).toBe(1);
    await expect(page.locator('.merchant-detail').locator('a,input,textarea')).toHaveCount(0); await expect(page.getByTestId('merchant-detail-snapshot')).toContainText(locale === 'zh-CN' ? '商户私钥已配置' : 'Merchant private key configured');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('merchant-exact-detail-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.merchant-detail').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mode = '503'; await page.getByTestId('merchant-detail-refresh').click(); await expect(page.getByTestId('merchant-detail-problem')).toContainText('Temporarily unavailable');
    await expect(page.getByTestId('merchant-detail-snapshot')).toHaveCount(0); await expect(page.getByTestId('merchant-detail-id')).toHaveText(id(4));
    await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
    mode = 'ok'; showRow = true; await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await page.getByTestId('merchant-detail-' + merchant.id).click(); await expect(page.getByTestId('merchant-detail-snapshot')).toContainText(merchant.name); expect(reads).toBe(3);
    mode = 'mismatch'; await page.getByTestId('merchant-detail-refresh').click(); await expect(page.getByTestId('merchant-detail-problem')).toContainText(locale === 'zh-CN' ? '身份、筛选或版本不一致' : 'identity, filters or version');
    await expect(page.getByTestId('merchant-detail-snapshot')).toHaveCount(0);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('merchant-detail-mismatch-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.merchant-detail').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mode = '403'; await page.getByTestId('merchant-detail-refresh').click(); await expect(page.getByTestId('merchant-detail-problem')).toContainText('Temporarily unavailable');
    await expect(page.getByTestId('merchant-detail-id')).toHaveText(merchant.id); expect(writes).toBe(0); expect(links).toBe(0);
  });
}
