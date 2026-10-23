import type { AiKnowledgeSourceLocationResponse } from './generated/models.generated.js';
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);

type ValidatedImageLocation = Omit<NonNullable<AiKnowledgeSourceLocationResponse['image']>, 'confidence' | 'parent'> & {
  readonly confidence: number;
  readonly parent: AiKnowledgeSourceLocation | null;
};
/** 复用生成契约，并收窄已由下方边界确认有限数值的置信度；不接受数字字符串或特殊浮点值。 */
export type AiKnowledgeSourceLocation = Omit<AiKnowledgeSourceLocationResponse, 'image'> & { readonly image: ValidatedImageLocation | null };
const kinds = ['document', 'pdf_page', 'docx_paragraph', 'docx_table_cell', 'csv_cell', 'worksheet_cell', 'word_paragraph', 'word_table_cell', 'slide_text', 'slide_notes', 'html_node', 'image_region'];
const integer = (value: unknown, min: number, max: number): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max;
const optional = (value: unknown, min: number, max: number) => value === null || integer(value, min, max);

/** HTTP 响应坐标按实际格式验收；只允许一层图片原文锚点，防止循环或无限递归。 */
export function isAiKnowledgeSourceLocation(value: unknown, parent = false): value is AiKnowledgeSourceLocation {
  if (!isRecord(value) || !integer(value.blockSequence, 0, 8191) || typeof value.kind !== 'string' || !kinds.includes(value.kind)) return false;
  const kind = kinds.indexOf(value.kind); const paragraph = [2, 3, 6, 7].includes(kind); const table = [3, 7].includes(kind);
  if (parent && kind === 11 || (value.pageNumber !== null) !== (kind === 1) || !optional(value.pageNumber, 1, 128)
    || (value.paragraphNumber !== null) !== paragraph || !optional(value.paragraphNumber, 1, 8192)
    || (value.tableNumber !== null) !== table || (value.rowNumber !== null) !== table || (value.cellNumber !== null) !== table
    || !optional(value.tableNumber, 1, 128) || !optional(value.rowNumber, 1, 4096) || !optional(value.cellNumber, 1, 64)
    || (value.grid !== null) !== [4, 5].includes(kind) || (value.slide !== null) !== [8, 9].includes(kind)
    || (value.htmlPath !== null) !== (kind === 10) || (value.image !== null) !== (kind === 11)) return false;
  if (value.grid !== null) {
    const g = value.grid;
    if (!isRecord(g) || !integer(g.sheetNumber, 1, 128) || typeof g.sheetName !== 'string' || !g.sheetName.length || g.sheetName.length > 128
      || /[\u0000-\u001f\u007f]/.test(g.sheetName) || !integer(g.row, 1, 1048576) || !integer(g.column, 1, 16384)
      || kind === 4 && (g.sheetNumber !== 1 || g.sheetName !== 'CSV')) return false;
  }
  if (value.slide !== null) {
    const s = value.slide;
    if (!isRecord(s) || !integer(s.slideNumber, 1, 128) || !integer(s.shapeNumber, 1, 8192) || !integer(s.rowSpan, 1, 4096) || !integer(s.columnSpan, 1, 64)
      || (s.rowNumber === null ? s.columnNumber !== null || s.rowSpan !== 1 || s.columnSpan !== 1
        : !integer(s.rowNumber, 1, 4097-s.rowSpan) || !integer(s.columnNumber, 1, 65-s.columnSpan))) return false;
  }
  if (value.htmlPath !== null && (typeof value.htmlPath !== 'string' || !value.htmlPath.startsWith('/') || value.htmlPath.length > 1024 || /[\u0000-\u001f\u007f]/.test(value.htmlPath))) return false;
  if (value.image !== null) {
    const i = value.image;
    if (!isRecord(i) || !integer(i.frameNumber, 1, 128) || !integer(i.imageWidth, 1, 16384) || !integer(i.imageHeight, 1, 16384)
      || i.imageWidth*i.imageHeight > 40000000 || !integer(i.width, 1, i.imageWidth) || !integer(i.height, 1, i.imageHeight)
      || !integer(i.x, 0, i.imageWidth-i.width) || !integer(i.y, 0, i.imageHeight-i.height) || typeof i.confidence !== 'number' || !Number.isFinite(i.confidence) || i.confidence < 0.8 || i.confidence > 1
      || (i.tableNumber === null ? i.rowNumber !== null || i.columnNumber !== null : !integer(i.tableNumber, 1, 128) || !integer(i.rowNumber, 1, 4096) || !integer(i.columnNumber, 1, 64))
      || i.parent !== null && !isAiKnowledgeSourceLocation(i.parent, true)) return false;
  }
  return true;
}
