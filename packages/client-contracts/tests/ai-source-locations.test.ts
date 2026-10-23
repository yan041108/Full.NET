import { describe, expect, it } from 'vitest';
import { isAiChatMessage } from '../src/ai-chat.js';
import { knowledgeFileContentTypes } from '../src/ai-knowledge-formats.js';
const id = '01900000-0000-7000-8000-000000000001';
const source = { blockSequence: 0, kind: 'worksheet_cell', pageNumber: null, paragraphNumber: null, tableNumber: null,
  rowNumber: null, cellNumber: null, grid: { sheetNumber: 2, sheetName: '中文表', row: 8, column: 3 }, slide: null, htmlPath: null, image: null };
const message = (location: unknown, pageNumber: number | null = null) => ({ id, sessionId: id, roleKey: 'assistant', content: '答案', statusKey: 'completed', promptTokens: null, completionTokens: null, createdAtUtc: '2026-10-23T00:00:00Z', citations: [{ id, documentId: id, versionId: id, sequence: 0, offset: 0, length: 4, pageNumber, sourceLocation: location }] });
describe('多格式来源契约', () => {
  it('保留真实工作表坐标', () => expect(isAiChatMessage(message(source))).toBe(true));
  it.each([ { ...source, kind: 'invented' }, { ...source, grid: { ...source.grid, row: 0 } }, { ...source, grid: null }, { ...source, pageNumber: 2 } ])('拒绝混合或伪造坐标 %j', value => expect(isAiChatMessage(message(value))).toBe(false));
  it('拒绝低置信度、越界或递归图片锚点', () => {
    const image = { frameNumber: 1, imageWidth: 100, imageHeight: 100, x: 2, y: 3, width: 80, height: 70, confidence: 0.95, tableNumber: null, rowNumber: null, columnNumber: null, parent: source };
    const location = { ...source, kind: 'image_region', grid: null, image };
    expect(isAiChatMessage(message(location))).toBe(true);
    for (const bad of [{ ...image, confidence: 0.79 }, { ...image, confidence: '0.95' }, { ...image, confidence: Number.NaN }, { ...image, x: 99 }, { ...image, parent: location }]) expect(isAiChatMessage(message({ ...location, image: bad }))).toBe(false);
  });
  it('格式选择包含全部扩展图片，排除音视频与任意二进制', () => {
    for (const kind of ['doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'csv', 'html', 'heic', 'heif', 'avif', 'jp2', 'j2k', 'jxl', 'svgz', 'wmf', 'emf', 'psd', 'pnm', 'pbm', 'pgm', 'ppm']) expect(knowledgeFileContentTypes[kind]).toBeTruthy();
    expect(knowledgeFileContentTypes.mp4).toBeUndefined(); expect(knowledgeFileContentTypes.exe).toBeUndefined();
    expect(knowledgeFileContentTypes['__proto__']).toBeUndefined(); expect(knowledgeFileContentTypes['constructor']).toBeUndefined();
  });
});
