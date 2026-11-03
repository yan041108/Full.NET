import type { AiKnowledgeSearchComparisonResponse, AiKnowledgeSearchComparisonRanking, CompareAiKnowledgeSearchRequest } from './generated/models.generated.js';
import { readAiKnowledgeSearchComparisonResponse } from './generated/guards.generated.js';
import { isPublishedSearchHit } from './ai-knowledge-search-validation.js';
import type { AiKnowledgeSearchResult } from './ai-knowledge-search.js';
import { isKnowledgeSourceIdentity, sameKnowledgeSource } from './ai-knowledge-source-identity.js';

type Ranking = Omit<AiKnowledgeSearchComparisonRanking, 'hits'> & { readonly hits: AiKnowledgeSearchResult['hits'] };
export type AiKnowledgeSearchComparisonResult = Omit<AiKnowledgeSearchComparisonResponse, 'keyword' | 'vector'> & { readonly keyword: Ranking; readonly vector: Ranking };
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const same = sameKnowledgeSource;
const positive = (value: number) => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;

/** 比较必须绑定同一查询和全部来源；分数语义、正文与真实用量状态分别核验。 */
export function validateKnowledgeSearchComparison(value: AiKnowledgeSearchComparisonResponse, input: CompareAiKnowledgeSearchRequest): AiKnowledgeSearchComparisonResult {
  const invalid = () => new Error('Invalid knowledge search comparison response.'); value = readAiKnowledgeSearchComparisonResponse(value);
  if (value.strategy !== 'same_candidates_v1' || value.query !== input.query || !guid.test(value.usage.operationId) || value.usage.operationId[14] !== '7'
    || !['reported', 'unknown'].includes(value.usage.status) || value.sources.length < 1 || value.sources.length > 8
    || value.sources.length !== input.searchScope.sources.length || new Set(value.sources.map(row => row.documentId)).size !== value.sources.length) throw invalid();
  for (const source of value.sources) {
    if (!isKnowledgeSourceIdentity(source) || !input.searchScope.sources.some(expected => same(source, expected)) || !input.knowledgeBaseIds.includes(source.knowledgeBaseId)
      || input.searchScope.documentIds.length > 0 && !input.searchScope.documentIds.includes(source.documentId)
      || !guid.test(source.knowledgeBaseId) || !guid.test(source.documentId) || !guid.test(source.versionId) || !guid.test(source.publicationId)
      || !guid.test(source.generationId) || !positive(source.knowledgeBaseVersion) || !positive(source.documentVersion) || !/^[a-f0-9]{64}$/i.test(source.manifestHash)) throw invalid();
  }
  for (const [ranking, keyword] of [[value.keyword, true], [value.vector, false]] as const) {
    if (ranking.strategy !== (keyword ? 'keyword_all_v1' : 'vector_cosine_v1') || ranking.scoreKind !== (keyword ? 'keyword_occurrences' : 'cosine_similarity')
      || ranking.hits.length > input.limit || ranking.hasMore && ranking.hits.length !== input.limit || !keyword && ranking.hits.length === 0) throw invalid();
    const seen = new Set<string>(); let previous = Number.POSITIVE_INFINITY;
    for (const hit of ranking.hits) {
      const key = hit.source.documentId + ':' + hit.sequence;
      if (seen.has(key) || !isPublishedSearchHit(hit, value.sources)
        || typeof hit.score !== 'number' || !Number.isFinite(hit.score) || hit.score > previous
        || (keyword ? !Number.isSafeInteger(hit.score) || hit.score < 1 || hit.score > 512 : hit.score < -1 || hit.score > 1)) throw invalid();
      seen.add(key); previous = hit.score;
    }
  }
  for (const left of value.keyword.hits) {
    const right = value.vector.hits.find(hit => hit.source.documentId === left.source.documentId && hit.sequence === left.sequence);
    if (right && (right.text !== left.text || right.offset !== left.offset || right.length !== left.length || right.pageNumber !== left.pageNumber
      || right.documentTitle !== left.documentTitle || right.documentMetadataVersion !== left.documentMetadataVersion)) throw invalid();
  }
  if (input.includeDiagnostics ? value.diagnostics === null || value.diagnostics.sourceCount !== value.sources.length
    || value.diagnostics.scoreKind !== 'separate_keyword_and_cosine' || !positive(value.diagnostics.candidateCount) || value.diagnostics.candidateCount > 256
    || value.diagnostics.candidateCount < value.keyword.hits.length || (value.vector.hasMore ? value.diagnostics.candidateCount <= input.limit : value.diagnostics.candidateCount !== value.vector.hits.length)
    : value.diagnostics !== null) throw invalid();
  return value as AiKnowledgeSearchComparisonResult;
}
