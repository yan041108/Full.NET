import { describe, expect, it } from 'vitest';
import { readAskAiKnowledgeRequest } from './generated/guards.generated.js';
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const body = { knowledgeBaseIds: [id(1)], embeddingModelConfigId: id(6), question: '问题', searchScope: { documentIds: [id(2)], embeddingModelVersion: 1, sources: [source] } };
describe('搜索范围的生成问答契约', () => {
  it('读取完整冻结来源且不丢弃新字段', () => { expect(readAskAiKnowledgeRequest(body)).toEqual(body); });
  it('普通会话问答仍可省略搜索范围', () => { const { searchScope, ...plain } = body; expect(readAskAiKnowledgeRequest(plain)).toEqual(plain); expect(searchScope.sources).toHaveLength(1); });
  it.each([{ ...body.searchScope, embeddingModelVersion: 'unknown' }, { ...body.searchScope, documentIds: ['not-an-id'] },
    { ...body.searchScope, sources: [{ ...source, publicationId: null }] }, { ...body.searchScope, sources: [null] }])('拒绝范围结构错误', scope => {
    expect(() => readAskAiKnowledgeRequest({ ...body, searchScope: scope })).toThrow();
  });
});
