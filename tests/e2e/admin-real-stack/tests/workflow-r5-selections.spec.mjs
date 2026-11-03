import { expect, test } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { loginAsHostUser } from './support/real-stack-auth.mjs';

test('当前保存草稿通过真实候选选人并仿真，选择改变后旧轨迹失效', async ({ page }) => {
  test.setTimeout(180_000); const errors = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    if (request.url().includes('/api/v1/workflow/') && request.method() !== 'GET' && !request.url().endsWith('/simulate')) writes.push(request.url());
  });
  await page.setViewportSize({ width: 1280, height: 900 });
  await loginAsHostUser(page, process.env.FULLNET_E2E_R5_USERNAME, process.env.FULLNET_E2E_PASSWORD);
  await page.goto('/#/workflow/definitions');
  const row = page.locator('.el-table__row').filter({ hasText: process.env.FULLNET_E2E_R5_KEY });
  await row.getByTestId('workflow-definition-edit').click();
  await page.getByTestId('workflow-definition-form-version').selectOption(process.env.FULLNET_E2E_R5_FORM);
  await page.getByTestId('workflow-simulation-open').click();
  const drawer = page.getByRole('dialog', { name: '发布前仿真' }); await expect(drawer).toBeVisible();
  await drawer.getByRole('spinbutton').fill('12');
  const run = drawer.getByTestId('workflow-simulation-run'); await expect(run).toBeDisabled();
  const candidate = drawer.getByTestId(`workflow-starter-candidate-${process.env.FULLNET_E2E_R5_USER}`);
  await candidate.focus(); await expect(candidate).toBeFocused(); await candidate.press('Enter'); await expect(run).toBeEnabled();
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/simulate'));
  await run.click(); const simulation = await response; expect(simulation.status()).toBe(200);
  const sent = simulation.request().postDataJSON();
  expect(sent.expectedRevision).toBe(3); expect(sent.formVersionId).toBe(process.env.FULLNET_E2E_R5_FORM);
  expect(sent.assigneeSelections).toEqual({ reviewChoice: [process.env.FULLNET_E2E_R5_USER] });
  const result = drawer.getByTestId('workflow-simulation-result'); await expect(result).toContainText('claim');
  await expect(result).toContainText('当前成员 1 人');
  await candidate.click(); await expect(result).toHaveCount(0); await expect(run).toBeDisabled();
  await candidate.click(); await expect(run).toBeEnabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(async () => { const bounds = await run.boundingBox(); return bounds !== null && bounds.x >= 0 && bounds.x + bounds.width <= 390; }).toBe(true);
  expect(errors).toEqual([]); expect(writes).toEqual([]);
  await writeFile(path.join(process.env.FULLNET_E2E_R5_DIRECTORY, 'result.json'), JSON.stringify({ starterSelections: true, draftRevision: 3,
    viewports: [[1280, 900], [390, 844]], keyboardSelection: true, writes: 0 }));
});
