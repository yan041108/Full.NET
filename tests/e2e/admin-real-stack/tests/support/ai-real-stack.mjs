import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const modelConfigsPath = `${apiBaseUrl}/api/v1/ai/model-configs`;
const tenantQuotasPath = `${apiBaseUrl}/api/v1/ai/tenant-quotas`;
const chatSessionsPath = `${apiBaseUrl}/api/v1/ai/chat/sessions`;
const agentToolsPath = `${apiBaseUrl}/api/v1/ai/agent-tools`;
const agentToolCallsPath = `${apiBaseUrl}/api/v1/ai/agent-tool-calls`;
const mcpRemoteConnectionsPath = `${apiBaseUrl}/api/v1/ai/mcp/remote-connections`;

export const aiChatSessionsListToolName = 'ai.chat.sessions.list';

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

/** Host 分页列出 AI 模型配置（列表端点脱敏）。 */
export async function listAiModelConfigsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${modelConfigsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取模型配置详情（不回显 Secret）。 */
export async function getAiModelConfigViaApi(request, clientKind, modelConfigId, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${modelConfigsPath}/${modelConfigId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建模型配置。 */
export async function createAiModelConfigViaApi(request, clientKind, body, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(modelConfigsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 连通性测试（不声称真实供应商可达）。 */
export async function testAiModelConfigViaApi(
  request,
  clientKind,
  modelConfigId,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(`${modelConfigsPath}/${modelConfigId}/test`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 分页列出租户 AI 配额。 */
export async function listAiTenantQuotasViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${tenantQuotasPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取指定租户配额。 */
export async function getAiTenantQuotaViaApi(request, clientKind, tenantId, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${tenantQuotasPath}/${tenantId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 列表项不得暴露 API 密钥。 */
export function expectAiModelConfigListItemMasked(item) {
  expect(item).toBeTruthy();
  expect(typeof item.maskedEndpointBaseUrl).toBe('string');
  expect(typeof item.hasApiKey).toBe('boolean');
  expect(item).not.toHaveProperty('apiKey');
  expect(item).not.toHaveProperty('apiKeyProtected');
}

/** 详情不得回显 API 密钥。 */
export function expectAiModelConfigDetailSafe(detail) {
  expect(detail).toBeTruthy();
  expect(typeof detail.hasApiKey).toBe('boolean');
  expect(detail).not.toHaveProperty('apiKey');
  expect(detail).not.toHaveProperty('apiKeyProtected');
}

/** 分页列出当前用户的聊天会话（Host 或租户上下文）。 */
export async function listAiChatSessionsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${chatSessionsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取会话详情（含历史消息）。 */
export async function getAiChatSessionViaApi(request, clientKind, sessionId, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${chatSessionsPath}/${sessionId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 创建聊天会话。 */
export async function createAiChatSessionViaApi(request, clientKind, body, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(chatSessionsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 请求取消进行中的生成。 */
export async function cancelAiChatGenerationViaApi(
  request,
  clientKind,
  sessionId,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(`${chatSessionsPath}/${sessionId}/cancel`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 列出静态 Agent Tool 目录（只读）。 */
export async function listAiAgentToolsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(agentToolsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 读取单个工具目录项。 */
export async function getAiAgentToolViaApi(request, clientKind, toolName, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(
    `${agentToolsPath}/${encodeURIComponent(toolName)}`,
    { headers: authHeaders(clientKind, token) }
  );
  return { response, accessToken: token };
}

/** 分页查询工具调用审计。 */
export async function listAiAgentToolCallsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(`${agentToolCallsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 列出 MCP 远程连接（只读目录；配置写操作另视图）。 */
export async function listAiMcpRemoteConnectionsViaApi(request, clientKind, accessToken = null) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.get(mcpRemoteConnectionsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 目录项暴露 MCP 暴露键与副作用，不含可执行实现细节。 */
export function expectAiAgentToolCatalogItem(item) {
  expect(item).toBeTruthy();
  expect(typeof item.toolName).toBe('string');
  expect(typeof item.mcpExposureKey).toBe('string');
  expect(typeof item.sideEffectKey).toBe('string');
  expect(item).not.toHaveProperty('handler');
  expect(item).not.toHaveProperty('endpointUrl');
}

/** 首切片应包含只读聊天会话列表工具。 */
export function expectReadOnlyChatListTool(catalog) {
  const match = catalog.find((item) => item.toolName === aiChatSessionsListToolName);
  expect(match).toBeTruthy();
  expect(match.sideEffectKey).toBe('read');
  return match;
}

/** 流式发送消息（SSE；不消费完整流时仅校验 HTTP 状态）。 */
export async function streamAiChatMessageViaApi(
  request,
  clientKind,
  sessionId,
  body,
  accessToken = null
) {
  const token = accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
  const response = await request.post(`${chatSessionsPath}/${sessionId}/messages/stream`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}
