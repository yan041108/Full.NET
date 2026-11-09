import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import type { ReportingExportTaskDetail } from '@fullnet/client-contracts';
import View from './ReportingExportTasksView.vue';
import * as api from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask } from '../test/data-output-fixtures';
vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-publications', () => ({ listReportingPublishedDefinitions: vi.fn() }));
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298'; let wrapper: VueWrapper;
async function setup(permissions = ['reporting.export_tasks.read']) {
  const context = createOutputSession(permissions), route = reactive({ name: 'reporting-export-tasks', query: {} });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context;
}
async function lookup(value: string) { await wrapper.get('[data-testid="reporting-export-exact-id"]').setValue(value); await wrapper.get('form.reporting-task-lookup').trigger('submit'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask });
  vi.mocked(api.listReportingExportTasks).mockImplementation(async (query = {}) => ({ items: [], page: query.page ?? 1, pageSize: query.pageSize ?? 20, total: 40 })); });
afterEach(() => wrapper?.unmount());
it('空列表只读按 ID 定位，去空格/规范大小写，不下载或写入', async () => {
  await setup(); await lookup(` ${exportTask.id.toUpperCase()} `);
  expect(api.getReportingExportTask).toHaveBeenCalledExactlyOnceWith(exportTask.id, expect.any(AbortSignal));
  expect(wrapper.get('[data-testid="reporting-export-detail-id"]').text()).toBe(exportTask.id);
  expect(wrapper.get('[data-testid="reporting-export-requester"]').text()).toBe(exportTask.requestedByUserId);
  expect(wrapper.find('[data-testid="reporting-export-result-availability"]').exists()).toBe(false);
  expect(api.downloadReportingExportTask).not.toHaveBeenCalled(); expect(api.createReportingExportTask).not.toHaveBeenCalled(); expect(api.cancelReportingExportTask).not.toHaveBeenCalled();
});
it('本人成功结果在详情独立显示待核对，读取详情不会下载', async () => {
  vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask, statusKey: 'succeeded' });
  await setup(['reporting.export_tasks.read', 'reporting.export_tasks.download']);
  await lookup(exportTask.id);
  expect(wrapper.get('[data-testid="reporting-export-result-availability"]').text()).toContain('待核对');
  expect(api.downloadReportingExportTask).not.toHaveBeenCalled();
});
it.each(['bad', '00000000-0000-0000-0000-000000000000'])('非法 ID %s 不读取、不触发列表回退', async value => {
  await setup(); await lookup(value); expect(api.getReportingExportTask).not.toHaveBeenCalled(); expect(api.listReportingExportTasks).toHaveBeenCalledTimes(1);
  expect(wrapper.text()).toContain('请输入非零导出任务 UUID');
});
it('当前企业中可见的他人任务显示发起人，但写操作继续受本人资格限制', async () => {
  vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask, requestedByUserId: otherId, statusKey: 'failed' });
  await setup(['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.published_definitions.read', 'reporting.export_tasks.download', 'reporting.export_tasks.cancel']);
  await lookup(exportTask.id); expect(wrapper.get('[data-testid="reporting-export-requester"]').text()).toBe(otherId);
  expect(wrapper.find('[data-testid="reporting-export-regenerate"]').exists()).toBe(false); expect(wrapper.find('[data-testid="reporting-export-detail-cancel"]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="reporting-export-detail-download"]').exists()).toBe(false);
});
it('精确读取未找到保留目标 ID，不借其他列表行展示', async () => {
  vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [exportTask], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getReportingExportTask).mockRejectedValue({ type: 'about:blank', status: 404, code: 'reporting.export_task.not_found', title: 'not found' });
  await setup(); await lookup(otherId); expect(wrapper.get('[data-testid="reporting-export-detail-id"]').text()).toBe(otherId);
  expect(wrapper.find('[data-testid="reporting-export-requester"]').exists()).toBe(false); expect(wrapper.get('[data-testid="reporting-export-detail-problem"]').text()).toContain('not found');
});
it('新 ID 取消旧详情，迟到响应不替换新目标', async () => {
  const pending = deferred<ReportingExportTaskDetail>(); vi.mocked(api.getReportingExportTask).mockReturnValueOnce(pending.promise); await setup(); await lookup(exportTask.id);
  const signal = vi.mocked(api.getReportingExportTask).mock.lastCall![1]!; vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...exportTask, id: otherId, definitionName: 'New target' });
  await lookup(otherId); expect(signal.aborted).toBe(true); pending.resolve(exportTask); await flushPromises(); expect(wrapper.get('[data-testid="reporting-export-detail-id"]').text()).toBe(otherId);
  expect(wrapper.text()).toContain('New target');
});
it('会话变化取消并清除 ID 草稿，迟到旧数据不回写', async () => {
  const pending = deferred<ReportingExportTaskDetail>(); vi.mocked(api.getReportingExportTask).mockReturnValue(pending.promise); const { session } = await setup(); await lookup(exportTask.id);
  const signal = vi.mocked(api.getReportingExportTask).mock.lastCall![1]!; session.currentUser!.sessionId = otherId; await flushPromises(); expect(signal.aborted).toBe(true);
  expect((wrapper.get('[data-testid="reporting-export-exact-id"]').element as HTMLInputElement).value).toBe(''); pending.resolve(exportTask); await flushPromises();
  expect(wrapper.find('[data-testid="reporting-export-requester"]').exists()).toBe(false);
});
it('列表换页保持精确详情目标和只读快照', async () => {
  await setup(); await lookup(exportTask.id); wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
  expect(vi.mocked(api.listReportingExportTasks).mock.lastCall![0]?.page).toBe(2); expect(wrapper.get('[data-testid="reporting-export-detail-id"]').text()).toBe(exportTask.id);
  expect(api.getReportingExportTask).toHaveBeenCalledTimes(1);
});
