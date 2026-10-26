import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import VForm3RuntimeRenderer from './VForm3RuntimeRenderer.vue';
const { render } = vi.hoisted(() => ({ render: vi.fn() }));
vi.mock('@fullnet/admin-form-designer', () => ({
  VFORM_RUNTIME_HASH: 'test-runtime',
  VForm3DesignerHost: { name: 'VForm3DesignerHost', props: ['disabled'], emits: ['ready', 'change', 'error'],
    setup(_props: unknown, { expose }: { expose: (value: unknown) => void }) { expose({ render }); return () => null; } }
}));
const props = {
  json: { widgetList: [{ type: 'input', options: { name: 'locked' } }], formConfig: {}, fullNetRuntime: { rendererHash: 'test-runtime', bindingVersion: 1, bindings: [{ fieldKey: 'title', codec: 'string' }, { fieldKey: 'locked', codec: 'string' }] } },
  submission: { title: '旧值', locked: '只读' },
  fieldPolicies: { title: 'editable', locked: 'readOnly', secret: 'hidden' } as const
};
beforeEach(() => { render.mockReset().mockImplementation(async value => value.data); });
function deferred() {
  let resolve!: () => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<void>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}
describe('VForm3RuntimeRenderer 授权接续', () => {
  it.each([null, [], 'invalid'])('非法初始化回执 %j 不能恢复办理', async value => {
    render.mockResolvedValue(value);
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    wrapper.findComponent({ name: 'VForm3DesignerHost' }).vm.$emit('ready'); await flushPromises();
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]);
    wrapper.unmount();
  });
  it('载入回执中的初始化字段值成为当前补丁，只读/隐藏值仍不能提交', async () => {
    render.mockResolvedValue({ title: '挂载脚本自动值', locked: '脚本伪造只读', secret: '隐藏值' });
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    wrapper.findComponent({ name: 'VForm3DesignerHost' }).vm.$emit('ready'); await flushPromises();
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ title: '挂载脚本自动值' }]);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([true]);
    wrapper.unmount();
  });

  it('时间显示补零不会把未编辑的分钟精度值当作修改', async () => {
    const timeProps = { json: { widgetList: [], formConfig: {}, fullNetRuntime: { rendererHash: 'test-runtime', bindingVersion: 1, bindings: [{ fieldKey: 'cutoff', codec: 'iso-time' }] } }, submission: { cutoff: '08:30' }, fieldPolicies: { cutoff: 'editable' as const } };
    render.mockResolvedValue({ cutoff: '08:30:00' });
    const wrapper = mount(VForm3RuntimeRenderer, { props: timeProps });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
    expect(render.mock.calls[0][0].data).toEqual({ cutoff: '08:30:00' });
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([true]);
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]);
    host.vm.$emit('change', { cutoff: '12:45:37' });
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ cutoff: '12:45:37' }]);
    wrapper.unmount();
  });
  it('宿主错误后属性刷新不能自行恢复办理，重新 Ready 并载入成功后才恢复', async () => {
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    host.vm.$emit('change', { title: '待提交' });
    host.vm.$emit('error', 'client.vform3_install_failed'); await flushPromises();
    await wrapper.setProps({ submission: { title: '新快照', locked: '只读' } }); await flushPromises();
    expect(render).toHaveBeenCalledTimes(1);
    expect(host.props('disabled')).toBe(true);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]);
    host.vm.$emit('change', { title: '报错帧的迟到值' });
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]);
    host.vm.$emit('ready'); await flushPromises();
    expect(render).toHaveBeenCalledTimes(2);
    expect(render.mock.calls[1][0].data.title).toBe('新快照');
    expect(host.props('disabled')).toBe(false);
    wrapper.unmount();
  });

  it('宿主错误使等待中的旧投影失效，旧回执不能恢复就绪', async () => {
    const gate = deferred(); render.mockReturnValueOnce(gate.promise);
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    await wrapper.setProps({ submission: { title: '排队的旧投影', locked: '只读' } });
    host.vm.$emit('error', 'client.vform3_install_failed');
    gate.resolve(); await flushPromises();
    expect(render).toHaveBeenCalledTimes(1);
    expect(host.props('disabled')).toBe(true);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
    wrapper.unmount();
  });

  it('最新投影校验失败时不再把先前排队的投影发到隔离帧', async () => {
    const gate = deferred(); render.mockReturnValueOnce(gate.promise);
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    await wrapper.setProps({ submission: { title: '已过期投影', locked: '只读' } });
    await wrapper.setProps({ json: { ...props.json, fullNetRuntime: { ...props.json.fullNetRuntime, rendererHash: 'wrong' } } });
    gate.resolve(); await flushPromises();
    expect(render).toHaveBeenCalledTimes(1);
    expect(host.props('disabled')).toBe(true);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
    wrapper.unmount();
  });

  it('旧载入失败不阻断当前合法投影，也不覆盖当前结果', async () => {
    const gate = deferred(); render.mockReturnValueOnce(gate.promise);
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    await wrapper.setProps({ submission: { title: '当前投影', locked: '只读' } });
    gate.reject(new Error('旧载入失败')); await flushPromises();
    expect(render).toHaveBeenCalledTimes(2);
    expect(host.props('disabled')).toBe(false);
    expect(wrapper.find('[role=alert]').exists()).toBe(false);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([true]);
    wrapper.unmount();
  });

  it('卸载取消排队投影，不接收迟到载入或变更', async () => {
    const gate = deferred(); render.mockReturnValueOnce(gate.promise);
    const wrapper = mount(VForm3RuntimeRenderer, { props });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    await wrapper.setProps({ submission: { title: '未执行投影', locked: '只读' } });
    wrapper.unmount();
    const availability = wrapper.emitted('availability')?.length;
    gate.resolve(); await flushPromises();
    expect(render).toHaveBeenCalledTimes(1);
    expect(wrapper.emitted('availability')?.length).toBe(availability);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
  });
  it('只读收紧原版配置，只向宿主回传允许修改的字段', async () => {
    const wrapper = mount(VForm3RuntimeRenderer, { props }); const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    expect(render.mock.calls[0][0].json.widgetList[0].options.disabled).toBe(true);
    expect(props.json.widgetList[0].options).not.toHaveProperty('disabled');
    host.vm.$emit('change', { title: '新值', locked: '伪造只读', secret: '隐藏值', unknown: '越界' });
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ title: '新值' }]);
    wrapper.unmount();
  });
  it('切换投影时串行加载并屏蔽旧 Renderer 的迟到变更', async () => {
    let finish!: () => void; render.mockImplementationOnce(() => new Promise<void>(resolve => { finish = resolve; }));
    const wrapper = mount(VForm3RuntimeRenderer, { props }); const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    host.vm.$emit('ready'); await flushPromises();
    await wrapper.setProps({ submission: { title: '新任务', locked: '只读' } }); await flushPromises();
    expect(render).toHaveBeenCalledTimes(1); expect(host.props('disabled')).toBe(true);
    host.vm.$emit('change', { title: '旧回执' }); expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]);
    finish(); await flushPromises();
    expect(render).toHaveBeenCalledTimes(2); expect(render.mock.calls[1][0].data.title).toBe('新任务');
    expect(host.props('disabled')).toBe(false);
    wrapper.unmount();
  });
  it('运行摘要不匹配时拒绝装载，清空补丁并给出可读提示', async () => {
    const wrapper = mount(VForm3RuntimeRenderer, { props: { ...props, json: { ...props.json, fullNetRuntime: { ...props.json.fullNetRuntime, rendererHash: 'old-build' } } } });
    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' }); host.vm.$emit('ready'); await flushPromises();
    expect(render).not.toHaveBeenCalled(); expect(host.props('disabled')).toBe(true);
    expect(wrapper.emitted('availability')?.at(-1)).toEqual([false]);
    expect(wrapper.get('[role=alert]').text()).not.toContain('client.');
    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{}]); wrapper.unmount();
  });
});
