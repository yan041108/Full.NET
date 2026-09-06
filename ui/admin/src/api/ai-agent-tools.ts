import {
  isAiAgentToolCallPage,
  isAiAgentToolCatalogItem,
  type AiAgentToolCallListQuery,
  type AiAgentToolCallPage,
  type AiAgentToolCatalogItem
} from '@fullnet/client-contracts';
import { request } from './http';

function buildCallListQuery(query: AiAgentToolCallListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId) {
    params.set('tenantId', query.tenantId);
  }
  if (query.toolName) {
    params.set('toolName', query.toolName);
  }
  if (query.statusKey) {
    params.set('statusKey', query.statusKey);
  }
  return params.toString();
}

export async function listAiAgentTools(
  signal?: AbortSignal
): Promise<AiAgentToolCatalogItem[]> {
  const value = await request<unknown>('/api/v1/ai/agent-tools', { method: 'GET' }, signal);
  if (!Array.isArray(value) || !value.every(isAiAgentToolCatalogItem)) {
    throw new Error('client.invalid_ai_agent_tool_catalog');
  }
  return value;
}

export async function listAiAgentToolCalls(
  query: AiAgentToolCallListQuery = {},
  signal?: AbortSignal
): Promise<AiAgentToolCallPage> {
  const value = await request<unknown>(
    `/api/v1/ai/agent-tool-calls?${buildCallListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiAgentToolCallPage(value)) {
    throw new Error('client.invalid_ai_agent_tool_call_page');
  }
  return value;
}
