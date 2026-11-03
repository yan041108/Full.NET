import { describe, expect, it } from 'vitest';
import { isAiChatMessage, isAiKnowledgeCorrectionCitation } from '../src/ai-chat';
import { readAiChatMessageResponse } from '../src/generated/guards.generated';

const id = '01900000-0000-7000-8000-000000000001';
const location = (pageNumber: number) => ({ blockSequence: pageNumber - 1, kind: 'pdf_page', pageNumber, paragraphNumber: null,
  tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null });
const correction = () => ({ revisionId: id, textLength: 12, spans: [
  { contentKind: 'original', textOffset: 0, textLength: 4, sourceLocation: location(1), sourceOffset: 2, sourceLength: 4 },
  { contentKind: 'corrected', textOffset: 5, textLength: 3, sourceLocation: location(2), sourceOffset: 0, sourceLength: 20 },
  { contentKind: 'supplement', textOffset: 9, textLength: 3, sourceLocation: null, sourceOffset: null, sourceLength: null }
] });
const citation = () => ({ id, documentId: id, versionId: id, sequence: 0, pageNumber: null, offset: null, length: null, correction: correction() });
const message = (source: unknown) => ({ id, sessionId: id, roleKey: 'assistant', content: '答案', statusKey: 'completed',
  promptTokens: null, completionTokens: null, createdAtUtc: '2026-10-28T00:00:00Z', citations: [source] });

describe('人工引用的真实来源边界', () => {
  it('真实 OpenAPI 生成读取器保留可空顶层位置及完整人工来源集合', () => {
    const expected = message(citation()); const decoded = readAiChatMessageResponse(expected);
    expect(decoded).toEqual(expected); expect(isAiChatMessage(decoded)).toBe(true);
  });
  it('接受多页原文、更正和无来源补写，保留不同的原文与编辑长度', () => {
    expect(isAiKnowledgeCorrectionCitation(correction())).toBe(true);
    expect(isAiChatMessage(message(citation()))).toBe(true);
  });
  it.each(['pageNumber', 'offset', 'length'] as const)('人工引用不能伪造顶层 %s', field => {
    expect(isAiChatMessage(message({ ...citation(), [field]: 1 }))).toBe(false);
  });
  it.each(['kind', 'overlap', 'range', 'literal', 'supplement', 'location', 'revision', 'count'])('拒绝损坏的 %s 元数据', fault => {
    const value = correction();
    if (fault === 'kind') value.spans[0]!.contentKind = 'model';
    if (fault === 'overlap') value.spans[1]!.textOffset = 0;
    if (fault === 'range') value.spans[1]!.textLength = 8001;
    if (fault === 'literal') value.spans[0]!.sourceLength = 5;
    if (fault === 'supplement') value.spans[2]!.sourceOffset = 0;
    if (fault === 'location') value.spans[1]!.sourceLocation = null;
    if (fault === 'revision') value.revisionId = 'invalid';
    if (fault === 'count') value.spans = Array.from({ length: 33 }, () => value.spans[0]!);
    expect(isAiKnowledgeCorrectionCitation(value)).toBe(false);
    expect(isAiChatMessage(message({ ...citation(), correction: value }))).toBe(false);
  });
  it('确认后的低置信度 OCR 保留实际置信度，不能当作未经校对的原文', () => {
    const value = correction();
    const image = { ...location(1), kind: 'image_region', pageNumber: null, image: { frameNumber: 1, imageWidth: 100, imageHeight: 100,
      x: 1, y: 2, width: 80, height: 70, confidence: .4, tableNumber: null, rowNumber: null, columnNumber: null, parent: null } };
    const reviewed = { ...value.spans[1], sourceLocation: image };
    value.spans = [reviewed] as typeof value.spans;
    expect(isAiKnowledgeCorrectionCitation(value)).toBe(true);
    value.spans = [{ ...reviewed, contentKind: 'original', sourceLength: reviewed.textLength }] as typeof value.spans;
    expect(isAiKnowledgeCorrectionCitation(value)).toBe(false);
  });
  it('自动引用仍然需要真实数值位置', () => expect(isAiChatMessage(message({ ...citation(), correction: null }))).toBe(false));
});
