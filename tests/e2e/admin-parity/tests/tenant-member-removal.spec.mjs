import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', user = '019bc2b1-2a40-7cc3-8992-a80de51bf298', now = '2026-11-04T00:00:00Z';
const row = { id, tenantId: id, userId: user, username: 'member', displayName: '待移除成员', memberRole: 'Member', status: 'Active',
  createdAtUtc: now, updatedAtUtc: now, version: 4 };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page) {
  const state = { reads: 0, writes: [], version: 4, status: 'Active', owner: false, pending: undefined };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json(route, { id, username: 'admin', displayName: '管理员', actorScope: 'host', scope: `tenant:${id.replaceAll('-', '')}`,
      tenantId: id, sessionId: id, isSuperAdministrator: false, passwordChangeRequired: false, profileVersion: 1, preferredLocale: 'zh-CN',
      permissions: ['identity.tenant_members.read', 'identity.tenant_members.remove'] });
    if (path === '/api/v1/navigation') return json(route, [{ id: 'members', parentId: null, routeName: 'tenant-members', path: '/identity/tenant-members',
      componentKey: 'tenant-members', title: '企业成员', caption: '', icon: 'user', order: 1, requiredPermission: 'identity.tenant_members.read', children: [] }]);
    if (path === '/api/v1/identity/tenant-members') return json(route, { items: [{ ...row, version: state.version, status: state.status }], total: 1, page: 1, pageSize: 20 });
    if (path === '/api/v1/identity/tenant-members/invitations') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
    if (path === `/api/v1/identity/tenant-members/${id}`) {
      if (route.request().method() === 'GET') { state.reads++; return json(route, { ...row, version: state.version, status: state.status, memberRole: state.owner ? 'Owner' : 'Member' }); }
      const version = Number(new URL(route.request().url()).searchParams.get('version')); state.writes.push(version);
      if (state.pending) { await state.pending; state.status = 'Removed'; state.version = version + 1;
        return json(route, { ...row, status: state.status, version: state.version }).catch(() => {}); }
      if (state.writes.length === 1) { state.version = 6; return json(route, { status: 409, title: '成员版本已变化', code: 'identity.tenant_member_version_conflict' }, 409); }
      state.status = 'Removed'; state.version = version + 1; return json(route, { ...row, status: state.status, version: state.version });
    }
    if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
    if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
    return json(route, []);
  });
  await page.goto('/#/identity/tenant-members'); await expect(page.getByTestId('tenant-members-action-remove')).toBeVisible(); return state;
}
async function fits(page) {
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect.poll(() => page.getByRole('dialog').evaluate(element => {
    const box = element.getBoundingClientRect(); return box.x >= 0 && box.right <= innerWidth;
  })).toBe(true);
}
test('读取成员再明确移除，冲突核对后固定新版本并保留回执', async ({ page }, info) => {
  const state = await boot(page); await page.getByTestId('tenant-members-action-remove').click(); await fits(page);
  const panel = page.getByTestId('tenant-member-removal-panel'); await expect(panel).toContainText('成员版本 4');
  expect(state.writes).toEqual([]); expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
  await page.getByTestId('tenant-member-removal-confirm').click(); await expect(panel).toContainText('成员版本已变化');
  expect(state.writes).toEqual([4]); expect(state.reads).toBe(1); await expect(page.getByTestId('tenant-member-removal-confirm')).toHaveCount(0);
  await page.getByTestId('tenant-member-removal-refresh').click(); await expect(panel).toContainText('成员版本 6');
  await page.screenshot({ path: info.outputPath('removal-confirm.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-member-removal-confirm').focus(); await page.keyboard.press('Enter');
  await expect(panel).toContainText('成员已移除'); expect(state.writes).toEqual([4, 6]);
  await expect(page.getByTestId('tenant-members-action-remove')).toHaveCount(0);
});
test('在途关闭取消HTTP，重开核对Removed事实而不重复移除', async ({ page }, info) => {
  const state = await boot(page); let release; state.pending = new Promise(resolve => { release = resolve; }); let cancelled = false;
  page.on('requestfailed', request => { if (request.method() === 'DELETE') cancelled = true; });
  await page.getByTestId('tenant-members-action-remove').click(); await page.getByTestId('tenant-member-removal-confirm').click();
  await expect.poll(() => state.writes.length).toBe(1); await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click();
  await expect.poll(() => cancelled).toBe(true); release(); await expect.poll(() => state.status).toBe('Removed');
  await page.getByTestId('tenant-members-action-remove').click(); await fits(page);
  await expect(page.getByTestId('tenant-member-removal-panel')).toContainText('移除结果尚未确认'); expect(state.reads).toBe(1); expect(state.writes).toEqual([4]);
  await page.screenshot({ path: info.outputPath('removal-unknown.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-member-removal-refresh').click(); await expect(page.getByTestId('tenant-member-removal-panel')).toContainText('当前成员状态为已移除');
  await expect(page.getByTestId('tenant-member-removal-confirm')).toHaveCount(0); expect(state.writes).toEqual([4]);
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('详情已成为所有者时保护生效，不提供移除确认', async ({ page }) => {
  const state = await boot(page); state.owner = true; await page.getByTestId('tenant-members-action-remove').click(); await fits(page);
  await expect(page.getByTestId('tenant-member-removal-panel')).toContainText('所有者受到保护');
  await expect(page.getByTestId('tenant-member-removal-confirm')).toHaveCount(0); expect(state.writes).toEqual([]);
});
