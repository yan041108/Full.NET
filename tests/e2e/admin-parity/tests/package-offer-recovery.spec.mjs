import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const packageId = id(1);
const json = (route, value, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(value) });
async function setup(page, locale) {
  await page.addInitScript(selected => localStorage.setItem('fullnet.admin.locale', selected), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'editor', displayName: 'Offer editor', actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    tenantId: null, permissions: ['identity.navigation.read', 'tenancy.tenant_packages.read', 'tenancy.tenant_packages.update', 'tenancy.tenant_entitlements.read', 'tenancy.tenant_entitlements.manage_bindings'],
    sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'plans', parentId: null, routeName: 'tenant-packages', path: '/tenant-packages', componentKey: 'tenant-packages',
    title: 'Plans', caption: '', icon: 'setting', order: 1, requiredPermission: 'tenancy.tenant_packages.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', route => json(route, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/tenant-packages?*', route => json(route, { items: [{ id: packageId, code: 'plan', name: 'Plan', description: null, isActive: true, version: 1, assignedTenantCount: 0 }], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/tenancy/entitlements/catalog', route => json(route, [{ id: id(4), code: 'files', name: 'Files', description: null, entitlementType: 'Feature', isActive: true, version: 1 }]));
  const state = { reads: 0, writes: [], current: { packageId, paymentTenantId: id(2), merchantConfigId: id(3), amountMinor: 12500, currency: 'CNY',
    seatLimit: 10, storageLimit: 1000, durationDays: 30, entitlementIds: [id(4)], isActive: true, version: 1 }, mode: 'unknown' };
  await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/offer`, route => {
    if (route.request().method() === 'GET') { state.reads++; return json(route, state.current); }
    const body = route.request().postDataJSON(); state.writes.push(body);
    if (state.writes.length === 1 && state.mode === 'conflict') {
      state.current = { ...state.current, amountMinor: 18000, seatLimit: 15, version: 4 };
      return json(route, { status: 409, code: 'tenancy.package_offer.version_conflict', title: 'changed' }, 409);
    }
    state.current = { packageId, ...body, version: body.version + 1 };
    if (state.writes.length === 1) return json(route, { status: 503, code: 'test.response_lost', title: 'unconfirmed' }, 503);
    return json(route, state.current);
  });
  await page.goto('/#/tenant-packages'); await page.getByTestId('package-offer-edit').click();
  await expect(page.getByTestId('package-offer-save')).toBeEnabled(); await expect(page.locator('.el-loading-mask')).toHaveCount(0);
  return state;
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`报价未知后核对原草稿并显式暂停新购买 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 }); const state = await setup(page, locale);
    let modal = page.getByRole('dialog');
    const amount = page.getByTestId('package-offer-amount').locator('input'); await amount.fill('24900'); await amount.press('Tab');
    await page.getByTestId('package-offer-save').click(); await expect(page.getByTestId('package-offer-receipt')).toContainText(locale === 'en-US' ? 'Save result unconfirmed' : '保存结果未确认');
    expect(state.writes.length).toBe(1); expect(state.writes[0].version).toBe(1); expect(state.writes[0].amountMinor).toBe(24900);
    await modal.getByRole('button', { name: locale === 'en-US' ? 'Close' : '关闭', exact: true }).click(); await expect(modal).toBeHidden();
    await page.getByTestId('package-offer-edit').click(); expect(state.reads).toBe(1); expect(state.writes.length).toBe(1);
    await page.getByTestId('package-offer-reload').click(); await expect(page.getByTestId('package-offer-current-state')).toContainText(locale === 'en-US' ? 'matches the submitted content' : '与提交内容一致');
    expect(state.reads).toBe(2); expect(state.writes.length).toBe(1);
    await modal.screenshot({ path: info.outputPath('offer-current-matches-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('package-offer-adopt-current').click(); await modal.getByRole('switch').click();
    expect(state.writes.length).toBe(1); await page.getByTestId('package-offer-save').click();
    await expect(page.getByTestId('package-offer-receipt')).toContainText(locale === 'en-US' ? 'Offer saved' : '报价已保存');
    expect(state.writes.length).toBe(2); expect(state.writes[1].version).toBe(2); expect(state.writes[1].isActive).toBe(false);
  });
  test(`报价冲突比较后保留草稿并重新确认 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 }); const state = await setup(page, locale); state.mode = 'conflict';
    const amount = page.getByTestId('package-offer-amount').locator('input'); await amount.fill('24900'); await amount.press('Tab');
    await page.getByTestId('package-offer-save').click(); await expect(page.getByTestId('package-offer-receipt')).toContainText(locale === 'en-US' ? 'version changed' : '版本已变更');
    await page.getByTestId('package-offer-reload').click(); await expect(page.getByTestId('package-offer-current-state')).toContainText(locale === 'en-US' ? 'differs from the draft' : '与草稿不同');
    const table = page.getByRole('region', { name: locale === 'en-US' ? 'Draft and current offer comparison' : '草稿与当前报价比较' });
    await expect(table).toContainText('24900'); await expect(table).toContainText('18000');
    await page.getByRole('dialog').screenshot({ path: info.outputPath('offer-conflict-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('package-offer-keep-draft').click(); expect(state.writes.length).toBe(1);
    await page.getByTestId('package-offer-save').click(); await expect(page.getByTestId('package-offer-receipt')).toContainText(locale === 'en-US' ? 'Offer saved' : '报价已保存');
    expect(state.writes.length).toBe(2); expect(state.writes[1].version).toBe(4); expect(state.writes[1].amountMinor).toBe(24900); expect(state.writes[1].seatLimit).toBe(10);
  });
}
