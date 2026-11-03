import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { effectScope, ref } from 'vue';
import { flushPromises } from '@vue/test-utils';
import type { ImportExportTaskDetailResponse } from '@fullnet/client-contracts';
import { useImportTaskDetailPolling } from './useImportTaskDetailPolling';

const task = (statusKey = 'queued') => ({ id: 'task-a', tenantId: 'tenant-a', statusKey }) as ImportExportTaskDetailResponse;
const scopes: ReturnType<typeof effectScope>[] = [];
let hidden = false;
function setup() {
  const scope = effectScope(); scopes.push(scope);
  const enabled = ref(true), selected = ref<ImportExportTaskDetailResponse | undefined>(task());
  const read = vi.fn<(id: string, signal: AbortSignal) => Promise<ImportExportTaskDetailResponse>>()
    .mockResolvedValue(task('execution_succeeded'));
  const failed = vi.fn(), accepted = vi.fn();
  const polling = scope.run(() => useImportTaskDetailPolling({
    enabled: () => enabled.value, task: () => selected.value, read,
    accept: result => { accepted(result); selected.value = result; }, failed
  }))!;
  return { scope, enabled, selected, read, failed, accepted, polling };
}
describe('后台导入详情刷新', () => {
  beforeEach(() => {
    vi.useFakeTimers(); hidden = false;
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
  });
  afterEach(() => { scopes.splice(0).forEach(scope => scope.stop()); vi.useRealTimers(); vi.restoreAllMocks(); });

  it('仅排队或执行状态轮询，成功后停止', async () => {
    const state = setup();
    state.read.mockResolvedValueOnce(task('executing')).mockResolvedValueOnce(task('execution_succeeded'));
    await vi.advanceTimersByTimeAsync(4000); await flushPromises();
    expect(state.selected.value?.statusKey).toBe('execution_succeeded');
    await vi.advanceTimersByTimeAsync(10000); expect(state.read).toHaveBeenCalledTimes(2);
  });
  it('关闭或上下文失效取消在途请求，丢弃迟到结果', async () => {
    const state = setup(); let resolve!: (result: ImportExportTaskDetailResponse) => void;
    state.read.mockImplementation(() => new Promise(done => { resolve = done; }));
    await vi.advanceTimersByTimeAsync(2000); const signal = state.read.mock.calls[0]![1];
    state.enabled.value = false; expect(signal.aborted).toBe(true);
    resolve(task('execution_succeeded')); await flushPromises();
    expect(state.accepted).not.toHaveBeenCalled(); await vi.advanceTimersByTimeAsync(10000);
    expect(state.read).toHaveBeenCalledTimes(1);
  });
  it('更换详情不接纳上一任务的结果', async () => {
    const state = setup(); let resolve!: (result: ImportExportTaskDetailResponse) => void;
    state.read.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    await vi.advanceTimersByTimeAsync(2000);
    state.selected.value = { ...task(), id: 'task-b' };
    resolve(task('execution_succeeded')); await flushPromises(); expect(state.accepted).not.toHaveBeenCalled();
    state.read.mockResolvedValue({ ...task('execution_succeeded'), id: 'task-b' });
    await vi.advanceTimersByTimeAsync(2000); expect(state.selected.value?.id).toBe('task-b');
  });
  it('隐藏页面暂停，恢复可见后继续读取', async () => {
    const state = setup(); hidden = true; document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(10000); expect(state.read).not.toHaveBeenCalled();
    hidden = false; document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(2000); expect(state.read).toHaveBeenCalledTimes(1);
  });
  it('网络失败停止，重新打开详情才恢复', async () => {
    const state = setup(); state.read.mockRejectedValueOnce(new Error('offline'));
    await vi.advanceTimersByTimeAsync(10000); expect(state.read).toHaveBeenCalledTimes(1);
    expect(state.polling.paused.value).toBe(true); expect(state.failed).toHaveBeenCalledTimes(1);
    state.enabled.value = false; state.enabled.value = true;
    await vi.advanceTimersByTimeAsync(2000); expect(state.selected.value?.statusKey).toBe('execution_succeeded');
  });
  it('拒绝身份不匹配的服务端结果', async () => {
    const state = setup(); state.read.mockResolvedValue({ ...task('execution_succeeded'), tenantId: 'tenant-b' });
    await vi.advanceTimersByTimeAsync(2000); expect(state.accepted).not.toHaveBeenCalled();
    expect(state.failed).toHaveBeenCalledTimes(1); expect(state.polling.paused.value).toBe(true);
  });
  it('三十秒无响应取消读取并停止刷新', async () => {
    const state = setup(); state.read.mockImplementation(() => new Promise(() => {}));
    await vi.advanceTimersByTimeAsync(32000);
    expect(state.read.mock.calls[0]![1].aborted).toBe(true);
    expect(state.failed).toHaveBeenCalledTimes(1); expect(state.polling.paused.value).toBe(true);
    await vi.advanceTimersByTimeAsync(10000); expect(state.read).toHaveBeenCalledTimes(1);
  });
  it('五分钟后停止自动刷新，销毁时清理资源', async () => {
    const state = setup(); state.read.mockResolvedValue(task());
    await vi.advanceTimersByTimeAsync(310000); expect(state.polling.paused.value).toBe(true);
    const calls = state.read.mock.calls.length;
    expect(calls).toBeLessThanOrEqual(150); await vi.advanceTimersByTimeAsync(10000);
    expect(state.read).toHaveBeenCalledTimes(calls); state.scope.stop(); expect(vi.getTimerCount()).toBe(0);
  });
});
