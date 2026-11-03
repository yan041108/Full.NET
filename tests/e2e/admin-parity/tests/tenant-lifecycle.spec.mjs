import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = '01912345-6789-7abc-8def-0123456789ab';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

// 页面验收使用受控 HTTP；跨入口最后一名保护由双库事务场景验证。
for (const width of [1440, 390]) {
  for (const readOnly of [false, true]) {
    test(`企业生命周期${readOnly ? '只读' : '冲突与关闭'} ${width}`, async ({ page }, info) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
      let tenant = { id, identifier: 'acme', name: 'Acme', domain: 'acme.localhost', defaultLocale: 'zh-CN',
        isActive: true, version: 3, lifecycleStatus: 'Active', provisioningStatus: 'Completed' };
      const writes = [], errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.route('**/api/v1/**', route => {
        const path = new URL(route.request().url()).pathname;
        if (path === '/api/v1/auth/refresh') return json(route, { accessToken: 'lifecycle-browser', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
        if (path === '/api/v1/me') return json(route, { id, username: 'operator', displayName: '企业操作员', tenantId: null, actorScope: 'host', scope: 'host',
          isSuperAdministrator: false, sessionId: id, profileVersion: 1, preferredLocale: 'zh-CN', passwordChangeRequired: false,
          permissions: ['identity.navigation.read', 'tenancy.host_tenants.read', 'tenancy.tenant_lifecycle.read',
            ...(readOnly ? [] : ['suspend', 'reactivate', 'close'].map(action => 'tenancy.tenant_lifecycle.' + action))] });
        if (path === '/api/v1/navigation') return json(route, [{ id: 'tenant-management', parentId: null, routeName: 'tenant-management', path: '/tenants', componentKey: 'tenants',
          title: '企业管理', caption: '企业管理', icon: 'building', order: 1, requiredPermission: 'tenancy.host_tenants.read', children: [] }]);
        if (path === '/api/v1/tenancy/tenants') return json(route, { items: [tenant], page: 1, pageSize: 20, total: 1 });
        if (path === '/api/v1/tenancy/tenant-packages') return json(route, { items: [], page: 1, pageSize: 100, total: 0 });
        if (path.endsWith('/lifecycle')) return json(route, tenant);
        if (/\/(suspend|reactivate|close)$/.test(path)) {
          const action = path.split('/').at(-1), body = route.request().postDataJSON();
          writes.push({ action, ...body }); expect(body).toEqual({ version: tenant.version });
          tenant = { ...tenant, lifecycleStatus: action === 'suspend' ? 'Suspended' : action === 'reactivate' ? 'Active' : 'Closed', isActive: action === 'reactivate', version: tenant.version + 1 };
          if (action === 'suspend') return json(route, { status: 409, code: 'tenancy.version_conflict', title: '企业状态已变化' }, 409);
          return json(route, tenant);
        }
        if (path.endsWith('/unread-count')) return json(route, { unreadCount: 0 });
        return json(route, []);
      });
      await page.goto('/#/tenants');
      const row = page.locator('.el-table__row').filter({ hasText: 'Acme' });
      await expect(row).toBeVisible();
      const opener = page.getByTestId('tenants-action-lifecycle');
      if (!await opener.isVisible()) await row.getByTestId('art-table-action-more').click();
      await opener.click();
      const dialog = page.getByRole('dialog', { name: '企业生命周期', exact: true });
      await expect(dialog).toBeVisible();
      await expect(dialog).toContainText('当前状态：正常');
      if (readOnly) {
        await expect(dialog).toContainText('当前权限仅允许查看');
        await expect(dialog.getByRole('button', { name: '暂停企业', exact: true })).toHaveCount(0);
        expect(writes).toEqual([]);
      } else {
        await dialog.getByRole('button', { name: '暂停企业', exact: true }).click();
        await dialog.getByRole('button', { name: '取消', exact: true }).click();
        expect(writes).toEqual([]);
        await dialog.getByRole('button', { name: '暂停企业', exact: true }).click();
        const confirm = dialog.getByTestId('tenant-lifecycle-confirm');
        await confirm.focus(); await page.keyboard.press('Enter');
        await expect(dialog).toContainText('企业状态已变化');
        await expect(dialog).toContainText('当前状态：已暂停');
        expect(writes).toHaveLength(1);
        await dialog.getByRole('button', { name: '恢复企业', exact: true }).click();
        await confirm.click();
        await expect(dialog).toContainText('当前状态：正常');
        await dialog.getByRole('button', { name: '关闭企业', exact: true }).click();
        await expect(confirm).toBeDisabled();
        const identifier = dialog.getByLabel('输入企业编码以确认关闭', { exact: true });
        await identifier.fill('other'); await expect(confirm).toBeDisabled();
        await identifier.fill('acme'); await expect(confirm).toBeEnabled();
        await confirm.focus(); await page.keyboard.press('Enter');
        await expect(dialog).toContainText('当前状态：已关闭');
        await expect(dialog.getByRole('button', { name: '恢复企业', exact: true })).toHaveCount(0);
        expect(writes).toEqual([{ action: 'suspend', version: 3 }, { action: 'reactivate', version: 4 }, { action: 'close', version: 5 }]);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      // 只读路径没有后续交互等待，须等弹窗淡入结束再测实际文字对比度。
      await dialog.evaluate(async element => {
        const root = element.closest('.el-overlay') ?? element;
        await Promise.all(root.getAnimations({ subtree: true }).filter(animation => animation.playState === 'running'
          && animation.effect?.getTiming().iterations !== Infinity).map(animation => animation.finished.catch(() => {})));
      });
      expect((await new AxeBuilder({ page }).include('.tenant-lifecycle-dialog').withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
      expect(errors).toEqual([]);
      await dialog.screenshot({ path: info.outputPath('tenant-lifecycle.png') });
      await page.keyboard.press('Escape'); await expect(dialog).not.toBeVisible();
    });
  }
}
