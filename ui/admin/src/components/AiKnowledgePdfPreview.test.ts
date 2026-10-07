import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AiKnowledgePdfPreview from './AiKnowledgePdfPreview.vue';

const mock = vi.hoisted(() => ({ load: vi.fn(), destroy: vi.fn(), cancel: vi.fn(), render: vi.fn(), getPage: vi.fn() }));
vi.mock('./knowledge-pdf', () => ({ loadKnowledgePdf: mock.load }));
let wrappers: VueWrapper[] = [];
const page = { getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }), render: mock.render, cleanup: vi.fn() };
function render(initialPage = 2) {
  const wrapper = mount(AiKnowledgePdfPreview, { props: { data: new Uint8Array([1]), initialPage } }); wrappers.push(wrapper); return wrapper;
}
beforeEach(() => {
  mock.getPage.mockResolvedValue(page);
  mock.render.mockReturnValue({ promise: Promise.resolve(), cancel: mock.cancel });
  mock.load.mockResolvedValue({ promise: Promise.resolve({ numPages: 3, getPage: mock.getPage }), destroy: mock.destroy });
});
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers = []; vi.useRealTimers(); vi.resetAllMocks(); });
describe('有界 PDF 来源画布', () => {
  it('首屏定位引用页，翻页和缩放不重新加载来源', async () => {
    const wrapper = render(); await flushPromises();
    expect(mock.getPage).toHaveBeenCalledWith(2);
    expect(wrapper.get('[data-testid="knowledge-pdf-page"]').text()).toContain('2 / 3');
    await wrapper.get('[data-testid="knowledge-pdf-next"]').trigger('click'); await flushPromises();
    expect(mock.getPage).toHaveBeenLastCalledWith(3);
    await wrapper.get('[data-testid="knowledge-pdf-zoom-in"]').trigger('click'); await flushPromises();
    expect(wrapper.get('canvas').element.width).toBe(750);
    expect(mock.load).toHaveBeenCalledTimes(1);
    expect(mock.render.mock.calls[0]![0].annotationMode).toBe(0);
  });
  it('不存在的引用页失败而不把第一页当作来源位置', async () => {
    const wrapper = render(4); await flushPromises();
    expect(wrapper.find('[data-testid="knowledge-pdf-error"]').exists()).toBe(true);
    expect(mock.getPage).not.toHaveBeenCalled(); expect(mock.destroy).toHaveBeenCalled();
  });
  it('卸载立即取消渲染、销毁文档并清空画布', async () => {
    mock.render.mockReturnValue({ promise: new Promise(() => {}), cancel: mock.cancel });
    const wrapper = render(); await flushPromises(); const canvas = wrapper.get('canvas').element;
    wrapper.unmount(); wrappers = []; expect(mock.cancel).toHaveBeenCalled(); expect(mock.destroy).toHaveBeenCalled(); expect(canvas.width).toBe(0);
  });
  it('15 秒解析超时销毁任务且迟到结果不能显示', async () => {
    vi.useFakeTimers(); let finish!: (value: unknown) => void;
    mock.load.mockResolvedValue({ promise: new Promise(resolve => { finish = resolve; }), destroy: mock.destroy });
    const wrapper = render(); await flushPromises(); await vi.advanceTimersByTimeAsync(15000); await flushPromises();
    expect(wrapper.find('[data-testid="knowledge-pdf-error"]').exists()).toBe(true); expect(mock.destroy).toHaveBeenCalled();
    finish({ numPages: 3, getPage: mock.getPage }); await flushPromises(); expect(mock.getPage).not.toHaveBeenCalled();
  });
  it('500 页以上失败且不取页', async () => {
    mock.load.mockResolvedValue({ promise: Promise.resolve({ numPages: 501, getPage: mock.getPage }), destroy: mock.destroy });
    const wrapper = render(); await flushPromises(); expect(wrapper.find('[data-testid="knowledge-pdf-error"]').exists()).toBe(true); expect(mock.getPage).not.toHaveBeenCalled();
  });
  it('15 秒渲染超时取消任务且清空画布', async () => {
    vi.useFakeTimers(); mock.render.mockReturnValue({ promise: new Promise(() => {}), cancel: mock.cancel });
    const wrapper = render(); await flushPromises(); const canvas = wrapper.get('canvas').element;
    await vi.advanceTimersByTimeAsync(15000); await flushPromises();
    expect(mock.cancel).toHaveBeenCalled(); expect(canvas.width).toBe(0); expect(wrapper.find('[data-testid="knowledge-pdf-error"]').exists()).toBe(true);
  });
  it('换源销毁旧任务，旧解析晚到不能替换新页', async () => {
    let finish!: (value: unknown) => void;
    const oldDestroy = vi.fn(); mock.load.mockResolvedValueOnce({ promise: new Promise(resolve => { finish = resolve; }), destroy: oldDestroy });
    const wrapper = render(); await flushPromises(); await wrapper.setProps({ data: new Uint8Array([2]), initialPage: 1 }); await flushPromises();
    finish({ numPages: 9, getPage: vi.fn() }); await flushPromises();
    expect(oldDestroy).toHaveBeenCalled(); expect(wrapper.get('[data-testid="knowledge-pdf-page"]').text()).toContain('1 / 3');
  });
  it('首页/末页和缩放上下限阻止越界，引用页可重新定位', async () => {
    const wrapper = render(1); await flushPromises();
    expect(wrapper.get('[data-testid="knowledge-pdf-previous"]').attributes('disabled')).toBeDefined();
    for (let i = 0; i < 4; i++) { await wrapper.get('[data-testid="knowledge-pdf-zoom-in"]').trigger('click'); await flushPromises(); }
    expect(wrapper.get('[data-testid="knowledge-pdf-zoom-in"]').attributes('disabled')).toBeDefined();
    for (let i = 0; i < 6; i++) { await wrapper.get('[data-testid="knowledge-pdf-zoom-out"]').trigger('click'); await flushPromises(); }
    expect(wrapper.get('[data-testid="knowledge-pdf-zoom-out"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="knowledge-pdf-next"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="knowledge-pdf-locate"]').trigger('click'); await flushPromises(); expect(mock.getPage).toHaveBeenLastCalledWith(1);
  });
  it('异常巨大页面有界降采样，缩放保持画布像素预算', async () => {
    mock.getPage.mockResolvedValue({ ...page, getViewport: ({ scale }: { scale: number }) => ({ width: 100000 * scale, height: 100000 * scale }) });
    const wrapper = render(); await flushPromises(); const canvas = wrapper.get('canvas').element;
    expect(canvas.width).toBeGreaterThan(0); expect(canvas.width).toBeLessThanOrEqual(2400); expect(canvas.width * canvas.height).toBeLessThanOrEqual(4000000);
  });
});
