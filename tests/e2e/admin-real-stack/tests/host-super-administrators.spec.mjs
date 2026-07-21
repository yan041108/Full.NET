import { expect, test } from '@playwright/test';
import {
  createHostUserViaApi,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

const adminPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const targetPassword = 'FullNet!2026SaTarget';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载超管目录并完成密码重认证授予与撤销', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(60_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const targetUsername = `sa-target-${Date.now().toString(36)}`;
  await createHostUserViaApi(request, clientKind, {
    username: targetUsername,
    displayName: '超管授予目标',
    password: targetPassword
  });

  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /超级管理员/ })).toBeVisible();
  await navigation.getByRole('link', { name: /超级管理员/ }).click();

  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '本账号 TOTP', exact: true })).toBeVisible();
  await expect(page.getByText('admin', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();

  await page.getByLabel('Host 账号', { exact: true }).fill(targetUsername);
  await page.getByLabel('当前密码', { exact: true }).fill(adminPassword);
  await page.getByRole('button', { name: '确认授予' }).click();

  await expect(page.getByText(targetUsername, { exact: true })).toBeVisible({
    timeout: 15_000
  });
  // 共享数据库下审计可能含历史授予记录，只断言至少一条可见。
  await expect(page.getByText('identity.super_administrator.granted', { exact: true }).first())
    .toBeVisible();

  const targetRow = page.locator('article').filter({
    has: page.locator('code', { hasText: targetUsername })
  });
  await targetRow.getByRole('button', { name: '撤销权限' }).click();
  await confirmRevokeDialogs(page, clientKind, adminPassword);

  await expect(page.getByText(targetUsername, { exact: true })).toHaveCount(0, {
    timeout: 15_000
  });
  await expect(page.getByText('identity.super_administrator.revoked', { exact: true }).first())
    .toBeVisible();
});

/** 确认撤销所需的密码与可选 TOTP 二次提示（Vue MessageBox / Layui Layer）。 */
async function confirmRevokeDialogs(page, clientKind, currentPassword) {
  if (clientKind === 'layui') {
    const passwordLayer = page.locator('.layui-layer').last();
    await expect(passwordLayer.locator('.layui-layer-input')).toBeVisible();
    await passwordLayer.locator('.layui-layer-input').fill(currentPassword);
    await passwordLayer.locator('.layui-layer-btn0').click({ force: true });

    const totpLayer = page.locator('.layui-layer').last();
    await expect(totpLayer.locator('.layui-layer-input')).toBeVisible();
    await totpLayer.locator('.layui-layer-input').fill('');
    await totpLayer.locator('.layui-layer-btn0').click({ force: true });
    return;
  }

  const passwordBox = page.locator('.el-message-box').last();
  await expect(passwordBox.locator('input')).toBeVisible();
  await passwordBox.locator('input').fill(currentPassword);
  // MessageBox 确认钮常被侧栏遮挡；用 Enter 提交避免 pointer 拦截。
  await passwordBox.locator('input').press('Enter');

  const totpBox = page.locator('.el-message-box').last();
  await expect(totpBox.locator('input')).toBeVisible();
  await totpBox.locator('input').fill('');
  await totpBox.locator('input').press('Enter');
}
