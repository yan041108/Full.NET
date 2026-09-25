import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  cancelAiChatGenerationViaApi,
  createAiChatSessionViaApi,
  getAiChatSessionViaApi,
  listAiChatSessionsViaApi,
  streamAiChatMessageViaApi
} from './support/ai-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'AI 对话页仅 Vue 交付线');
}

test('API：会话列表、缺失会话与取消/流式端点契约（清单 73）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: listResponse } = await listAiChatSessionsViaApi(request, clientKind);
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(Array.isArray(pageResult.items)).toBeTruthy();

  const missingId = randomUUID();
  const { response: getMissing } = await getAiChatSessionViaApi(request, clientKind, missingId);
  expect(getMissing.status()).toBe(404);

  const { response: cancelMissing } = await cancelAiChatGenerationViaApi(
    request,
    clientKind,
    missingId
  );
  expect(cancelMissing.status()).toBe(404);

  const { response: streamMissing } = await streamAiChatMessageViaApi(
    request,
    clientKind,
    missingId,
    { content: 'hello' }
  );
  expect([404, 422]).toContain(streamMissing.status());

  const { response: createInvalid } = await createAiChatSessionViaApi(request, clientKind, {
    modelConfigId: randomUUID(),
    title: null
  });
  expect([404, 422]).toContain(createInvalid.status());
});

test('UI：AI 对话页入口（清单 73）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /AI 对话/);

  await expect(page.getByRole('heading', { name: 'AI 对话', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('ai-chat-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
