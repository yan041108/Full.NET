import { test, expect } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';
import { writeFileSync } from 'node:fs';

test('消息运维展示真实来源、筛选与贴底分页，切换来源不会误报查询失败', async ({ page }, testInfo) => {
  test.setTimeout(90_000);
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await loginAsHostAdmin(page);
  const overviewResponse = page.waitForResponse(response => response.url().includes('/api/v1/messaging/delivery-status') && response.request().method() === 'GET');
  await page.goto('/#/messaging/operations');
  expect((await overviewResponse).status()).toBe(200);
  const view = page.locator('.host-messaging-ops-view');
  await expect(view).toBeVisible();
  await expect(view.getByRole('tab', { name: '交付总览', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(view.getByText('尚未观测', { exact: true })).toBeVisible();
  await expect.poll(async () => {
    const body = await view.locator('.el-table__body-wrapper').first().boundingBox();
    const row = await view.locator('.el-table__body-wrapper .el-table__row').first().boundingBox();
    return Boolean(body && row && row.y >= body.y - 1 && row.y + row.height <= body.y + body.height + 1);
  }).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('messaging-overview.png'), fullPage: false, animations: 'disabled' });
  const captureResponse = page.waitForResponse(response => response.url().includes('/api/v1/messaging/delivery/shadow-captures/current?'));
  await view.getByRole('button', { name: '影子采集', exact: true }).first().click();
  expect((await captureResponse).status()).toBe(404);
  const capture = page.getByRole('dialog').filter({ hasText: '影子采集' });
  await expect(capture.getByText('尚无采集记录。')).toBeVisible();
  await expect(capture.getByText(/当前部署未允许此流采集/)).toBeVisible();
  await expect(capture.getByRole('button', { name: '开始新代次' })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath('messaging-shadow-capture.png'), fullPage: false, animations: 'disabled' });
  await capture.getByRole('button', { name: '关闭', exact: true }).click();
  const replayResponse = page.waitForResponse(response => response.url().includes('/api/v1/messaging/kafka/replay-operations?') && response.request().method() === 'GET');
  await view.getByRole('tab', { name: '重放操作', exact: true }).click();
  expect((await replayResponse).status()).toBe(200);
  await expect(view.getByText('部署策略已关闭范围重放，历史记录仍可读取。')).toBeVisible();
  await expect(view.getByRole('button', { name: '创建范围重放', exact: true })).toBeDisabled();
  await page.screenshot({ path: testInfo.outputPath('messaging-replay-operations.png'), fullPage: false, animations: 'disabled' });
  const legacyResponse = page.waitForResponse(response => response.url().includes('/api/v1/messaging/outbox/dead-letters?') && response.request().method() === 'GET');
  await view.getByRole('tab', { name: 'Legacy 死信', exact: true }).click();
  expect((await legacyResponse).status()).toBe(200);
  await expect(view.getByRole('tab', { name: 'Legacy 死信', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(view.getByText('回队保留历史尝试次数；回队成功后仍需观察 Worker 处理与业务对账。')).toBeVisible();
  const table = view.locator('.el-table');
  const pagination = view.locator('.art-table-pagination');
  await expect(pagination).toBeInViewport({ ratio: 1 });
  await expect.poll(async () => {
    const body = await table.boundingBox();
    const footer = await pagination.boundingBox();
    return Boolean(body && footer && footer.y >= body.y + body.height - 2);
  }).toBe(true);
  const kafkaResponse = page.waitForResponse(response => response.url().includes('/api/v1/messaging/kafka/dead-letters?') && response.request().method() === 'GET');
  await view.getByRole('tab', { name: 'Kafka 死信', exact: true }).click();
  expect((await kafkaResponse).status()).toBe(200);
  await expect(view.getByText(/空列表不证明 Broker 无死信/)).toBeVisible();
  await expect(view.locator('[role="alert"]')).toHaveCount(0);
  await expect(view.locator('.art-table-pagination')).toBeInViewport({ ratio: 1 });
  await page.screenshot({ path: testInfo.outputPath('messaging-operations.png'), fullPage: false, animations: 'disabled' });
  if (process.env.FULLNET_CLIENT_OPENAPI_EXPORT_PATH) {
    const schema = await page.request.get(`${process.env.FULLNET_E2E_API_URL}/openapi/v1.json`);
    expect(schema.status()).toBe(200);
    writeFileSync(process.env.FULLNET_CLIENT_OPENAPI_EXPORT_PATH, await schema.text());
  }
  expect(errors).toEqual([]);
});
