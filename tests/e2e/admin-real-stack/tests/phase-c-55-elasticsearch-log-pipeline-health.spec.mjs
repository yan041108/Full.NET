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

test('API：Elasticsearch 日志管道健康（Serilog 适配，无凭据）（清单 55）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const healthResponse = await request.get(
    `${apiBaseUrl}/api/v1/observability/elasticsearch-log-pipeline/health`,
    { headers }
  );
  expect(healthResponse.ok()).toBeTruthy();
  const health = await healthResponse.json();
  expect(health.adapterKind).toBe('serilog-elasticsearch');
  expect(typeof health.isEnabled).toBe('boolean');
  expect(typeof health.isSinkRegistered).toBe('boolean');
  expect(typeof health.indexFormat).toBe('string');
  expect(Array.isArray(health.nodeEndpoints)).toBeTruthy();
  expect(typeof health.openTelemetryOtlpEndpointConfigured).toBe('boolean');
  expect(health.pipelineNotice.length).toBeGreaterThan(0);
  expect(typeof health.clusterStatus).toBe('string');
  const serialized = JSON.stringify(health);
  expect(serialized.toLowerCase()).not.toMatch(/apikey|password|secret/);
});

test('UI：Elasticsearch 日志管道健康页（清单 55，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'Elasticsearch 健康页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /Elasticsearch 日志/);

  await expect(page.getByText('Elasticsearch 日志管道', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('serilog-elasticsearch', { exact: true })).toBeVisible();
  await expect(page.getByText('不经 OTel', { exact: false })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
