import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const grantId = id(1), tenantId = id(2), actor = id(3), expiry = '2099-01-01T00:00:00Z';
async function setup(page, revoke = true) {
  const requests = []; let current = { id: grantId, revision: 1, statusKey: 'proposed', sourceTenantScopeKey: 'host', targetTenantScopeKey: `tenant:${tenantId.replaceAll('-', '')}`,
    sourceActorId: actor, targetActorId: null, offer: { schemaVersion: 1, sourceDefinitionId: id(4), sourceFormVersionId: id(5), targetWorkflowKey: 'approval', inputFieldKeys: ['title'], outputFieldKeys: ['title'], maximumTargetActivations: 64, expiresAtUtc: expiry },
    acceptance: null, policyHash: null, targetSummary: null, expiresAtUtc: expiry, createdAtUtc: '2026-10-28T00:00:00Z', acceptedAtUtc: null, revokedAtUtc: null, revokedById: null, revokedByScopeKey: null };
  const json = (route, value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'grant-test', tokenType: 'Bearer', expiresAtUtc: expiry }));
  await page.route('**/api/v1/me', route => json(route, { id: actor, username: 'collaboration', displayName: '协作授权验收', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    permissions: ['identity.navigation.read', 'workflow.collaboration_grants.read', ...(revoke ? ['workflow.collaboration_grants.revoke'] : [])], sessionId: id(6), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'grants', parentId: null, routeName: 'workflow-collaboration-grants', path: '/workflow/collaboration-grants', componentKey: 'workflow-collaboration-grants', title: '协作授权', caption: '双方同意', icon: 'workflow', order: 1, requiredPermission: 'workflow.collaboration_grants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/workflow/collaboration-grants/**', route => {
    if (route.request().method() === 'GET') return json(route, current);
    requests.push(route.request().postDataJSON());
    current = { ...current, revision: 2, statusKey: 'revoked', revokedAtUtc: '2026-10-28T01:00:00Z', revokedById: actor, revokedByScopeKey: 'host' };
    return json(route, current);
  });
  await page.goto('/#/workflow/collaboration-grants');
  await page.getByTestId('grant-id').fill(grantId); await page.getByTestId('grant-load').click();
  await expect(page.getByTestId('grant-detail')).toContainText('待接受'); return requests;
}
test('读取政策、键盘撤销和原回执可见，页面无可访问性错误', async ({ page }) => {
  const requests = await setup(page);
  await page.getByTestId('grant-reason').fill('双方终止共享');
  await page.getByTestId('grant-revoke').focus(); await page.keyboard.press('Enter');
  await expect(page.getByTestId('grant-detail')).toContainText('已撤销'); await expect(page.getByTestId('grant-receipt')).toContainText(grantId);
  expect(requests).toEqual([expect.objectContaining({ expectedRevision: 1, reason: '双方终止共享', idempotencyKey: expect.any(String) })]);
  await expect(page.getByTestId('grant-detail').locator('.el-tag')).not.toHaveClass(/enter-active|leave-active/u);
  expect((await new AxeBuilder({ page }).include('[data-testid="collaboration-page"]').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
});
test('无撤销权限不创建操作入口，移动视口可读完整政策', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); const requests = await setup(page, false);
  await expect(page.getByTestId('grant-revoke')).toHaveCount(0); await expect(page.getByTestId('grant-propose')).toHaveCount(0);
  expect(requests).toHaveLength(0); expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
});
