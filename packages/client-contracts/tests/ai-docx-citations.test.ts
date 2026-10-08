import { describe, expect, it } from 'vitest';
import { isAiChatMessage } from '../src/ai-chat';

const id = '01900000-0000-7000-8000-000000000001';
const citation = { id, documentId: id, versionId: id, sequence: 0, pageNumber: null, offset: 0, length: 8 };
const message = (source: unknown) => ({ id, sessionId: id, roleKey: 'assistant', content: '答案', statusKey: 'completed',
  promptTokens: null, completionTokens: null, createdAtUtc: '2026-10-08T00:00:00Z', citations: [source] });

describe('DOCX 引用契约', () => {
  it.each([null, 1])('空可选位置符合公开契约并保留既有页码 %j', pageNumber =>
    expect(isAiChatMessage(message({ ...citation, pageNumber, docxLocation: null }))).toBe(true));
  it('旧 TXT/PDF 消息继续兼容', () => expect(isAiChatMessage(message(citation))).toBe(true));
  it('接受无页码的逻辑表格位置', () => expect(isAiChatMessage(message({ ...citation,
    docxLocation: { blockSequence: 3, paragraphNumber: 4, tableNumber: 1, rowNumber: 2, cellNumber: 3 } }))).toBe(true));
  it.each([
    { blockSequence: 3, paragraphNumber: 4, tableNumber: 1, rowNumber: null, cellNumber: 3 },
    { blockSequence: -1, paragraphNumber: 4, tableNumber: null, rowNumber: null, cellNumber: null },
    { blockSequence: 3, paragraphNumber: 0, tableNumber: null, rowNumber: null, cellNumber: null },
    { blockSequence: 3, paragraphNumber: 4, tableNumber: 1, rowNumber: 4097, cellNumber: 3 }
  ])('损坏逻辑位置拒绝进入消息展示 %j', docxLocation => expect(isAiChatMessage(message({ ...citation, docxLocation }))).toBe(false));
  it('DOCX 位置不能同时声称 PDF 页码', () => expect(isAiChatMessage(message({ ...citation, pageNumber: 1,
    docxLocation: { blockSequence: 3, paragraphNumber: 4, tableNumber: null, rowNumber: null, cellNumber: null } }))).toBe(false));
});
