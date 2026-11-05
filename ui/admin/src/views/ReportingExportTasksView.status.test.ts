import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue'; import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'; import { ElPagination } from 'element-plus';
import type { ReportingExportTaskDetail, ReportingExportTaskPage } from '@fullnet/client-contracts';
import View from './ReportingExportTasksView.vue'; import * as api from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask } from '../test/data-output-fixtures';
vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-publications', () => ({ listReportingPublishedDefinitions: vi.fn() }));
const queued = { ...exportTask, statusKey: 'queued', outputFileId: null, outputFileName: null, completedAtUtc: null, rowCount: 0 };
const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf298'; let wrapper: VueWrapper;
interface State { statusDraft: string; appliedStatus?: string; page: number; total: number; items: ReportingExportTaskDetail[]; selectedTask?: ReportingExportTaskDetail; receipt?: { outcome: string };
  searchStatus(): void; resetStatus(): void; load(): Promise<boolean>; openDetail(id: string): Promise<void>; prepareRegeneration(task: ReportingExportTaskDetail): Promise<void>;
  submitCreate(): Promise<void>; requestCancellation(task: ReportingExportTaskDetail): void; confirmCancellation(): Promise<void> }
const state = () => wrapper.vm as unknown as State;
async function setup(writable = false) {
  const context = createOutputSession(['reporting.export_tasks.read', ...(writable ? ['reporting.export_tasks.create', 'reporting.published_definitions.read', 'reporting.export_tasks.cancel'] : [])]);
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: reactive({ name: 'reporting-export-tasks', query: {} }) }, directives: { loading: () => {} }, stubs: {
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return context;
}
async function filter(status: string) { state().statusDraft = status; state().searchStatus(); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.listReportingExportTasks).mockImplementation(async (query = {}) => ({
  items: [{ ...queued, statusKey: query.statusKey ?? 'queued' }], page: query.page ?? 1, pageSize: query.pageSize ?? 20, total: 40 }));
  vi.mocked(api.getReportingExportTask).mockResolvedValue(queued);
});
afterEach(() => wrapper?.unmount());
describe('报表导出服务端状态筛选', () => {
  it('只读角色提交后生效，草稿不改变分页/刷新条件，重置回全部第一页', async () => {
    await setup(); await filter('failed'); expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]).toEqual({ page: 1, pageSize: 20, statusKey: 'failed' });
    state().statusDraft = 'canceled'; wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
    expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]).toEqual({ page: 2, pageSize: 20, statusKey: 'failed' });
    await state().load(); expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]?.statusKey).toBe('failed');
    state().resetStatus(); await flushPromises(); expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]).toEqual({ page: 1, pageSize: 20, statusKey: undefined });
  });
  it('未知状态不请求，刷新也不回退到全部', async () => {
    await setup(); await filter('unknown'); await state().load(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(1); expect(state().items).toEqual([]);
    expect(wrapper.text()).toContain('请选择已支持的导出任务状态');
  });
  it('来源状态不符拒绝整页，不在浏览器筛成成功结果', async () => {
    await setup(); vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [{ ...queued, statusKey: 'succeeded' }], page: 1, pageSize: 20, total: 1 });
    await filter('failed'); expect(state().items).toEqual([]); expect(state().total).toBe(0); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(2);
  });
  it('读取中保留总数避免第二页归零，失败不自动读取全部或首页', async () => {
    await setup(); await filter('failed'); const pending = deferred<ReportingExportTaskPage>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(pending.promise);
    wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); expect(state().page).toBe(2); expect(state().total).toBe(40);
    pending.reject(new Error('unavailable')); await flushPromises(); expect(state().total).toBe(0); expect(state().page).toBe(2); expect(state().appliedStatus).toBe('failed');
    expect(api.listReportingExportTasks).toHaveBeenCalledTimes(3);
  });
  it('新筛选取消旧响应，迟到数据不能覆盖', async () => {
    await setup(); const pending = deferred<ReportingExportTaskPage>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(pending.promise); await filter('failed');
    const signal = vi.mocked(api.listReportingExportTasks).mock.lastCall![1]!; await filter('canceled'); expect(signal.aborted).toBe(true);
    pending.resolve({ items: [{ ...queued, statusKey: 'failed', definitionName: 'late' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(state().items[0]?.statusKey).toBe('canceled'); expect(wrapper.text()).not.toContain('late');
  });
  it('会话变化清除筛选，不把旧状态自动发送给新会话', async () => {
    const { session } = await setup(); await filter('failed'); session.currentUser!.sessionId = nextId; await flushPromises();
    expect(state().statusDraft).toBe('all'); expect(state().appliedStatus).toBeUndefined(); expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]?.statusKey).toBeUndefined();
  });
  it('筛选不替换独立读取的其他状态详情', async () => {
    await setup(); vi.mocked(api.getReportingExportTask).mockResolvedValue(exportTask); await state().openDetail(exportTask.id); await filter('failed');
    expect(state().selectedTask?.statusKey).toBe('succeeded'); expect(wrapper.get('[data-testid="reporting-export-detail-id"]').text()).toBe(exportTask.id);
  });
  it('失败筛选下新排队回执只进独立详情，不混入列表', async () => {
    await setup(true); await filter('failed'); const failed = { ...queued, statusKey: 'failed' }; vi.mocked(api.getReportingExportTask).mockResolvedValue(failed);
    await state().openDetail(failed.id); await state().prepareRegeneration(failed); vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...queued, id: nextId });
    const pending = deferred<ReportingExportTaskPage>(); vi.mocked(api.listReportingExportTasks).mockReturnValueOnce(pending.promise);
    const writing = state().submitCreate(); await flushPromises(); expect(state().selectedTask?.id).toBe(nextId); expect(state().items.every(row => row.statusKey === 'failed')).toBe(true);
    pending.resolve({ items: [failed], page: 1, pageSize: 20, total: 1 }); await writing; expect(state().items.some(row => row.id === nextId)).toBe(false);
  });
  it('排队筛选取消后移出原行，迟到排队页不能恢复或混入已取消行', async () => {
    await setup(true); await filter('queued'); await state().openDetail(queued.id); vi.mocked(api.cancelReportingExportTask).mockResolvedValue({ ...queued, statusKey: 'canceled', version: 2 });
    state().requestCancellation(queued); await flushPromises(); await state().confirmCancellation();
    expect(api.cancelReportingExportTask).toHaveBeenCalledTimes(1); expect(state().items).toEqual([]); expect(state().selectedTask?.statusKey).toBe('canceled'); expect(state().receipt?.outcome).toBe('succeeded');
  });
});
