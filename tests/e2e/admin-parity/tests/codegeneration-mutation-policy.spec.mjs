import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01912345-6789-7abc-8def-0123456789ab';
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });

// 受控 HTTP 只验证浏览器请求与交互；生成器规则由后端针对性测试验证。
for (const width of [1440, 390]) {
  test(`业务写入策略开关与预览请求 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    const errors = [], schemas = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/api/v1/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'policy-browser', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
      if (path === '/api/v1/me') return json(route, { id, username: 'generator', displayName: '生成开发者', tenantId: null,
        actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1,
        permissions: ['identity.navigation.read', 'codegen.previews.read', 'codegen.runs.execute'] });
      if (path === '/api/v1/navigation') return json(route, [{ id: 'codegen', parentId: null, routeName: 'code-generation-previews',
        path: '/code-generation/previews', componentKey: 'code-generation-previews', title: '代码生成', caption: '代码生成', icon: 'code', order: 1,
        requiredPermission: 'codegen.previews.read', children: [] }]);
      if (path === '/api/v1/code-generation/runs/preview') {
        schemas.push(route.request().postDataJSON().schema);
        return json(route, { runId: id, preview: { databaseTableName: 'acme_catalog_product', readPermission: 'catalog.products.read', writePermission: 'catalog.products.write',
          artifacts: [{ path: 'backend/ProductFeature.g.cs', kind: 'backend', sha256: 'a'.repeat(64), content: 'IProductMutationPolicy mutationPolicy' }] } });
      }
      if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
      return json(route, []);
    });
    await page.goto('/#/code-generation/previews');
    const toggle = page.getByRole('switch', { name: '业务写入策略', exact: true });
    await expect(toggle).toBeEnabled();
    await toggle.focus();
    await page.keyboard.press('Space');
    await expect(toggle).toBeChecked();
    await page.getByTestId('codegen-preview').click();
    await expect.poll(() => schemas.length).toBe(1);
    expect(schemas[0].entityCapabilities.hasMutationPolicy).toBe(true);
    await expect(page.getByTestId('codegen-content')).toContainText('IProductMutationPolicy');
    const editor = page.locator('textarea[data-testid="codegen-schema"]');
    const schema = JSON.parse(await editor.inputValue());
    await editor.fill(JSON.stringify({ ...schema, entityCapabilities: { ...schema.entityCapabilities, hasMutationPolicy: false, hasVersion: false } }, null, 2));
    await expect(toggle).toBeDisabled();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).include('.codegen-workbench__mutation-policy').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
    expect(errors).toEqual([]);
    await page.locator('.codegen-workbench__mutation-policy').screenshot({ path: info.outputPath('mutation-policy-control.png') });
    await page.screenshot({ path: info.outputPath('mutation-policy.png'), fullPage: true });
  });
}
