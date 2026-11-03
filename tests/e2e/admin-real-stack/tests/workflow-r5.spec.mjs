import { expect, test } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { loginAsHostUser } from './support/real-stack-auth.mjs';

test('实际规则仿真与运营读取不发布、启动或恢复业务', async ({ page }) => {
  test.setTimeout(180_000); const errors = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.url().includes('/api/v1/workflow/') && request.method() !== 'GET' && !request.url().endsWith('/simulate')) writes.push(request.url()); });
  await loginAsHostUser(page, process.env.FULLNET_E2E_R5_USERNAME, process.env.FULLNET_E2E_PASSWORD);
  await page.goto('/#/workflow/definitions');
  const row = page.locator('.el-table__row').filter({ hasText: process.env.FULLNET_E2E_R5_KEY });
  await row.getByTestId('workflow-definition-edit').click();
  await page.getByTestId('workflow-definition-form-version').selectOption(process.env.FULLNET_E2E_R5_FORM);
  await page.getByTestId('workflow-simulation-open').click();
  const drawer = page.getByRole('dialog', { name: '发布前仿真' }); await expect(drawer).toBeVisible();
  await drawer.getByRole('spinbutton').fill('12');
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/simulate'));
  await drawer.getByTestId('workflow-simulation-run').click(); const simulation = await response; expect(simulation.status()).toBe(200);
  const result = drawer.getByTestId('workflow-simulation-result'); await expect(result).toContainText('large'); await expect(result).toContainText('approve'); await expect(result).toContainText('当前成员 1 人');
  await drawer.getByRole('spinbutton').fill('5'); await expect(result).toHaveCount(0);
  await drawer.getByTestId('workflow-simulation-run').click(); await expect(result).toContainText('review');
  await page.setViewportSize({ width: 390, height: 844 });
  // 抽屉使用Element Plus的尺寸过渡；等待视口变化后的最终布局，再核对真实可操作边界。
  await expect.poll(async () => { const bounds = await drawer.getByTestId('workflow-simulation-run').boundingBox();
    return bounds !== null && bounds.x >= 0 && bounds.x + bounds.width <= 390; }).toBe(true);
  await page.goto('/#/workflow/instances'); await page.getByTestId('workflow-operations-open').click();
  const panel = page.getByTestId('workflow-operations-panel'); await expect(panel).toContainText('approve'); await expect(panel).toContainText('SLA');
  await panel.getByTestId('workflow-operations-inspect').first().click(); await expect(page).toHaveURL(/resourceId=/u);
  await expect(page.getByTestId('workflow-instance-recover')).toHaveCount(0);
  await page.getByTestId('workflow-operations-open').click(); await expect(panel).toBeVisible();
  await panel.getByTestId('workflow-operations-recovery').click(); await expect(page).toHaveURL(/workflow\/recovery-tasks/u);
  expect(errors).toEqual([]); expect(writes).toEqual([]);
  await writeFile(path.join(process.env.FULLNET_E2E_R5_DIRECTORY, 'result.json'), JSON.stringify({ simulation: true, operations: true, writes: 0 }));
});
