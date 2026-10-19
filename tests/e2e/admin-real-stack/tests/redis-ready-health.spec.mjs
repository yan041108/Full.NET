import { expect, test } from '@playwright/test';

test('真实栈在配置 Redis 时 ready 健康检查通过', async ({ request }) => {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const response = await request.get(`${apiBaseUrl}/health/ready`);
  expect(response.status()).toBe(200);
  const body = await response.text();
  // 尚无真实广播证据时允许 Degraded；ready 200 与全链路 Healthy 是不同事实。
  expect(body.trim()).toMatch(/^(Healthy|Degraded)$/);
});
