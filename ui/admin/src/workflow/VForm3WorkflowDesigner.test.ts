import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import VForm3WorkflowDesigner from './VForm3WorkflowDesigner.vue';

const { setFormJson, getFormJson } = vi.hoisted(() => ({ setFormJson: vi.fn(), getFormJson: vi.fn() }));
vi.mock('@fullnet/admin-form-designer', () => ({
  VForm3DesignerHost: { name: 'VForm3DesignerHost', props: ['disabled'], emits: ['ready', 'error'],
    setup(_props: unknown, { expose }: { expose: (value: unknown) => void }) { expose({ setFormJson, getFormJson }); return () => null; } }
}));
const makeDesign = (title: string) => ({ widgetList: [], formConfig: { jsonVersion: 3, title }, extra: { preserved: title } });
let current: ReturnType<typeof makeDesign>;
function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>(done => { resolve = done; });
  return { promise, resolve };
}
beforeEach(() => {
  current = makeDesign('旧画布');
  setFormJson.mockReset().mockImplementation(async value => { current = structuredClone(value); });
  getFormJson.mockReset().mockImplementation(async () => structuredClone(current));
});

describe('VForm3WorkflowDesigner', () => {
  it('通过独立表单设计器 Host 接入 Workflow，并接续外部禁用状态', () => {
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('当前设计'), disabled: true } });
    expect(wrapper.findComponent({ name: 'VForm3DesignerHost' }).props('disabled')).toBe(true);
    wrapper.unmount();
  });
  it('宿主尚未 Ready 时不允许读取旧画布，也不允许编辑', async () => {
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('当前设计'), disabled: false } });
    try {
      await expect(wrapper.vm.readDesign()).rejects.toThrow('client.vform3_not_ready');
      expect(getFormJson).not.toHaveBeenCalled();
      expect(wrapper.findComponent({ name: 'VForm3DesignerHost' }).props('disabled')).toBe(true);
    } finally { wrapper.unmount(); }
  });
  it('Ready 后立即读取必须等待首次完整载入，不能保存旧画布', async () => {
    const gate = deferred(); const design = makeDesign('当前设计');
    setFormJson.mockImplementationOnce(async value => { await gate.promise; current = structuredClone(value); });
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: design, disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready');
      const reading = wrapper.vm.readDesign(); await flushPromises();
      const premature = getFormJson.mock.calls.length; const disabled = host.props('disabled');
      gate.resolve(); const actual = await reading;
      expect(premature).toBe(0); expect(disabled).toBe(true); expect(actual).toEqual(design);
      await flushPromises(); expect(host.props('disabled')).toBe(false);
    } finally { gate.resolve(); wrapper.unmount(); }
  });
  it('同一轮更新设计后立即读取必须等最新属性载入', async () => {
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('第一份'), disabled: false } });
    try {
      wrapper.findComponent({ name: 'VForm3DesignerHost' }).vm.$emit('ready'); await flushPromises();
      const updated = makeDesign('第二份'); const updating = wrapper.setProps({ designerJson: updated });
      const actual = await wrapper.vm.readDesign(); await updating;
      expect(actual).toEqual(updated);
    } finally { wrapper.unmount(); }
  });
  it('排队载入期间保持禁用，旧载入完成不能提前开放新画布', async () => {
    const first = deferred(); const second = deferred();
    setFormJson.mockImplementationOnce(async value => { await first.promise; current = structuredClone(value); })
      .mockImplementationOnce(async value => { await second.promise; current = structuredClone(value); });
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('第一份'), disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
      const updated = makeDesign('第二份'); await wrapper.setProps({ designerJson: updated }); await flushPromises();
      expect(setFormJson).toHaveBeenCalledTimes(1);
      first.resolve(); await flushPromises();
      expect(setFormJson).toHaveBeenCalledTimes(2); expect(host.props('disabled')).toBe(true);
      const reading = wrapper.vm.readDesign(); second.resolve(); expect(await reading).toEqual(updated);
      await flushPromises(); expect(host.props('disabled')).toBe(false);
    } finally { first.resolve(); second.resolve(); wrapper.unmount(); }
  });
  it('载入失败后禁止读取旧配置，重试成功才恢复编辑和保存', async () => {
    setFormJson.mockRejectedValueOnce(new Error('load-failed'));
    const design = makeDesign('当前设计');
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: design, disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
      await expect(wrapper.vm.readDesign()).rejects.toThrow(); expect(getFormJson).not.toHaveBeenCalled();
      expect(host.props('disabled')).toBe(true);
      expect(wrapper.emitted('validation-error')).toEqual([['client.vform3_operation_failed']]);
      await wrapper.vm.loadDesign(); await flushPromises();
      expect(await wrapper.vm.readDesign()).toEqual(design); expect(host.props('disabled')).toBe(false);
    } finally { wrapper.unmount(); }
  });
  it('读取回执期间切换设计不能把上一份结果交给保存方', async () => {
    const gate = deferred();
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('第一份'), disabled: false } });
    try {
      wrapper.findComponent({ name: 'VForm3DesignerHost' }).vm.$emit('ready'); await flushPromises();
      getFormJson.mockImplementationOnce(async () => { const old = structuredClone(current); await gate.promise; return old; });
      const reading = wrapper.vm.readDesign(); await flushPromises();
      const updated = makeDesign('第二份'); await wrapper.setProps({ designerJson: updated }); await flushPromises();
      gate.resolve(); expect(await reading).toEqual(updated);
      expect(getFormJson).toHaveBeenCalledTimes(2);
    } finally { gate.resolve(); wrapper.unmount(); }
  });
  it('宿主出错后不读取残留画布，新的 Ready 和载入成功后才能恢复', async () => {
    const design = makeDesign('当前设计');
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: design, disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
      host.vm.$emit('error', 'client.vform3_install_failed'); await flushPromises();
      await expect(wrapper.vm.readDesign()).rejects.toThrow('client.vform3_not_ready');
      expect(getFormJson).not.toHaveBeenCalled(); expect(host.props('disabled')).toBe(true);
      host.vm.$emit('ready'); await flushPromises(); expect(await wrapper.vm.readDesign()).toEqual(design);
    } finally { wrapper.unmount(); }
  });
  it('上一份载入失败不阻断已排队的新资产，也不回传过期错误', async () => {
    const gate = deferred();
    setFormJson.mockImplementationOnce(async () => { await gate.promise; throw new Error('old-load-failed'); });
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('第一份'), disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
      const reading = wrapper.vm.readDesign(); await flushPromises();
      const updated = makeDesign('第二份'); await wrapper.setProps({ designerJson: updated });
      gate.resolve(); expect(await reading).toEqual(updated); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined(); expect(host.props('disabled')).toBe(false);
    } finally { gate.resolve(); wrapper.unmount(); }
  });
  it('不可序列化的新资产载入失败后不能回退读取上一份设计', async () => {
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('第一份'), disabled: false } });
    try {
      const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
      const invalid: Record<string, unknown> = makeDesign('错误资产'); invalid.self = invalid;
      await wrapper.setProps({ designerJson: invalid }); await flushPromises();
      await expect(wrapper.vm.readDesign()).rejects.toThrow('client.invalid_workflow_form_draft');
      expect(getFormJson).not.toHaveBeenCalled(); expect(host.props('disabled')).toBe(true);
      expect(wrapper.emitted('validation-error')).toEqual([['client.vform3_operation_failed']]);
    } finally { wrapper.unmount(); }
  });
  it('卸载后到达的读取回执不能作为有效设计交付', async () => {
    const gate = deferred();
    const wrapper = mount(VForm3WorkflowDesigner, { props: { designerJson: makeDesign('当前设计'), disabled: false } });
    wrapper.findComponent({ name: 'VForm3DesignerHost' }).vm.$emit('ready'); await flushPromises();
    getFormJson.mockImplementationOnce(async () => { const old = structuredClone(current); await gate.promise; return old; });
    const reading = wrapper.vm.readDesign().then(() => 'resolved', (error: Error) => error.message); await flushPromises();
    wrapper.unmount(); gate.resolve();
    expect(await reading).toBe('client.vform3_not_ready');
  });
});
