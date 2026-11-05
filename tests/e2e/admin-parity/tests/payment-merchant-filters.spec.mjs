import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const merchant = { id: id(1), tenantId: id(2), name: 'Original merchant', channelKey: 'wechat_native', maskedAppId: 'app***', maskedMerchantId: 'merchant***',
  maskedCertificateSerialNo: 'cert***', maskedNotifyUrl: 'https://masked/***', maskedReturnUrl: '-', hasApiV3Key: true, hasPrivateKey: true,
  isDefault: false, isEnabled: true, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, version: 7 };
async function setup(page, locale, write = true) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'merchant', displayName: 'Merchant operator', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'payments.merchants.read', ...(write ? ['payments.merchants.create', 'payments.merchants.update'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'payments-merchant-configs', parentId: null, routeName: 'payments-merchant-configs', componentKey: 'payments-merchant-configs',
    path: '/payments/merchant-configs', title: 'Merchants', caption: '', icon: 'setting', order: 1, requiredPermission: 'payments.merchants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('商户运营筛选保持原条件并明确停用原配置 ' + width, async ({ page }, info) => {
    await setup(page, locale); const reads = []; let disables = 0, detailReads = 0, mismatch = false, disabled = false;
    await page.route('**/api/v1/payments/merchant-configs?*', route => {
      expect(route.request().method()).toBe('GET'); const query = Object.fromEntries(new URL(route.request().url()).searchParams); reads.push(query);
      const filtered = !!query.channelKey; const item = filtered ? { ...merchant, channelKey: mismatch ? 'wechat_native' : query.channelKey, isEnabled: query.isEnabled === 'true', name: 'Alipay & 商户' }
        : { ...merchant, isEnabled: !disabled, version: disabled ? 8 : 7 };
      return json(route, { items: [item], page: Number(query.page), pageSize: 20, total: 40 });
    });
    await page.route(`**/api/v1/payments/merchant-configs/${merchant.id}`, route => {
      detailReads++; expect(route.request().method()).toBe('GET'); return json(route, { ...merchant, appId: 'actual-app', merchantId: 'actual-merchant',
        certificateSerialNo: 'actual-cert', notifyUrl: 'https://example.com/notify', returnUrl: '' });
    });
    await page.route(`**/api/v1/payments/merchant-configs/${merchant.id}/disable`, route => {
      disables++; disabled = true; expect(route.request().method()).toBe('POST'); return json(route, { ...merchant, appId: 'actual-app', merchantId: 'actual-merchant',
        certificateSerialNo: 'actual-cert', notifyUrl: 'https://example.com/notify', returnUrl: '', isEnabled: false, version: 8 });
    });
    await page.goto('/#/payments/merchant-configs'); await page.setViewportSize({ width, height: 1000 }); await expect.poll(() => reads.length).toBe(1);
    const view = page.locator('.payment-merchant-configs-view'); await page.getByTestId('merchant-filter-tenant').fill(id(2));
    await page.getByTestId('merchant-filter-channel').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '支付宝网页支付' : 'Alipay Page Pay', exact: true }).click();
    await page.getByTestId('merchant-filter-enabled').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '否' : 'No', exact: true }).click();
    await page.getByTestId('merchant-filter-name').fill('Alipay & 商户'); await page.getByTestId('merchant-filter-search').click(); await expect.poll(() => reads.length).toBe(2);
    expect(reads.at(-1)).toEqual({ page: '1', pageSize: '20', tenantId: id(2), channelKey: 'alipay_page', nameContains: 'Alipay & 商户', isEnabled: 'false' });
    await page.getByTestId('merchant-filter-name').fill('unsent draft'); await view.locator('.el-pagination .btn-next').click(); await expect.poll(() => reads.length).toBe(3);
    expect(reads.at(-1)).toMatchObject({ page: '2', nameContains: 'Alipay & 商户', isEnabled: 'false' });
    await expect(view).toHaveAttribute('aria-busy', 'false'); await page.screenshot({ path: info.outputPath('merchant-filters-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-merchant-configs-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mismatch = true; await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await expect(view.locator('.el-alert')).toContainText(locale === 'zh-CN' ? '筛选或版本不一致' : 'filters or version'); await expect(view.locator('.el-table__body-wrapper')).not.toContainText('Alipay & 商户'); expect(disables).toBe(0);
    mismatch = false; await page.getByTestId('merchant-filter-reset').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText(merchant.name);
    await page.getByTestId('merchant-edit-' + merchant.id).click(); await expect(page.getByTestId('merchant-editor-name')).toHaveValue(merchant.name);
    await page.getByTestId('merchant-editor-secret').fill('new-private-draft'); const close = async () => {
      await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0); };
    await close(); await page.getByTestId('merchant-edit-' + merchant.id).click(); await expect(page.getByTestId('merchant-editor-secret')).toHaveValue(''); expect(detailReads).toBe(2); await close();
    await page.getByTestId('merchant-disable-' + merchant.id).click(); await expect(page.getByTestId('merchant-disable-identity')).toContainText(merchant.tenantId); expect(disables).toBe(0);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('merchant-disable-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await close(); expect(disables).toBe(0); await page.getByTestId('merchant-disable-' + merchant.id).click(); const readsBeforeDisable = reads.length;
    await page.getByTestId('merchant-disable-confirm').click(); await expect.poll(() => disables).toBe(1);
    await expect.poll(() => reads.length).toBe(readsBeforeDisable + 1); await expect(view).toHaveAttribute('aria-busy', 'false');
    await expect(page.getByTestId('merchant-edit-' + merchant.id)).toBeEnabled(); await expect(page.getByTestId('merchant-disable-' + merchant.id)).toBeDisabled();
  });
  test('只读商户列表保持脱敏且无写操作 ' + width, async ({ page }) => {
    await setup(page, locale, false); let writes = 0;
    await page.route('**/api/v1/payments/merchant-configs/**', route => { writes++; return route.fulfill({ status: 403 }); });
    await page.route('**/api/v1/payments/merchant-configs?*', route => json(route, { items: [{ ...merchant, tenantId: null }], page: 1, pageSize: 20, total: 1 }));
    await page.goto('/#/payments/merchant-configs'); await page.setViewportSize({ width, height: 1000 }); const body = page.locator('.payment-merchant-configs-view .el-table__body-wrapper');
    await expect(body).toContainText('merchant***'); await expect(body).toContainText('Host'); await expect(page.getByTestId('payment-merchant-config-create')).toHaveCount(0);
    await expect(page.getByTestId('merchant-edit-' + merchant.id)).toHaveCount(0); await expect(page.getByTestId('merchant-disable-' + merchant.id)).toHaveCount(0); expect(writes).toBe(0);
  });
}
