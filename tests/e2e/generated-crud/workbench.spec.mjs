import { expect, test } from '../admin-parity/node_modules/@playwright/test/index.mjs';
import AxeBuilder from '../admin-parity/node_modules/@axe-core/playwright/dist/index.mjs';

// 页面、页面模型和客户端来自真实 CLI；HTTP 受控，不把本验收计为真实宿主端到端。
for (const width of [1440, 390]) test(`实际生成工作台精度、空值、查询与语言 ${width}`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  const errors = [], reads = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  const row = { id: '01912345-6789-7abc-8def-0123456789ab', tenantId: '01912345-6789-7abc-8def-0123456789ac',
    name: 'precision', quantity: '9223372036854775807', price: '12345678901234.5678', isReady: null, referenceId: null,
    occurredAtUtc: '2026-10-26T12:34:56.123456Z', version: '1', isDeleted: false, deletedAtUtc: null, deletedById: null };
  await page.route('**/api/v1/catalog/workbenches**', route => {
    if (route.request().method() === 'GET') {
      reads.push(new URL(route.request().url()).searchParams);
      return route.fulfill({ json: { items: [row], page: 1, pageSize: 20, total: 1 } });
    }
    writes.push(route.request().postDataJSON());
    return route.fulfill({ json: { ...row, ...writes.at(-1) } });
  });
  await page.goto('/');
  await expect(page.getByRole('cell', { name: row.quantity, exact: true })).toBeVisible();
  await page.getByRole('button', { name: '编辑', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: '编辑', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Quantity', exact: true })).toHaveValue(row.quantity);
  await expect(dialog.getByRole('textbox', { name: 'Price', exact: true })).toHaveValue(row.price);
  await dialog.getByRole('button', { name: '保存', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[0]).toEqual({ name: row.name, quantity: row.quantity, price: row.price, isReady: null, referenceId: null,
    occurredAtUtc: row.occurredAtUtc, version: '1' });
  await page.getByRole('button', { name: '创建', exact: true }).click();
  dialog = page.getByRole('dialog', { name: '创建', exact: true });
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('new precision');
  await dialog.getByRole('textbox', { name: 'Quantity', exact: true }).fill('9223372036854775808');
  await dialog.getByRole('button', { name: '保存', exact: true }).click();
  await expect(dialog.getByText('请输入范围内的整数。', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  await dialog.getByRole('textbox', { name: 'Quantity', exact: true }).fill(row.quantity);
  await dialog.getByRole('textbox', { name: 'Price', exact: true }).fill(row.price);
  await dialog.getByRole('radiogroup', { name: 'IsReady', exact: true }).getByText('否', { exact: true }).click();
  await expect(dialog.getByRole('radiogroup', { name: 'IsReady', exact: true }).getByRole('radio', { name: '否', exact: true })).toBeChecked();
  await dialog.locator('.el-date-editor input').fill('2026-10-27 08:30:00');
  await dialog.locator('.el-date-editor input').press('Tab');
  await dialog.getByRole('button', { name: '保存', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(writes[1]).toEqual({ name: 'new precision', quantity: row.quantity, price: row.price, isReady: false, referenceId: null, occurredAtUtc: '2026-10-27T00:30:00.000Z' });
  const filters = page.locator('.generated-crud-view__filters');
  await filters.getByRole('textbox', { name: 'Name', exact: true }).fill('%_[~');
  await filters.getByRole('button', { name: '查询', exact: true }).click();
  await expect.poll(() => reads.at(-1)?.get('filterName')).toBe('%_[~');
  await page.getByRole('columnheader', { name: 'Quantity' }).click();
  await expect.poll(() => reads.at(-1)?.get('sortBy')).toBe('quantity');
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Create', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Create', exact: true });
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog.getByText('Complete this required field.', { exact: true })).toBeVisible();
  // axe 依赖元素几何与背景采样；等待真实过渡结束，不把移动端进场动画的中间帧当作终态。
  await page.evaluate(async () => Promise.all(document.getAnimations()
    .filter(animation => animation.playState === 'running' && animation.effect?.getTiming().iterations !== Infinity)
    .map(animation => animation.finished.catch(() => undefined))));
  const dialogAudit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  if (dialogAudit.violations.length) await testInfo.attach('dialog-layout.json', { contentType: 'application/json', body: JSON.stringify(await page.evaluate(() =>
    [...document.querySelectorAll('.el-dialog span')].filter(node => node.textContent === 'Select' && node.getClientRects().length).map(node => {
      const rect = node.getBoundingClientRect();
      return { rect: rect.toJSON(), ancestors: [node, node.parentElement, node.parentElement.parentElement, node.closest('.el-dialog')].map(element => ({ tag: element.className, rect: element.getBoundingClientRect().toJSON(), background: getComputedStyle(element).backgroundColor })),
        center: document.elementsFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2).slice(0, 8).map(element => element.className) };
    }))) });
  expect(dialogAudit.violations).toEqual([]);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toBeHidden();
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(audit.violations).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath(`workbench-${width}.png`), fullPage: true });
});
