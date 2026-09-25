import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  expectAiAgentToolCatalogItem,
  expectReadOnlyChatListTool,
  getAiAgentToolViaApi,
  listAiAgentToolCallsViaApi,
  listAiAgentToolsViaApi,
  listAiMcpRemoteConnectionsViaApi,
  aiChatSessionsListToolName
} from './support/ai-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Agent 工具页仅 Vue 交付线');
}

test('API：静态工具目录、MCP 暴露元数据与调用审计（清单 74）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: catalogResponse } = await listAiAgentToolsViaApi(request, clientKind);
  expect(catalogResponse.ok()).toBeTruthy();
  const catalog = await catalogResponse.json();
  expect(Array.isArray(catalog)).toBeTruthy();
  expect(catalog.length).toBeGreaterThan(0);
  for (const item of catalog) {
    expectAiAgentToolCatalogItem(item);
  }
  expectReadOnlyChatListTool(catalog);

  const { response: toolDetail } = await getAiAgentToolViaApi(
    request,
    clientKind,
    aiChatSessionsListToolName
  );
  expect(toolDetail.ok()).toBeTruthy();

  const { response: unknownTool } = await getAiAgentToolViaApi(
    request,
    clientKind,
    'evil.http.loopback'
  );
  expect(unknownTool.status()).toBe(404);

  const { response: callsResponse } = await listAiAgentToolCallsViaApi(request, clientKind);
  expect(callsResponse.ok()).toBeTruthy();
  const callsPage = await callsResponse.json();
  expect(Array.isArray(callsPage.items)).toBeTruthy();

  const { response: mcpListResponse } = await listAiMcpRemoteConnectionsViaApi(
    request,
    clientKind
  );
  expect(mcpListResponse.ok()).toBeTruthy();
  expect(Array.isArray(await mcpListResponse.json())).toBeTruthy();
});

test('UI：Agent 工具目录与审计页签（清单 74）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /Agent 工具/);

  await expect(page.getByRole('heading', { name: 'Agent 工具', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: '静态目录' })).toBeVisible();
});
