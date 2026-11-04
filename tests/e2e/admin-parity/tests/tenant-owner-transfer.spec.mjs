import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`, now = '2026-11-04T00:00:00Z';
const actor = id(1), binding = id(2), tenant = id(3), member = id(4), recipient = id(5), source = id(6);
const target = { id: member, tenantId: tenant, userId: recipient, username: 'receiving-member', displayName: '接收成员',
  memberRole: 'Member', status: 'Active', version: 4, createdAtUtc: now, updatedAtUtc: now };
const original = { ...target, id: source, userId: actor, username: 'current-owner', displayName: '原所有者', memberRole: 'Owner', version: 6 };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, initialize = false) {
  const state = { reads: 0, memberReads: 0, writes: 0, checks: 0, refreshes: 0, pending: undefined, receipt: undefined,
    initialize, owner: initialize ? null : { ...original }, target: { ...target }, unauthorized: false, denied: false };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/auth/refresh') { state.refreshes++; return json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }); }
    if (path === '/api/v1/me') return json(route, { id: actor, username: 'owner', displayName: '当前操作者', actorScope: 'host',
      scope: `tenant:${tenant.replaceAll('-', '')}`, tenantId: tenant, sessionId: binding, isSuperAdministrator: initialize,
      passwordChangeRequired: false, profileVersion: 1, preferredLocale: 'zh-CN',
      permissions: ['identity.tenant_members.read', 'identity.tenant_members.transfer_ownership'] });
    if (path === '/api/v1/navigation') return json(route, [{ id: 'members', parentId: null, routeName: 'tenant-members', path: '/identity/tenant-members',
      componentKey: 'tenant-members', title: '企业成员', caption: '', icon: 'user', order: 1, requiredPermission: 'identity.tenant_members.read', children: [] }]);
    if (path === '/api/v1/identity/tenant-members') return json(route, { items: initialize ? [state.target] : [original, state.target], total: initialize ? 1 : 2, page: 1, pageSize: 20 });
    if (path === '/api/v1/identity/tenant-members/invitations') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
    if (path === `/api/v1/identity/tenant-members/${member}`) { state.memberReads++; return json(route, state.target); }
    if (path === '/api/v1/identity/tenant-members/ownership') { state.reads++; return json(route, { tenantId: tenant, owner: state.owner,
      canInitialize: !state.owner && initialize, canTransfer: !state.denied && state.owner?.userId === actor, requiresTotp: !initialize }); }
    if (path.includes('/ownership/operations/')) { state.checks++; expect(path.split('/').at(-1)).toBe(state.receipt.operationId); return json(route, state.receipt); }
    if (path.endsWith('/ownership/transfer')) {
      expect(route.request().method()).toBe('POST'); const body = route.request().postDataJSON(); state.writes++;
      expect(body.targetMemberId).toBe(member); expect(body.targetVersion).toBe(4); expect(body.currentPassword).toBe('fixture-only-password');
      if (!initialize) { expect(body.sourceMemberId).toBe(source); expect(body.sourceVersion).toBe(6); expect(body.totpCode).toBe('593712'); }
      if (state.unauthorized) return json(route, { status: 401, title: '请重新验证', code: 'identity.reauthentication_failed' }, 401);
      if (state.pending) await state.pending;
      state.receipt = { tenantId: tenant, operationId: body.operationId, actorUserId: actor,
        sourceMemberId: initialize ? null : source, sourceUserId: initialize ? null : actor,
        sourcePreviousVersion: initialize ? null : 6, sourceVersion: initialize ? null : 7,
        targetMemberId: member, targetUserId: recipient, targetPreviousVersion: 4, targetVersion: 5, createdAtUtc: now };
      state.target = { ...target, memberRole: 'Owner', version: 5 }; state.owner = state.target;
      return json(route, state.receipt).catch(() => {});
    }
    if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
    if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
    return json(route, []);
  });
  await page.goto('/#/identity/tenant-members'); await expect(page.getByTestId('tenant-members-action-transfer-owner')).toBeVisible(); return state;
}
async function open(page) {
  await page.getByTestId('tenant-members-action-transfer-owner').click(); await expect(page.getByRole('dialog')).toBeVisible();
  await expect.poll(() => page.getByRole('dialog').evaluate(element => {
    const box = element.getBoundingClientRect();
    for (let node = element; node && node !== document.body; node = node.parentElement)
      if (Number(getComputedStyle(node).opacity) < 1 || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return box.x >= 0 && box.right <= innerWidth;
  })).toBe(true);
}
async function fill(page, totp = true) {
  await page.getByLabel('当前密码', { exact: true }).fill('fixture-only-password');
  if (totp) await page.getByLabel('当前 TOTP 验证码', { exact: true }).fill('593712');
}
test('真实当前 Owner 和接收版本、强认证与键盘提交', async ({ page }, info) => {
  const state = await boot(page); await open(page); const panel = page.getByTestId('tenant-owner-transfer-panel');
  await expect(panel).toContainText('原所有者'); await expect(panel).toContainText('版本 6'); await expect(panel).toContainText('版本 4');
  expect(state.writes).toBe(0); await expect(page.getByTestId('tenant-owner-transfer-confirm')).toHaveCount(0); await fill(page);
  expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
  await page.screenshot({ path: path.resolve(info.outputDir, '..', 'owner-confirm.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-owner-transfer-confirm').focus(); await page.keyboard.press('Enter');
  await expect(panel).toContainText('所有者职责已交接'); expect(state.writes).toBe(1); await expect(page.getByLabel('当前密码', { exact: true })).toHaveCount(0);
});
test('关闭取消 HTTP，重开保留原操作并人工核对回执', async ({ page }, info) => {
  const state = await boot(page); let release; state.pending = new Promise(resolve => { release = resolve; }); let cancelled = false;
  page.on('requestfailed', request => { if (request.url().endsWith('/ownership/transfer')) cancelled = true; });
  await open(page); await fill(page); await page.getByTestId('tenant-owner-transfer-confirm').click(); await expect.poll(() => state.writes).toBe(1);
  await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click(); await expect.poll(() => cancelled).toBe(true); release();
  await expect.poll(() => state.receipt !== undefined).toBe(true); await open(page);
  const panel = page.getByTestId('tenant-owner-transfer-panel'); await expect(panel).toContainText('本次结果未知');
  expect(state.reads).toBe(1); expect(state.memberReads).toBe(1); expect(state.writes).toBe(1);
  await page.screenshot({ path: path.resolve(info.outputDir, '..', 'owner-unknown.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-owner-transfer-check').click(); await expect(panel).toContainText('所有者职责已交接');
  expect(state.checks).toBe(1); expect(state.writes).toBe(1); await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('401 不刷新重放，凭据从窗口清除', async ({ page }) => {
  const state = await boot(page); state.unauthorized = true; await open(page); await fill(page); const refreshes = state.refreshes;
  await page.getByTestId('tenant-owner-transfer-confirm').click(); await expect(page.getByTestId('tenant-owner-transfer-panel')).toContainText('请重新验证');
  expect(state.writes).toBe(1); expect(state.refreshes).toBe(refreshes); await expect(page.getByLabel('当前密码', { exact: true })).toHaveCount(0);
});
test('当前账号不再是 Owner 时不能填写或提交', async ({ page }) => {
  const state = await boot(page); state.denied = true; await open(page);
  await expect(page.getByTestId('tenant-owner-transfer-panel')).toContainText('当前账号不能建立或交接');
  await expect(page.getByLabel('当前密码', { exact: true })).toHaveCount(0); await expect(page.getByTestId('tenant-owner-transfer-confirm')).toHaveCount(0); expect(state.writes).toBe(0);
});
test('首位 Owner 由明确资格建立，原职责为空', async ({ page }) => {
  const state = await boot(page, true); await open(page); await expect(page.getByTestId('tenant-owner-transfer-panel')).toContainText('当前尚无所有者职责');
  await fill(page, false); await page.getByTestId('tenant-owner-transfer-confirm').click();
  await expect(page.getByTestId('tenant-owner-transfer-panel')).toContainText('所有者职责已交接'); expect(state.receipt.sourceMemberId).toBeNull(); expect(state.writes).toBe(1);
});
