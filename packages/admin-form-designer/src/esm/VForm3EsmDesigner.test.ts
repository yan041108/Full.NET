import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import VForm3EsmDesigner from './VForm3EsmDesigner.vue';

describe('VForm3 ESM 安全设计器', () => {
  it('同步加载 JSON 并立即由同一响应式状态渲染字段机器码', async () => {
    const wrapper = mount(VForm3EsmDesigner);
    const designer = wrapper.vm as unknown as {
      getFormJson: () => unknown;
      setFormJson: (value: unknown) => void;
    };

    designer.setFormJson({
      widgetList: [{
        id: 'fn-amount',
        type: 'number',
        options: { name: 'amount_e2e', label: '申请金额', required: true }
      }],
      formConfig: { labelWidth: 120 }
    });
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain('amount_e2e');
    designer.setFormJson({
      widgetList: [{
        id: 'fn-summary',
        type: 'input',
        options: { name: 'summary_e2e', label: '摘要', required: false }
      }],
      formConfig: { labelWidth: 100 }
    });
    await wrapper.vm.$nextTick();
    expect(wrapper.text()).not.toContain('amount_e2e');
    expect(wrapper.text()).toContain('summary_e2e');
    expect(designer.getFormJson()).toEqual({
      widgetList: [{
        id: 'fn-summary',
        type: 'input',
        options: { name: 'summary_e2e', label: '摘要', required: false }
      }],
      formConfig: { labelWidth: 100 }
    });
  });

  it('支持新增、属性编辑、重排和删除字段', async () => {
    const wrapper = mount(VForm3EsmDesigner);

    await wrapper.get('[data-testid="vform3-add-input"]').trigger('click');
    await wrapper.get('[data-testid="vform3-add-number"]').trigger('click');
    await wrapper.get('[data-testid="vform3-field-0"]').trigger('click');
    await wrapper.get('[data-testid="vform3-property-label"]').setValue('合同名称');
    await wrapper.get('[data-testid="vform3-move-down"]').trigger('click');
    await wrapper.get('[data-testid="vform3-delete"]').trigger('click');

    const designer = wrapper.vm as unknown as { getFormJson: () => {
      widgetList: Array<{ type: string; options: Record<string, unknown> }>;
    } };
    expect(designer.getFormJson().widgetList).toHaveLength(1);
    expect(designer.getFormJson().widgetList[0]?.type).toBe('number');
  });

  it('为整数、小数、金额和日期时间新增路径写入可发布的 Workflow 语义', async () => {
    const wrapper = mount(VForm3EsmDesigner);
    await wrapper.get('[data-testid="vform3-add-integer"]').trigger('click');
    await wrapper.get('[data-testid="vform3-add-number"]').trigger('click');
    await wrapper.get('[data-testid="vform3-add-money"]').trigger('click');
    await wrapper.get('[data-testid="vform3-add-datetime"]').trigger('click');

    const designer = wrapper.vm as unknown as { getFormJson: () => {
      widgetList: Array<{ options: Record<string, unknown> }>;
    } };
    const widgets = designer.getFormJson().widgetList;
    expect(widgets.map(widget => widget.options.fullNetFieldType))
      .toEqual(['integer', 'decimal', 'money', 'datetime']);
    expect(widgets[1]?.options.precision).toBe(2);
    expect(widgets[2]?.options.precision).toBe(2);
  });
});
