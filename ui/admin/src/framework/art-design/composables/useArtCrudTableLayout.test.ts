import { defineComponent, h, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useArtCrudTableLayout } from './useArtCrudTableLayout';
import { ElCard } from 'element-plus';
import { useAdminI18n } from '../../../i18n/adminI18n';

vi.mock('../../../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: () => '每页条数' }) }));

describe('CRUD 分页补标的根节点', () => {
  afterEach(() => vi.useRealTimers());

  it.each([false, true])('在原生或组件容器内完成异步补标，组件 ref=%s', async componentRef => {
    vi.useFakeTimers();
    const Card = defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) });
    const View = defineComponent({
      setup() {
        const { tableMainRef } = useArtCrudTableLayout();
        const content = () => h('div', { class: 'el-pagination' }, h('input', { class: 'el-select__input' }));
        return () => componentRef
          ? h(Card, { ref: tableMainRef }, { default: content })
          : h('div', { ref: tableMainRef }, [content()]);
      }
    });
    const wrapper = mount(View);
    try {
      await nextTick(); await vi.runAllTimersAsync(); await nextTick();
      expect(wrapper.get('input').attributes('aria-label')).toBe('每页条数');
    } finally { wrapper.unmount(); }
  });
});

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
