import { expect, test } from '../admin-parity/node_modules/@playwright/test/index.mjs';
import AxeBuilder from '../admin-parity/node_modules/@axe-core/playwright/dist/index.mjs';

for (const width of [1440, 390]) test(`实际生成树层级、父节点选择与完整性拒绝 ${width}`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'en-US'));
  const id = suffix => `01912345-6789-7abc-8def-0123456789a${suffix}`;
  const rows = [['0', 'Root', null], ['1', 'Child', id('0')], ['2', 'Grandchild', id('1')], ['3', 'Other root', null]]
    .map(([key, name, parentId]) => ({ id: id(key), tenantId: id('9'), name, parentId, version: '1', isDeleted: false, deletedAtUtc: null, deletedById: null }));
  const writes = []; let overflow = false;
  await page.route('**/api/v1/catalog/tree-nodes**', route => {
    if (route.request().method() === 'GET') return route.fulfill({ json: { items: rows, total: overflow ? 1001 : rows.length, page: 1, pageSize: 100 } });
    const body = route.request().postDataJSON(); writes.push(body);
    return route.fulfill({ json: { ...rows[0], ...body } });
  });
  await page.goto('/');
  const rootRow = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'Root', exact: true }) });
  await expect(rootRow).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Grandchild', exact: true })).toBeVisible();
  expect(await page.locator('.el-table__expand-icon').count()).toBeGreaterThan(0);
  await rootRow.getByRole('button', { name: 'Edit', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit', exact: true });
  const parent = dialog.getByRole('combobox', { name: 'Parent', exact: true });
  await parent.focus(); await parent.press('ArrowDown');
  await expect(page.getByRole('option', { name: 'Other root', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Root', exact: true })).toHaveCount(0);
  await expect(page.getByRole('option', { name: 'Child', exact: true })).toHaveCount(0);
  await expect(page.getByRole('option', { name: 'Grandchild', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: 'Other root', exact: true }).click();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[0]).toEqual({ name: 'Root', parentId: id('3'), version: '1' });
  overflow = true;
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('catalog.tree_nodes.tree_limit');
  await expect(rootRow).toBeVisible();
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(audit.violations).toEqual([]);
});
