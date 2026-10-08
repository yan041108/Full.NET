import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import { useAdminI18n } from '../i18n/adminI18n';
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
