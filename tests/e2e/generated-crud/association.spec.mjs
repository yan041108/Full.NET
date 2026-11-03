import { expect, test } from '../admin-parity/node_modules/@playwright/test/index.mjs';
import AxeBuilder from '../admin-parity/node_modules/@axe-core/playwright/dist/index.mjs';

// 真实生成客户端与选择器；候选 HTTP 受控，数据库隔离另由真实双库测试证明。
for (const width of [1440, 390]) test(`实际生成关联选择及一次保存 ${width}`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'en-US'));
  const item = '01912345-6789-7abc-8def-0123456789ab', label = '01912345-6789-7abc-8def-0123456789ac';
  const row = { id: '01912345-6789-7abc-8def-0123456789ad', tenantId: '01912345-6789-7abc-8def-0123456789ae', itemReferenceId: item, labelId: label,
    version: '1', isDeleted: false, deletedAtUtc: null, deletedById: null };
  const writes = [], lookups = [], errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/v1/catalog/item-labels**', route => {
    const request = route.request(), url = new URL(request.url());
    if (url.pathname.includes('/relationship-options/')) {
      lookups.push(url);
      const source = url.pathname.endsWith('/source');
      return route.fulfill({ json: { items: [{ value: source ? item : label, label: source ? 'Catalog item' : 'Approved label' }], page: 1, pageSize: 20, total: 1 } });
    }
    if (request.method() === 'GET') return route.fulfill({ json: { items: [row], page: 1, pageSize: 20, total: 1 } });
    writes.push({ method: request.method(), body: request.postDataJSON() });
    return route.fulfill({ json: row });
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Create', exact: true });
  for (const [name, option] of [['ItemId', 'Catalog item'], ['LabelId', 'Approved label']]) {
    await dialog.getByRole('combobox', { name, exact: true }).focus();
    await dialog.getByRole('combobox', { name, exact: true }).press('ArrowDown');
    await page.getByRole('option', { name: option, exact: true }).click();
  }
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[0]).toEqual({ method: 'POST', body: { itemReferenceId: item, labelId: label } });
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit', exact: true });
  await expect(dialog.getByText('Catalog item', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Approved label', { exact: true })).toBeVisible();
  expect(lookups.some(url => url.searchParams.get('selected') === item)).toBe(true);
  expect(lookups.some(url => url.searchParams.get('selected') === label)).toBe(true);
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[1]).toEqual({ method: 'PUT', body: { itemReferenceId: item, labelId: label, version: '1' } });
  expect(errors).toEqual([]);
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()).violations).toEqual([]);
});
