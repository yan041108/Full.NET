const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export interface AiMcpRemoteConnectionListItem {
  id: string;
  connectionKey: string;
  displayName: string;
  maskedEndpointUrl: string;
  hasServiceToken: boolean;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface AiMcpRemoteConnectionResponse extends AiMcpRemoteConnectionListItem {
  endpointUrl: string;
  oauthScopesJson: string | null;
}

export interface CreateAiMcpRemoteConnectionRequest {
  connectionKey: string;
  displayName: string;
  endpointUrl: string;
  serviceToken: string;
  oauthScopesJson?: string | null;
}

export interface AiMcpRemoteDiscoveredToolItem {
  remoteToolName: string;
  inputSchemaJson: string;
  isApproved: boolean;
  approvalStatusKey: string | null;
}

export interface ApproveAiMcpRemoteToolRequest {
  remoteToolName: string;
  sideEffectKey: string;
  permissionCode: string;
}

export function isAiMcpRemoteConnectionListItem(value: unknown): value is AiMcpRemoteConnectionListItem {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.connectionKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.maskedEndpointUrl === 'string'
    && typeof value.hasServiceToken === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && typeof value.updatedAtUtc === 'string'
    && typeof value.version === 'number';
}

export function isAiMcpRemoteConnectionResponse(value: unknown): value is AiMcpRemoteConnectionResponse {
  if (!isAiMcpRemoteConnectionListItem(value)) {
    return false;
  }

  const record = value as unknown as Record<string, unknown>;
  return typeof record.endpointUrl === 'string'
    && isNullableString(record.oauthScopesJson);
}

export function isAiMcpRemoteConnectionList(value: unknown): value is AiMcpRemoteConnectionListItem[] {
  return Array.isArray(value) && value.every(isAiMcpRemoteConnectionListItem);
}

export function isAiMcpRemoteDiscoveredToolList(value: unknown): value is AiMcpRemoteDiscoveredToolItem[] {
  return Array.isArray(value) && value.every((item) =>
    isRecord(item)
    && typeof item.remoteToolName === 'string'
    && typeof item.inputSchemaJson === 'string'
    && typeof item.isApproved === 'boolean'
    && isNullableString(item.approvalStatusKey));
}
