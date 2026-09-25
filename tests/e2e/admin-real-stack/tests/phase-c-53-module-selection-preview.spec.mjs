import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：运行时模块集与候选校验（只读，不改 DI）（清单 53）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const runtimeResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/modules/selection/runtime`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(runtimeResponse.ok()).toBeTruthy();
  const runtime = await runtimeResponse.json();
  expect(typeof runtime.isValid).toBe('boolean');
  expect(runtime.deploymentNotice.length).toBeGreaterThan(0);
  expect(runtime.officialModuleKeys.length).toBeGreaterThan(0);
  expect(runtime.enabledModuleKeys.length).toBeGreaterThan(0);
  expect(runtime.modules.length).toBe(runtime.officialModuleKeys.length);

  const presetResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/modules/selection/validate`,
    {
      headers,
      data: { preset: 'Full' }
    }
  );
  expect(presetResponse.ok()).toBeTruthy();
  const presetAnalysis = await presetResponse.json();
  expect(presetAnalysis.isValid).toBe(true);
  expect(presetAnalysis.preset).toBe('Full');

  const invalidResponse = await request.post(
    `${apiBaseUrl}/api/v1/identity/modules/selection/validate`,
    {
      headers,
      data: { enabled: ['Identity', 'Document'] }
    }
  );
  expect(invalidResponse.ok()).toBeTruthy();
  const invalidAnalysis = await invalidResponse.json();
  expect(invalidAnalysis.isValid).toBe(false);
  expect(
    invalidAnalysis.issues.some((issue) => issue.code === 'modules.missing_dependency')
  ).toBe(true);
});

test('UI：模块启用预览页（清单 53，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '模块启用预览仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /模块启用预览/);

  await expect(page.getByText('模块启用预览', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('不支持运行时动态加载', { exact: false })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
