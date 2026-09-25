import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

/** 清单 83：Host 管理员主导航可达性与页面标题（Vue）。 */
const hostScopePages = [
  { link: /用户管理/, heading: '用户管理' },
  { link: /角色管理/, heading: '角色管理' },
  { link: /菜单管理/, heading: '菜单管理' },
  { link: /租户管理/, heading: '租户管理' },
  { link: /租户套餐/, heading: '租户套餐' },
  { link: /数据字典/, heading: '数据字典' },
  { link: /系统配置/, heading: '系统配置' },
  { link: /限时诊断/, heading: '限时诊断策略' },
  { link: /枚举常量/, heading: '枚举常量' }
];

const tenantOrgPages = [
  { link: /机构管理/, heading: '机构管理' },
  { link: /职位管理/, heading: '职位管理' },
  { link: /职级管理/, heading: '职级管理' },
  { link: /用户机构隶属/, heading: '用户机构隶属' },
  { link: /用户职位隶属/, heading: '用户职位隶属' }
];

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Phase D-83：Identity/租户/设置页面导航冒烟（清单 83）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '清单 83 页面验收以 Vue 交付线为准');
  test.setTimeout(180_000);

  await loginAsHostAdmin(page);

  for (const entry of hostScopePages) {
    await clickMainNavLink(page, entry.link);
    await expect(page.getByRole('heading', { name: entry.heading, exact: true })).toBeVisible({
      timeout: 20_000
    });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }

  await enterDevelopmentTenant(page);

  await page.goto('/#/settings/tenant-branding');
  await expect(page.getByRole('heading', { name: '租户品牌', exact: true })).toBeVisible({
    timeout: 20_000
  });

  await clickMainNavLink(page, /租户数据字典/);
  await expect(page.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible({
    timeout: 20_000
  });

  for (const entry of tenantOrgPages) {
    await clickMainNavLink(page, entry.link);
    await expect(page.getByRole('heading', { name: entry.heading, exact: true })).toBeVisible({
      timeout: 20_000
    });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }
});
