import { describe, expect, it } from 'vitest';
import { validateKnowledgeCorrectionPreparation, validateKnowledgeCorrectionWorkspace, validateKnowledgeCorrectionRevision, validateKnowledgeCorrectionHistory } from './ai-knowledge-correction-workspace.js';
import type { AiKnowledgeCorrectionPreparationResponse, AiKnowledgeCorrectionWorkspaceResponse } from './generated/models.generated.js';
const id = (n: number) => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const location = { blockSequence: 0, kind: 'document', pageNumber: null, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null };
const page = (): AiKnowledgeCorrectionWorkspaceResponse => ({
  versionId: id(1), documentVersion: 3, sourceId: id(2), revisionId: id(3), headVersion: 1, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64),
  parserRuntimeId: 'managed:utf8-text-v1', contentType: 'text/plain', originalFileName: 'source.txt', blockIndex: 0, totalBlocks: 2, isSourceView: false,
  block: { id: id(4), excluded: false, separator: '\n', parts: [{ id: id(5), text: '已校对', prefix: '', contentKind: 'corrected', anchorId: id(6), originalText: '原识别文字', sourceOffset: 0, sourceLength: 5, sourceLocation: location }] }
});
const task = (): AiKnowledgeCorrectionPreparationResponse => ({ id: id(7), versionId: id(1), documentVersion: 3, state: 'queued', attemptCount: 0, sourceId: null, failureCode: null, createdAtUtc: '2026-10-28T00:00:00Z', updatedAtUtc: '2026-10-28T00:00:00Z' });
describe('校对维护来源的语义边界', () => {
  const expected = { documentVersion: 3, sourceId: id(2), revisionId: id(3), headVersion: 1, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64) };
  const revision = () => ({ id: id(9), versionId: id(1), sourceId: id(2), parentId: id(3), headVersion: 2, documentVersion: 4, payloadHash: 'c'.repeat(64), reason: '\uFEFF 原样说明 \uFEFF', createdAtUtc: '2026-10-28T00:00:00Z' });
  it('提交回执同时绑定父链、两层版本、来源和原样 Unicode 说明', () => {
    const value = revision();
    expect(validateKnowledgeCorrectionRevision(value, id(1), { expected, sourceId: id(2), reason: value.reason })).toEqual(value);
    for (const bad of [{ ...value, parentId: null }, { ...value, documentVersion: 5 }, { ...value, sourceId: id(8) }, { ...value, reason: value.reason.trim() }])
      expect(() => validateKnowledgeCorrectionRevision(bad, id(1), { expected, sourceId: id(2), reason: value.reason })).toThrow();
  });
  it('历史游标严格递减且绑定返回窗口，历史预览不能冒充另一修订', () => {
    const value = revision();
    expect(validateKnowledgeCorrectionHistory({ items: [value], nextHeadVersion: null }, id(1)).items).toHaveLength(1);
    expect(() => validateKnowledgeCorrectionHistory({ items: [value, value], nextHeadVersion: null }, id(1))).toThrow();
    expect(() => validateKnowledgeCorrectionHistory({ items: [value], nextHeadVersion: 1 }, id(1))).toThrow();
    expect(() => validateKnowledgeCorrectionWorkspace(page(), { versionId: id(1), revisionId: id(8) })).toThrow();
    expect(validateKnowledgeCorrectionWorkspace(page(), { versionId: id(1), revisionId: id(3) }).revisionId).toBe(id(3));
  });
  it('保留更正的完整原始上下文，逐块读取绑定相同来源与修订', () => {
    const value = validateKnowledgeCorrectionWorkspace(page(), { versionId: id(1) });
    expect(value.block.parts[0]?.originalText).toBe('原识别文字'); expect(Object.isFrozen(value.block.parts)).toBe(true);
  });
  it.each(['version', 'page', 'head', 'hash', 'range', 'original', 'supplement', 'anchor', 'duplicate', 'layout', 'unicode', 'oversized', 'source-view', 'position'])('拒绝异常工作台 %s', fault => {
    const value = structuredClone(page()); const part = value.block.parts[0]!;
    const malformed = fault === 'version' ? { ...value, versionId: id(9) } : fault === 'page' ? { ...value, blockIndex: 2 }
      : fault === 'head' ? { ...value, revisionId: null } : fault === 'hash' ? { ...value, payloadHash: '' }
      : fault === 'source-view' ? { ...value, isSourceView: true }
      : { ...value, block: { ...value.block, separator: fault === 'layout' ? '<script>' : value.block.separator, parts: [
        { ...part, ...(fault === 'range' ? { sourceLength: 1 } : fault === 'original' ? { contentKind: 'original' } : fault === 'supplement' ? { contentKind: 'supplement' }
          : fault === 'anchor' ? { anchorId: null } : fault === 'unicode' ? { text: '\ud800' } : fault === 'oversized' ? { text: 'x'.repeat(32769) }
          : fault === 'position' ? { sourceLocation: { ...location, kind: 'pdf_page', pageNumber: null } } : {}) },
        ...(fault === 'duplicate' ? [part] : [])
      ] } };
    expect(() => validateKnowledgeCorrectionWorkspace(malformed, { versionId: id(1) })).toThrow('client.invalid_knowledge_correction');
  });
  it('补写与空单元格使用各自真实语义', () => {
    const value = page(); const part = value.block.parts[0]!;
    const supplement = { ...value, block: { ...value.block, parts: [{ ...part, contentKind: 'supplement', anchorId: null, originalText: null, sourceOffset: null, sourceLength: null, sourceLocation: null }] } };
    expect(validateKnowledgeCorrectionWorkspace(supplement, { versionId: id(1) }).block.parts[0]?.sourceLocation).toBeNull();
    const empty = { ...value, block: { ...value.block, parts: [{ ...part, contentKind: 'original', text: '', anchorId: null, originalText: '', sourceOffset: 0, sourceLength: 0 }] } };
    expect(validateKnowledgeCorrectionWorkspace(empty, { versionId: id(1) }).block.parts[0]?.text).toBe('');
  });
  it('原始低置信度只供维护预览，保持原识别值及 original 性质', () => {
    const value = page(); const part = value.block.parts[0]!;
    const low = { ...value, block: { ...value.block, parts: [{ ...part, contentKind: 'original', text: part.originalText!, sourceLocation: {
      ...location, kind: 'image_region', image: { frameNumber: 1, imageWidth: 100, imageHeight: 100, x: 0, y: 0, width: 30, height: 20,
        confidence: 0.4, tableNumber: null, rowNumber: null, columnNumber: null, parent: null }
    } }] } };
    expect(validateKnowledgeCorrectionWorkspace(low, { versionId: id(1) }).block.parts[0]?.sourceLocation?.image?.confidence).toBe(0.4);
  });
  it.each(['state', 'attempt', 'premature-source', 'missing-source', 'failure', 'date', 'identity', 'document-version'])('拒绝异常准备状态 %s', fault => {
    const value = task();
    const bad = fault === 'state' ? { ...value, state: 'ready' } : fault === 'attempt' ? { ...value, attemptCount: 4 }
      : fault === 'premature-source' ? { ...value, sourceId: id(2) } : fault === 'missing-source' ? { ...value, state: 'completed', attemptCount: 1 }
      : fault === 'failure' ? { ...value, state: 'failed', failureCode: 'unsafe-external-error' }
      : fault === 'date' ? { ...value, updatedAtUtc: 'invalid' } : fault === 'identity' ? { ...value, id: id(9) }
      : { ...value, documentVersion: 0 };
    expect(() => validateKnowledgeCorrectionPreparation(bad, id(1), id(7))).toThrow('client.invalid_knowledge_correction');
  });
  it('完成准备只产生来源身份，严格检查所选重解析而不把它当成人工头', () => {
    expect(validateKnowledgeCorrectionPreparation({ ...task(), state: 'completed', attemptCount: 1, sourceId: id(2) }, id(1)).state).toBe('completed');
    const value = { ...page(), isSourceView: true, revisionId: null, headVersion: 0 };
    expect(validateKnowledgeCorrectionWorkspace(value, { versionId: id(1), sourceId: id(2) }).isSourceView).toBe(true);
    expect(() => validateKnowledgeCorrectionWorkspace(value, { versionId: id(1), sourceId: id(8) })).toThrow();
  });
});
