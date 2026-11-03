import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import type { AiKnowledgeCitation } from '@fullnet/client-contracts';
const citation = { id: 'id', documentId: 'doc', versionId: 'version', sequence: 0, pageNumber: null, offset: 7, length: 9,
  docxLocation: { blockSequence: 3, paragraphNumber: 4, tableNumber: 1, rowNumber: 2, cellNumber: 3 } };
describe('DOCX 引用位置', () => {
  it.each(['zh-CN', 'en-US'] as const)('使用 %s 显示逻辑表格位置而不产生页码', locale => {
    const i18n = useAdminI18n(); const previous = i18n.locale.value; i18n.setLocale(locale);
    const wrapper = mount(AiKnowledgeCitationPosition, { props: { citation } });
    try {
      expect(wrapper.text()).toContain(locale === 'zh-CN' ? '单元格 3' : 'Cell 3');
      expect(wrapper.text()).not.toMatch(/页|Page/); expect(wrapper.text()).toContain('7 + 9');
    } finally { wrapper.unmount(); i18n.setLocale(previous); }
  });
});

describe('人工修订的来源集合', () => {
  it.each(['zh-CN', 'en-US'] as const)('使用 %s 区分真实原文、更正上下文和无坐标补写', locale => {
    const i18n = useAdminI18n(); const previous = i18n.locale.value; i18n.setLocale(locale);
    const location = (page: number) => ({ blockSequence: page - 1, kind: 'pdf_page', pageNumber: page, paragraphNumber: null,
      tableNumber: null, rowNumber: null, cellNumber: null, grid: null, slide: null, htmlPath: null, image: null });
    const human: AiKnowledgeCitation = { ...citation, docxLocation: null, offset: null, length: null, correction: {
      revisionId: 'revision', textLength: 12, spans: [
        { contentKind: 'original', textOffset: 0, textLength: 4, sourceLocation: location(1), sourceOffset: 2, sourceLength: 4 },
        { contentKind: 'corrected', textOffset: 5, textLength: 3, sourceLocation: location(2), sourceOffset: 0, sourceLength: 20 },
        { contentKind: 'supplement', textOffset: 9, textLength: 3, sourceLocation: null, sourceOffset: null, sourceLength: null }
      ] } };
    const wrapper = mount(AiKnowledgeCitationPosition, { props: { citation: human } });
    try {
      const spans = wrapper.findAll('[data-testid="knowledge-correction-span"]'); expect(spans).toHaveLength(3);
      expect(spans.map(span => span.attributes('data-kind'))).toEqual(['original', 'corrected', 'supplement']);
      expect(spans[0]!.text()).toContain(locale === 'zh-CN' ? '页 1' : 'Page 1');
      expect(spans[1]!.text()).toContain('0 + 20'); expect(spans[1]!.text()).toContain('5 + 3');
      expect(spans[2]!.text()).toContain(locale === 'zh-CN' ? '人工补写' : 'Human supplement');
      expect(spans[2]!.text()).not.toMatch(/页 \d|Page|0 \+ 20/);
    } finally { wrapper.unmount(); i18n.setLocale(previous); }
  });
});


describe('统一来源位置', () => {
  const source = { blockSequence: 0, kind: 'worksheet_cell', pageNumber: null, paragraphNumber: null, tableNumber: null, rowNumber: null, cellNumber: null,
    grid: { sheetNumber: 1, sheetName: '中文表', row: 8, column: 3 }, slide: null, htmlPath: null, image: null };
  it.each(['zh-CN', 'en-US'] as const)('使用 %s 显示单元格原生位置', locale => {
    const i18n = useAdminI18n(); const before = i18n.locale.value; i18n.setLocale(locale);
    const wrapper = mount(AiKnowledgeCitationPosition, { props: { citation: { ...citation, docxLocation: null, sourceLocation: source } } });
    try { expect(wrapper.text()).toContain('中文表'); expect(wrapper.text()).toContain(locale === 'zh-CN' ? '行 8' : 'Row 8'); expect(wrapper.text()).not.toMatch(/页|Page/); }
    finally { wrapper.unmount(); i18n.setLocale(before); }
  });
});
