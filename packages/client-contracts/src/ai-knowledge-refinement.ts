import type { AiKnowledgeSearchRefinementResponse, AiKnowledgeSearchComparisonRanking, RefineAiKnowledgeSearchRequest } from './generated/models.generated.js';
import { readAiKnowledgeSearchRefinementResponse } from './generated/guards.generated.js';
import { isPublishedSearchHit, sameSearchSource, searchHitBodyKey } from './ai-knowledge-search-validation.js';
import type { AiKnowledgeSearchResult } from './ai-knowledge-search.js';

type Ranking = Omit<AiKnowledgeSearchComparisonRanking, 'hits'> & { readonly hits: AiKnowledgeSearchResult['hits'] };
export type AiKnowledgeSearchRefinementResult = Omit<AiKnowledgeSearchRefinementResponse, 'originalKeyword' | 'originalVector' | 'rewrittenKeyword' | 'rewrittenVector' | 'reranked'> & {
  readonly originalKeyword: Ranking; readonly originalVector: Ranking; readonly rewrittenKeyword: Ranking | null; readonly rewrittenVector: Ranking | null; readonly reranked: Ranking | null;
};
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const positive = (value: number) => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
function queryValid(query: string): boolean {
  if (!query || query.length > 512 || [...query].some(character => { const code = character.codePointAt(0)!; return code >= 0xd800 && code <= 0xdfff; })) return false;
  const terms = query.split(/[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+/).filter(Boolean);
  return terms.length >= 1 && terms.length <= 16 && terms.every(term => term.length <= 64 && !/[\u0000-\u001f\u007f-\u009f]/.test(term));
}

/** 实验只能保留冻结来源与原查询；阶段顺序、真实回执、独立分数和全部重复片段分别核验。 */
export function validateKnowledgeSearchRefinement(value: AiKnowledgeSearchRefinementResponse, input: RefineAiKnowledgeSearchRequest): AiKnowledgeSearchRefinementResult {
  const invalid = () => new Error('Invalid controlled knowledge search experiment.'); value = readAiKnowledgeSearchRefinementResponse(value);
  if (value.strategy !== 'controlled_refinement_v1' || !['rewrite', 'rerank', 'rewrite_rerank'].includes(input.mode) || value.mode !== input.mode
    || value.query !== input.query || !queryValid(value.query) || !queryValid(value.effectiveQuery) || value.processingModelConfigId !== input.processingModelConfigId
    || value.processingModelVersion !== input.processingModelVersion || !guid.test(value.processingModelConfigId) || !positive(value.processingModelVersion)
    || ![null, 'rewrite', 'rerank'].includes(value.refusedStage) || value.sources.length < 1 || value.sources.length > 8
    || value.sources.length !== input.searchScope.sources.length || new Set(value.sources.map(source => source.documentId)).size !== value.sources.length) throw invalid();
  for (const source of value.sources) {
    if (!input.searchScope.sources.some(expected => sameSearchSource(source, expected)) || !input.knowledgeBaseIds.includes(source.knowledgeBaseId)
      || input.searchScope.documentIds.length > 0 && !input.searchScope.documentIds.includes(source.documentId)
      || !guid.test(source.knowledgeBaseId) || !guid.test(source.documentId) || !guid.test(source.versionId) || !guid.test(source.publicationId)
      || !guid.test(source.generationId) || !positive(source.knowledgeBaseVersion) || !positive(source.documentVersion) || !/^[a-f0-9]{64}$/i.test(source.manifestHash)) throw invalid();
  }
  const rewrite = input.mode !== 'rerank'; const rerank = input.mode !== 'rewrite' && value.refusedStage !== 'rewrite';
  if (!rewrite && value.refusedStage === 'rewrite' || input.mode === 'rewrite' && value.refusedStage === 'rerank'
    || (!rewrite || value.refusedStage === 'rewrite') && value.effectiveQuery !== value.query) throw invalid();
  const stages = ['original_embedding']; if (rewrite) { stages.push('rewrite'); if (value.refusedStage !== 'rewrite') stages.push('rewritten_embedding'); } if (rerank) stages.push('rerank');
  if (value.operations.length !== stages.length || new Set(value.operations.map(operation => operation.operationId)).size !== stages.length
    || value.operations.some((operation, index) => operation.stage !== stages[index] || !guid.test(operation.operationId) || operation.operationId[14] !== '7' || !['reported', 'unknown'].includes(operation.status))) throw invalid();
  const rewritten = rewrite && value.refusedStage !== 'rewrite';
  if (rewritten !== (value.rewrittenKeyword !== null && value.rewrittenVector !== null)
    || !rewritten && (value.rewrittenKeyword !== null || value.rewrittenVector !== null)
    || (rerank && value.refusedStage !== 'rerank') !== (value.reranked !== null)
    || (rerank ? value.rerankCandidateCount === null || !positive(value.rerankCandidateCount) || value.rerankCandidateCount > 8 : value.rerankCandidateCount !== null)) throw invalid();
  const bodies = new Map<string, string>();
  for (const [ranking, kind] of [[value.originalKeyword, 'keyword'], [value.originalVector, 'vector'], [value.rewrittenKeyword, 'keyword'], [value.rewrittenVector, 'vector'], [value.reranked, 'ordinal']] as const) {
    if (ranking === null) continue;
    if (ranking.strategy !== (kind === 'keyword' ? 'keyword_all_v1' : kind === 'vector' ? 'vector_cosine_v1' : 'model_order_prefix_v1')
      || ranking.scoreKind !== (kind === 'keyword' ? 'keyword_occurrences' : kind === 'vector' ? 'cosine_similarity' : 'ordinal_priority')
      || ranking.hits.length > input.limit || ranking.hasMore && ranking.hits.length !== input.limit || kind !== 'keyword' && ranking.hits.length === 0) throw invalid();
    const seen = new Set<string>(); let previous = Number.POSITIVE_INFINITY;
    for (let index = 0; index < ranking.hits.length; index++) {
      const hit = ranking.hits[index]!; const key = hit.source.documentId + ':' + hit.sequence;
      if (seen.has(key) || !isPublishedSearchHit(hit, value.sources) || typeof hit.score !== 'number' || !Number.isFinite(hit.score) || hit.score > previous
        || (kind === 'vector' ? hit.score < -1 || hit.score > 1 : !Number.isSafeInteger(hit.score) || hit.score < 1 || hit.score > (kind === 'keyword' ? 512 : 8))
        || kind === 'ordinal' && hit.score !== value.rerankCandidateCount! - index) throw invalid();
      const body = searchHitBodyKey(hit); if (bodies.has(key) && bodies.get(key) !== body) throw invalid(); bodies.set(key, body); seen.add(key); previous = hit.score;
    }
  }
  const active = value.rewrittenVector ?? value.originalVector;
  if (rerank && (value.rerankCandidateCount! < active.hits.length || !active.hasMore && value.rerankCandidateCount !== active.hits.length
    || active.hasMore && input.limit < 8 && value.rerankCandidateCount! <= input.limit)) throw invalid();
  if (value.reranked && (value.reranked.hits.length !== active.hits.length || value.reranked.hasMore !== active.hasMore
    || !active.hasMore && value.reranked.hits.some(hit => !active.hits.some(candidate => candidate.source.documentId === hit.source.documentId && candidate.sequence === hit.sequence)))) throw invalid();
  if (input.includeDiagnostics ? value.diagnostics === null || value.diagnostics.sourceCount !== value.sources.length
    || value.diagnostics.scoreKind !== 'separate_keyword_cosine_and_model_order' || !positive(value.diagnostics.candidateCount) || value.diagnostics.candidateCount > 256
    || (value.originalVector.hasMore ? value.diagnostics.candidateCount <= input.limit : value.diagnostics.candidateCount !== value.originalVector.hits.length)
    || rerank && value.rerankCandidateCount !== Math.min(8, value.diagnostics.candidateCount) : value.diagnostics !== null) throw invalid();
  return value as AiKnowledgeSearchRefinementResult;
}
