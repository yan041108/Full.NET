import { describe, expect, it } from 'vitest';
import { validateKnowledgeSearchComparison } from './ai-knowledge-search-comparison.js';
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const request = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), query: '事实', limit: 2, includeDiagnostics: false,
  searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
const response = () => ({ strategy: 'same_candidates_v1', query: '事实', sources: [{ ...source }], keyword: { strategy: 'keyword_all_v1', scoreKind: 'keyword_occurrences', hasMore: false,
  hits: [{ source: { ...source }, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 2, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null }] },
  vector: { strategy: 'vector_cosine_v1', scoreKind: 'cosine_similarity', hasMore: false,
    hits: [{ source: { ...source }, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 0.8, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null }] },
  usage: { operationId: id(7), status: 'reported' }, diagnostics: null as { sourceCount: number; candidateCount: number; scoreKind: string } | null });
describe('同候选策略比较契约', () => {
  it.each(['reported', 'unknown'])('保留真实用量状态及独立分数 %s', status => { const value = response(); value.usage.status = status;
    expect(validateKnowledgeSearchComparison(value, request).usage.status).toBe(status); expect(value.keyword.hits[0]!.score).toBe(2); expect(value.vector.hits[0]!.score).toBe(0.8); });
  it('无字面命中仍允许合法向量排名', () => { const value = response(); value.keyword.hits = []; expect(validateKnowledgeSearchComparison(value, request).keyword.hits).toHaveLength(0); });
  it('拒绝以命名浮点字符串交付余弦分数', () => { const value = response(); expect(() => validateKnowledgeSearchComparison({ ...value,
    vector: { ...value.vector, hits: [{ ...value.vector.hits[0]!, score: 'NaN' }] } }, request)).toThrow(); });
  it('只有请求诊断时返回与实际排名一致的完整候选计数', () => { const value = response(); value.diagnostics = { sourceCount: 1, candidateCount: 1, scoreKind: 'separate_keyword_and_cosine' };
    expect(validateKnowledgeSearchComparison(value, { ...request, includeDiagnostics: true }).diagnostics?.candidateCount).toBe(1); });
  it.each(['keywordScore', 'vectorScore', 'query', 'source', 'usage', 'diagnostics', 'duplicate', 'inconsistent', 'prefix'])('拒绝漂移或混合语义 %s', fault => {
    const value = response();
    if (fault === 'keywordScore') value.keyword.hits[0]!.score = 0.7;
    if (fault === 'vectorScore') value.vector.hits[0]!.score = 2;
    if (fault === 'query') value.query = '另一查询';
    if (fault === 'source') value.vector.hits[0]!.source.publicationId = id(9);
    if (fault === 'usage') value.usage.status = 'zero_cost';
    if (fault === 'diagnostics') value.diagnostics = { sourceCount: 1, candidateCount: 1, scoreKind: 'separate_keyword_and_cosine' };
    if (fault === 'duplicate') value.vector.hits.push({ ...value.vector.hits[0]! });
    if (fault === 'inconsistent') value.vector.hits[0]!.text = '别处';
    if (fault === 'prefix') value.vector.hasMore = true;
    expect(() => validateKnowledgeSearchComparison(value, request)).toThrow();
  });
});
