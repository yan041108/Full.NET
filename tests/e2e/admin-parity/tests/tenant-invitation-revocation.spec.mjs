import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', now = '2026-11-04T00:00:00Z';
const invitation = { id, tenantId: id, targetUserId: null, invitedByUserId: id, targetEmail: 'invited-member@example.test', memberRole: 'Member',
  status: 'Pending', expiresAtUtc: '2030-01-01T00:00:00Z', createdAtUtc: now, updatedAtUtc: now, version: 4 };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page) {
  const state = { reads: 0, writes: 0, status: 'Pending', version: 4, conflict: false, pending: undefined };
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json(route, { id, username: 'admin', displayName: '管理员', actorScope: 'host', scope: `tenant:${id.replaceAll('-', '')}`,
      tenantId: id, sessionId: id, isSuperAdministrator: false, passwordChangeRequired: false, profileVersion: 1, preferredLocale: 'zh-CN',
      permissions: ['identity.tenant_members.read', 'identity.tenant_members.revoke_invitation'] });
    if (path === '/api/v1/navigation') return json(route, [{ id: 'members', parentId: null, routeName: 'tenant-members', path: '/identity/tenant-members',
      componentKey: 'tenant-members', title: '企业成员', caption: '', icon: 'user', order: 1, requiredPermission: 'identity.tenant_members.read', children: [] }]);
    if (path === '/api/v1/identity/tenant-members') return json(route, { items: [], total: 0, page: 1, pageSize: 20 });
    if (path === '/api/v1/identity/tenant-members/invitations') return json(route, { items: [{ ...invitation, status: state.status, version: state.version }], total: 1, page: 1, pageSize: 20 });
    if (path === `/api/v1/identity/tenant-members/invitations/${id}`) { state.reads++; return json(route, { ...invitation, status: state.status, version: state.version }); }
    if (path === `/api/v1/identity/tenant-members/invitations/${id}/revoke`) {
      expect(route.request().method()).toBe('POST'); expect(route.request().postData()).toBeNull(); state.writes++;
      if (state.pending) await state.pending;
      if (state.conflict) { state.status = 'Accepted'; state.version = 5; return json(route, { status: 409, title: '邀请已变化', code: 'identity.tenant_invitations.invalid' }, 409); }
      state.status = 'Revoked'; state.version = 5; return json(route, { ...invitation, status: state.status, version: state.version }).catch(() => {});
    }
    if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
    if (path.endsWith('/session-policy')) return json(route, { loginPolicy: 0 });
    return json(route, []);
  });
  await page.goto('/#/identity/tenant-members'); await page.getByRole('tab', { name: '待处理邀请', exact: true }).click();
  await expect(page.getByTestId('tenant-members-action-revoke')).toBeVisible(); return state;
}
async function fits(page) {
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect.poll(() => page.getByRole('dialog').evaluate(element => {
    const box = element.getBoundingClientRect();
    for (let node = element; node && node !== document.body; node = node.parentElement)
      if (Number(getComputedStyle(node).opacity) < 1 || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return box.x >= 0 && box.right <= innerWidth;
  })).toBe(true);
}
test('核对邀请邮箱、身份与有效期后明确撤销并保留回执', async ({ page }, info) => {
  const state = await boot(page); await page.getByTestId('tenant-members-action-revoke').click(); await fits(page);
  const panel = page.getByTestId('tenant-invitation-revocation-panel'); await expect(panel).toContainText(invitation.targetEmail);
  await expect(panel).toContainText('受邀身份'); await expect(panel).toContainText('有效期至'); await expect(panel).toContainText('邀请版本 4'); expect(state.writes).toBe(0);
  expect((await new AxeBuilder({ page }).include('[role="dialog"]').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
  await page.screenshot({ path: info.outputPath('revocation-confirm.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-invitation-revocation-confirm').focus(); await page.keyboard.press('Enter'); await expect(panel).toContainText('邀请已撤销');
  await expect(page.getByTestId('tenant-members-action-revoke')).toHaveCount(0); expect(state.writes).toBe(1);
});
test('在途关闭取消HTTP，重开保留未知并通过读取Revoked核对事实', async ({ page }, info) => {
  const state = await boot(page); let release; state.pending = new Promise(resolve => { release = resolve; }); let cancelled = false;
  page.on('requestfailed', request => { if (request.url().endsWith('/revoke')) cancelled = true; });
  await page.getByTestId('tenant-members-action-revoke').click(); await page.getByTestId('tenant-invitation-revocation-confirm').click(); await expect.poll(() => state.writes).toBe(1);
  await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click(); await expect.poll(() => cancelled).toBe(true); release();
  await expect.poll(() => state.status).toBe('Revoked'); await page.getByTestId('tenant-members-action-revoke').click(); await fits(page);
  await expect(page.getByTestId('tenant-invitation-revocation-panel')).toContainText('撤销结果尚未确认'); expect(state.reads).toBe(1); expect(state.writes).toBe(1);
  await page.screenshot({ path: info.outputPath('revocation-unknown.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('tenant-invitation-revocation-refresh').click(); await expect(page.getByTestId('tenant-invitation-revocation-panel')).toContainText('当前邀请状态为已撤销');
  await expect(page.getByTestId('tenant-invitation-revocation-confirm')).toHaveCount(0); expect(state.writes).toBe(1);
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('接受竞争返回冲突，人工核对Accepted后不能再次撤销', async ({ page }) => {
  const state = await boot(page); state.conflict = true; await page.getByTestId('tenant-members-action-revoke').click();
  await page.getByTestId('tenant-invitation-revocation-confirm').click(); await expect(page.getByTestId('tenant-invitation-revocation-panel')).toContainText('邀请已变化');
  expect(state.reads).toBe(1); expect(state.writes).toBe(1); await page.getByTestId('tenant-invitation-revocation-refresh').click();
  await expect(page.getByTestId('tenant-invitation-revocation-panel')).toContainText('邀请已接受');
  await expect(page.getByTestId('tenant-invitation-revocation-confirm')).toHaveCount(0); expect(state.writes).toBe(1);
});
