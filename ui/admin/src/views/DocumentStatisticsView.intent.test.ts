import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElInput, ElPagination, ElSelect, ElTabs } from 'element-plus';
import View from './DocumentStatisticsView.vue';
import * as statistics from '../api/document-statistics';
import * as logs from '../api/document-access-logs';
import * as retention from '../api/document-version-retention';
import { createShareSession, shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';

vi.mock('../api/document-statistics', () => ({ getDocumentStatistics: vi.fn() }));
vi.mock('../api/document-access-logs', () => ({ listDocumentAccessLogs: vi.fn() }));
vi.mock('../api/document-version-retention', async original => ({ ...await original<typeof retention>(), getDocumentVersionRetentionSettings: vi.fn(), updateDocumentVersionRetentionSettings: vi.fn() }));
const parent = 'document.host_statistics.read', logRead = 'document.host_access_logs.read', read = 'document.host_documents.read', write = 'document.host_documents.update';
const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50, version: 7 };
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(statistics.getDocumentStatistics).mockResolvedValue({ summary: { totalItems: 0, totalVersions: 0, totalSizeKb: 0, totalSizeInfo: '0 B' }, byType: [], byCategory: [], shareCount: 0, todayAccessCount: 0, todayDownloadCount: 0, todayCreatedCount: 0, recycleBinCount: 0 });
  vi.mocked(logs.listDocumentAccessLogs).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 100 });
  vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValue(settings);
  vi.mocked(retention.updateDocumentVersionRetentionSettings).mockImplementation(async value => ({ ...value, version: value.version + 1 }));
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(malformedHost = false) {
  const context = createShareSession([parent, logRead, read, write]); if (malformedHost) context.session.currentUser!.tenantId = shareId;
  wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} } } }); await flushPromises(); return context;
}
async function tab(name: string) { wrapper!.getComponent(ElTabs).vm.$emit('update:modelValue', name); await flushPromises(); }
const refresh = async () => { await wrapper!.get('[data-testid="document-statistics-refresh"]').trigger('click'); await flushPromises(); };
const vm = () => wrapper!.vm as unknown as { applyAccessLogFilters(): void; resetAccessLogFilters(): void; accessLogFilters: { documentItemId: string; accessTypeKey: string; sourceKey: string }; retentionForm: typeof settings; saveRetentionSettings(): Promise<void> };
const apply = async () => { vm().applyAccessLogFilters(); await flushPromises(); };
const changePage = async () => { const pager = wrapper!.getComponent(ElPagination); pager.vm.$emit('update:currentPage', 2); pager.vm.$emit('current-change', 2); await flushPromises(); };

describe('文档统计日志查询意图', () => {
  it('Host标识携带租户时统计及子页签均不读取', async () => {
    await setup(true); expect(statistics.getDocumentStatistics).not.toHaveBeenCalled();
    expect(wrapper!.findComponent(ElTabs).exists()).toBe(false); expect(logs.listDocumentAccessLogs).not.toHaveBeenCalled(); expect(retention.getDocumentVersionRetentionSettings).not.toHaveBeenCalled();
  });
  it('修改未应用文档条件后翻页仍使用已应用条件', async () => {
    await setup(); await tab('accessLogs'); await wrapper!.getComponent(ElInput).setValue(shareId); await apply();
    await wrapper!.getComponent(ElInput).setValue(otherShareId); await changePage();
    expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.slice(0, 3)).toEqual([2, 20, { documentItemId: shareId }]);
  });
  it('未应用的访问类型与来源不影响刷新', async () => {
    await setup(); await tab('accessLogs'); const selects = wrapper!.findAllComponents(ElSelect);
    selects[0]!.vm.$emit('update:modelValue', 'download'); selects[1]!.vm.$emit('update:modelValue', 'share'); await refresh();
    expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({});
  });
  it('应用时修剪全部条件并冻结为同一次查询', async () => {
    await setup(); await tab('accessLogs'); Object.assign(vm().accessLogFilters, { documentItemId: ' ' + shareId + ' ', accessTypeKey: ' download ', sourceKey: ' share ' }); await apply();
    Object.assign(vm().accessLogFilters, { documentItemId: otherShareId, accessTypeKey: 'view', sourceKey: 'admin' }); await refresh();
    expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({ documentItemId: shareId, accessTypeKey: 'download', sourceKey: 'share' });
  });
  it('重置同时清除草稿与已应用条件并回第一页', async () => {
    await setup(); await tab('accessLogs'); vm().accessLogFilters.documentItemId = shareId; await apply(); await changePage();
    vm().resetAccessLogFilters(); await flushPromises(); await refresh();
    expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.slice(0, 3)).toEqual([1, 20, {}]); expect(wrapper!.getComponent(ElInput).props('modelValue')).toBe('');
  });
  it('查询失败后刷新仍重试已应用查询，不能带入新的草稿', async () => {
    await setup(); await tab('accessLogs'); vm().accessLogFilters.documentItemId = shareId;
    vi.mocked(logs.listDocumentAccessLogs).mockRejectedValueOnce(new Error('offline')); await apply();
    vm().accessLogFilters.documentItemId = otherShareId; await refresh(); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({ documentItemId: shareId });
  });
  it('换页签后恢复已应用日志条件而非未应用草稿', async () => {
    await setup(); await tab('accessLogs'); vm().accessLogFilters.documentItemId = shareId; await apply(); vm().accessLogFilters.documentItemId = otherShareId;
    await tab('statistics'); await tab('accessLogs'); expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({ documentItemId: shareId });
  });
  it('上下文变更清除草稿和已应用查询，旧日志不回填', async () => {
    const { session } = await setup(); await tab('accessLogs'); vm().accessLogFilters.documentItemId = shareId; await apply();
    const old = deferred<Awaited<ReturnType<typeof logs.listDocumentAccessLogs>>>(); vi.mocked(logs.listDocumentAccessLogs).mockReturnValueOnce(old.promise);
    void refresh(); const signal = vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[3]; session.currentUser!.sessionId = 'new-session'; await flushPromises();
    expect(signal?.aborted).toBe(true); old.resolve({ items: [], page: 9, pageSize: 20, total: 900 }); await flushPromises();
    expect(vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[2]).toEqual({}); expect(wrapper!.getComponent(ElPagination).props('total')).toBe(100);
  });
  it('版本策略写入使用权威读取版本而非表单中被改写的版本', async () => {
    await setup(); await tab('retention'); vm().retentionForm.version = 99; await vm().saveRetentionSettings(); await flushPromises();
    expect(vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[0]?.[0].version).toBe(7);
  });
});
