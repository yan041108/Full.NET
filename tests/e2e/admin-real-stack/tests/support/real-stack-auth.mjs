import { expect } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

/** 登录 Host 管理员并等待动态导航就绪。 */
export async function loginAsHostAdmin(page, baseUrl = '/') {
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
}

/** 双管理端均使用 hash 路由访问状态页。 */
export function statusPath(_clientKind, code) {
  return `/#/${code}`;
}
