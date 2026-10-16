import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElInput, ElPagination, ElTable } from 'element-plus';
import View from './DocumentRecycleBinView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import * as api from '../api/document-recycle-bin';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { recycleItem as row, recyclePage, restoredRecycleItem } from '../test/document-recycle-fixtures';
vi.mock('../api/document-recycle-bin', () => ({ listRecycleBinItems: vi.fn(), restoreRecycleBinItem: vi.fn(), purgeRecycleBinItem: vi.fn() }));
const second = { ...row, id: '019bc2b1-2a40-7cc3-8992-a80de51bf298', title: '其他文档' }, failure = { title: '服务暂时不可用', status: 503, code: 'fixture.unavailable' };
const permissions = ['read', 'restore', 'purge'].map(action => 'document.host_recycle_bin.' + action);
let wrapper: VueWrapper;
interface State {
  items: typeof row[]; selectedRows: typeof row[]; total: number; page: number; pageSize: number; catalogReady: boolean; changing: boolean; problem?: unknown;
  searchKeyword: string; batchReceipt?: { succeeded: number; items: { outcome: string }[] };
  load: () => Promise<void>; restore: (value: typeof row) => Promise<void>; purge: (value: typeof row) => Promise<void>; batchRestore: () => Promise<void>;
  finishConfirmation: (value: boolean) => void; recover: (value: typeof row) => Promise<void>; changePage: (page: number, size: number) => void; handleSearch: () => void;
}
const state = () => wrapper.vm as unknown as State;
async function setup(route?: { name: string }) { const context = createShareSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], provide: route ? { [routeLocationKey as symbol]: route } : {} } }); await flushPromises(); return context.session; }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listRecycleBinItems).mockResolvedValue(recyclePage([row, second])); vi.mocked(api.restoreRecycleBinItem).mockImplementation(async id => restoredRecycleItem(id === row.id ? row : second)); vi.mocked(api.purgeRecycleBinItem).mockResolvedValue(true); });
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
describe('回收站目录、批次与恢复流程', () => {
  it('同页目录失败保留已确认内容，写入暂停到重新读取成功', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockRejectedValueOnce(failure); await state().load(); expect(state().items).toHaveLength(2); expect(state().catalogReady).toBe(false);
    await state().restore(row); expect(api.restoreRecycleBinItem).not.toHaveBeenCalled(); await state().load(); expect(state().catalogReady).toBe(true);
  });
  it('切页失败清除旧页，不把旧行标成新页内容', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockRejectedValueOnce(failure); state().changePage(2, 20); await flushPromises(); expect(state().page).toBe(2); expect(state().items).toHaveLength(0);
  });
  it('畸形和重复目录不能替换已确认行', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row, { ...row }])); await state().load(); expect(state().catalogReady).toBe(false); expect(state().items).toHaveLength(2);
  });
  it('晚目录结果不能覆盖较新读取或提前清除忙状态', async () => {
    const old = deferred<ReturnType<typeof recyclePage>>(), next = deferred<ReturnType<typeof recyclePage>>(); await setup(); vi.mocked(api.listRecycleBinItems).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    const first = state().load(), secondLoad = state().load(); old.resolve(recyclePage([row])); await first; next.resolve(recyclePage([second])); await secondLoad; expect(state().items.map(item => item.id)).toEqual([second.id]);
  });
  it('空末页只回退一次，大小改变归一第一页并合并重复 change', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([], 3, 20, 1)).mockResolvedValueOnce(recyclePage([row]));
    state().changePage(3, 20); await flushPromises(); expect(state().page).toBe(1); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(3);
    vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row], 1, 50)); state().changePage(2, 50); state().changePage(1, 50); await flushPromises(); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(4); expect(state().page).toBe(1);
  });
  it('非空页保留并以实际行数兜底分页显示下界', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row], 2, 20, 0)); state().changePage(2, 20); await flushPromises(); expect(state().items).toHaveLength(1); expect(wrapper.getComponent(ElPagination).props('total')).toBe(21);
  });
  it('分页真实组件的模型更新与 change 只读取归一后的新页一次', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row], 1, 50)); const pagination = wrapper.getComponent(ElPagination);
    pagination.vm.$emit('update:page-size', 50); pagination.vm.$emit('update:current-page', 2); pagination.vm.$emit('change', 2, 50); pagination.vm.$emit('change', 1, 50); await flushPromises();
    expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2); expect(api.listRecycleBinItems).toHaveBeenLastCalledWith(1, 50, expect.any(AbortSignal)); expect(state().page).toBe(1);
  });
  it('真实分页器等待第二页响应时不因暂时空行压回第一页或取消请求', async () => {
    vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row], 1, 20, 60)); await setup(); const pending = deferred<ReturnType<typeof recyclePage>>(); vi.mocked(api.listRecycleBinItems).mockReturnValueOnce(pending.promise);
    const pagination = wrapper.getComponent(ElPagination); pagination.vm.$emit('update:current-page', 2); pagination.vm.$emit('change', 2, 20); await flushPromises();
    const signal = vi.mocked(api.listRecycleBinItems).mock.calls[1]![2]!; expect(signal.aborted).toBe(false); expect(state().page).toBe(2); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2); expect(pagination.props('total')).toBeGreaterThanOrEqual(21);
    pending.resolve(recyclePage([second], 2, 20, 60)); await flushPromises(); expect(state().page).toBe(2); expect(state().items[0]?.id).toBe(second.id); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2);
  });
  it('成功恢复先移除行并保留回执，旧补读不能复活行', async () => {
    await setup(); await state().restore(row); expect(state().batchReceipt?.succeeded).toBe(1); expect(state().items.map(item => item.id)).toEqual([second.id]); expect(state().catalogReady).toBe(false);
  });
  it('已恢复文档后来重新删除且修订更高时允许再次从回收站恢复', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([second])); await state().restore(row); expect(state().catalogReady).toBe(true);
    const deletedAgain = { ...row, version: row.version + 2 }; vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([deletedAgain])); await state().load(); expect(state().catalogReady).toBe(true);
    vi.mocked(api.restoreRecycleBinItem).mockResolvedValueOnce(restoredRecycleItem(deletedAgain)); await state().restore(deletedAgain); expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(2);
  });
  it('已确认永久清除的资源不能被后续目录中的更高修订复活', async () => {
    await setup(); vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([second])); const action = state().purge(row); state().finishConfirmation(true); await action;
    vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([{ ...row, version: 99 }])); await state().load(); expect(state().catalogReady).toBe(false); expect(state().items.map(item => item.id)).toEqual([second.id]); expect(state().batchReceipt?.succeeded).toBe(1);
  });
  it('恢复响应身份或修订不符作为未知结果，不接着执行下一项', async () => {
    await setup(); vi.mocked(api.restoreRecycleBinItem).mockResolvedValueOnce(row); state().selectedRows = [row, second]; await state().batchRestore(); expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce(); expect(state().batchReceipt?.items.map(item => item.outcome)).toEqual(['unconfirmed', 'not_sent']);
  });
  it('明确拒绝标为失败，修正或刷新后可手动重试', async () => {
    await setup(); vi.mocked(api.restoreRecycleBinItem).mockRejectedValueOnce({ status: 409, title: '修订冲突', code: 'document.version_conflict' }); await state().restore(row); expect(state().batchReceipt?.items[0]?.outcome).toBe('failed');
    await state().restore(row); expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(2);
  });
  it('未知恢复同修订补读仍受保护，新修订读取才解除对应门槛', async () => {
    await setup(); vi.mocked(api.restoreRecycleBinItem).mockRejectedValueOnce(failure); await state().restore(row); await state().restore(row); expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce();
    const newer = { ...row, version: row.version + 2 }; vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([newer])); await state().load(); vi.mocked(api.restoreRecycleBinItem).mockResolvedValueOnce(restoredRecycleItem(newer)); await state().restore(newer); expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(2);
  });
  it('未知永久清除即使读到新修订也不能自动解锁', async () => {
    await setup(); vi.mocked(api.purgeRecycleBinItem).mockRejectedValueOnce(failure); const first = state().purge(row); state().finishConfirmation(true); await first;
    const newer = { ...row, version: row.version + 2 }; vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([newer])); await state().load(); await state().purge(newer); expect(api.purgeRecycleBinItem).toHaveBeenCalledOnce();
    const recover = state().recover(newer); state().finishConfirmation(true); await recover; expect(api.purgeRecycleBinItem).toHaveBeenCalledOnce();
    const next = state().purge(newer); state().finishConfirmation(false); await next; expect(api.purgeRecycleBinItem).toHaveBeenCalledOnce();
  });
  it('永久清除 false 明确失败且不锁住下一次人工操作', async () => {
    await setup(); vi.mocked(api.purgeRecycleBinItem).mockResolvedValueOnce(false); const action = state().purge(row); state().finishConfirmation(true); await action; expect(state().batchReceipt?.items[0]?.outcome).toBe('failed');
    const retry = state().purge(row); state().finishConfirmation(false); await retry; expect(api.purgeRecycleBinItem).toHaveBeenCalledOnce();
  });
  it('旧确认回调不能确认后来打开的永久清除', async () => {
    await setup(); const first = state().purge(row); await flushPromises(); const old = wrapper.getComponent(ArtFormDialog).vm.$.vnode.props!.onConfirm as () => void;
    state().finishConfirmation(false); await first; const next = state().purge(second); old(); await flushPromises(); expect(api.purgeRecycleBinItem).not.toHaveBeenCalled(); state().finishConfirmation(false); await next;
  });
  it('旧行按钮、选择和刷新回调不能操作新目录', async () => {
    await setup(); const restore = wrapper.findAllComponents(ArtTableActionButton).find(button => button.props('testId') === 'document-recycle-restore')!.vm.$.vnode.props!.onClick as () => void;
    const select = wrapper.getComponent({ name: 'ElTable' }).vm.$.vnode.props!['onSelection-change'] as (rows: typeof row[]) => void;
    const refresh = wrapper.getComponent(ArtTableHeader).vm.$.vnode.props!.onRefresh as () => void; await state().load(); restore(); select([row]); refresh(); await flushPromises(); expect(api.restoreRecycleBinItem).not.toHaveBeenCalled(); expect(state().selectedRows).toHaveLength(0); expect(api.listRecycleBinItems).toHaveBeenCalledTimes(2);
  });
  it('批次选择改变后旧批量按钮不能借用新选择或执行旧选择', async () => {
    await setup(); state().selectedRows = [row]; await flushPromises(); const old = wrapper.findAllComponents(ElButton).find(button => button.attributes('data-testid') === 'document-recycle-batch-restore')!.vm.$.vnode.props!.onClick as () => void;
    state().selectedRows = [second]; old(); await flushPromises(); expect(api.restoreRecycleBinItem).not.toHaveBeenCalled(); expect(state().selectedRows.map(item => item.id)).toEqual([second.id]);
  });
  it('搜索只筛选当前页并清除被隐藏的批次选择，旧输入回调不能改新目录', async () => {
    await setup(); const input = wrapper.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (text: string) => void; state().selectedRows = [row]; state().searchKeyword = second.title; state().handleSearch(); await flushPromises(); expect(state().selectedRows).toHaveLength(0);
    await state().load(); input('stale'); expect(state().searchKeyword).not.toBe('stale');
  });
  it('确认期间目录修订变化关闭确认，后续项变化标为未发送', async () => {
    await setup(); const purge = state().purge(row); state().items = [{ ...row, version: 99 }, second]; state().finishConfirmation(true); await purge; expect(api.purgeRecycleBinItem).not.toHaveBeenCalled();
    vi.mocked(api.listRecycleBinItems).mockResolvedValueOnce(recyclePage([row, second])); await state().load(); const pending = deferred<typeof row>(); vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(pending.promise); state().selectedRows = [row, second]; const batch = state().batchRestore(); state().items = [row, { ...second, version: 99 }]; pending.resolve(restoredRecycleItem()); await batch; expect(state().batchReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'not_sent']);
  });
  it('离开路由取消在途写入，迟到成功不接入回执或继续下一项', async () => {
    const route = reactive({ name: 'document-recycle-bin' }), pending = deferred<typeof row>(); await setup(route); vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(pending.promise); state().selectedRows = [row, second]; const action = state().batchRestore(); const signal = vi.mocked(api.restoreRecycleBinItem).mock.calls[0]![2]!;
    route.name = 'home'; pending.resolve(restoredRecycleItem()); await action; expect(signal.aborted).toBe(true); expect(state().batchReceipt).toBeUndefined(); expect(api.restoreRecycleBinItem).toHaveBeenCalledOnce();
  });
  it('旧批次等待补读时切会话，旧 finally 不能清除新批次忙状态', async () => {
    const oldRead = deferred<ReturnType<typeof recyclePage>>(), newWrite = deferred<typeof row>(); const session = await setup();
    vi.mocked(api.listRecycleBinItems).mockReturnValueOnce(oldRead.promise); const old = state().restore(row); await flushPromises(); session.currentUser!.sessionId = second.id; await flushPromises();
    vi.mocked(api.restoreRecycleBinItem).mockReturnValueOnce(newWrite.promise); const next = state().restore(second); oldRead.resolve(recyclePage([])); await old; expect(state().changing).toBe(true);
    newWrite.resolve(restoredRecycleItem(second)); await next; expect(api.restoreRecycleBinItem).toHaveBeenCalledTimes(2);
  });
});
