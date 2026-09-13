export interface AiAgentToolCatalogItem {
  toolName: string;
  displayName: string;
  description: string;
  permissionCode: string;
  sideEffectKey: string;
  inputSchemaJson: string;
  outputSchemaJson: string;
  isEnabled: boolean;
  mcpExposureKey: string;
}

export interface AiAgentToolCallListItem {
  id: string;
  tenantId: string | null;
  actorUserId: string;
  toolName: string;
  permissionCode: string;
  statusKey: string;
  durationMs: number | null;
  inputSummary: string;
  outputSummary: string | null;
  errorCode: string | null;
  traceId: string | null;
  runId: string | null;
  argumentsHash: string | null;
  approvalId: string | null;
  createdAtUtc: string;
}

export interface AiAgentToolCallPage {
  items: AiAgentToolCallListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface AiAgentToolCallListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  toolName?: string;
  statusKey?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isAiAgentToolCatalogItem(value: unknown): value is AiAgentToolCatalogItem {
  return isRecord(value)
    && typeof value.toolName === 'string'
    && typeof value.displayName === 'string'
    && typeof value.description === 'string'
    && typeof value.permissionCode === 'string'
    && typeof value.sideEffectKey === 'string'
    && typeof value.inputSchemaJson === 'string'
    && typeof value.outputSchemaJson === 'string'
    && typeof value.isEnabled === 'boolean'
    && typeof value.mcpExposureKey === 'string';
}

export function isAiAgentToolCallListItem(value: unknown): value is AiAgentToolCallListItem {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && isGuid(value.actorUserId)
    && typeof value.toolName === 'string'
    && typeof value.permissionCode === 'string'
    && typeof value.statusKey === 'string'
    && (value.durationMs === null || typeof value.durationMs === 'number')
    && typeof value.inputSummary === 'string'
    && (value.outputSummary === null || typeof value.outputSummary === 'string')
    && (value.errorCode === null || typeof value.errorCode === 'string')
    && (value.traceId === null || typeof value.traceId === 'string')
    && (value.runId === null || isGuid(value.runId))
    && (value.argumentsHash === null || typeof value.argumentsHash === 'string')
    && (value.approvalId === null || isGuid(value.approvalId))
    && typeof value.createdAtUtc === 'string';
}

export function isAiAgentToolCallPage(value: unknown): value is AiAgentToolCallPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAiAgentToolCallListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}
