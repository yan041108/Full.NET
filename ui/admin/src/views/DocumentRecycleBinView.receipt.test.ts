import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import View from './DocumentRecycleBinView.vue';
import * as api from '../api/document-recycle-bin';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, shareDocument as doc, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-recycle-bin', () => ({ listRecycleBinItems: vi.fn(), restoreRecycleBinItem: vi.fn(), purgeRecycleBinItem: vi.fn() }));
const first = { ...doc, title: '第一项' }, second = { ...doc, id: otherShareId, title: '第二项' }, third = { ...doc, id: '01980000-0000-7000-8000-000000000003', title: '第三项' };
const read = 'document.host_recycle_bin.read', restore = 'document.host_recycle_bin.restore', purge = 'document.host_recycle_bin.purge';
const all = [read, restore, purge]; const page = { items: [first, second, third], page: 1, pageSize: 20, total: 3 };
let wrapper: VueWrapper | undefined; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listRecycleBinItems).mockResolvedValue(page);
  vi.mocked(api.restoreRecycleBinItem).mockImplementation(async id => ({ ...first, id })); vi.mocked(api.purgeRecycleBinItem).mockResolvedValue(true);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(permissions = all, keepAlive = false, tenantId: string | null = null) {
  const context = createShareSession(permissions); context.session.currentUser!.tenantId = tenantId; visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(View) : h(blank) }) });
  wrapper = mount(keepAlive ? root : View, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
function vm() { return wrapper!.findComponent(View).vm as unknown as {
  items: typeof first[]; selectedRows: typeof first[]; total: number; changing: boolean;
  batchReceipt?: { succeeded: number; total: number; items: { id: string; outcome: string; errorCode?: string }[] };
  load: () => Promise<void>; batchRestore: () => Promise<void>; batchPurge: () => Promise<void>; finishConfirmation: (confirmed: boolean) => void;
}; }
async function run(kind: 'restore' | 'purge', rows = [first, second, third], confirmed = true) {
  vm().selectedRows = rows; const action = kind === 'restore' ? vm().batchRestore() : vm().batchPurge();
  if (kind === 'purge') vm().finishConfirmation(confirmed); await action; await flushPromises();
}

describe('回收站批量结果及中断回执', () => {
  it('在途条目显示等待结果，后续项目保持未发送', async () => {
    const pending = deferred<typeof first>(); vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);
    await setup(); vm().selectedRows = [first, second]; const action = vm().batchRestore();
    expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['processing', 'not_sent']);
    pending.resolve(first); await action;
  });
  it.each(['restore', 'purge'] as const)('健康 %s 显示逐项成功并只补读一次', async kind => {
    await setup(); await run(kind); expect(vm().batchReceipt?.succeeded).toBe(3);
    expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'succeeded', 'succeeded']);
    expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2); expect(vm().selectedRows).toHaveLength(0);
  });
  it('恢复中途失败保留已成功与未发送项目，不自动重试', async () => {
    vi.mocked(api.restoreRecycleBinItem).mockResolvedValueOnce(first).mockRejectedValueOnce({ status: 409, code: 'document.version_conflict', title: 'conflict' });
    const success = vi.spyOn(feedback, 'showSuccess'); await setup(); await run('restore');
    expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(2); expect(vm().batchReceipt?.succeeded).toBe(1);
    expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'unconfirmed', 'not_sent']);
    expect(vm().batchReceipt?.items[1]?.errorCode).toBe('document.version_conflict'); expect(success).not.toHaveBeenCalled(); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2);
  });
  it('清除部分成功后 false 停止，不重复已成功项目', async () => {
    vi.mocked(api.purgeRecycleBinItem).mockResolvedValueOnce(true).mockResolvedValueOnce(false); await setup(); await run('purge');
    expect(api.purgeRecycleBinItem).toHaveBeenCalledTimes(2); expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'unconfirmed', 'not_sent']);
    expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2);
  });
  it('首项 false 保留目录和未发送项目，不报成功或额外补读', async () => {
    vi.mocked(api.purgeRecycleBinItem).mockResolvedValueOnce(false); await setup(); await run('purge');
    expect(vm().batchReceipt?.succeeded).toBe(0); expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['unconfirmed', 'not_sent', 'not_sent']);
    expect(api.listRecycleBinItems).toHaveBeenCalledOnce();
  });
  it('未知网络结果不冒充明确失败，补读也不自动重试写入', async () => {
    vi.mocked(api.restoreRecycleBinItem).mockRejectedValueOnce(new Error('internal diagnostics')); await setup(); await run('restore');
    expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce(); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2);
    expect(vm().batchReceipt?.items[0]?.outcome).toBe('unconfirmed'); expect(wrapper!.text()).not.toContain('internal diagnostics');
  });
  it('补读失败时成功条目已从本地移除，回执仍保留', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockRejectedValueOnce(new Error('reload failed'));
    vi.mocked(api.restoreRecycleBinItem).mockResolvedValueOnce(first).mockRejectedValueOnce(new Error('network'));
    await run('restore'); expect(vm().items.map(item => item.id)).toEqual([second.id, third.id]); expect(vm().total).toBe(2);
    expect(vm().batchReceipt?.succeeded).toBe(1); expect(vm().selectedRows).toHaveLength(0);
  });
  it('放弃永久清除确认无写入，也不创建本次回执', async () => {
    await setup(); await run('purge', [first, second], false); expect(api.purgeRecycleBinItem).not.toHaveBeenCalled(); expect(vm().batchReceipt).toBeUndefined();
  });
  it('确认期间修订变更拒绝陈旧清除目标', async () => {
    await setup(); vm().selectedRows = [first]; const action = vm().batchPurge(); vm().items = [{ ...first, version: first.version + 1 }, second, third];
    vm().finishConfirmation(true); await action; expect(api.purgeRecycleBinItem).not.toHaveBeenCalled();
  });
  it('批次后续目标修订变化时停止，不发送陈旧目标', async () => {
    const pending = deferred<typeof first>(); vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);
    await setup(); vm().selectedRows = [first, second, third]; const action = vm().batchRestore(); vm().items = [first, { ...second, version: 99 }, third];
    pending.resolve(first); await action; expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce();
    expect(vm().batchReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'unconfirmed', 'not_sent']);
  });
  it('重复选择按 UUID 去重，同一条目只发送一次', async () => {
    await setup(); await run('restore', [first, { ...first }, second]); expect(vi.mocked(api.restoreRecycleBinItem).mock.calls.map(call => call[0])).toEqual([first.id, second.id]);
    expect(vm().batchReceipt?.total).toBe(2);
  });
  it('未知目录对象不能从选择集合直接恢复', async () => {
    await setup(); await run('restore', [{ ...first, id: '01980000-0000-7000-8000-000000000099' }]); expect(api.restoreRecycleBinItem).not.toHaveBeenCalled();
  });
  it('Host 文本上下文带租户标识不读取或写入', async () => {
    await setup(all, false, first.id); await run('restore'); expect(api.listRecycleBinItems).not.toHaveBeenCalled(); expect(api.restoreRecycleBinItem).not.toHaveBeenCalled();
  });
  it('缺少原操作权限不创建回执或写入', async () => {
    await setup([read]); await run('restore'); await run('purge'); expect(vm().batchReceipt).toBeUndefined();
    expect(api.restoreRecycleBinItem).not.toHaveBeenCalled(); expect(api.purgeRecycleBinItem).not.toHaveBeenCalled();
  });
  it('在途切会话取消后续项目，清除回执且不接入晚结果', async () => {
    const pending = deferred<typeof first>(); vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);
    const session = await setup(); vm().selectedRows = [first, second]; const action = vm().batchRestore(); const signal = vi.mocked(api.restoreRecycleBinItem).mock.calls[0]![2]!;
    session.currentUser!.sessionId = second.id; pending.resolve(first); await action; await flushPromises();
    expect(signal.aborted).toBe(true); expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce(); expect(vm().batchReceipt).toBeUndefined();
  });
  it('KeepAlive 停用清回执，恢复不重放写操作', async () => {
    await setup(undefined, true); await run('restore'); visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(vm().batchReceipt).toBeUndefined(); expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(3);
  });
});
