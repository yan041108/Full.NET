import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const date = '2026-10-10T00:00:00Z';
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
const cases = [
  { module: 'reporting', entity: 'definitions', objectKey: 'definitionId', path: '/reporting/definitions', component: 'reporting-definitions',
    value: { id, groupId: id, dataSourceId: id, definitionKey: 'fixture', name: '报表授权样例', description: null,
      queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}', latestPublishedVersionNumber: 2,
      isEnabled: true, createdAtUtc: date, updatedAtUtc: null, version: 1 },
    version: { dataSourceId: id, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}' } },
  { module: 'printing', entity: 'templates', objectKey: 'templateId', path: '/printing/preview', component: 'printing-preview',
    value: { id, templateKey: 'fixture', name: '打印授权样例', formSchemaKey: 'tenant-profile', layoutHtml: '<p>Frozen</p>',
      latestPublishedVersionNumber: 2, isEnabled: true, createdAtUtc: date, updatedAtUtc: null, version: 1 },
    version: { layoutHtml: '<p>Frozen</p>' } }
];

async function boot(page, item) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions: [`${item.module}.${item.entity}.read`, `${item.module}.${item.entity}.grant_tenants`],
    sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'fixture', parentId: null, routeName: item.component,
    path: item.path, componentKey: item.component, title: '授权样例', caption: '', icon: 'document', order: 1,
    requiredPermission: `${item.module}.${item.entity}.read`, children: [] }]));
  await page.route(`**/api/v1/${item.module}/${item.entity}`, route => json(route, [item.value]));
  await page.route(`**/api/v1/${item.module}/${item.entity}?*`, route => json(route, [item.value]));
  await page.route(`**/api/v1/${item.module}/${item.entity}/${id}/versions`, route => json(route, [1, 2].map(versionNumber => ({
    id, [item.objectKey]: id, versionNumber, ...item.version, changeNote: null, publishedByUserId: id, publishedAtUtc: date }))));
  await page.route(`**/api/v1/${item.module}/${item.entity}/${id}/versions/*/tenant-grants?*`, route => json(route, {
    items: [tenant], page: 1, pageSize: 20, total: 1 }));
  await page.goto(`/#${item.path}`);
  await page.getByTestId(`${item.module}-tenant-grants-open`).click();
  await expect(page.getByTestId(`${item.module}-grant-revoke`)).toBeVisible();
}

for (const item of cases) {
  test(`${item.module} 冻结版本撤销确认可取消，重复确认只发送一次`, async ({ page }) => {
    await boot(page, item); let writes = 0; let release;
    const pending = new Promise(resolve => { release = resolve; });
    await page.route(`**/api/v1/${item.module}/${item.entity}/${id}/versions/2/tenant-grants/${tenant}`, async route => {
      expect(route.request().method()).toBe('DELETE'); writes++; await pending; await json(route, true);
    });
    await page.getByTestId(`${item.module}-grant-revoke`).click();
    const confirmation = page.getByRole('dialog').filter({ has: page.getByTestId(`${item.module}-grant-confirm-revoke`) });
    await confirmation.getByRole('button', { name: '取消', exact: true }).click();
    await expect(confirmation).toHaveCount(0); expect(writes).toBe(0);
    await page.getByTestId(`${item.module}-grant-revoke`).click();
    await page.getByTestId(`${item.module}-grant-confirm-revoke`).evaluate(button => { button.click(); button.click(); });
    await expect.poll(() => writes).toBe(1); release();
    await expect(page.locator('.el-message--success')).toBeVisible();
    await expect(page.getByTestId(`${item.module}-grant-revoke`)).toBeEnabled(); expect(writes).toBe(1);
  });

  test(`${item.module} 关闭授权弹窗忽略迟到失败，再打开仍能读取`, async ({ page }) => {
    await boot(page, item); let writes = 0; let release; let responseFinished = false;
    const pending = new Promise(resolve => { release = resolve; });
    await page.route(`**/api/v1/${item.module}/${item.entity}/${id}/versions/2/tenant-grants/${tenant}`, async route => {
      expect(route.request().method()).toBe('PUT'); writes++; await pending;
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({
        status: 503, code: 'fixture.old_target', title: '旧授权失败' }) }).catch(() => {});
      responseFinished = true;
    });
    await page.getByTestId(`${item.module}-grant-tenant`).fill(tenant);
    await page.getByTestId(`${item.module}-grant-save`).evaluate(button => { button.click(); button.click(); });
    await expect.poll(() => writes).toBe(1);
    await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0); release(); await expect.poll(() => responseFinished).toBe(true);
    await expect(page.locator('.el-message--error')).toHaveCount(0);
    await page.getByTestId(`${item.module}-tenant-grants-open`).click();
    await expect(page.getByTestId(`${item.module}-grant-revoke`)).toBeVisible();
    await expect(page.getByTestId(`${item.module}-grant-tenant`)).toHaveValue(''); expect(writes).toBe(1);
  });
}
