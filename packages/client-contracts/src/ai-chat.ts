import { isAiKnowledgeSourceLocation, type AiKnowledgeSourceLocation } from './ai-knowledge-location.js';
import { isKnowledgeSourceIdentity } from './ai-knowledge-source-identity.js';
import type { AiKnowledgeSearchSource } from './generated/models.generated.js';

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
  /** 服务器知识答案标记，包括拒答；来源资格由不可用机器码独立表示。 */
  isKnowledgeAnswer?: boolean;
  citations?: AiKnowledgeCitation[];
  personalScheduleCitations?: AiPersonalScheduleCitation[];
}

/** 日程引用有独立协议，不提供文档或文件下载身份。 */
export interface AiPersonalScheduleCitation {
  id: string; kind: 'calendar-personal-schedule-v1'; purpose: 'knowledge-assistant-v1';
  scheduleId: string; version: number; projectionHash: string;
}

/** DOCX 正文的逻辑坐标；不代表版式页码。 */
export interface AiKnowledgeDocxLocation {
  blockSequence: number;
  paragraphNumber: number;
  tableNumber: number | null;
  rowNumber: number | null;
  cellNumber: number | null;
}

export interface AiKnowledgeCitation {
  id: string;
  documentId: string;
  versionId: string;
  sequence: number;
  pageNumber: number | null;
  docxLocation?: AiKnowledgeDocxLocation | null;
  sourceLocation?: AiKnowledgeSourceLocation | null;
  correction?: AiKnowledgeCorrectionCitation | null;
  /** FAQ 修订来源不提供普通文件下载。 */
  faqSource?: { source: AiKnowledgeSearchSource; embeddingModelConfigId: string; embeddingModelVersion: number } | null;
  offset: number | null;
  length: number | null;
}

/** 人工片段内的来源性质与真实上下文；补写没有任何原文坐标。 */
export interface AiKnowledgeCorrectionCitation {
  revisionId: string;
  textLength: number;
  spans: readonly {
    contentKind: 'original' | 'corrected' | 'supplement';
    textOffset: number;
    textLength: number;
    sourceLocation: AiKnowledgeSourceLocation | null;
    sourceOffset: number | null;
    sourceLength: number | null;
  }[];
}

/** 原文位置不能由人工片段长度推导；保留全部交集并拒绝重叠、越界或补写伪造位置。 */
export function isAiKnowledgeCorrectionCitation(value: unknown): value is AiKnowledgeCorrectionCitation {
  if (!isRecord(value) || !isGuid(value.revisionId) || !isNonNegativeInteger(value.textLength) || value.textLength < 1 || value.textLength > 8000
    || !Array.isArray(value.spans) || value.spans.length < 1 || value.spans.length > 32) return false;
  let end = 0;
  for (const span of value.spans) {
    if (!isRecord(span) || !isNonNegativeInteger(span.textOffset) || !isNonNegativeInteger(span.textLength) || span.textLength < 1
      || span.textOffset < end || span.textOffset > value.textLength - span.textLength) return false;
    end = span.textOffset + span.textLength;
    if (span.contentKind === 'supplement') {
      if (span.sourceLocation !== null || span.sourceOffset !== null || span.sourceLength !== null) return false;
    } else if (span.contentKind !== 'original' && span.contentKind !== 'corrected' || span.sourceLocation === null
      || !isAiKnowledgeSourceLocation(span.sourceLocation, false, span.contentKind === 'corrected')
      || !isNonNegativeInteger(span.sourceOffset) || !isNonNegativeInteger(span.sourceLength) || span.sourceLength < 1
      || span.sourceOffset + span.sourceLength > 2147483647 || span.contentKind === 'original' && span.sourceLength !== span.textLength) return false;
  }
  return true;
}

export interface AskAiKnowledgeRequest {
  knowledgeBaseIds: string[];
  embeddingModelConfigId: string;
  question: string;
  personalSchedules?: { id: string; version: number; projectionHash: string }[] | null;
  searchScope?: {
    documentIds: string[];
    embeddingModelVersion: number;
    sources: import('./generated/models.generated.js').AiKnowledgeSearchSource[];
  } | null;
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
  hasOlderMessages?: boolean;
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
    && (value.isKnowledgeAnswer === undefined || typeof value.isKnowledgeAnswer === 'boolean')
    && (value.citations === undefined || (Array.isArray(value.citations) && value.citations.every(isAiKnowledgeCitation)))
    && (value.personalScheduleCitations === undefined || (Array.isArray(value.personalScheduleCitations)
      && value.personalScheduleCitations.length <= 7 && value.personalScheduleCitations.every(isAiPersonalScheduleCitation)))
    && validCombinedCitations(value);
}

function isAiPersonalScheduleCitation(value: unknown): value is AiPersonalScheduleCitation {
  return isRecord(value) && isGuid(value.id) && value.id !== '00000000-0000-0000-0000-000000000000'
    && value.kind === 'calendar-personal-schedule-v1' && value.purpose === 'knowledge-assistant-v1'
    && isGuid(value.scheduleId) && value.scheduleId !== '00000000-0000-0000-0000-000000000000'
    && isNonNegativeInteger(value.version) && value.version > 0
    && typeof value.projectionHash === 'string' && /^[A-F0-9]{64}$/.test(value.projectionHash)
    && !['documentId', 'fileId', 'content', 'ownerUserId', 'tenantId'].some(key => key in value);
}

function validCombinedCitations(value: Record<string, unknown>): boolean {
  const all = [...(Array.isArray(value.citations) ? value.citations : []),
    ...(Array.isArray(value.personalScheduleCitations) ? value.personalScheduleCitations : [])];
  return all.length <= 8 && new Set(all.map(item => isRecord(item) && typeof item.id === 'string' ? item.id.toLowerCase() : '')).size === all.length;
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
  return isRecord(value) && isGuid(value.id) && isAiKnowledgeSourceReference(value);
}

/** 搜索来源和答案引用共用真实位置规则；搜索预览不伪造一个答案引用标识。 */
export function isAiKnowledgeSourceReference(value: unknown): value is Omit<AiKnowledgeCitation, 'id'> {
  if (isRecord(value) && value.faqSource != null) {
    const faq = value.faqSource;
    return isRecord(faq) && isKnowledgeSourceIdentity(faq.source) && faq.source.sourceKind === 'faq-revision-v1'
      && faq.source.documentId === value.documentId && faq.source.versionId === value.versionId
      && isGuid(faq.embeddingModelConfigId) && isNonNegativeInteger(faq.embeddingModelVersion) && faq.embeddingModelVersion > 0
      && isNonNegativeInteger(value.sequence) && value.pageNumber === null && isNonNegativeInteger(value.offset)
      && isNonNegativeInteger(value.length) && value.length > 0 && value.offset + value.length <= 2147483647
      && value.correction == null && value.sourceLocation == null && value.docxLocation == null;
  }
  if (isRecord(value) && value.correction != null) return isGuid(value.documentId) && isGuid(value.versionId)
    && isNonNegativeInteger(value.sequence) && value.pageNumber === null && value.offset === null && value.length === null
    && value.sourceLocation == null && value.docxLocation == null && isAiKnowledgeCorrectionCitation(value.correction);
  return isRecord(value) && isGuid(value.documentId) && isGuid(value.versionId)
    && isNonNegativeInteger(value.sequence) && isNonNegativeInteger(value.offset)
    && isNonNegativeInteger(value.length) && value.length > 0
    && (value.pageNumber === null || (isNonNegativeInteger(value.pageNumber) && value.pageNumber > 0))
    && (value.sourceLocation == null || isAiKnowledgeSourceLocation(value.sourceLocation) && value.docxLocation == null && value.pageNumber === value.sourceLocation.pageNumber)
    && (value.docxLocation == null || (value.pageNumber === null && isAiKnowledgeDocxLocation(value.docxLocation)));
}

function isAiKnowledgeDocxLocation(value: unknown): value is AiKnowledgeDocxLocation {
  if (!isRecord(value) || !isNonNegativeInteger(value.blockSequence) || value.blockSequence >= 8192
    || !isNonNegativeInteger(value.paragraphNumber) || value.paragraphNumber < 1 || value.paragraphNumber > 8192) return false;
  return value.tableNumber === null && value.rowNumber === null && value.cellNumber === null
    || isNonNegativeInteger(value.tableNumber) && value.tableNumber >= 1 && value.tableNumber <= 128
      && isNonNegativeInteger(value.rowNumber) && value.rowNumber >= 1 && value.rowNumber <= 4096
      && isNonNegativeInteger(value.cellNumber) && value.cellNumber >= 1 && value.cellNumber <= 64;
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
  const count = message.citations.length + (message.personalScheduleCitations?.length ?? 0);
  return value.refused ? message.content === '' && count === 0
    : message.content.trim().length > 0 && count > 0;
}
