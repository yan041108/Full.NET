import { expect, test } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { loginAsHostUser } from './support/real-stack-auth.mjs';

test('真实固定版本比较重读定义及表单，展示字段授权和约束变化且不写业务', async ({ page }) => {
  test.setTimeout(180_000);
  const before = process.env.FULLNET_E2E_COMPARE_BEFORE, after = process.env.FULLNET_E2E_COMPARE_AFTER;
  const definition = process.env.FULLNET_E2E_COMPARE_DEFINITION, directory = process.env.FULLNET_E2E_COMPARE_DIRECTORY;
  for (const id of [before, after, definition]) expect(id).toMatch(/^[0-9a-f-]{36}$/iu);
  const errors = [], writes = [], reads = new Map();
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.url().includes('/api/v1/workflow/') && request.method() !== 'GET') writes.push(request.url()); });
  page.on('response', response => { if (response.request().method() === 'GET') reads.set(new URL(response.url()).pathname, response.status()); });
  await loginAsHostUser(page, process.env.FULLNET_E2E_COMPARE_USERNAME, process.env.FULLNET_E2E_PASSWORD);
  await page.goto('/#/workflow/definitions');
  const row = page.locator('.el-table__row').filter({ hasText: process.env.FULLNET_E2E_COMPARE_KEY });
  await row.getByTestId('workflow-definition-versions').click();
  await page.getByTestId('workflow-version-compare-open').click();
  const drawer = page.getByRole('dialog', { name: '固定版本对比' }); await expect(drawer).toBeVisible();
  await drawer.getByTestId('workflow-version-compare-before').selectOption(before);
  await drawer.getByTestId('workflow-version-compare-after').selectOption(after);
  await drawer.getByTestId('workflow-version-compare-run').click();
  const result = drawer.getByTestId('workflow-version-compare-result'); await expect(result).toBeVisible();
  await expect(result.getByText('/fields/title/constraints/maxLength', { exact: true })).toBeVisible();
  await expect(result.getByText('/nodes/approve/config/fieldPolicies/title', { exact: true })).toBeVisible();
  await expect(result.getByText('/formVersionId', { exact: true })).toBeVisible();
  await expect(result).toContainText('已有实例继续固定原版本');
  for (const route of [`/definition-versions/${before}`, `/definition-versions/${after}`,
    `/form-versions/${process.env.FULLNET_E2E_COMPARE_FORM_BEFORE}`, `/form-versions/${process.env.FULLNET_E2E_COMPARE_FORM_AFTER}`]) {
    await expect.poll(() => reads.get('/api/v1/workflow' + route)).toBe(200);
  }
  await page.setViewportSize({ width: 390, height: 844 });
  const button = drawer.getByTestId('workflow-version-compare-run');
  const bounds = await button.boundingBox(); expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.x + bounds.width).toBeLessThanOrEqual(390);
  await drawer.getByTestId('workflow-version-compare-before').selectOption(after);
  await expect(result).toHaveCount(0); await expect(button).toBeDisabled();
  expect(writes).toEqual([]); expect(errors).toEqual([]);
  await writeFile(path.join(directory, 'result.json'), JSON.stringify({ before, after, definition }));
});
