import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

/**
 * 从真实 API 拉取 403 ProblemDetails，供探针 UI 断言（traceId 来自服务端）。
 */
async function fetchPermissionDeniedProblem(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);
  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/super-administrators`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');
  expect(typeof problem.traceId).toBe('string');
  expect(problem.traceId.length).toBeGreaterThan(0);
  return problem;
}

test('受限 Host 账号点击检查会话可连接真实 /api/v1/me', async ({ page }) => {
  await loginAsHostViewer(page);

  await expect(page.getByRole('button', { name: '检查会话', exact: true })).toBeVisible();
  await page.getByTestId('load-current-user').click();

  await expect(page.getByText('已连接：E2E 受限查看者', { exact: true })).toBeVisible({
    timeout: 15_000
  });
});

test('工作台探针呈现真实 API 返回的 authorization.permission_denied', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const problem = await fetchPermissionDeniedProblem(request, clientKind);

  let interceptProbe = false;
  await page.route('**/api/v1/me', async route => {
    if (!interceptProbe) {
      await route.continue();
      return;
    }

    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify(problem)
    });
  });

  await loginAsHostAdmin(page);
  interceptProbe = true;

  await page.getByTestId('load-current-user').click();

  await expect(page.getByTestId('error-code'))
    .toHaveText('authorization.permission_denied', { timeout: 15_000 });
  await expect(page.getByTestId('trace-id')).toHaveText(problem.traceId);
});
