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
  knowledgeUnavailableCode?: string | null;
  citations?: AiKnowledgeCitation[];
}

export interface AiKnowledgeCitation {
  id: string;
  documentId: string;
  versionId: string;
  sequence: number;
  pageNumber: number | null;
  offset: number;
  length: number;
}

export interface AskAiKnowledgeRequest {
  knowledgeBaseIds: string[];
  embeddingModelConfigId: string;
  question: string;
}

export interface AskAiKnowledgeResponse {
  message: AiChatMessage & { citations: AiKnowledgeCitation[] };
  refused: boolean;
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
    && typeof value.createdAtUtc === 'string'
    && (value.knowledgeUnavailableCode === undefined || value.knowledgeUnavailableCode === null || typeof value.knowledgeUnavailableCode === 'string')
    && (value.citations === undefined || (Array.isArray(value.citations) && value.citations.every(isAiKnowledgeCitation)));
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

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isAiKnowledgeCitation(value: unknown): value is AiKnowledgeCitation {
  return isRecord(value) && isGuid(value.id) && isGuid(value.documentId) && isGuid(value.versionId)
    && isNonNegativeInteger(value.sequence) && isNonNegativeInteger(value.offset)
    && isNonNegativeInteger(value.length) && value.length > 0
    && (value.pageNumber === null || (isNonNegativeInteger(value.pageNumber) && value.pageNumber > 0));
}

// 只有已完成且引用完整的响应可进入展示；拒答不能夹带正文或引用。
export function isAskAiKnowledgeResponse(value: unknown): value is AskAiKnowledgeResponse {
  if (!isRecord(value) || typeof value.refused !== 'boolean' || !isAiChatMessage(value.message)) return false;
  const message = value.message;
  if (message.roleKey !== 'assistant' || message.statusKey !== 'completed'
    || message.knowledgeUnavailableCode != null || message.content.length > 16000
    || !Array.isArray(message.citations) || message.citations.length > 8
    || !message.citations.every(isAiKnowledgeCitation)
    || new Set(message.citations.map(citation => citation.id)).size !== message.citations.length) return false;
  return value.refused ? message.content === '' && message.citations.length === 0
    : message.content.trim().length > 0 && message.citations.length > 0;
}
