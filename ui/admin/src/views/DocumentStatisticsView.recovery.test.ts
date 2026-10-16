import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElInput, ElInputNumber, ElPagination, ElTabs } from 'element-plus';
import View from './DocumentStatisticsView.vue';
import * as statistics from '../api/document-statistics';
import * as logs from '../api/document-access-logs';
import * as retention from '../api/document-version-retention';
import { createShareSession, shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-statistics', () => ({ getDocumentStatistics: vi.fn() }));
vi.mock('../api/document-access-logs', () => ({ listDocumentAccessLogs: vi.fn() }));
vi.mock('../api/document-version-retention', async original => ({ ...await original<typeof retention>(), getDocumentVersionRetentionSettings: vi.fn(), updateDocumentVersionRetentionSettings: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn() }));
const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50, version: 7 };
const stat = { summary: { totalItems: 1, totalVersions: 1, totalSizeKb: 1, totalSizeInfo: '1 KB' }, byType: [], byCategory: [], shareCount: 0, todayAccessCount: 0, todayDownloadCount: 0, todayCreatedCount: 0, recycleBinCount: 0 };
const log = { id: shareId, documentItemId: otherShareId, documentTitle: '文档', accessTypeKey: 'download', sourceKey: 'share', actorUserId: null, occurredAtUtc: '2026-10-16T00:00:00Z', clientIpFingerprint: null };
let wrapper: VueWrapper;
interface State { activeTab: 'statistics' | 'accessLogs' | 'retention'; accessLogs: typeof log[]; accessLogsPage: number; accessLogsPageSize: number; accessLogsTotal: number; accessLogFilters: { documentItemId: string; accessTypeKey: string; sourceKey: string }; retentionSettings: typeof settings | null; retentionForm: typeof settings; retentionReceipt?: { outcome: string; version: number }; loadAccessLogs: () => Promise<void>; loadRetentionSettings: () => Promise<void>; saveRetentionSettings: () => Promise<void>; applyAccessLogFilters: () => void }
const state = () => wrapper.vm as unknown as State;
const button = (id: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === id)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(statistics.getDocumentStatistics).mockResolvedValue(stat); vi.mocked(logs.listDocumentAccessLogs).mockResolvedValue({ items: [log], page: 1, pageSize: 20, total: 1 }); vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValue({ ...settings }); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockImplementation(async body => ({ ...body, version: body.version + 1 }));
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup() {
  const context = createShareSession(['document.host_statistics.read', 'document.host_access_logs.read', 'document.host_documents.read', 'document.host_documents.update']); const route = reactive({ name: 'document-statistics' });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} } } }); await flushPromises(); return { ...context, route };
}
const tab = async (name: State['activeTab']) => { wrapper.getComponent(ElTabs).vm.$emit('update:modelValue', name); await flushPromises(); };
describe('文档统计、访问日志与策略恢复', () => {
  it('首次统计失败有可见重试，旧页签刷新回调不能读取新页签', async () => {
    vi.mocked(statistics.getDocumentStatistics).mockRejectedValueOnce(new Error('offline')); await setup(); const old = button('document-statistics-refresh').vm.$.vnode.props!.onClick as () => void; await wrapper.get('[data-testid="document-statistics-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('1 KB'); await tab('accessLogs'); old(); await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledOnce();
  });
  it('旧页签选择回调不能在重读后切换页签', async () => {
    await setup(); const old = wrapper.getComponent(ElTabs).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void; await wrapper.get('[data-testid="document-statistics-refresh"]').trigger('click'); await flushPromises(); old('retention'); await flushPromises(); expect(retention.getDocumentVersionRetentionSettings).not.toHaveBeenCalled();
  });
  it('同查询日志失败保留确认行与总数，错误可见', async () => {
    await setup(); await tab('accessLogs'); vi.mocked(logs.listDocumentAccessLogs).mockRejectedValueOnce(new Error('offline')); await state().loadAccessLogs(); expect(state().accessLogs).toEqual([log]); expect(state().accessLogsTotal).toBe(1); expect(wrapper.find('[data-testid="document-access-logs-problem"]').exists()).toBe(true);
  });
  it('新查询开始前清空旧日志，错页响应不得接入', async () => {
    await setup(); await tab('accessLogs'); state().accessLogsPage = 2; const pending = deferred<Awaited<ReturnType<typeof logs.listDocumentAccessLogs>>>(); vi.mocked(logs.listDocumentAccessLogs).mockReturnValueOnce(pending.promise); const reading = state().loadAccessLogs(); expect(state().accessLogs).toEqual([]); pending.resolve({ items: [log], page: 1, pageSize: 20, total: 100 }); await reading; expect(state().accessLogs).toEqual([]);
  });
  it('页大小变化归一第一页，重复分页事件去重', async () => {
    await setup(); vi.mocked(logs.listDocumentAccessLogs).mockImplementation(async (page = 1, pageSize = 20) => ({ items: [log], page, pageSize, total: 200 })); await tab('accessLogs'); const pager = wrapper.getComponent(ElPagination); pager.vm.$emit('change', 3, 50); pager.vm.$emit('change', 3, 50); await flushPromises(); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.slice(0, 2)).toEqual([1, 50]); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(2);
  });
  it('越界末页最多恢复一次', async () => {
    await setup(); await tab('accessLogs'); state().accessLogsPage = 9; vi.mocked(logs.listDocumentAccessLogs).mockImplementation(async (page = 1, pageSize = 20) => ({ items: [], page, pageSize, total: 2 })); await state().loadAccessLogs(); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.slice(1).map(call => call[0])).toEqual([9, 1]);
  });
  it('非空页遇到较旧总数仍保留该页，分页显示下界不缩回前页', async () => {
    await setup(); await tab('accessLogs'); state().accessLogsPage = 2; vi.mocked(logs.listDocumentAccessLogs).mockResolvedValueOnce({ items: [log], page: 2, pageSize: 20, total: 20 }); await state().loadAccessLogs(); await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(2); expect(state().accessLogsPage).toBe(2); expect(state().accessLogs).toEqual([log]); expect(state().accessLogsTotal).toBe(20); expect(wrapper.getComponent(ElPagination).props('total')).toBe(21);
  });
  it('真实分页DOM存在，点击下一页由模型更新后只查询一次', async () => {
    await setup(); vi.mocked(logs.listDocumentAccessLogs).mockImplementation(async (page = 1, pageSize = 20) => ({ items: [log], page, pageSize, total: 100 })); await tab('accessLogs'); const pager = wrapper.getComponent(ElPagination); expect(pager.find('.el-pagination').exists()).toBe(true); await pager.get('.btn-next').trigger('click'); await flushPromises(); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.slice(0, 2)).toEqual([2, 20]); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(2);
  });
  it('无效文档过滤不覆盖已应用条件，刷新仍使用原查询', async () => {
    await setup(); await tab('accessLogs'); state().accessLogFilters.documentItemId = otherShareId; state().applyAccessLogFilters(); await flushPromises(); const count = vi.mocked(logs.listDocumentAccessLogs).mock.calls.length; state().accessLogFilters.documentItemId = 'bad'; state().applyAccessLogFilters(); await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(count); await state().loadAccessLogs(); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({ documentItemId: otherShareId });
  });
  it('旧筛选提交和旧输入回调不得迁移到新草稿', async () => {
    await setup(); await tab('accessLogs'); const oldApply = button('document-access-logs-filter').vm.$.vnode.props!.onClick as () => void, input = wrapper.getComponent(ElInput), oldInput = input.vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    await input.setValue(otherShareId); oldApply(); oldInput(shareId); await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledOnce(); expect(state().accessLogFilters.documentItemId).toBe(otherShareId);
  });
  it('旧分页回调在换页签再返回后无效', async () => {
    await setup(); await tab('accessLogs'); const old = wrapper.getComponent(ElPagination).vm.$.vnode.props!.onChange as (page: number, size: number) => void; await tab('statistics'); await tab('accessLogs'); old(3, 20); await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(2);
  });
  it('相同策略重读保留编辑，读取失败保留确认快照但暂停保存', async () => {
    await setup(); await tab('retention'); await wrapper.findAllComponents(ElInputNumber)[3]!.setValue(99); await state().loadRetentionSettings(); expect(state().retentionForm.batchSize).toBe(99);
    vi.mocked(retention.getDocumentVersionRetentionSettings).mockRejectedValueOnce(new Error('offline')); await state().loadRetentionSettings(); expect(state().retentionSettings).toEqual(settings); expect(state().retentionForm.batchSize).toBe(99); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled(); await state().loadRetentionSettings(); await state().saveRetentionSettings(); expect(vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[0]![0].batchSize).toBe(99);
  });
  it('策略重读的旧保存和字段事件不能提交或修改新版本', async () => {
    await setup(); await tab('retention'); const oldSave = button('document-version-retention-save').vm.$.vnode.props!.onClick as () => void, oldInput = wrapper.findAllComponents(ElInputNumber)[3]!.vm.$.vnode.props!['onUpdate:modelValue'] as (value: number) => void;
    vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValueOnce({ ...settings, version: 8, batchSize: 70 }); await state().loadRetentionSettings(); oldSave(); oldInput(999); await flushPromises(); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled(); expect(state().retentionForm.batchSize).toBe(70);
  });
  it('直接编辑在途策略取消保存并记未知回执，迟到成功不改写', async () => {
    await setup(); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockReturnValueOnce(pending.promise); const saving = state().saveRetentionSettings(), signal = vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[0]![1]!; state().retentionForm.batchSize = 99; expect(signal.aborted).toBe(true); expect(state().retentionReceipt?.outcome).toBe('unconfirmed'); pending.resolve({ ...settings, version: 8 }); await saving; expect(state().retentionSettings).toBeNull();
  });
  it.each([0, 302, 408, 503])('保存%s记录未知结果，成功重读前不允许继续写', async status => {
    await setup(); await tab('retention'); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockRejectedValueOnce({ status, code: 'policy.unknown', title: 'unknown' }); await state().saveRetentionSettings(); expect(state().retentionReceipt?.outcome).toBe('unconfirmed'); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce();
    await tab('statistics'); expect(wrapper.find('[data-testid="document-retention-receipt"]').exists()).toBe(true); await tab('retention'); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledTimes(2);
  });
  it('明确409保存拒绝与未知结果区分，仍须重读策略', async () => {
    await setup(); await tab('retention'); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockRejectedValueOnce({ status: 409, code: 'policy.conflict', title: 'conflict' }); await state().saveRetentionSettings(); expect(state().retentionReceipt?.outcome).toBe('failed'); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce();
  });
  it('离开策略页取消保存仍保留未知回执，旧回包不写新页签', async () => {
    await setup(); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockReturnValueOnce(pending.promise); const saving = state().saveRetentionSettings(); await flushPromises(); await tab('statistics'); expect(state().retentionReceipt?.outcome).toBe('unconfirmed'); pending.resolve({ ...settings, version: 8 }); await saving; expect(state().activeTab).toBe('statistics'); expect(state().retentionSettings).toBeNull();
  });
  it('成功保存确认版本不能被随后较低版本重读覆盖', async () => {
    await setup(); await tab('retention'); await state().saveRetentionSettings(); expect(state().retentionReceipt).toMatchObject({ outcome: 'succeeded', version: 8 }); await state().loadRetentionSettings(); expect(state().retentionSettings?.version).toBe(8); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce();
  });
  it('较低版本策略跨页签也不能重开保存', async () => {
    await setup(); await tab('retention'); await state().saveRetentionSettings(); await tab('statistics'); await tab('retention'); expect(state().retentionSettings).toBeNull(); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce();
  });
  it('同版本不同策略值不能覆盖已确认版本', async () => {
    await setup(); await tab('retention'); vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValueOnce({ ...settings, batchSize: 999 }); await state().loadRetentionSettings(); expect(state().retentionSettings).toEqual(settings); await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled();
  });
  it('错误保存响应不得标记成功', async () => {
    await setup(); await tab('retention'); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockResolvedValueOnce({ ...settings, version: 8, batchSize: 999 }); await state().saveRetentionSettings(); expect(state().retentionReceipt?.outcome).toBe('unconfirmed'); expect(state().retentionSettings).toBeNull();
  });
  it('原保存finally不释放重读后新保存的忙状态', async () => {
    await setup(); await tab('retention'); const first = deferred<typeof settings>(), second = deferred<typeof settings>(); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise); const old = state().saveRetentionSettings(); await flushPromises(); await tab('statistics'); await tab('retention'); const fresh = state().saveRetentionSettings(); first.resolve({ ...settings, version: 8 }); await old; await state().saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledTimes(2); second.resolve({ ...settings, version: 8 }); await fresh;
  });
  it('会话切换清除策略回执和版本底线，旧回调无效', async () => {
    const { session } = await setup(); await tab('retention'); await state().saveRetentionSettings(); const old = button('document-version-retention-save').vm.$.vnode.props!.onClick as () => void; session.currentUser!.sessionId = otherShareId; await flushPromises(); expect(state().retentionReceipt).toBeUndefined(); old(); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce(); expect(state().retentionSettings?.version).toBe(7);
  });
  it('路由离开取消读取，返回读取当前页签一次', async () => {
    const { route } = await setup(); await tab('accessLogs'); const pending = deferred<Awaited<ReturnType<typeof logs.listDocumentAccessLogs>>>(); vi.mocked(logs.listDocumentAccessLogs).mockReturnValueOnce(pending.promise); const reading = state().loadAccessLogs(), signal = vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)![3]!; route.name = 'other'; expect(signal.aborted).toBe(true); pending.resolve({ items: [log], page: 1, pageSize: 20, total: 1 }); await reading; expect(state().accessLogs).toEqual([]); route.name = 'document-statistics'; await flushPromises(); expect(logs.listDocumentAccessLogs).toHaveBeenCalledTimes(3);
  });
});
