import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { ElButton, ElInputNumber, ElTabs, ElPagination } from 'element-plus';
import View from './DocumentStatisticsView.vue';
import * as stats from '../api/document-statistics';
import * as logs from '../api/document-access-logs';
import * as retention from '../api/document-version-retention';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';

vi.mock('../api/document-statistics', () => ({ getDocumentStatistics: vi.fn() }));
vi.mock('../api/document-access-logs', () => ({ listDocumentAccessLogs: vi.fn() }));
vi.mock('../api/document-version-retention', async original => ({ ...await original<typeof retention>(), getDocumentVersionRetentionSettings: vi.fn(), updateDocumentVersionRetentionSettings: vi.fn() }));
const parent = 'document.host_statistics.read', logRead = 'document.host_access_logs.read', read = 'document.host_documents.read', write = 'document.host_documents.update';
const all = [parent, logRead, read, write];
const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50 };
const statistics = { summary: { totalItems: 3, totalVersions: 5, totalSizeKb: 1024, totalSizeInfo: '原统计快照' }, byType: [], byCategory: [], shareCount: 1, todayAccessCount: 0, todayDownloadCount: 0, todayCreatedCount: 1, recycleBinCount: 0 };
const page = { items: [], page: 1, pageSize: 20, total: 100 };
let wrapper: VueWrapper | undefined, view: Omit<VueWrapper, 'exists'>;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(stats.getDocumentStatistics).mockResolvedValue(statistics); vi.mocked(logs.listDocumentAccessLogs).mockResolvedValue(page); vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValue(settings); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockResolvedValue(settings); });
afterEach(async () => { wrapper?.unmount(); wrapper = undefined; await flushPromises(); document.body.replaceChildren(); vi.restoreAllMocks(); });
async function setup(permissions = all, host = true, keepAlive = false) {
  const context = createShareSession(permissions); if (!host) context.session.currentUser!.scope = 'tenant:other';
  const visible = ref(true), Other = defineComponent({ setup: () => () => h('span') });
  wrapper = mount(keepAlive ? defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => h(visible.value ? View : Other) }) }) : View, { global: { plugins: [context.pinia], directives: { loading: () => {} } } });
  view = keepAlive ? wrapper.getComponent(View) : wrapper;
  await flushPromises(); return { ...context, visible };
}
async function tab(name: string) { view.getComponent(ElTabs).vm.$emit('update:modelValue', name); await flushPromises(); }
function save() { return view.get('[data-testid="document-version-retention-save"]').trigger('click'); }
function refresh() { return view.get('[data-testid="document-statistics-refresh"]').trigger('click'); }
function filter() { return view.findAllComponents(ElButton).find(button => button.text() === '筛选')!.trigger('click'); }

describe('文档统计、日志与版本策略归属', () => {
  it.each([{ permissions: [] }, { permissions: [logRead, read, write] }])('缺少页面read不读取也不显示 %j', async ({ permissions }) => { await setup(permissions); expect(stats.getDocumentStatistics).not.toHaveBeenCalled(); expect(view.find('[data-testid="document-statistics-tabs"]').exists()).toBe(false); });
  it('租户上下文不读取Host统计', async () => { await setup(all, false); expect(stats.getDocumentStatistics).not.toHaveBeenCalled(); });
  it('子页签独立read，直接切换不能读取无权内容', async () => { await setup([parent, write]); await tab('accessLogs'); await tab('retention'); expect(logs.listDocumentAccessLogs).not.toHaveBeenCalled(); expect(retention.getDocumentVersionRetentionSettings).not.toHaveBeenCalled(); expect(view.find('[data-testid="document-version-retention-panel"]').exists()).toBe(false); });
  it.each(['resolve', 'reject'] as const)('撤权取消统计，迟到%s不回填', async outcome => {
    const pending = deferred<typeof statistics>(); vi.mocked(stats.getDocumentStatistics).mockReturnValue(pending.promise); const { session } = await setup();
    const signal = vi.mocked(stats.getDocumentStatistics).mock.calls[0]?.[0]; session.currentUser!.permissions = [];
    expect(signal?.aborted).toBe(true); if (outcome === 'resolve') pending.resolve(statistics); else pending.reject(new Error('late'));
    await flushPromises(); expect(view.text()).not.toContain('原统计快照'); expect(view.find('.el-alert--error').exists()).toBe(false);
  });
  it('替换统计读取取消旧请求，旧finally不结束新loading', async () => {
    await setup(); const old = deferred<typeof statistics>(), latest = deferred<typeof statistics>(); vi.mocked(stats.getDocumentStatistics).mockReturnValueOnce(old.promise).mockReturnValueOnce(latest.promise);
    await refresh(); const signal = vi.mocked(stats.getDocumentStatistics).mock.calls.at(-1)?.[0]; await refresh(); expect(signal?.aborted).toBe(true);
    old.resolve(statistics); await flushPromises(); expect(view.text()).not.toContain('原统计快照'); expect((view.vm as unknown as { statisticsLoading: boolean }).statisticsLoading).toBe(true); latest.resolve({ ...statistics, summary: { ...statistics.summary, totalSizeInfo: '新统计' } }); await flushPromises(); expect(view.text()).toContain('新统计');
  });
  it.each(['resolve', 'reject'] as const)('日志换页签取消，迟到%s不恢复分页或错误', async outcome => {
    await setup(); const pending = deferred<typeof page>(); vi.mocked(logs.listDocumentAccessLogs).mockReturnValue(pending.promise); await tab('accessLogs'); const signal = vi.mocked(logs.listDocumentAccessLogs).mock.calls[0]?.[3]; await tab('statistics'); expect(signal?.aborted).toBe(true);
    if (outcome === 'resolve') pending.resolve(page); else pending.reject(new Error('late')); await flushPromises(); expect(view.findComponent(ElPagination).exists()).toBe(false); expect(view.find('.el-alert--error').exists()).toBe(false);
  });
  it('日志替换和失败清空旧total', async () => {
    await setup(); await tab('accessLogs'); expect(view.getComponent(ElPagination).props('total')).toBe(100);
    const old = deferred<typeof page>(); vi.mocked(logs.listDocumentAccessLogs).mockReturnValueOnce(old.promise).mockRejectedValueOnce(new Error('latest failed'));
    await filter(); const signal = vi.mocked(logs.listDocumentAccessLogs).mock.calls.at(-1)?.[3]; await filter(); expect(signal?.aborted).toBe(true); await flushPromises(); old.resolve(page); await flushPromises();
    expect(view.findComponent(ElPagination).exists()).toBe(false); expect(view.find('.el-alert--error').exists()).toBe(true);
  });
  it('成功读取前不能保存；失败后可重读', async () => {
    await setup(); vi.mocked(retention.getDocumentVersionRetentionSettings).mockRejectedValueOnce(new Error('read failed'));
    await tab('retention'); expect(view.find('[data-testid="document-version-retention-save"]').exists()).toBe(false);
    await refresh(); await flushPromises(); expect(view.get('[data-testid="document-version-retention-save"]').attributes('disabled')).toBeUndefined();
  });
  it('只读策略无保存入口', async () => { await setup([parent, read]); await tab('retention'); expect(view.find('[data-testid="document-version-retention-save"]').exists()).toBe(false); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled(); });
  it('未成功读取直接调用保存也不写入', async () => { await setup(); const pending = deferred<typeof settings>(); vi.mocked(retention.getDocumentVersionRetentionSettings).mockReturnValue(pending.promise); await tab('retention'); void (view.vm as unknown as { saveRetentionSettings(): Promise<void> }).saveRetentionSettings(); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled(); pending.resolve(settings); });
  it('保存冻结参数、防重复并锁定编辑', async () => {
    await setup(); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockReturnValue(pending.promise);
    await save(); await save(); view.findAllComponents(ElInputNumber)[0]!.vm.$emit('update:modelValue', 7);
    expect(vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[0]?.[0]).toEqual(settings); expect(retention.updateDocumentVersionRetentionSettings).toHaveBeenCalledOnce(); expect(view.findAllComponents(ElInputNumber)[0]!.get('input').attributes('disabled')).toBeDefined(); pending.resolve(settings); await flushPromises();
  });
  it.each(['resolve', 'reject'] as const)('离开策略取消保存，迟到%s不反馈或回填', async outcome => {
    await setup(); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockReturnValue(pending.promise); const success = vi.spyOn(feedback, 'showSuccess'); await save(); const signal = vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[0]?.[1]; await tab('statistics'); expect(signal?.aborted).toBe(true);
    if (outcome === 'resolve') pending.resolve(settings); else pending.reject(new Error('late')); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(view.find('.el-alert--error').exists()).toBe(false); await tab('retention'); expect(retention.getDocumentVersionRetentionSettings).toHaveBeenCalledTimes(2);
  });
  it('失败保存必须重读新策略后继续，旧表单不能重试', async () => {
    await setup(); await tab('retention'); vi.mocked(retention.updateDocumentVersionRetentionSettings).mockRejectedValueOnce(new Error('response lost')); await save(); await flushPromises(); expect(view.find('[data-testid="document-version-retention-save"]').exists()).toBe(false);
    const latest = { ...settings, batchSize: 99 }; vi.mocked(retention.getDocumentVersionRetentionSettings).mockResolvedValue(latest); await refresh(); await flushPromises(); await save(); expect(vi.mocked(retention.updateDocumentVersionRetentionSettings).mock.calls[1]?.[0]).toEqual(latest);
  });
  it.each([undefined, NaN, 1.5, 0, 1001])('无效最低保留数%s不提交', async value => { await setup(); await tab('retention'); view.findAllComponents(ElInputNumber)[0]!.vm.$emit('update:modelValue', value); await save(); expect(retention.updateDocumentVersionRetentionSettings).not.toHaveBeenCalled(); });
  it.each(['sessionId', 'tenantId', 'id', 'scope', 'permissions'] as const)('%s改变取消策略读取并清空旧表单', async field => {
    const { session: current } = await setup(); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.getDocumentVersionRetentionSettings).mockReturnValueOnce(pending.promise).mockResolvedValue(settings); await refresh(); const signal = vi.mocked(retention.getDocumentVersionRetentionSettings).mock.calls.at(-1)?.[0];
    if (field === 'permissions') current.currentUser!.permissions = [parent]; else current.currentUser![field] = 'changed';
    expect(signal?.aborted).toBe(true); pending.resolve({ ...settings, batchSize: 888 }); await flushPromises(); expect(view.findAllComponents(ElInputNumber).map(input => input.props('modelValue'))).not.toContain(888);
  });
  it('KeepAlive失活取消，重入重新读取一次', async () => { const { visible } = await setup(all, true, true); await tab('retention'); const pending = deferred<typeof settings>(); vi.mocked(retention.getDocumentVersionRetentionSettings).mockReturnValueOnce(pending.promise).mockResolvedValue(settings); await refresh(); const signal = vi.mocked(retention.getDocumentVersionRetentionSettings).mock.calls.at(-1)?.[0]; visible.value = false; await flushPromises(); expect(signal?.aborted).toBe(true); pending.resolve({ ...settings, batchSize: 888 }); await flushPromises(); visible.value = true; await flushPromises(); expect(retention.getDocumentVersionRetentionSettings).toHaveBeenCalledTimes(3); expect(wrapper!.getComponent(View).findAllComponents(ElInputNumber)[3]!.props('modelValue')).toBe(50); });
});
