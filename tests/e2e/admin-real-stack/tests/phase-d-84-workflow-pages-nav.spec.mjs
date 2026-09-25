import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

const workflowGroup = '工作流';

/** 清单 84：工作流相关页面主导航可达性（Vue）。 */
const workflowPages = [
  { link: /我的待办/, heading: '我的工作流待办' },
  { link: /我的抄送/, heading: '我的工作流抄送' },
  { link: /工作流定义/, heading: '工作流定义', level: 1 },
  { link: /工作流表单/, heading: '工作流表单', level: 1 },
  { link: /工作流实例/, heading: '工作流实例' },
  { link: /恢复任务/, heading: '恢复任务' }
];

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Phase D-84：Workflow 页面导航冒烟（清单 84）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '清单 84 以 Vue 交付线为准');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);

  for (const entry of workflowPages) {
    await clickMainNavLink(page, entry.link, workflowGroup);
    const heading = page.getByRole('heading', {
      name: entry.heading,
      exact: true,
      level: entry.level ?? undefined
    });
    await expect(heading).toBeVisible({ timeout: 20_000 });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }
});
