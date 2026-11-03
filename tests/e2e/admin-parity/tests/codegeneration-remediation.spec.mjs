import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const actor = '01912345-6789-7abc-8def-0123456789ab';
const previewId = '01912345-6789-7abc-8def-0123456789ac';
const planId = '01912345-6789-7abc-8def-0123456789ad';
const workspaceId = '01912345-6789-7abc-8def-0123456789ae';

// 此处只验收浏览器交互，HTTP 由受控夹具响应；文件恢复与双库行为另有真实测试。
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`代码生成冻结计划、历史到期与显式恢复 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const plans = [], applies = [], recoveries = [];
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    const idle = { workspaceId, revision: 2, tipOperationId: planId, lastOperationId: planId, pendingOperationId: null, pendingKind: null, pendingClient: null, phase: 'idle' };
    let workspace = { ...idle, pendingOperationId: planId, pendingKind: 'apply', pendingClient: 'host', phase: 'locally_applied' };
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'codegen-browser-fixture', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
    await page.route('**/api/v1/me', route => json(route, { id: actor, username: 'codegen-browser', displayName: '生成验收', tenantId: null,
      actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, sessionId: actor, preferredLocale: 'zh-CN', profileVersion: 1,
      permissions: ['identity.navigation.read', 'codegen.previews.read', 'codegen.templates.read', 'codegen.runs.read', 'codegen.runs.execute',
        'codegen.runs.apply', 'codegen.runs.rollback', 'codegen.runs.download'] }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'codegen', parentId: null, routeName: 'code-generation-previews',
      path: '/code-generation/previews', componentKey: 'code-generation-previews', title: '代码生成', caption: '代码生成验收', icon: 'code', order: 1,
      requiredPermission: 'codegen.previews.read', children: [] }]));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/code-generation/templates?*', route => json(route, { items: [], page: 1, pageSize: 100, total: 0 }));
    await page.route('**/api/v1/code-generation/runs?*', route => json(route, { items: [{ id: previewId, templateId: null, templateVersion: null,
      operationKind: 'preview', status: 'succeeded', moduleKey: 'catalog', entityKey: 'product', schemaSha256: 'a'.repeat(64), artifactCount: 1,
      manifestSha256: 'b'.repeat(64), errorCode: null, requestedByUserId: actor, startedAtUtc: '2026-10-26T00:00:00Z', finishedAtUtc: '2026-10-26T00:00:01Z',
      sourceApplyRunId: null, publicationStatus: 'unavailable', commitSha: null }], page: 1, pageSize: 20, total: 1 }));
    await page.route('**/api/v1/code-generation/runs/workspace', route => json(route, workspace));
    await page.route('**/api/v1/code-generation/runs/workspace/recover', route => {
      recoveries.push(route.request().postDataJSON()); workspace = idle; return json(route, workspace);
    });
    await page.route(`**/api/v1/code-generation/runs/${previewId}/artifacts.zip`, route => json(route,
      { type: 'about:blank', title: 'Expired', status: 409, code: 'codegen.run.archive_expired' }, 409));
    await page.route('**/api/v1/code-generation/runs/preview', route => {
      expect(route.request().postDataJSON().schema).toBeTruthy();
      return json(route, { runId: previewId, preview: { databaseTableName: 'acme_catalog_product', readPermission: 'catalog.products.read', writePermission: 'catalog.products.write',
        artifacts: [{ path: 'backend/Product.g.cs', kind: 'backend', sha256: 'a'.repeat(64), content: 'immutable preview source\n' }] } });
    });
    await page.route('**/api/v1/code-generation/runs/apply-plan', route => {
      plans.push(route.request().postDataJSON());
      return json(route, { planId, previewRunId: previewId, workspaceId, workspaceRevision: 2, generatorVersion: '2026.10.24.1', planSha256: 'd'.repeat(64), expiresAtUtc: '2099-01-01T00:00:00Z',
        files: [{ relativePath: 'generated/acme/catalog/product/backend/ProductWithLongDescriptiveName.g.cs', kind: 'create', beforeSha256: null, afterSha256: 'e'.repeat(64) },
          { relativePath: '.fullnet/codegeneration-manifest.json', kind: 'manifest', beforeSha256: 'f'.repeat(64), afterSha256: 'a'.repeat(64) }] });
    });
    await page.route('**/api/v1/code-generation/runs/apply', route => {
      applies.push(route.request().postDataJSON());
      return json(route, { runId: planId, previewRunId: previewId, artifactCount: 1, changedArtifactCount: 1, manifestSha256: 'b'.repeat(64), publicationStatus: 'pending', commitSha: 'a'.repeat(40) });
    });
    await page.goto('/#/code-generation/previews');
    await expect(page.getByTestId('codegen-workspace-recover')).toBeVisible();
    await page.getByTestId('codegen-workspace-recover').click();
    await page.getByRole('dialog').getByRole('button', { name: '恢复已保存的操作', exact: true }).click();
    await expect.poll(() => recoveries).toEqual([{ operationId: planId }]);
    await expect(page.getByText('本地操作已恢复，请查看运行记录和发布状态。')).toBeVisible();
    expect(applies).toHaveLength(0);
    await page.getByTestId('codegen-history-download').click();
    await expect(page.getByRole('alert')).toContainText('保留期限');
    await page.getByTestId('codegen-preview').click();
    await expect(page.getByTestId('codegen-content')).toContainText('immutable preview source');
    await page.getByTestId('codegen-apply').click();
    const review = page.getByTestId('codegen-apply-plan');
    await expect(review).toBeVisible();
    expect(plans).toEqual([{ previewRunId: previewId }]);
    expect(applies).toHaveLength(0);
    await expect(review).toContainText('d'.repeat(64));
    await expect(review).toContainText('ProductWithLongDescriptiveName.g.cs');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('review.png'), fullPage: true });
    const a11y = await new AxeBuilder({ page }).include('.codegen-workbench').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
    expect(a11y.violations).toEqual([]);
    await page.getByTestId('codegen-apply-plan-confirm').focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: '应用已审查预览', exact: true }).click();
    await expect.poll(() => applies.length).toBe(1);
    expect(applies[0]).toEqual({ planId, planSha256: 'd'.repeat(64), idempotencyKey: expect.any(String) });
    await expect(page.getByTestId('codegen-publication-status')).toContainText('待发布');
    expect(errors).toEqual([]);
  });
}
