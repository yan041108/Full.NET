import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, enterDevelopmentTenant, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {clickEnterpriseRequestAction} from './support/enterprise-request-actions.mjs';

test('业务申请在退回轮次中保持冻结，幂等重投后重新办理并回写通知', async ({ page }) => {
  test.setTimeout(150_000);
  const root = '/api/v1/enterprise_request/enterprise-requests', evidence = process.env.FULLNET_ENTERPRISE_RETURN_UI_EVIDENCE;
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page); await enterDevelopmentTenant(page, 'Acme Corporation');
  await clickMainNavLink(page, /企业申请/); await page.getByRole('button', { name: '创建主从单据', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '申请单与明细', exact: true }), number = `RETURN-${Date.now()}`, title = '退回轮次业务闭包';
  await editor.getByTestId('aggregate-organization').fill(process.env.FULLNET_E2E_ENTERPRISE_UNIT_ID);
  await editor.getByTestId('aggregate-number').fill(number); await editor.getByTestId('aggregate-title').fill(title);
  const creating = page.waitForResponse(response => new URL(response.url()).pathname === `${root}/aggregate` && response.request().method() === 'POST');
  await editor.getByTestId('aggregate-save').click(); const created = await creating; expect(created.status(), await created.text()).toBe(200);
  const document = await created.json(); await expect(editor).not.toBeVisible();
  const row = page.locator('.generated-crud-view .el-table__row').filter({ hasText: number });
  await clickEnterpriseRequestAction(page,row,'提交审批'); await page.getByRole('dialog', { name: '确认提交审批', exact: true }).getByRole('button', { name: '确认提交', exact: true }).click(); await expect(row).toContainText('已提交');
  await clickMainNavLink(page, /我的待办/);
  const todo = page.locator('.workflow-todos__table .el-table__row').filter({ hasText: title });
  async function openTodo() {
    await expect.poll(async () => { await page.getByTestId('workflow-todo-filter-apply').click(); return todo.count(); }, { timeout: 30_000, intervals: [500] }).toBe(1);
    await todo.getByTestId('workflow-todo-open').click(); await expect(page.getByTestId('workflow-todo-comment')).toBeVisible();
  }
  async function approve() {
    await openTodo(); await page.getByTestId('workflow-todo-comment').fill('重新核对业务资料');
    const pending = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/approve') && response.request().method() === 'POST');
    await page.getByTestId('workflow-todo-approve').click(); const response = await pending; expect(response.status(), await response.text()).toBe(200); await page.keyboard.press('Escape');
  }
  await approve(); await openTodo();
  const target = page.getByTestId('workflow-todo-return-target'); await expect(target.locator('option')).toHaveCount(2);
  await target.selectOption({ index: 1 }); await page.getByTestId('workflow-todo-comment').fill('退回前级重新核对');
  const accessibility = await new AxeBuilder({ page }).include('.workflow-todos').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'enterprise-return-desktop.png'), animations: 'disabled' });
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await target.scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, 'enterprise-return-mobile.png'), animations: 'disabled' }); await page.setViewportSize({ width: 1440, height: 1000 });
  let replayed = false;
  // 用真实 HTTP 重投相同幂等请求；后台可同时推进轮次，第二次仍必须返回原固定回执。
  await page.route('**/api/v1/workflow/todos/*/return', async route => {
    const first = await route.fetch(), second = await route.fetch(); expect(first.status(), await first.text()).toBe(200);
    expect(second.status(), await second.text()).toBe(200); expect(await second.json()).toEqual(await first.json()); replayed = true;
    await route.fulfill({ response: first });
  });
  const returning = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/return') && response.request().method() === 'POST');
  await page.getByTestId('workflow-todo-return').click(); expect((await returning).status()).toBe(200); expect(replayed).toBe(true); await page.keyboard.press('Escape');
  await clickMainNavLink(page, /企业申请/); await expect(row).toContainText('已提交'); await row.getByRole('button', { name: '主从明细', exact: true }).click();
  await expect(editor).toContainText('当前状态或权限仅允许查看'); await expect(editor.getByTestId('aggregate-save')).toHaveCount(0); await page.keyboard.press('Escape');
  await clickMainNavLink(page, /我的待办/); await approve(); await approve();
  await clickMainNavLink(page, /企业申请/);
  await expect.poll(async () => { await page.reload(); return row.textContent(); }, { timeout: 30_000, intervals: [1000] }).toContain('已通过');
  await clickMainNavLink(page, /消息中心/); await expect(page.locator('.inbox-messages-view .el-table__row').filter({ hasText: '审批已完成' }).filter({ hasText: document.master.id })).toBeVisible({ timeout: 30_000 });
});
