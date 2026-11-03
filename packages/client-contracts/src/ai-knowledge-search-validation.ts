import type { AiKnowledgeSearchHit, AiKnowledgeSearchSource } from './generated/models.generated.js';
import { isAiKnowledgeCorrectionCitation, isAiKnowledgeSourceReference } from './ai-chat.js';
import { isKnowledgeSourceIdentity, sameKnowledgeSource, validKnowledgeSourcePosition } from './ai-knowledge-source-identity.js';

export const sameSearchSource = sameKnowledgeSource;
/** 分数由策略分别核验；所有策略共用同一完整正文与真实位置边界。 */
export function isPublishedSearchHit(hit: AiKnowledgeSearchHit, sources: readonly AiKnowledgeSearchSource[]): boolean {
  return isKnowledgeSourceIdentity(hit.source) && validKnowledgeSourcePosition(hit.source, hit) && sources.some(source => sameSearchSource(source, hit.source)) && Number.isSafeInteger(hit.sequence) && hit.sequence >= 0 && hit.sequence <= 255
    && !!hit.text && hit.text.length <= 32768 && !!hit.documentTitle.trim() && hit.documentTitle.length <= 200
    && Number.isSafeInteger(hit.documentMetadataVersion) && hit.documentMetadataVersion >= hit.source.documentVersion && hit.documentMetadataVersion <= 2147483647
    && isAiKnowledgeSourceReference({ ...hit, documentId: hit.source.documentId, versionId: hit.source.versionId })
    && (hit.correction !== null ? isAiKnowledgeCorrectionCitation(hit.correction) && hit.correction.textLength === hit.text.length
      && hit.offset === null && hit.length === null && hit.sourceLocation === null
      : hit.offset !== null && Number.isSafeInteger(hit.offset) && hit.offset >= 0 && hit.length === hit.text.length && hit.offset + hit.length <= 2147483647);
}
/** 只在已验收的有界 DTO 上规范属性顺序，用于多个排名间的完整片段一致性。 */
export function searchHitBodyKey(hit: AiKnowledgeSearchHit): string {
  const { score: _score, ...body } = hit;
  const canonical = (value: unknown): unknown => Array.isArray(value) ? value.map(canonical)
    : value !== null && typeof value === 'object' ? Object.fromEntries(Object.entries(value).sort(([left], [right]) => left.localeCompare(right)).map(([key, field]) => [key, canonical(field)])) : value;
  return JSON.stringify(canonical(body));
}
