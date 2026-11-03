import { describe, expect, it } from 'vitest';
import { validateKnowledgeSearch } from './ai-knowledge-search.js';
import type { AiKnowledgeSearchResponse, SearchAiKnowledgeRequest } from './generated/models.generated.js';
const id = (n: number) => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const request: SearchAiKnowledgeRequest = { knowledgeBaseIds: [id(1)], documentIds: [], embeddingModelConfigId: id(6), embeddingModelVersion: 1, query: '事实', limit: 20, includeDiagnostics: false, expectedSources: null };
type Mutable<T> = T extends readonly (infer Item)[] ? Mutable<Item>[] : T extends object ? { -readonly [Key in keyof T]: Mutable<T[Key]> } : T;
const response = (): Mutable<AiKnowledgeSearchResponse> => ({ strategy: 'keyword_all_v1', sources: [{ ...source }], totalMatches: 1, diagnostics: null,
  hits: [{ source: { ...source }, documentTitle: '制度', documentMetadataVersion: 1, sequence: 0, text: '事实', score: 1, pageNumber: null, offset: 0, length: 2, sourceLocation: null, docxLocation: null, correction: null }] });
describe('已发布搜索的交付守卫', () => {
  it('接受同一来源和真实空结果，拒绝旧发布悄悄替换', () => {
    expect(validateKnowledgeSearch(response(), request).hits).toHaveLength(1);
    expect(validateKnowledgeSearch({ ...response(), totalMatches: 0, hits: [] }, request).hits).toHaveLength(0);
    expect(() => validateKnowledgeSearch(response(), { ...request, expectedSources: [{ ...source, publicationId: id(9) }] })).toThrow();
  });
  it.each(['scope', 'version', 'duplicate', 'missing', 'coordinates', 'score', 'title'] as const)('拒绝无效或混合命中 %s', fault => {
    const value = response();
    if (fault === 'scope') value.sources[0]!.knowledgeBaseId = id(9);
    if (fault === 'version') value.hits[0]!.source.versionId = id(9);
    if (fault === 'duplicate') { value.hits = [value.hits[0]!, value.hits[0]!]; value.totalMatches = 2; }
    if (fault === 'missing') value.hits = [];
    if (fault === 'coordinates') value.hits[0]!.length = 3;
    if (fault === 'score') value.hits[0]!.score = Number.NaN;
    if (fault === 'title') value.hits[0]!.documentTitle = ' ';
    expect(() => validateKnowledgeSearch(value, request)).toThrow();
  });
  it('诊断资格不能静默降低，也不接受额外诊断数据', () => {
    const value = response(); value.diagnostics = { sourceCount: 1, candidateCount: 1, scoreKind: 'keyword_occurrences' };
    expect(() => validateKnowledgeSearch(value, request)).toThrow();
    expect(validateKnowledgeSearch(value, { ...request, includeDiagnostics: true }).diagnostics?.candidateCount).toBe(1);
    expect(() => validateKnowledgeSearch(response(), { ...request, includeDiagnostics: true })).toThrow();
  });
  it('人工补写不接受伪造原位置或不完整来源集合', () => {
    const value = response(); const hit = value.hits[0]!;
    hit.offset = null; hit.length = null;
    hit.correction = { revisionId: id(7), textLength: 2, spans: [{ contentKind: 'supplement', textOffset: 0, textLength: 2, sourceLocation: null, sourceOffset: null, sourceLength: null }] };
    expect(validateKnowledgeSearch(value, request).hits[0]!.correction).not.toBeNull();
    hit.correction.spans[0]!.sourceOffset = 0;
    expect(() => validateKnowledgeSearch(value, request)).toThrow();
  });
});
