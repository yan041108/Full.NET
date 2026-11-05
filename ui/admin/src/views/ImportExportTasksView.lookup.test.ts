import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import View from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/import-export-tasks', () => ({ listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), cancelImportExportTask: vi.fn(), executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn(), listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
let wrapper: VueWrapper;
async function setup(permissions = ['import_export.import_tasks.read'], host = false) {
  const context = createOutputSession(permissions); if (host) Object.assign(context.session.currentUser!, { tenantId: null, scope: 'host', actorScope: 'host' });
  wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }, ImportTaskCreateDialog: true
  } } }); await flushPromises(); return context;
}
async function lookup(value: string) { await wrapper.get('[data-testid="import-task-exact-id"]').setValue(value); await wrapper.get('form.import-task-lookup').trigger('submit'); await flushPromises(); }
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.getImportExportTask).mockResolvedValue({ ...task });
  vi.mocked(api.listImportExportTasks).mockImplementation(async (page = 1, pageSize = 20) => ({ items: [], page, pageSize, total: 40 }));
});
afterEach(() => wrapper?.unmount());

it('只读当前企业在空页外读取规范 ID，显示原来源身份且不执行或下载', async () => {
  await setup(); await lookup(` ${task.id.toUpperCase()} `); expect(api.getImportExportTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  for (const [field, value] of Object.entries({ 'detail-id': task.id, tenant: task.tenantId, requester: task.requestedByUserId, 'source-file': task.sourceFileId, 'schema-key': task.schemaKey, 'worksheet-key': task.worksheetKey })) {
    expect(wrapper.get(`[data-testid="import-task-${field}"]`).text()).toBe(value);
  }
  expect(api.executeImportExportTask).not.toHaveBeenCalled(); expect(api.resumeImportExportTask).not.toHaveBeenCalled(); expect(api.retryImportExportTask).not.toHaveBeenCalled(); expect(api.cancelImportExportTask).not.toHaveBeenCalled(); expect(api.downloadImportExportTaskErrorReceipt).not.toHaveBeenCalled();
});
it.each(['bad', '', '00000000-0000-0000-0000-000000000000'])('非法 ID %j 不读取或回退列表', async value => {
  await setup(); await lookup(value); expect(api.getImportExportTask).not.toHaveBeenCalled(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(1); expect(wrapper.get('[data-testid="import-task-lookup-problem"]').text()).toContain('请输入非零导入任务 UUID');
});
it('404 保留请求目标供显式重读，不借其他任务内容展示', async () => {
  vi.mocked(api.getImportExportTask).mockRejectedValue({ status: 404, code: 'import_export.task.not_found', title: '未找到' });
  await setup(); await lookup(otherId); expect(wrapper.get('[data-testid="import-task-detail-id"]').text()).toBe(otherId); expect(wrapper.find('[data-testid="import-task-source-file"]').exists()).toBe(false);
  await wrapper.get('[data-testid="import-export-task-detail-refresh"]').trigger('click'); await flushPromises(); expect(api.getImportExportTask).toHaveBeenLastCalledWith(otherId, expect.any(AbortSignal));
});
it('替换目标取消旧读取，迟到内容不能覆盖新详情', async () => {
  const pending = deferred<typeof task>(); vi.mocked(api.getImportExportTask).mockReturnValueOnce(pending.promise); await setup(); await lookup(task.id); const signal = vi.mocked(api.getImportExportTask).mock.calls[0]![1]!;
  vi.mocked(api.getImportExportTask).mockResolvedValueOnce({ ...task, id: otherId, schemaDisplayName: '新目标' }); await lookup(otherId); pending.resolve({ ...task, schemaDisplayName: '旧内容' }); await flushPromises();
  expect(signal.aborted).toBe(true); expect(wrapper.text()).toContain('新目标'); expect(wrapper.text()).not.toContain('旧内容');
});
it.each(['session', 'account', 'tenant', 'permissions'] as const)('%s 变化清草稿并取消详情，旧响应不重开', async change => {
  const pending = deferred<typeof task>(); vi.mocked(api.getImportExportTask).mockReturnValueOnce(pending.promise); const { session } = await setup(); await lookup(task.id);
  const signal = vi.mocked(api.getImportExportTask).mock.calls[0]![1]!;
  const staleSubmit = (wrapper.vm as unknown as { lookupControls: { submit: () => void } }).lookupControls.submit;
  if (change === 'session') session.currentUser!.sessionId = otherId;
  if (change === 'account') session.currentUser!.id = otherId;
  if (change === 'tenant') Object.assign(session.currentUser!, { tenantId: otherId, scope: `tenant:${otherId.replaceAll('-', '')}`, actorScope: `tenant:${otherId.replaceAll('-', '')}` });
  if (change === 'permissions') session.currentUser!.permissions = [];
  await flushPromises(); staleSubmit(); pending.resolve({ ...task, schemaDisplayName: '失效内容' }); await flushPromises(); expect(signal.aborted).toBe(true); expect(wrapper.find('[data-testid="import-task-detail-id"]').exists()).toBe(false); expect(wrapper.text()).not.toContain('失效内容');
  if (change !== 'permissions') expect((wrapper.get('[data-testid="import-task-exact-id"]').element as HTMLInputElement).value).toBe('');
});
it('分页不替换精确目标，新会话中空输入回调仍可操作', async () => {
  const { session } = await setup(); session.currentUser!.sessionId = otherId; await flushPromises(); await lookup(task.id); wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
  expect(api.listImportExportTasks).toHaveBeenLastCalledWith(2, 20, undefined, expect.any(AbortSignal)); expect(wrapper.get('[data-testid="import-task-detail-id"]').text()).toBe(task.id); expect(api.getImportExportTask).toHaveBeenCalledTimes(1);
});
it('第二页读取中保留总数，失败保留目标页且显式刷新只重读原页', async () => {
  await setup(); const pending = deferred<Awaited<ReturnType<typeof api.listImportExportTasks>>>(); vi.mocked(api.listImportExportTasks).mockReturnValueOnce(pending.promise);
  wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); const state = wrapper.vm as unknown as { page: number; total: number };
  expect(state.page).toBe(2); expect(state.total).toBe(40); wrapper.getComponent(ElPagination).vm.$emit('change', 1, 20); expect(state.page).toBe(2);
  pending.reject({ status: 503, title: '读取失败' }); await flushPromises(); expect(state.page).toBe(2); expect(state.total).toBe(0);
  wrapper.getComponent(ElPagination).vm.$emit('change', 1, 20); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(2);
  await (wrapper.vm as unknown as { refreshResults(): void }).refreshResults(); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenLastCalledWith(2, 20, undefined, expect.any(AbortSignal));
});
it.each(['tenantId', 'sourceFileId', 'schemaKey', 'worksheetKey', 'requestedByUserId', 'version'] as const)('拒绝原行 %s 漂移或任务修订倒退', async field => {
  vi.mocked(api.listImportExportTasks).mockResolvedValue({ items: [{ ...task, version: 4 }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getImportExportTask).mockResolvedValue({ ...task, version: 4, [field]: field === 'version' ? 3 : otherId, schemaDisplayName: '错误来源' });
  await setup(); await lookup(task.id); expect(wrapper.text()).not.toContain('错误来源'); expect(wrapper.find('[data-testid="import-task-source-file"]').exists()).toBe(false); expect(wrapper.find('[data-testid="import-task-detail-problem"]').exists()).toBe(true);
});
it('来源已清理的历史仍可只读定位，刷新失败明确标记上次快照', async () => {
  vi.mocked(api.getImportExportTask).mockResolvedValueOnce({ ...task, sourceRetention: { expiresAtUtc: '2026-01-01T00:00:00Z', stateKey: 'released', releasedAtUtc: '2026-01-02T00:00:00Z' } });
  await setup(); await lookup(task.id); expect(wrapper.get('[data-testid="import-task-source-file"]').text()).toBe(task.sourceFileId);
  vi.mocked(api.getImportExportTask).mockRejectedValueOnce({ status: 503, title: '读取失败' }); await wrapper.get('[data-testid="import-export-task-detail-refresh"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="import-task-stale-snapshot"]').text()).toContain('上次读取的快照'); expect(wrapper.get('[data-testid="import-task-detail-id"]').text()).toBe(task.id);
});
it.each(['host', 'missing-read'])('%s 不提供定位入口或读取', async kind => {
  await setup(kind === 'missing-read' ? [] : ['import_export.import_tasks.read'], kind === 'host'); expect(wrapper.find('[data-testid="import-task-exact-id"]').exists()).toBe(false); expect(api.getImportExportTask).not.toHaveBeenCalled(); expect(api.listImportExportTasks).not.toHaveBeenCalled();
});
