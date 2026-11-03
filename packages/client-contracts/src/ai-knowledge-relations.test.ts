import { describe, expect, it } from 'vitest';
import { validateKnowledgeSearchRelations } from './ai-knowledge-relations.js';
import type { AiKnowledgeSearchRelationsResponse, AiKnowledgeSourceLocationResponse, ReadAiKnowledgeSearchRelationsRequest } from './generated/models.generated.js';
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const input: ReadAiKnowledgeSearchRelationsRequest = { source, embeddingModelConfigId: id(6), embeddingModelVersion: 1, anchorSequence: 3 };
const parent: AiKnowledgeSourceLocationResponse = { blockSequence: 0, kind: 'docx_paragraph', paragraphNumber: 1, pageNumber: null,
  tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
const child: AiKnowledgeSourceLocationResponse = { ...parent, blockSequence: 1, kind: 'image_region', paragraphNumber: null,
  image: { frameNumber: 1, imageWidth: 100, imageHeight: 100, x: 0, y: 0, width: 100, height: 100, confidence: 1, tableNumber: null, rowNumber: null, columnNumber: null, parent } };
type Mutable<T> = T extends readonly (infer V)[] ? Mutable<V>[] : T extends object ? { -readonly [K in keyof T]: Mutable<T[K]> } : T;
const response = (): Mutable<AiKnowledgeSearchRelationsResponse> => ({ strategy: 'embedded_image_parent_v1', source, documentTitle: '制度', documentMetadataVersion: 2,
  anchorSequence: 3, relationState: 'complete', relations: [{ parentSequence: 0, childSequence: 3, parentLocation: parent, childLocation: child }],
  blocks: [0, 3].map(sequence => ({ sequence, text: '事实', pageNumber: null, offset: 0, length: 2, sourceLocation: sequence === 0 ? parent : child, docxLocation: null, correction: null })) });
describe('真实嵌入父子交付边界', () => {
  it('接受非相邻的真实关系及完整独立正文', () => { expect(validateKnowledgeSearchRelations(response(), input).blocks.map(block => block.sequence)).toEqual([0, 3]); });
  it('父正文未索引时不接受伪造父正文', () => { const value = response(); value.blocks = [value.blocks[1]!]; value.relations[0]!.parentSequence = null; value.relationState = 'parent_not_indexed';
    expect(validateKnowledgeSearchRelations(value, input).relationState).toBe('parent_not_indexed'); });
  it('人工合并可在同一块保留真实关系两端', () => { const value = response(); value.blocks = [{ ...value.blocks[1]!, sourceLocation: null, offset: null, length: null,
    correction: { revisionId: id(9), textLength: 2, spans: [{ contentKind: 'corrected', textOffset: 0, textLength: 1, sourceLocation: parent, sourceOffset: 0, sourceLength: 1 },
      { contentKind: 'corrected', textOffset: 1, textLength: 1, sourceLocation: child, sourceOffset: 0, sourceLength: 1 }] } }]; value.relations[0]!.parentSequence = 3;
    expect(validateKnowledgeSearchRelations(value, input).blocks).toHaveLength(1); });
  it('普通锚块明确没有关系', () => { const value = response(); value.blocks = [{ ...value.blocks[1]!, sourceLocation: null }]; value.relations = []; value.relationState = 'none';
    expect(validateKnowledgeSearchRelations(value, input).relations).toHaveLength(0); });
  it.each(['source', 'parent', 'child', 'extra', 'duplicate', 'order', 'budget', 'metadata', 'anchor', 'state', 'missing', 'unindexed', 'raw-confidence', 'dropped-proof'] as const)('拒绝伪造或不完整 %s', fault => {
    const value = response();
    if (fault === 'source') value.source = { ...source, publicationId: id(8) };
    if (fault === 'parent') value.relations[0]!.parentLocation = { ...parent, paragraphNumber: 2 };
    if (fault === 'child') value.relations[0]!.childSequence = 0;
    if (fault === 'extra') value.blocks.splice(1, 0, { ...value.blocks[0]!, sequence: 2 });
    if (fault === 'duplicate') value.relations.push({ ...value.relations[0]! });
    if (fault === 'order') value.blocks.reverse();
    if (fault === 'budget') value.blocks = value.blocks.map(block => ({ ...block, text: '字'.repeat(17000), length: 17000 }));
    if (fault === 'metadata') value.documentMetadataVersion = 0;
    if (fault === 'anchor') value.anchorSequence = 0;
    if (fault === 'state') value.relationState = 'none';
    if (fault === 'missing') value.blocks = [value.blocks[1]!];
    if (fault === 'unindexed') { value.relations[0]!.parentSequence = null; value.relationState = 'parent_not_indexed'; }
    if (fault === 'raw-confidence') value.blocks[1]!.sourceLocation = { ...child, image: { ...child.image!, confidence: 0.2 } };
    if (fault === 'dropped-proof') { value.blocks = [value.blocks[1]!]; value.relations = []; value.relationState = 'none'; }
    expect(() => validateKnowledgeSearchRelations(value, input)).toThrow();
  });
});
