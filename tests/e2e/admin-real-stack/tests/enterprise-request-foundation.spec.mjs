import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import AxeBuilder from '@axe-core/playwright';
import { clickMainNavLink, enterDevelopmentTenant, loginAsHostAdmin } from './support/real-stack-auth.mjs';

const root = '/api/v1/enterprise_request/enterprise-requests';
function fixture(key) {
  const value = process.env[`FULLNET_E2E_ENTERPRISE_${key}`];
  if (!value) throw new Error('请通过 EnterpriseRequestFoundationBrowserUiTests 准备真实应用夹具。');
  return value;
}
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await loginAsHostAdmin(page); await enterDevelopmentTenant(page, 'Acme Corporation');
});

test('真实主从页面经 Worker 审批回写、消息通知与业务详情往返', async ({ page }) => {
  test.setTimeout(120000);
  await clickMainNavLink(page, /企业申请/);
  await page.getByRole('button', { name: '创建主从单据', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '申请单与明细', exact: true });
  const number = `UI-${Date.now()}`;
  await dialog.getByTestId('aggregate-organization').fill(fixture('UNIT_ID'));
  await dialog.getByTestId('aggregate-number').fill(number); await dialog.getByTestId('aggregate-title').fill('真实主从交付');
  await dialog.getByTestId('aggregate-total').fill('10.00'); await dialog.getByTestId('aggregate-add-line').click();
  await dialog.getByTestId('aggregate-line-description').fill('首行'); await dialog.getByTestId('aggregate-line-quantity').fill('2.0000');
  await dialog.getByTestId('aggregate-line-price').fill('5.00'); await dialog.getByTestId('aggregate-line-amount').fill('10.00');
  const creation = page.waitForResponse(r => new URL(r.url()).pathname === `${root}/aggregate` && r.request().method() === 'POST');
  await dialog.getByTestId('aggregate-save').click(); const created = await creation;
  expect(created.status(), await created.text()).toBe(200); const document = await created.json();
  expect(document.details).toHaveLength(1); expect(document.master.version).toBe('1');
  await expect(dialog).not.toBeVisible();
  const row = page.locator('.generated-crud-view .el-table__row').filter({ hasText: number }); await expect(row).toBeVisible();
  await row.getByRole('button', { name: '主从明细', exact: true }).click();
  await expect(dialog.getByTestId('aggregate-line')).toHaveCount(1);
  await dialog.getByTestId('aggregate-total').fill('25.00'); await dialog.getByTestId('aggregate-line-quantity').fill('3.0000');
  await dialog.getByTestId('aggregate-line-amount').fill('15.00'); await dialog.getByTestId('aggregate-add-line').click();
  await dialog.getByTestId('aggregate-line-description').last().fill('第二行'); await dialog.getByTestId('aggregate-line-quantity').last().fill('2.0000');
  await dialog.getByTestId('aggregate-line-price').last().fill('5.00'); await dialog.getByTestId('aggregate-line-amount').last().fill('10.00');
  async function save() {
    const response = page.waitForResponse(r => new URL(r.url()).pathname === `${root}/${document.master.id}/aggregate` && r.request().method() === 'PUT');
    await dialog.getByTestId('aggregate-save').click(); const saved = await response;
    expect(saved.status(), await saved.text()).toBe(200); await expect(dialog).not.toBeVisible(); return saved.json();
  }
  const updated = await save(); expect(updated.details).toHaveLength(2); expect(updated.master.totalAmount).toBe('25.00');
  await row.getByRole('button', { name: '主从明细', exact: true }).click(); await expect(dialog.getByTestId('aggregate-line')).toHaveCount(2);
  await dialog.getByTestId('aggregate-remove-line').first().click(); await dialog.getByTestId('aggregate-total').fill('10.00');
  const reduced = await save(); expect(reduced.details).toHaveLength(1);
  const submitting = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/submit-for-approval') && r.request().method() === 'POST');
  await row.getByRole('button', { name: '提交审批', exact: true }).click(); await page.getByRole('dialog', { name: '确认提交审批', exact: true }).getByRole('button', { name: '确认提交', exact: true }).click(); const submitted = await submitting;
  expect(submitted.status(), await submitted.text()).toBe(200); await expect(row).toContainText('Submitted');
  await row.getByRole('button', { name: '主从明细', exact: true }).click();
  await expect(dialog).toContainText('当前状态或权限仅允许查看'); await expect(dialog.getByTestId('aggregate-save')).toHaveCount(0);
  await page.keyboard.press('Escape');
  await clickMainNavLink(page, /我的待办/);
  const todo = page.locator('.workflow-todos__table .el-table__row').filter({ hasText: '真实主从交付' });
  await expect.poll(async () => {
    await page.getByTestId('workflow-todo-filter-apply').click();
    return todo.count();
  }, { timeout: 30000, intervals: [1000] }).toBe(1);
  await todo.getByTestId('workflow-todo-open-business').click();
  await expect(page).toHaveURL(new RegExp(`requestId=${document.master.id}`));
  await expect(dialog).toBeVisible(); await expect(dialog.getByTestId('aggregate-save')).toHaveCount(0);
  await page.keyboard.press('Escape'); await expect(page).not.toHaveURL(/requestId=/);
  await clickMainNavLink(page, /我的待办/); await todo.getByTestId('workflow-todo-open').click();
  await page.getByTestId('workflow-todo-comment').fill('真实 Worker 审批交付');
  const approval = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/approve') && r.request().method() === 'POST');
  await page.getByTestId('workflow-todo-approve').click(); const approved = await approval;
  expect(approved.status(), await approved.text()).toBe(200);
  await page.keyboard.press('Escape'); await clickMainNavLink(page, /企业申请/);
  await expect.poll(async () => {
    await page.reload(); return row.textContent();
  }, { timeout: 30000, intervals: [1000] }).toContain('Approved');
  await clickMainNavLink(page, /消息中心/);
  const notification = page.locator('.inbox-messages-view .el-table__row')
    .filter({ hasText: '审批已完成' }).filter({ hasText: document.master.id });
  await expect(notification).toBeVisible({ timeout: 30000 });
  await expect(notification).toContainText('demo.enterprise_request');
});

test('真实导入、附件下载、后台报表和同单据打印', async ({ page }) => {
  test.setTimeout(120000); await clickMainNavLink(page, /导入任务/);
  const create = page.getByTestId('import-export-task-create');
  const dialog = page.getByRole('dialog', { name: '提交导入', exact: true });
  const detail = page.getByRole('dialog', { name: '任务详情', exact: true });
  async function upload(path) {
    await create.click(); await expect(dialog).toBeVisible(); const schema = dialog.getByTestId('import-task-schema');
    // 默认已选目标时不再次切换，避免与对话框自动聚焦竞争导致下拉被反向关闭。
    if (!(await schema.innerText()).includes('企业申请')) { await schema.click(); await page.getByRole('option', { name: '企业申请', exact: true }).click(); }
    await expect(schema).toContainText('企业申请');
    await dialog.getByTestId('import-task-file').setInputFiles(path);
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/api/v1/import-export/tasks' && r.request().method() === 'POST');
    await dialog.getByTestId('import-task-submit').click(); const result = await response;
    expect(result.status(), await result.text()).toBe(201); await expect(detail).toBeVisible(); return result.json();
  }
  await create.click();
  const downloading = page.waitForEvent('download'); await dialog.getByTestId('import-task-template').click();
  expect((await downloading).suggestedFilename()).toMatch(/\.xlsx$/);
  await page.keyboard.press('Escape'); await expect(dialog).not.toBeVisible();
  const invalid = await upload(fixture('INVALID_WORKBOOK'));
  expect(invalid.invalidRowCount).toBe(1); await expect(detail).toContainText('申请单行不符合模板约束');
  await expect(detail.getByTestId('import-export-task-execute')).toHaveCount(0);
  await page.keyboard.press('Escape'); await expect(detail).not.toBeVisible();
  const valid = await upload(fixture('VALID_WORKBOOK')); expect(valid.validRowCount).toBe(1);
  const executing = page.waitForResponse(r => new URL(r.url()).pathname.endsWith(`/tasks/${valid.id}/execute`));
  await detail.getByTestId('import-export-task-execute').click(); const execution = await executing;
  expect(execution.status(), await execution.text()).toBe(200);
  expect((await execution.json()).statusKey).toBe('queued');
  await expect(detail).toContainText('执行成功', { timeout: 30000 });
  await page.keyboard.press('Escape'); await clickMainNavLink(page, /企业申请/);
  const imported = page.locator('.generated-crud-view .el-table__row').filter({ hasText: fixture('IMPORT_NUMBER') });
  await expect(imported).toBeVisible(); await imported.getByRole('button', { name: '附件与文档', exact: true }).click();
  const delivery = page.getByRole('dialog', { name: '企业申请交付', exact: true });
  await expect(delivery.getByRole('button', { name: '刷新状态', exact: true })).toBeEnabled();
  await delivery.locator('input[type=file]').setInputFiles({ name: 'evidence.txt', mimeType: 'text/plain', buffer: Buffer.from('real delivery evidence') });
  await delivery.getByRole('button', { name: '上传附件', exact: true }).click();
  const attachment = delivery.locator('article').filter({ hasText: 'evidence.txt' });
  await expect.poll(async () => {
    await delivery.getByRole('button', { name: '刷新状态', exact: true }).click();
    return attachment.getByRole('button', { name: '下载', exact: true }).count();
  }, { timeout: 30000, intervals: [1000] }).toBe(1);
  const attachmentDownload = page.waitForEvent('download'); await attachment.getByRole('button', { name: '下载', exact: true }).click();
  expect(await readFile(await (await attachmentDownload).path(), 'utf8')).toBe('real delivery evidence');
  const reporting = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/reports') && r.request().method() === 'POST');
  await delivery.getByRole('button', { name: '生成报表', exact: true }).click(); const queuedReport = await reporting;
  expect(queuedReport.status(), await queuedReport.text()).toBe(200); const report = await queuedReport.json();
  expect(report.phase).toBe('Queued');
  const reportRow = delivery.locator('article').filter({ hasText: report.id });
  await expect(reportRow.getByTestId('enterprise-report-expiry')).toHaveAttribute('datetime', report.expiresAtUtc);
  await expect(reportRow).toContainText('下载时会再次检查有效期和当前权限');
  await expect(reportRow.getByRole('button', { name: '下载', exact: true })).toBeVisible({ timeout: 30000 });
  const reportDownload = page.waitForEvent('download'); await reportRow.getByRole('button', { name: '下载', exact: true }).click();
  const workbook = await readFile(await (await reportDownload).path());
  expect(workbook.length).toBeGreaterThan(500); expect(workbook.subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 3, 4]));
  if (process.env.FULLNET_ENTERPRISE_EXPIRY_UI_EVIDENCE) {
    const evidence = process.env.FULLNET_ENTERPRISE_EXPIRY_UI_EVIDENCE;
    await page.setViewportSize({ width: 1440, height: 1000 }); await reportRow.getByTestId('enterprise-report-expiry').scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(evidence, 'report-expiry-desktop.png'), animations: 'disabled' });
    await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await expect(page.locator('.el-message')).toHaveCount(0);
    const accessibility = await new AxeBuilder({ page }).include('.enterprise-delivery').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
    await reportRow.getByTestId('enterprise-report-expiry').scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, 'report-expiry-mobile.png'), animations: 'disabled' });
  }
  // 新增提示位于外层弹窗；先验收该区域，再验证已有隔离打印内容，避免扫描器进入禁脚本 iframe 后悬挂。
  await delivery.getByRole('button', { name: '打印预览', exact: true }).click();
  const preview = page.frameLocator('iframe[title="打印预览"]');
  await expect(preview.getByRole('heading', { name: '真实 UI 导入', exact: true })).toBeVisible();
  await expect(preview.locator('body')).toContainText(fixture('IMPORT_NUMBER'));
});

test('真实待办驳回经 Worker 回写终态并生成单据通知', async ({ page }) => {
  test.setTimeout(120000); await clickMainNavLink(page, /企业申请/);
  await page.getByRole('button', { name: '创建主从单据', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '申请单与明细', exact: true });
  const number = `REJ-${Date.now()}`, title = '真实驳回交付';
  await dialog.getByTestId('aggregate-organization').fill(fixture('UNIT_ID'));
  await dialog.getByTestId('aggregate-number').fill(number); await dialog.getByTestId('aggregate-title').fill(title);
  const creation = page.waitForResponse(r => new URL(r.url()).pathname === `${root}/aggregate` && r.request().method() === 'POST');
  await dialog.getByTestId('aggregate-save').click(); const created = await creation;
  expect(created.status(), await created.text()).toBe(200); const document = await created.json(); await expect(dialog).not.toBeVisible();
  const row = page.locator('.generated-crud-view .el-table__row').filter({ hasText: number });
  await row.getByRole('button', { name: '提交审批', exact: true }).click(); await page.getByRole('dialog', { name: '确认提交审批', exact: true }).getByRole('button', { name: '确认提交', exact: true }).click(); await expect(row).toContainText('Submitted');
  await clickMainNavLink(page, /我的待办/);
  const todo = page.locator('.workflow-todos__table .el-table__row').filter({ hasText: title });
  await expect.poll(async () => {
    await page.getByTestId('workflow-todo-filter-apply').click(); return todo.count();
  }, { timeout: 30000, intervals: [1000] }).toBe(1);
  await todo.getByTestId('workflow-todo-open').click(); await page.getByTestId('workflow-todo-comment').fill('资料不足，驳回申请');
  const rejecting = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/reject') && r.request().method() === 'POST');
  await page.getByTestId('workflow-todo-reject').click(); const rejected = await rejecting;
  expect(rejected.status(), await rejected.text()).toBe(200);
  await page.keyboard.press('Escape'); await clickMainNavLink(page, /企业申请/);
  await expect.poll(async () => { await page.reload(); return row.textContent(); }, { timeout: 30000, intervals: [1000] }).toContain('Rejected');
  await row.getByRole('button', { name: '主从明细', exact: true }).click(); await expect(dialog).toBeVisible();
  await expect(dialog.getByTestId('aggregate-save')).toHaveCount(0); await expect(dialog).toContainText('已驳回');
  await page.keyboard.press('Escape'); await clickMainNavLink(page, /消息中心/);
  await expect(page.locator('.inbox-messages-view .el-table__row').filter({ hasText: '审批已驳回' })
    .filter({ hasText: document.master.id })).toBeVisible({ timeout: 30000 });
});
