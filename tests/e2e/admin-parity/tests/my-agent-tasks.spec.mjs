import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
const run = (n, statusKey) => ({ id: id(n), definitionKey: `workflow-${n}`, definitionVersion: 2, statusKey,
  deadlineAtUtc: '2026-11-05T01:00:00Z', createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: '2026-11-05T00:00:00Z' });

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`AI 本人来源独立分页筛选与详情重读 ${width}`, async ({ page }, info) => {
    const queries = []; const details = []; const writes = [];
    page.on('request', request => {
      if (request.url().includes('/api/v1/') && request.method() !== 'GET' && !request.url().includes('/auth/refresh')) writes.push(request.url());
    });
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => json(route, { status: 404, title: 'not found' }, 404));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(1), username: 'tasks', displayName: 'Tasks reader', actorScope: 'tenant',
      scope: `tenant:${id(2).replaceAll('-', '')}`, tenantId: id(2), sessionId: id(3), isSuperAdministrator: false, passwordChangeRequired: false,
      preferredLocale: locale, profileVersion: 1, permissions: ['identity.navigation.read', 'platform.my_tasks.read', 'import_export.import_tasks.read',
        'reporting.export_tasks.read', 'ai.agent_runs.read'] }));
    const nodes = [['my-tasks', '/platform/my-tasks', 'platform.my_tasks.read'], ['ai-agent-runs', '/ai/agent-runs', 'ai.agent_runs.read']];
    await page.route('**/api/v1/navigation', route => json(route, nodes.map(([key, path, permission], order) => ({ id: key, parentId: null,
      routeName: key, path, componentKey: key, title: key, caption: '', icon: 'collection', order, requiredPermission: permission, children: [] }))));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/import-export/tasks/mine?*', route => json(route, { items: [], nextCursor: null, hasMore: false }));
    await page.route('**/api/v1/reporting/export-tasks/mine?*', route => json(route, { status: 503, code: 'test.unavailable', title: 'unavailable' }, 503));
    await page.route('**/api/v1/ai/agent/runs/mine?*', route => {
      const params = Object.fromEntries(new URL(route.request().url()).searchParams); queries.push(params);
      expect(params).not.toHaveProperty('actorUserId'); expect(params).not.toHaveProperty('tenantId');
      const next = !params.cursor && !params.statusKey;
      return json(route, { items: [params.statusKey ? run(12, 'completed') : params.cursor ? run(11, 'reconciliation_required') : run(10, 'awaiting_approval')],
        nextCursor: next ? 'next-ai' : null, hasMore: next });
    });
    await page.route(`**/api/v1/ai/agent/runs/${id(12)}`, route => { details.push(route.request().url()); return json(route, run(12, 'completed')); });
    await page.route(`**/api/v1/ai/agent/runs/${id(12)}/events/stream`, route => route.fulfill({ status: 200, contentType: 'text/event-stream', body: '' }));
    await page.goto('/platform/my-tasks');
    await page.getByRole('link', { name: locale === 'en-US' ? 'My tasks' : '我的任务', exact: true }).click();
    await page.setViewportSize({ width, height: 1000 });
    const panel = page.locator('.my-task-source').filter({ has: page.getByRole('heading', { name: locale === 'en-US' ? 'AI runs' : 'AI 运行', exact: true }) });
    await expect(panel).toContainText(locale === 'en-US' ? 'Awaiting approval' : '等待审批');
    await expect(page.locator('.my-task-source').filter({ hasText: locale === 'en-US' ? 'Report exports' : '报表导出' })).toContainText(locale === 'en-US' ? 'This source is unavailable' : '此来源暂时无法读取');
    await expect(panel.getByTestId('my-tasks-execute')).toHaveCount(0); await expect(panel.getByTestId('my-tasks-cancel')).toHaveCount(0);
    await panel.getByTestId('my-tasks-next').click(); await expect(panel).toContainText('workflow-11');
    await expect(panel).toContainText(locale === 'en-US' ? 'Reconciliation required' : '需要核证');
    await panel.getByTestId('my-tasks-previous').click(); await expect(panel).toContainText('workflow-10');
    const status = panel.getByTestId('my-tasks-status').getByRole('combobox'); await status.focus(); await status.press('ArrowDown');
    await page.getByRole('option', { name: locale === 'en-US' ? 'Completed' : '已完成', exact: true }).click();
    await expect(panel).toContainText('workflow-12');
    await expect(panel.getByTestId('my-tasks-previous')).toBeDisabled();
    await panel.screenshot({ path: info.outputPath(`my-agent-tasks-${width}.png`), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.my-task-source:last-child').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await panel.locator('.el-table__body-wrapper').getByTestId('my-tasks-detail').click();
    await expect(page.getByTestId('ai-agent-runs-id')).toHaveValue(id(12));
    await expect(page.getByTestId('ai-agent-runs-create')).toHaveCount(0);
    await expect.poll(() => details.length).toBeGreaterThan(0);
    expect(queries).toEqual([{ pageSize: '20' }, { pageSize: '20', cursor: 'next-ai' }, { pageSize: '20' }, { pageSize: '20', statusKey: 'completed' }]);
    expect(writes).toEqual([]);
  });
}
