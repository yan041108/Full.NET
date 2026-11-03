import type { AiKnowledgeSearchResponse, AiKnowledgeSearchHit, SearchAiKnowledgeRequest,
  AiKnowledgeSearchContextResponse, AiKnowledgeSearchContextBlock, ReadAiKnowledgeSearchContextRequest } from './generated/models.generated.js';
import { readAiKnowledgeSearchResponse, readAiKnowledgeSearchContextResponse } from './generated/guards.generated.js';
import { isAiKnowledgeSourceLocation, type AiKnowledgeSourceLocation } from './ai-knowledge-location.js';
import { isAiKnowledgeCorrectionCitation, isAiKnowledgeSourceReference, type AiKnowledgeCorrectionCitation } from './ai-chat.js';
import { isKnowledgeSourceIdentity, sameKnowledgeSource, validKnowledgeSourcePosition } from './ai-knowledge-source-identity.js';

export type AiKnowledgeSearchResult = Omit<AiKnowledgeSearchResponse, 'hits'> & {
  readonly hits: readonly (Omit<AiKnowledgeSearchHit, 'sourceLocation' | 'correction' | 'score'> & {
    readonly score: number;
    readonly sourceLocation: AiKnowledgeSourceLocation | null;
    readonly correction: AiKnowledgeCorrectionCitation | null;
  })[];
};
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const hash = /^[0-9a-f]{64}$/i;
const positive = (value: number) => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
const same = sameKnowledgeSource;

/** 只有同一完整来源集合、真实坐标及显式诊断资格的响应才能进入展示缓冲。 */
export function validateKnowledgeSearch(value: AiKnowledgeSearchResponse, input: SearchAiKnowledgeRequest): AiKnowledgeSearchResult {
  const invalid = () => new Error('Invalid published knowledge search response.');
  value = readAiKnowledgeSearchResponse(value);
  if (value.strategy !== 'keyword_all_v1' || value.sources.length < 1 || value.sources.length > 8
    || !Number.isSafeInteger(value.totalMatches) || value.totalMatches < 0 || value.totalMatches > 256
    || value.hits.length !== Math.min(value.totalMatches, input.limit)
    || value.sources.some(source => !isKnowledgeSourceIdentity(source) || !guid.test(source.knowledgeBaseId) || !input.knowledgeBaseIds.includes(source.knowledgeBaseId)
      || !guid.test(source.documentId) || input.documentIds.length > 0 && !input.documentIds.includes(source.documentId)
      || !guid.test(source.versionId) || !guid.test(source.publicationId) || !guid.test(source.generationId)
      || !positive(source.knowledgeBaseVersion) || !positive(source.documentVersion) || !hash.test(source.manifestHash))
    || new Set(value.sources.map(source => source.documentId)).size !== value.sources.length
    || input.knowledgeBaseIds.some(id => !value.sources.some(source => source.knowledgeBaseId === id))
    || input.documentIds.some(id => !value.sources.some(source => source.documentId === id))
    || input.expectedSources !== null && (input.expectedSources.length !== value.sources.length
      || input.expectedSources.some(previous => !value.sources.some(source => same(previous, source))))) throw invalid();
  const seen = new Set<string>();
  for (const hit of value.hits) {
    const key = `${hit.source.generationId}:${hit.sequence}`;
    if (!validKnowledgeSourcePosition(hit.source, hit) || !value.sources.some(source => same(source, hit.source)) || seen.has(key) || !Number.isSafeInteger(hit.sequence) || hit.sequence < 0
      || !isAiKnowledgeSourceReference({ ...hit, documentId: hit.source.documentId, versionId: hit.source.versionId })
      || !hit.documentTitle.trim() || hit.documentTitle.length > 200 || !hit.text || hit.text.length > 32768
      || !positive(hit.documentMetadataVersion) || hit.documentMetadataVersion < hit.source.documentVersion
      || typeof hit.score !== 'number' || !Number.isSafeInteger(hit.score) || hit.score < 1 || hit.score > 512
      || hit.pageNumber !== null && !positive(hit.pageNumber)
      || hit.sourceLocation !== null && !isAiKnowledgeSourceLocation(hit.sourceLocation)
      || (hit.correction !== null ? !isAiKnowledgeCorrectionCitation(hit.correction) || hit.correction.textLength !== hit.text.length
          || hit.offset !== null || hit.length !== null || hit.sourceLocation !== null
        : hit.offset === null || hit.length === null || !Number.isSafeInteger(hit.offset) || hit.offset < 0 || !positive(hit.length)
          || hit.length !== hit.text.length || hit.offset + hit.length > 2147483647)) throw invalid();
    seen.add(key);
  }
  if (input.includeDiagnostics ? value.diagnostics === null || value.diagnostics.sourceCount !== value.sources.length
    || value.diagnostics.scoreKind !== 'keyword_occurrences' || !Number.isSafeInteger(value.diagnostics.candidateCount)
    || value.diagnostics.candidateCount < value.totalMatches || value.diagnostics.candidateCount > 256 : value.diagnostics !== null) throw invalid();
  return value as AiKnowledgeSearchResult;
}

export type AiKnowledgeSearchContextResult = Omit<AiKnowledgeSearchContextResponse, 'blocks'> & {
  readonly blocks: readonly (Omit<AiKnowledgeSearchContextBlock, 'sourceLocation' | 'correction'> & {
    readonly sourceLocation: AiKnowledgeSourceLocation | null;
    readonly correction: AiKnowledgeCorrectionCitation | null;
  })[];
};

/** 窗口必须属于请求的完整发布且连续包含锚块；边界、正文预算与每块位置分别核验。 */
export function validateKnowledgeSearchContext(value: AiKnowledgeSearchContextResponse, input: ReadAiKnowledgeSearchContextRequest): AiKnowledgeSearchContextResult {
  const invalid = () => new Error('Invalid published knowledge context response.');
  value = readAiKnowledgeSearchContextResponse(value);
  const source = value.source;
  if (value.strategy !== 'adjacent_sequence_v1' || !isKnowledgeSourceIdentity(source) || !same(source, input.source)
    || !guid.test(source.knowledgeBaseId) || !guid.test(source.documentId) || !guid.test(source.versionId)
    || !guid.test(source.publicationId) || !guid.test(source.generationId) || !positive(source.knowledgeBaseVersion)
    || !positive(source.documentVersion) || !hash.test(source.manifestHash)
    || value.anchorSequence !== input.anchorSequence || value.radius !== input.radius || !Number.isSafeInteger(value.anchorSequence)
    || value.anchorSequence < 0 || value.anchorSequence > 255 || !Number.isSafeInteger(value.radius) || value.radius < 0 || value.radius > 2
    || !value.documentTitle.trim() || value.documentTitle.length > 200 || !positive(value.documentMetadataVersion)
    || value.documentMetadataVersion < source.documentVersion || value.blocks.length < 1 || value.blocks.length > 2 * value.radius + 1) throw invalid();
  const start = Math.max(0, input.anchorSequence - input.radius); let size = 0;
  for (let i = 0; i < value.blocks.length; i++) {
    const block = value.blocks[i]!;
    if (!validKnowledgeSourcePosition(source, block) || block.sequence !== start + i || block.sequence > 255 || !block.text || block.text.length > 32768
      || !isAiKnowledgeSourceReference({ ...block, documentId: source.documentId, versionId: source.versionId })
      || (block.correction !== null ? !isAiKnowledgeCorrectionCitation(block.correction) || block.correction.textLength !== block.text.length
          || block.offset !== null || block.length !== null || block.sourceLocation !== null
        : block.offset === null || !Number.isSafeInteger(block.offset) || block.offset < 0 || block.length !== block.text.length
          || block.offset + block.length > 2147483647)) throw invalid();
    size += block.text.length; if (size > 32768) throw invalid();
  }
  const end = value.blocks[value.blocks.length - 1]!.sequence;
  if (end < input.anchorSequence || end > input.anchorSequence + input.radius || value.hasPreviousBlocks !== (start > 0)
    || end < input.anchorSequence + input.radius && value.hasNextBlocks) throw invalid();
  return value as AiKnowledgeSearchContextResult;
}
