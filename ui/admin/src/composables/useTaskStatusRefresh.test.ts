import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { deferred } from '../test/data-output-fixtures';
import { useTaskStatusRefresh } from './useTaskStatusRefresh';

let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); vi.spyOn(document, 'hidden', 'get').mockReturnValue(false); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); vi.useRealTimers(); });
const tick = async (milliseconds = 5_000) => { await vi.advanceTimersByTimeAsync(milliseconds); await flushPromises(); };
function fixture(refresh = vi.fn<Parameters<typeof useTaskStatusRefresh>[1]>().mockResolvedValue(), keepAlive = false) {
  const enabled = ref(true); const show = ref(true);
  const View = defineComponent({ setup() { useTaskStatusRefresh(() => enabled.value, refresh); return () => h('div'); } });
  const Other = defineComponent({ setup: () => () => h('span') });
  wrapper = mount(keepAlive ? defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => h(show.value ? View : Other) }) }) : View);
  return { enabled, show, refresh };
}
describe('任务状态刷新调度', () => {
  it('挂载和 KeepAlive 首次激活只注册一个定时器，停用后释放，恢复仅刷新一次', async () => {
    const { show, refresh } = fixture(undefined, true); await tick(); expect(refresh).toHaveBeenCalledTimes(1);
    show.value = false; await flushPromises(); await tick(20_000); expect(refresh).toHaveBeenCalledTimes(1);
    show.value = true; await flushPromises(); await tick(); expect(refresh).toHaveBeenCalledTimes(2);
  });
  it('慢读取结束后再等待五秒，不补发积压的轮次', async () => {
    const waiting = deferred<void>(); const refresh = vi.fn<Parameters<typeof useTaskStatusRefresh>[1]>().mockReturnValueOnce(waiting.promise).mockResolvedValue();
    fixture(refresh); await tick(); await tick(60_000); expect(refresh).toHaveBeenCalledTimes(1);
    waiting.resolve(); await flushPromises(); await tick(4_999); expect(refresh).toHaveBeenCalledTimes(1);
    await tick(1); expect(refresh).toHaveBeenCalledTimes(2);
  });
  it('读取被拒绝时观察错误并停住，重新启用才恢复', async () => {
    const refresh = vi.fn<Parameters<typeof useTaskStatusRefresh>[1]>().mockRejectedValueOnce(new Error('failure')).mockResolvedValue();
    const { enabled } = fixture(refresh); await tick(); await tick(20_000); expect(refresh).toHaveBeenCalledTimes(1);
    enabled.value = false; enabled.value = true; await tick(); expect(refresh).toHaveBeenCalledTimes(2);
  });
  it('隐藏或禁用使旧轮次的后续读取资格失效，恢复不会与旧轮次重叠', async () => {
    const waiting = deferred<void>(); let current!: () => boolean;
    const refresh = vi.fn<Parameters<typeof useTaskStatusRefresh>[1]>().mockImplementationOnce(value => { current = value; return waiting.promise; }).mockResolvedValue();
    const { enabled } = fixture(refresh); await tick(); expect(current()).toBe(true);
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(true); document.dispatchEvent(new Event('visibilitychange')); expect(current()).toBe(false);
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false); document.dispatchEvent(new Event('visibilitychange'));
    enabled.value = false; enabled.value = true; await tick(); expect(refresh).toHaveBeenCalledTimes(1);
    waiting.resolve(); await flushPromises(); await tick(); expect(refresh).toHaveBeenCalledTimes(2); expect(current()).toBe(false);
  });
  it('卸载后释放监听器，迟到的完成不能再次安排读取', async () => {
    const waiting = deferred<void>(); const refresh = vi.fn<Parameters<typeof useTaskStatusRefresh>[1]>().mockReturnValue(waiting.promise);
    const remove = vi.spyOn(document, 'removeEventListener'); fixture(refresh); await tick(); wrapper!.unmount(); wrapper = undefined;
    expect(remove).toHaveBeenCalledWith('visibilitychange', expect.any(Function)); waiting.resolve(); await flushPromises();
    document.dispatchEvent(new Event('visibilitychange')); await tick(20_000); expect(refresh).toHaveBeenCalledTimes(1);
  });
});
