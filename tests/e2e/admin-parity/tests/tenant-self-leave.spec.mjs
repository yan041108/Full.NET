import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', now = '2026-11-04T00:00:00Z';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
const row = { id, userId: id, tenantId: id, username: 'member', displayName: '普通成员', memberRole: 'Member', status: 'Active', createdAtUtc: now, updatedAtUtc: now, version: 4 };
async function boot(page) {
  const state = { tenant: true, reads: 0, writes: [], version: 4, owner: false, pending: undefined, memberStatus: 'Active' };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json(route, { id, username: 'member', displayName: '普通成员', tenantId: state.tenant ? id : null,
      actorScope: 'host', scope: state.tenant ? `tenant:${id.replaceAll('-', '')}` : 'host', isSuperAdministrator: false,
      sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
      permissions: ['tenancy.tenants.read', 'tenancy.tenants.switch', 'identity.tenant_members.leave_self'] });
    if (path === '/api/v1/navigation') return json(route, [{ id: 'context', parentId: null, routeName: 'tenant-context', path: '/tenant-context',
      componentKey: 'tenant-context', title: '企业上下文', caption: '', icon: 'user', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]);
    if (path === '/api/v1/tenancy/available') return json(route, state.tenant ? [{ id, identifier: 'acme', name: 'Acme', domain: 'acme.localhost' }] : []);
    if (path === '/api/v1/tenancy/context') { expect(route.request().postDataJSON()).toEqual({ tenantId: null }); state.tenant = false;
      return json(route, { accessToken: 'host', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z', context: { scope: 'host', tenantId: null, identifier: 'host', name: 'Full.NET Host' } }); }
    if (path === '/api/v1/identity/tenant-members/me') { state.reads++; return json(route, { ...row, status: state.memberStatus, version: state.version, memberRole: state.owner ? 'Owner' : 'Member' }); }
    if (path === '/api/v1/identity/tenant-members/me/leave') {
      state.writes.push(route.request().postDataJSON()); if (state.pending) { await state.pending; state.memberStatus = 'Removed'; state.version = 5; return json(route, { ...row, status: 'Removed', version: 5 }).catch(() => {}); }
      if (state.writes.length === 1) { state.version = 6; return json(route, { status: 409, title: '成员版本已变化', code: 'identity.tenant_member_version_conflict' }, 409); }
      return json(route, { ...row, status: 'Removed', version: 7 });
    }
    if (path === '/api/v1/me/tenant-invitations') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
    if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
    if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
    return json(route, []);
  });
  await page.goto('/#/tenant-context'); await expect(page.getByTestId('leave-current-tenant')).toBeVisible(); return state;
}
async function fits(page) {
  const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
  await expect.poll(() => dialog.evaluate(element => { const box = element.getBoundingClientRect();
    for (let node = element; node && node !== document.body; node = node.parentElement)
      if (Number(getComputedStyle(node).opacity) < 1 || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return box.x >= 0 && box.right <= innerWidth;
  })).toBe(true);
}
test('本人退出显示原企业与成员版本，冲突人工重读后使用新版本并返回平台', async ({ page }, info) => {
  const state = await boot(page); await page.getByTestId('leave-current-tenant').click(); await fits(page);
  await expect(page.getByTestId('tenant-leave-panel')).toContainText('成员版本 4'); expect(state.writes).toEqual([]);
  expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
  await page.getByTestId('tenant-leave-confirm').click(); await expect(page.getByRole('alert').filter({ hasText: '成员版本已变化' })).toBeVisible();
  expect(state.writes).toEqual([{ version: 4 }]); expect(state.reads).toBe(1);
  await page.getByTestId('tenant-leave-refresh').click(); await expect(page.getByTestId('tenant-leave-panel')).toContainText('成员版本 6');
  await page.screenshot({ path: info.outputPath('leave-confirm.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-leave-confirm').focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByTestId('leave-current-tenant')).toHaveCount(0);
  expect(state.writes).toEqual([{ version: 4 }, { version: 6 }]); expect(state.tenant).toBe(false);
});
test('在途退出关闭取消 HTTP，重开保留未知结果且不会自动重发', async ({ page }, info) => {
  const state = await boot(page); let release; state.pending = new Promise(resolve => { release = resolve; }); let cancelled = false;
  page.on('requestfailed', request => { if (request.url().endsWith('/tenant-members/me/leave')) cancelled = true; });
  await page.getByTestId('leave-current-tenant').click(); await page.getByTestId('tenant-leave-confirm').click(); await expect.poll(() => state.writes.length).toBe(1);
  await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click(); await expect.poll(() => cancelled).toBe(true); release();
  await page.getByTestId('leave-current-tenant').click(); await fits(page); await expect(page.getByTestId('tenant-leave-panel')).toContainText('退出结果尚未确认');
  await expect(page.getByTestId('tenant-leave-confirm')).toHaveCount(0); expect(state.writes).toHaveLength(1); expect(state.reads).toBe(1);
  await page.screenshot({ path: info.outputPath('leave-unknown.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-leave-refresh').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('leave-current-tenant')).toHaveCount(0); expect(state.writes).toHaveLength(1);
});
test('所有者看到保护原因，关闭不会退出企业', async ({ page }) => {
  const state = await boot(page); state.owner = true; await page.getByTestId('leave-current-tenant').click(); await fits(page);
  await expect(page.getByTestId('tenant-leave-panel')).toContainText('所有者'); await expect(page.getByTestId('tenant-leave-confirm')).toHaveCount(0);
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0); expect(state.writes).toEqual([]);
});
