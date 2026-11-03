import { describe, expect, it } from 'vitest';
import { validateKnowledgeSearchContext } from './ai-knowledge-search.js';
import type { AiKnowledgeSearchContextResponse, ReadAiKnowledgeSearchContextRequest } from './generated/models.generated.js';
const id = (n: number) => `01900000-0000-7000-8000-${String(n).padStart(12, '0')}`;
const source = { knowledgeBaseId: id(1), knowledgeBaseVersion: 1, documentId: id(2), documentVersion: 1, versionId: id(3), publicationId: id(4), generationId: id(5), manifestHash: 'a'.repeat(64) };
const input: ReadAiKnowledgeSearchContextRequest = { source, embeddingModelConfigId: id(6), embeddingModelVersion: 1, anchorSequence: 0, radius: 1 };
const response = (): AiKnowledgeSearchContextResponse => ({ strategy: 'adjacent_sequence_v1', source, documentTitle: '制度', documentMetadataVersion: 2,
  anchorSequence: 0, radius: 1, hasPreviousBlocks: false, hasNextBlocks: true,
  blocks: [0, 1].map(sequence => ({ sequence, text: '事实', pageNumber: null, offset: sequence * 10, length: 2, sourceLocation: null, docxLocation: null, correction: null })) });
describe('完整发布上下文交付边界', () => {
  it('窗口不能越过完整代次的二百五十六块上限', () => {
    const value = { ...response(), anchorSequence: 255, radius: 2, hasPreviousBlocks: true, hasNextBlocks: true,
      blocks: [253, 254, 255, 256, 257].map(sequence => ({ ...response().blocks[0]!, sequence })) };
    expect(() => validateKnowledgeSearchContext(value, { ...input, anchorSequence: 255, radius: 2 })).toThrow();
  });
  it('交付真实连续窗口和末端边界', () => { expect(validateKnowledgeSearchContext(response(), input).blocks).toHaveLength(2);
    expect(validateKnowledgeSearchContext({ ...response(), blocks: [response().blocks[0]!], hasNextBlocks: false }, input).blocks).toHaveLength(1); });
  it.each(['source', 'gap', 'anchor', 'radius', 'edge', 'location', 'metadata', 'budget'] as const)('拒绝混代、漏块或伪造 %s', fault => {
    let value = response();
    if (fault === 'source') value = { ...value, source: { ...source, publicationId: id(8) } };
    if (fault === 'gap') value = { ...value, blocks: value.blocks.map((block, i) => i === 1 ? { ...block, sequence: 2 } : block) };
    if (fault === 'anchor') value = { ...value, anchorSequence: 1 };
    if (fault === 'radius') value = { ...value, radius: 0 };
    if (fault === 'edge') value = { ...value, hasPreviousBlocks: true };
    if (fault === 'location') value = { ...value, blocks: value.blocks.map((block, i) => i === 0 ? { ...block, length: 3 } : block) };
    if (fault === 'metadata') value = { ...value, documentMetadataVersion: 0 };
    if (fault === 'budget') value = { ...value, blocks: value.blocks.map(block => ({ ...block, text: '字'.repeat(20000), length: 20000 })) };
    expect(() => validateKnowledgeSearchContext(value, input)).toThrow();
  });
});
