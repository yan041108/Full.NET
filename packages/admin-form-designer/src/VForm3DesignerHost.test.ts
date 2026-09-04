import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import VForm3DesignerHost from './VForm3DesignerHost.vue';

describe('VForm3DesignerHost', () => {
  it('延迟加载本地 ESM 设计器并暴露通用 JSON 读写接口', async () => {
    const onError = vi.fn();
    const wrapper = mount(VForm3DesignerHost, { props: { onError } });
    await flushPromises();

    await vi.waitFor(() => {
      expect(onError).not.toHaveBeenCalled();
      expect(wrapper.find('[data-testid="vform3-esm-designer"]').exists()).toBe(true);
    }, { timeout: 15_000 });
    const host = wrapper.vm as unknown as {
      getFormJson: () => unknown;
      setFormJson: (value: unknown) => void;
    };
    const value = {
      widgetList: [{
        id: 'fn-contract',
        type: 'input',
        options: { name: 'contract_name', label: '合同名称' }
      }],
      formConfig: {}
    };
    host.setFormJson(value);
    expect(host.getFormJson()).toEqual(value);
    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain('contract_name');
  }, 20_000);
});
