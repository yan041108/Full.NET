import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import View from './DocumentSharesView.vue';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import * as api from '../api/document-shares';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, documentShare as share, otherShareId as other } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';

vi.mock('../api/document-shares', () => ({ listDocumentShares: vi.fn(), createDocumentShare: vi.fn(), batchCreateDocumentShares: vi.fn(), updateDocumentShareStatus: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
let wrapper: VueWrapper | undefined;
const read = 'document.host_shares.read', write = 'document.host_shares.update_status';
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listDocumentShares).mockResolvedValue({ items: [{ ...share }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.updateDocumentShareStatus).mockResolvedValue({ ...share, isEnabled: false, version: 2 });
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup() { const context = createShareSession([read, write]); wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} } } }); await flushPromises(); return context; }
const vm = () => wrapper!.vm as unknown as { toggleStatus(row: typeof share): Promise<void>; load(): Promise<void>; items: typeof share[] };
const toggle = () => wrapper!.get('[data-testid="document-share-toggle"]').trigger('click');

describe('文档分享状态写入与重读', () => {
  it('大小写等价行使用权威目录标识切换状态', async () => {
    await setup(); await vm().toggleStatus({ ...share, id: share.id.toUpperCase() });
    expect(api.updateDocumentShareStatus).toHaveBeenCalledExactlyOnceWith(share.id, { isEnabled: false, version: 1 }, expect.any(AbortSignal));
  });
  it('同标识和修订的事件不能替换权威文档归属', async () => {
    const success = vi.spyOn(feedback, 'showSuccess'); await setup(); await vm().toggleStatus({ ...share, documentId: other });
    expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce(); expect(success).toHaveBeenCalledOnce();
  });
  it('已确认更新在补读失败后保留新状态和版本', async () => {
    await setup(); vi.mocked(api.listDocumentShares).mockRejectedValueOnce(new Error('offline')); await toggle(); await flushPromises();
    expect(vm().items[0]).toMatchObject({ id: share.id, isEnabled: false, version: 2 });
    vi.mocked(api.updateDocumentShareStatus).mockResolvedValueOnce({ ...share, isEnabled: true, version: 3 });
    await toggle(); await flushPromises(); expect(vi.mocked(api.updateDocumentShareStatus).mock.calls[1]?.[1]).toEqual({ isEnabled: true, version: 2 });
  });
  it('更新后不再满足已应用启用条件时立即移除并扣减总数', async () => {
    await setup(); wrapper!.getComponent(ArtSearchBar).vm.$emit('search', { isEnabled: 'true' }); await flushPromises();
    vi.mocked(api.listDocumentShares).mockRejectedValueOnce(new Error('offline')); await toggle(); await flushPromises();
    expect(vm().items).toEqual([]); expect(wrapper!.getComponent(ElPagination).props('total')).toBe(0);
  });
  it('写入前取消旧列表，旧目录不能在成功补读失败后覆盖确认结果', async () => {
    await setup(); const old = deferred<Awaited<ReturnType<typeof api.listDocumentShares>>>(); vi.mocked(api.listDocumentShares).mockReturnValueOnce(old.promise);
    const reading = vm().load(); const signal = vi.mocked(api.listDocumentShares).mock.calls.at(-1)?.[3];
    vi.mocked(api.listDocumentShares).mockRejectedValueOnce(new Error('fresh read failed')); await vm().toggleStatus(share); expect(signal?.aborted).toBe(true);
    old.resolve({ items: [share], page: 1, pageSize: 20, total: 1 }); await reading; expect(vm().items[0]).toMatchObject({ isEnabled: false, version: 2 });
  });
  it('已观察到更高修订时旧成功回包不能覆盖它', async () => {
    await setup(); const pending = deferred<typeof share>(); vi.mocked(api.updateDocumentShareStatus).mockReturnValueOnce(pending.promise);
    const writing = vm().toggleStatus(share);
    vi.mocked(api.listDocumentShares).mockResolvedValueOnce({ items: [{ ...share, version: 4, accessCount: 8 }], page: 1, pageSize: 20, total: 1 }); await vm().load();
    vi.mocked(api.listDocumentShares).mockRejectedValueOnce(new Error('latest unavailable')); pending.resolve({ ...share, isEnabled: false, version: 2 }); await writing;
    expect(vm().items[0]).toMatchObject({ isEnabled: true, version: 4, accessCount: 8 });
  });
  it.each([409, 500, 'network'] as const)('写入结果%s失败后不能重复写入旧状态，成功刷新才恢复', async status => {
    await setup(); vi.mocked(api.updateDocumentShareStatus).mockRejectedValueOnce(status === 'network' ? new Error('response lost') : { status, title: '拒绝', code: 'document.share.conflict' });
    await toggle(); await flushPromises(); await vm().toggleStatus(share); expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce();
    expect(wrapper!.get('[data-testid="document-share-toggle"]').attributes('disabled')).toBeDefined();
    await vm().load(); await toggle(); await flushPromises(); expect(api.updateDocumentShareStatus).toHaveBeenCalledTimes(2);
  });
  it('写入未知结果后失败的刷新不能重新开放写入', async () => {
    await setup(); vi.mocked(api.updateDocumentShareStatus).mockRejectedValueOnce(new Error('response lost')); await toggle(); await flushPromises();
    vi.mocked(api.listDocumentShares).mockRejectedValueOnce(new Error('offline')); await vm().load(); await vm().toggleStatus(share); expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce();
  });
  it('写入失败时取消较早发起的补读，迟到目录不能解除未确认标记', async () => {
    await setup(); const pending = deferred<typeof share>(); vi.mocked(api.updateDocumentShareStatus).mockReturnValueOnce(pending.promise); const writing = vm().toggleStatus(share);
    const old = deferred<Awaited<ReturnType<typeof api.listDocumentShares>>>(); vi.mocked(api.listDocumentShares).mockReturnValueOnce(old.promise); const reading = vm().load(); const signal = vi.mocked(api.listDocumentShares).mock.calls.at(-1)?.[3];
    pending.reject(new Error('response lost')); await writing; expect(signal?.aborted).toBe(true);
    old.resolve({ items: [share], page: 1, pageSize: 20, total: 1 }); await reading; await vm().toggleStatus(share);
    expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce(); expect(wrapper!.get('[data-testid="document-share-toggle"]').attributes('disabled')).toBeDefined();
  });
  it.each([{ isEnabled: true, version: 2 }, { isEnabled: false, version: 1 }, { isEnabled: false, version: 3 }])('状态或版本错误回包%j不更新本地或报告成功', async change => {
    await setup(); const success = vi.spyOn(feedback, 'showSuccess'); vi.mocked(api.updateDocumentShareStatus).mockResolvedValueOnce({ ...share, ...change });
    await toggle(); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(vm().items[0]).toMatchObject({ isEnabled: true, version: 1 }); await vm().toggleStatus(share); expect(api.updateDocumentShareStatus).toHaveBeenCalledOnce();
  });
  it('未知结果记录在会话变化时清除，晚结果不污染新会话', async () => {
    const { session } = await setup(); const pending = deferred<typeof share>(); vi.mocked(api.updateDocumentShareStatus).mockReturnValueOnce(pending.promise);
    const old = vm().toggleStatus(share); session.currentUser!.sessionId = 'next'; await flushPromises(); pending.reject(new Error('old error')); await old;
    expect(wrapper!.get('[data-testid="document-share-toggle"]').attributes('disabled')).toBeUndefined();
  });
});
