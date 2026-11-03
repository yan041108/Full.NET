import { describe, expect, it } from 'vitest';
import { isKnowledgeSourceIdentity, knowledgeFaqCitation, sameKnowledgeSource, validKnowledgeSourcePosition } from './ai-knowledge-source-identity.js';
import { isAiKnowledgeSourceReference } from './ai-chat.js';
const id = (n: number) => `01990000-0000-7000-8000-${n.toString().padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 2, documentId: id(2), documentVersion: 3,
  versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'A'.repeat(64), sourceKind: 'faq-revision-v1', faqId: id(6), faqRevisionId: id(7) };
describe('FAQ 来源身份不可降级', () => {
  it('接受完整 FAQ 节点及普通文件，拒绝丢失或未知的类型身份', () => {
    expect(isKnowledgeSourceIdentity(source)).toBe(true);
    for (const value of [{ ...source, sourceKind: 'document-v1' }, { ...source, faqId: null }, { ...source, faqRevisionId: null }, { ...source, sourceKind: 'future' }]) expect(isKnowledgeSourceIdentity(value)).toBe(false);
    expect(isKnowledgeSourceIdentity({ ...source, sourceKind: 'document-v1', faqId: null, faqRevisionId: null })).toBe(true);
  });
  it('发布、FAQ 根及修订漂移不能以相同文档身份掩盖', () => {
    expect(sameKnowledgeSource(source, { ...source })).toBe(true);
    for (const value of [{ ...source, faqId: id(8) }, { ...source, faqRevisionId: id(8) }, { ...source, publicationId: id(8) }, { ...source, sourceKind: 'document-v1', faqId: null, faqRevisionId: null }]) expect(sameKnowledgeSource(source, value)).toBe(false);
  });
  it('FAQ 预览保留冻结发布及批准索引空间，没有文件下载身份', () => {
    const faq = knowledgeFaqCitation(source, id(9), 2)!;
    expect(faq.source).toEqual(source); expect(faq.source).not.toBe(source); expect(faq.embeddingModelConfigId).toBe(id(9));
    expect(knowledgeFaqCitation({ ...source, sourceKind: 'document-v1', faqId: null, faqRevisionId: null }, undefined, undefined)).toBeUndefined();
    expect(() => knowledgeFaqCitation(source, id(9), 0)).toThrow();
  });
  it('FAQ 只使用修订区间，拒绝假文件页码和原文坐标', () => {
    const position = { pageNumber: null, sourceLocation: null, docxLocation: null, correction: null };
    expect(validKnowledgeSourcePosition(source, position)).toBe(true);
    for (const value of [{ ...position, pageNumber: 1 }, { ...position, correction: {} }, { ...position, sourceLocation: {} }]) expect(validKnowledgeSourcePosition(source, value)).toBe(false);
    const reference = { documentId: source.documentId, versionId: source.versionId, sequence: 0, offset: 0, length: 5,
      ...position, faqSource: knowledgeFaqCitation(source, id(9), 2) };
    expect(isAiKnowledgeSourceReference(reference)).toBe(true);
    expect(isAiKnowledgeSourceReference({ ...reference, documentId: id(10) })).toBe(false);
    expect(isAiKnowledgeSourceReference({ ...reference, faqSource: { ...reference.faqSource, source: { ...source, sourceKind: 'future' } } })).toBe(false);
  });
});
