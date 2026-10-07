import { describe, expect, it } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h } from 'vue';
import { ElCard } from 'element-plus';
import { useArtCrudTableLayout } from './useArtCrudTableLayout';
import { useAdminI18n } from '../../../i18n/adminI18n';

describe('CRUD 表格组件引用', () => {
  it('labels pagination through an Element Plus card component ref', async () => {
    useAdminI18n().setLocale('en-US');
    const fixture = defineComponent({
      setup() {
        const { tableMainRef } = useArtCrudTableLayout();
        return () => h(ElCard, { ref: tableMainRef }, () =>
          h('div', { class: 'el-pagination' }, h('input', { class: 'el-select__input' })));
      }
    });
    const wrapper = mount(fixture);
    await flushPromises();
    expect(wrapper.get('input').attributes('aria-label')).toBe('Page size');
    wrapper.unmount();
  });
});
