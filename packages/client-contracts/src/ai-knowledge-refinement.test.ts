import { describe, expect, it } from 'vitest';
import { validateKnowledgeSearchRefinement } from './ai-knowledge-refinement.js';
import type { AiKnowledgeSearchRefinementResponse } from './generated/models.generated.js';
type Mutable<T> = T extends Array<infer U> ? Array<Mutable<U>> : T extends object ? { -readonly [K in keyof T]: Mutable<T[K]> } : T;
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const request = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), processingModelConfigId: id(7), processingModelVersion: 1, query: '事实', mode: 'rewrite_rerank', limit: 2, includeDiagnostics: false,
  searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
const hit = { source, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 1, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null };
const keyword = () => ({ strategy: 'keyword_all_v1', scoreKind: 'keyword_occurrences', hasMore: false, hits: [{ ...hit, source: { ...source } }] });
const vector = () => ({ strategy: 'vector_cosine_v1', scoreKind: 'cosine_similarity', hasMore: false, hits: [{ ...hit, source: { ...source }, score: 0.8 }] });
const response = (): Mutable<AiKnowledgeSearchRefinementResponse> => ({ strategy: 'controlled_refinement_v1', mode: 'rewrite_rerank', query: '事实', effectiveQuery: '事实 范围', processingModelConfigId: id(7), processingModelVersion: 1, sources: [{ ...source }], refusedStage: null,
  originalKeyword: keyword(), originalVector: vector(), rewrittenKeyword: keyword(), rewrittenVector: vector(), reranked: { strategy: 'model_order_prefix_v1', scoreKind: 'ordinal_priority', hasMore: false, hits: [{ ...hit, source: { ...source } }] }, rerankCandidateCount: 1,
  operations: ['original_embedding', 'rewrite', 'rewritten_embedding', 'rerank'].map((stage, index) => ({ stage, operationId: id(10 + index), status: 'reported' })), diagnostics: null });
describe('受控改写重排契约', () => {
  it.each(['reported', 'unknown'])('保留完整来源、原查询和逐阶段回执 %s', status => { const value = response(); value.operations.forEach(row => { row.status = status; });
    expect(validateKnowledgeSearchRefinement(value, request).query).toBe('事实'); expect(value.reranked?.hits[0]?.score).toBe(1); });
  it.each(['rewrite', 'rerank'])('独立模式只允许实际阶段 %s', mode => { const value = response(); value.mode = mode;
    if (mode === 'rewrite') { value.operations.pop(); value.reranked = null; value.rerankCandidateCount = null; }
    else { value.effectiveQuery = value.query; value.operations.splice(1, 2); value.rewrittenKeyword = null; value.rewrittenVector = null; }
    expect(validateKnowledgeSearchRefinement(value, { ...request, mode }).mode).toBe(mode); });
  it.each(['rewrite', 'rerank'])('拒绝是明确结果，不能伪装已完成阶段 %s', stage => { const value = response(); value.refusedStage = stage; value.reranked = null;
    if (stage === 'rewrite') { value.effectiveQuery = value.query; value.operations.splice(2); value.rewrittenKeyword = null; value.rewrittenVector = null; value.rerankCandidateCount = null; }
    expect(validateKnowledgeSearchRefinement(value, request).refusedStage).toBe(stage); });
  it('只在请求诊断时接受完整计数', () => { const value = response(); value.diagnostics = { sourceCount: 1, candidateCount: 1, scoreKind: 'separate_keyword_cosine_and_model_order' };
    expect(validateKnowledgeSearchRefinement(value, { ...request, includeDiagnostics: true }).diagnostics?.candidateCount).toBe(1); });
  it.each(['query', 'effective', 'version', 'model', 'source', 'body', 'ordinal', 'scoreKind', 'cosine', 'frequency', 'stage', 'id', 'usage', 'count', 'prefix', 'diagnostics', 'mixedRefusal', 'halfRewrite'])('拒绝漂移或混合语义 %s', fault => {
    const value = response();
    if (fault === 'query') value.query = '别处'; if (fault === 'effective') value.effectiveQuery = 'x'.repeat(65);
    if (fault === 'version') value.processingModelVersion = 2; if (fault === 'model') value.processingModelConfigId = id(9);
    if (fault === 'source') value.reranked!.hits[0]!.source.publicationId = id(9); if (fault === 'body') value.reranked!.hits[0]!.text = '伪造';
    if (fault === 'ordinal') value.reranked!.hits[0]!.score = 0.8; if (fault === 'scoreKind') value.reranked!.scoreKind = 'confidence';
    if (fault === 'cosine') value.originalVector.hits[0]!.score = 2; if (fault === 'frequency') value.originalKeyword.hits[0]!.score = 0.8;
    if (fault === 'stage') value.operations.reverse(); if (fault === 'id') value.operations[1]!.operationId = value.operations[0]!.operationId;
    if (fault === 'usage') value.operations[1]!.status = 'zero_cost'; if (fault === 'count') value.rerankCandidateCount = 9;
    if (fault === 'prefix') value.reranked!.hasMore = true; if (fault === 'diagnostics') value.diagnostics = { sourceCount: 1, candidateCount: 1, scoreKind: 'separate_keyword_cosine_and_model_order' };
    if (fault === 'mixedRefusal') value.refusedStage = 'rewrite'; if (fault === 'halfRewrite') value.rewrittenKeyword = null;
    expect(() => validateKnowledgeSearchRefinement(value, request)).toThrow();
  });
});
