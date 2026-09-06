export interface AiChatSessionListItem {
  id: string;
  tenantId: string | null;
  ownerUserId: string;
  modelConfigId: string;
  modelName: string;
  title: string;
  messageCount: number;
  lastMessageAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiChatMessage {
  id: string;
  sessionId: string;
  roleKey: string;
  content: string;
  statusKey: string;
  promptTokens: number | null;
  completionTokens: number | null;
  createdAtUtc: string;
}

export interface AiChatSession {
  id: string;
  tenantId: string | null;
  ownerUserId: string;
  modelConfigId: string;
  modelName: string;
  title: string;
  isGenerating: boolean;
  messages: AiChatMessage[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiChatSessionPage {
  items: AiChatSessionListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateAiChatSessionRequest {
  modelConfigId: string;
  title?: string | null;
}

export interface UpdateAiChatSessionRequest {
  title: string;
  version: number;
}

export interface StreamAiChatMessageRequest {
  content: string;
}

export interface AiChatStreamDeltaEvent {
  delta: string;
}

export interface AiChatStreamDoneEvent {
  assistantMessageId: string;
  promptTokens: number | null;
  completionTokens: number | null;
}

export interface AiChatStreamErrorEvent {
  message: string;
}

export interface AiChatSessionListQuery {
  page?: number;
  pageSize?: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isAiChatSessionListItem(value: unknown): value is AiChatSessionListItem {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && isGuid(value.ownerUserId)
    && isGuid(value.modelConfigId)
    && typeof value.modelName === 'string'
    && typeof value.title === 'string'
    && typeof value.messageCount === 'number'
    && (value.lastMessageAtUtc === null || typeof value.lastMessageAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isAiChatMessage(value: unknown): value is AiChatMessage {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.sessionId)
    && typeof value.roleKey === 'string'
    && typeof value.content === 'string'
    && typeof value.statusKey === 'string'
    && (value.promptTokens === null || typeof value.promptTokens === 'number')
    && (value.completionTokens === null || typeof value.completionTokens === 'number')
    && typeof value.createdAtUtc === 'string';
}

export function isAiChatSession(value: unknown): value is AiChatSession {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && isGuid(value.ownerUserId)
    && isGuid(value.modelConfigId)
    && typeof value.modelName === 'string'
    && typeof value.title === 'string'
    && typeof value.isGenerating === 'boolean'
    && Array.isArray(value.messages)
    && value.messages.every(isAiChatMessage)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isAiChatSessionPage(value: unknown): value is AiChatSessionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAiChatSessionListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}
