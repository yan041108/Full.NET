import { expect, test } from '../admin-parity/node_modules/@playwright/test/index.mjs';
import AxeBuilder from '../admin-parity/node_modules/@axe-core/playwright/dist/index.mjs';

for (const width of [1440, 390]) test(`实际生成主从表单一次提交新增修改移除 ${width}`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'en-US'));
  const id = '01912345-6789-7abc-8def-0123456789ab';
  const tenantId = '01912345-6789-7abc-8def-0123456789ac';
  const master = { id, tenantId, name: 'Order', version: '1', isDeleted: false, deletedAtUtc: null, deletedById: null };
  const detail = { id: '01912345-6789-7abc-8def-0123456789ad', tenantId, orderReferenceId: id, description: 'Existing', quantity: '9223372036854775807',
    version: '1', isDeleted: false, deletedAtUtc: null, deletedById: null };
  const writes = [], ordinary = [];
  const response = route => route.fulfill({ json: { master, details: [detail] } });
  await page.route('**/api/v1/catalog/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'GET') return path.endsWith('/aggregate') ? response(route)
      : route.fulfill({ json: { items: [master], total: 1, page: 1, pageSize: 20 } });
    if (!path.endsWith('/aggregate')) ordinary.push(path);
    writes.push(route.request().postDataJSON()); return response(route);
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Create', exact: true });
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('New order');
  await dialog.getByRole('button', { name: 'Add detail', exact: true }).click();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog.getByText('Complete this required field.', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(0);
  await dialog.getByRole('textbox', { name: 'Description', exact: true }).fill('New child');
  await dialog.getByRole('textbox', { name: 'Quantity', exact: true }).fill('9223372036854775807');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[0]).toEqual({ master: { name: 'New order' }, details: [{ description: 'New child', quantity: '9223372036854775807' }] });
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue('Existing');
  await dialog.getByRole('textbox', { name: 'Description', exact: true }).fill('Changed');
  await dialog.getByRole('button', { name: 'Add detail', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Description', exact: true }).nth(1).fill('Additional');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[1]).toEqual({ master: { name: 'Order', version: '1' }, added: [{ description: 'Additional', quantity: '0' }],
    updated: [{ id: detail.id, version: '1', input: { description: 'Changed', quantity: detail.quantity } }], removed: [] });
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit', exact: true });
  await dialog.getByRole('button', { name: 'Remove this detail', exact: true }).click();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[2]).toEqual({ master: { name: 'Order', version: '1' }, added: [], updated: [], removed: [{ id: detail.id, version: '1' }] });
  expect(ordinary).toEqual([]);
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(audit.violations).toEqual([]);
});
