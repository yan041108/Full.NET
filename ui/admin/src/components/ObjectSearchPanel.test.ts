import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import ObjectSearchPanel from './ObjectSearchPanel.vue';
import type { ObjectSearchPage, ObjectSearchSource } from '../search/objectSearch';
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330', wrappers: VueWrapper[] = [];
const result = (title = 'Found', page = 1): ObjectSearchPage => ({ items: [{ id, title, number: 'REQ-1' }], page, hasMore: false, isTruncated: false, windowDays: 90 });
const source = (key: string, load = vi.fn().mockResolvedValue(result())): ObjectSearchSource => ({ key, label: key, routeName: 'objects', queryKey: 'requestId', load });
async function render(sources: ObjectSearchSource[], query = 'Find') {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }, { path: '/objects', name: 'objects', component: { template: '<div />' } }] });
  await router.push('/'); await router.isReady();
  const wrapper = mount(ObjectSearchPanel, { props: { active: true, query, context: 'scope-one', sources }, global: { plugins: [router] } }); wrappers.push(wrapper);
  return { wrapper, router };
}
async function start() { await vi.advanceTimersByTimeAsync(250); await flushPromises(); }
beforeEach(() => vi.useFakeTimers());
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.useRealTimers(); });
describe('分组对象搜索', () => {
  it('最小输入与防抖阻止无意义请求，每来源一次有界读取', async () => {
    const load = vi.fn().mockResolvedValue(result()); const { wrapper } = await render([source('one', load)], 'x');
    await start(); expect(load).not.toHaveBeenCalled(); await wrapper.setProps({ query: ' Find ' });
    await vi.advanceTimersByTimeAsync(249); expect(load).not.toHaveBeenCalled(); await vi.advanceTimersByTimeAsync(1); await flushPromises();
    expect(load).toHaveBeenCalledWith('Find', 1, expect.any(AbortSignal)); expect(wrapper.text()).toContain('Found');
  });
  it('一个来源失败不隐藏其他来源，也不伪装为无结果', async () => {
    const { wrapper } = await render([source('one', vi.fn().mockRejectedValue(new Error('offline'))), source('two')]); await start();
    expect(wrapper.get('[data-search-source="one"]').text()).toContain('暂时不可用'); expect(wrapper.get('[data-search-source="two"]').text()).toContain('Found');
  });
  it('新查询取消旧读取，迟到响应不能覆盖新结果', async () => {
    let resolve!: (value: ObjectSearchPage) => void;
    const load = vi.fn().mockImplementationOnce(() => new Promise<ObjectSearchPage>(done => { resolve = done; })).mockResolvedValue(result('New'));
    const { wrapper } = await render([source('one', load)]); await start(); const signal = load.mock.calls[0]![2] as AbortSignal;
    await wrapper.setProps({ query: 'New query' }); expect(signal.aborted).toBe(true); await start(); resolve(result('Old')); await flushPromises();
    expect(wrapper.text()).toContain('New'); expect(wrapper.text()).not.toContain('Old');
  });
  it('八秒超时明确失败，重试仅重新读取且旧响应仍被丢弃', async () => {
    let resolve!: (value: ObjectSearchPage) => void;
    const load = vi.fn().mockImplementationOnce(() => new Promise<ObjectSearchPage>(done => { resolve = done; })).mockResolvedValue(result('Recovered'));
    const { wrapper } = await render([source('one', load)]); await start(); const signal = load.mock.calls[0]![2] as AbortSignal;
    await vi.advanceTimersByTimeAsync(8000); expect(signal.aborted).toBe(true); expect(wrapper.text()).toContain('暂时不可用');
    await wrapper.get('[data-search-source="one"] button').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('Recovered');
    resolve(result('Late')); await flushPromises(); expect(wrapper.text()).not.toContain('Late');
  });
  it('分页保持来源与关键词，只允许当前有权结果跳转固定路由', async () => {
    const first = { ...result(), items: Array.from({ length: 5 }, (_, index) => ({ id: id.slice(0, -1) + index, title: 'First ' + index, number: null })), hasMore: true };
    const load = vi.fn().mockResolvedValueOnce(first).mockResolvedValue(result('Second', 2)); const { wrapper, router } = await render([source('one', load)]); await start();
    const next = wrapper.findAll('button').find(button => button.text() === '下一页')!; await next.trigger('click'); await flushPromises();
    expect(load).toHaveBeenLastCalledWith('Find', 2, expect.any(AbortSignal)); await wrapper.get('[data-search-object]').trigger('click'); await flushPromises();
    expect(router.currentRoute.value.name).toBe('objects'); expect(router.currentRoute.value.query).toEqual({ requestId: id });
  });
  it('切换作用域、关闭或撤销来源立即清空并取消，不接受旧上下文结果', async () => {
    let resolve!: (value: ObjectSearchPage) => void; const load = vi.fn().mockImplementation(() => new Promise<ObjectSearchPage>(done => { resolve = done; }));
    const { wrapper } = await render([source('one', load)]); await start(); const signal = load.mock.calls[0]![2] as AbortSignal;
    await wrapper.setProps({ context: 'scope-two', active: false }); expect(signal.aborted).toBe(true); resolve(result('Other tenant')); await flushPromises(); expect(wrapper.text()).not.toContain('Other tenant');
    await wrapper.setProps({ sources: [] }); expect(wrapper.find('[data-testid="object-search-panel"]').exists()).toBe(false);
  });
  it('损坏或过大的摘要响应按来源失败关闭', async () => {
    const { wrapper } = await render([source('one', vi.fn().mockResolvedValue({ ...result(), items: Array(6).fill(result().items[0]) }))]); await start();
    expect(wrapper.text()).toContain('暂时不可用'); expect(wrapper.find('[data-search-object]').exists()).toBe(false);
  });
});
