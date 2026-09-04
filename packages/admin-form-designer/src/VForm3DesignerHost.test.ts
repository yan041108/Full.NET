import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, type App } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import VForm3DesignerHost from './VForm3DesignerHost.vue';

let currentJson: unknown;
const setFormJson = vi.fn((value: unknown) => {
  currentJson = value;
});
const loadFormJson = vi.fn((value: unknown) => {
  currentJson = value;
  return true;
});
const fakeDesigner = defineComponent({
  setup(_, { expose }) {
    expose({
      setFormJson,
      getFormJson: () => currentJson,
      designer: { loadFormJson }
    });
    return () => h('div', { 'data-testid': 'vform3-fake-designer' });
  }
});
const fakePlugin = {
  install(app: App) {
    if (app.component('ElInput') === undefined) throw new Error('element-plus-must-be-installed-first');
    (window as unknown as Record<string, unknown>).axios = { source: 'vform3' };
    app.component('VFormDesigner', fakeDesigner);
  }
};

vi.mock('vform3-builds', () => ({
  default: fakePlugin
}));
describe('VForm3DesignerHost', () => {
  it('延迟安装真实注册名的 VForm3 并暴露通用 JSON 读写接口', async () => {
    const hostAxios = { source: 'host' };
    (window as unknown as Record<string, unknown>).axios = hostAxios;
    const onError = vi.fn();
    const wrapper = mount(VForm3DesignerHost, { props: { onError } });
    await flushPromises();

    await vi.waitFor(() => {
      expect(onError).not.toHaveBeenCalled();
      expect(wrapper.find('[data-testid="vform3-fake-designer"]').exists()).toBe(true);
    }, { timeout: 15_000 });
    const host = wrapper.vm as unknown as {
      getFormJson: () => unknown;
      setFormJson: (value: unknown) => void;
    };
    const value = { widgetList: [{ type: 'input' }] };
    host.setFormJson(value);
    expect(host.getFormJson()).toEqual(value);
    expect(loadFormJson).toHaveBeenCalledWith(value);
    expect((window as unknown as Record<string, unknown>).axios).toBe(hostAxios);
    delete (window as unknown as Record<string, unknown>).axios;
  }, 20_000);
});
