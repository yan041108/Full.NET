import {
  isAiMcpRemoteConnectionList,
  isAiMcpRemoteConnectionResponse,
  isAiMcpRemoteDiscoveredToolList,
  type AiMcpRemoteConnectionListItem,
  type AiMcpRemoteConnectionResponse,
  type AiMcpRemoteDiscoveredToolItem,
  type ApproveAiMcpRemoteToolRequest,
  type CreateAiMcpRemoteConnectionRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAiMcpRemoteConnections(signal?: AbortSignal): Promise<AiMcpRemoteConnectionListItem[]> {
  const value = await request<unknown>('/api/v1/ai/mcp/remote-connections', { method: 'GET' }, signal);
  if (!isAiMcpRemoteConnectionList(value)) {
    throw new Error('client.invalid_ai_mcp_remote_connection_list');
  }
  return value;
}

export async function createAiMcpRemoteConnection(
  body: CreateAiMcpRemoteConnectionRequest,
  signal?: AbortSignal
): Promise<AiMcpRemoteConnectionResponse> {
  const value = await request<unknown>(
    '/api/v1/ai/mcp/remote-connections',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiMcpRemoteConnectionResponse(value)) {
    throw new Error('client.invalid_ai_mcp_remote_connection');
  }
  return value;
}

export async function discoverAiMcpRemoteTools(
  connectionId: string,
  signal?: AbortSignal
): Promise<AiMcpRemoteDiscoveredToolItem[]> {
  const value = await request<unknown>(
    `/api/v1/ai/mcp/remote-connections/${encodeURIComponent(connectionId)}/discover-tools`,
    { method: 'POST' },
    signal
  );
  if (!isAiMcpRemoteDiscoveredToolList(value)) {
    throw new Error('client.invalid_ai_mcp_remote_discovered_tools');
  }
  return value;
}

export async function approveAiMcpRemoteTool(
  connectionId: string,
  body: ApproveAiMcpRemoteToolRequest,
  signal?: AbortSignal
): Promise<void> {
  await request<unknown>(
    `/api/v1/ai/mcp/remote-connections/${encodeURIComponent(connectionId)}/approve-tool`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
}

/** 导出 MCP 远端连接列表、发现工具与创建请求模型，供管理页与审批流程共享同一契约。 */
export type {
  AiMcpRemoteConnectionListItem,
  AiMcpRemoteConnectionResponse,
  AiMcpRemoteDiscoveredToolItem,
  ApproveAiMcpRemoteToolRequest,
  CreateAiMcpRemoteConnectionRequest
};
