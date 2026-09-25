import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin, statusPath } from './support/real-stack-auth.mjs';

const documentGroup = '文档';
const codegenGroup = '代码生成';
const jobsGroup = '任务';

/** 清单 86：Files/Document/Jobs/CodeGen/运维页面导航冒烟（Vue）。 */
const navEntries = [
  { link: /文件管理/, heading: '文件管理' },
  { link: /Host 文档库/, heading: 'Host 文档库', group: documentGroup },
  { link: /文档分类/, heading: /文档分类/, group: documentGroup, level: 1 },
  { link: /文档标签/, heading: /文档标签/, group: documentGroup, level: 1 },
  { link: /文档分享/, heading: '文档分享', group: documentGroup },
  { link: /文档统计/, heading: '文档统计', group: documentGroup },
  { link: /文档回收站/, heading: '文档回收站', group: documentGroup },
  { link: '代码生成模板', heading: '代码生成模板', group: codegenGroup },
  { link: /数据库目录/, heading: '数据库目录', group: codegenGroup },
  { link: /代码生成预览/, heading: 'CRUD 产物预览', group: codegenGroup },
  { link: /任务定义/, heading: /任务定义/, group: jobsGroup, level: 1 },
  { link: /任务计划/, heading: /任务计划/, level: 1 },
  { link: /运行日志/, heading: '运行日志' },
  { link: /服务器监控/, heading: '服务器监控' },
  { link: /缓存管理/, heading: '缓存管理' }
];

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Phase D-86：运维与新模块页面导航冒烟（清单 86）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '清单 86 以 Vue 交付线为准');
  test.setTimeout(240_000);

  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  for (const entry of navEntries) {
    if (entry.group) {
      await clickMainNavLink(page, entry.link, entry.group);
    } else {
      await clickMainNavLink(page, entry.link);
    }
    const headingOptions =
      typeof entry.heading === 'string'
        ? { name: entry.heading, exact: true, level: entry.level ?? undefined }
        : { name: entry.heading, level: entry.level ?? undefined };
    const heading = page.getByRole('heading', headingOptions);
    await expect(heading).toBeVisible({ timeout: 25_000 });
    await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  }

  await page.goto(statusPath(clientKind, 'document/permissions'));
  await expect(page.getByRole('heading', { name: '文档权限', exact: true })).toBeVisible({
    timeout: 20_000
  });

  await page.goto('/#/jobs/host-executions');
  await expect(
    page.locator('.host-job-executions-view').getByRole('heading', { name: '执行历史', exact: true })
  ).toBeVisible({ timeout: 20_000 });
});
