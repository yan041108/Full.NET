import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

const notificationGroup = '通知';
const dataApprovalGroup = '数据审批';

const notificationPages = [
  { link: /渠道配置/, heading: '渠道配置' },
  { link: /通知模板/, heading: '通知模板' },
  { link: /场景绑定/, heading: '场景绑定' },
  { link: /投递运维/, heading: '投递运维' },
  { link: /通知偏好/, heading: '通知偏好' },
  { link: /公告管理/, heading: '公告管理' }
];

const dataApprovalPages = [
  { link: /数据审批/, heading: '数据审批请求' },
  { link: /审批场景/, heading: '审批场景配置' }
];

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Phase D-85：通知与数据审批页面导航冒烟（清单 85）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '清单 85 以 Vue 交付线为准');
  test.setTimeout(150_000);

  await loginAsHostAdmin(page);

  for (const entry of notificationPages) {
    await clickMainNavLink(page, entry.link, notificationGroup);
    await expect(page.getByRole('heading', { name: entry.heading, exact: true })).toBeVisible({
      timeout: 20_000
    });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }

  await clickMainNavLink(page, /消息中心/);
  await expect(page.getByRole('heading', { name: '消息中心', exact: true })).toBeVisible({
    timeout: 20_000
  });

  for (const entry of dataApprovalPages) {
    await clickMainNavLink(page, entry.link, dataApprovalGroup);
    await expect(page.getByRole('heading', { name: entry.heading, exact: true })).toBeVisible({
      timeout: 20_000
    });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }
});
