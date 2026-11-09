import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewCapabilities: vi.fn(), retryDocumentPreviewTask: vi.fn(), getDocumentPreviewTask: vi.fn(), listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
let wrapper: VueWrapper;
async function setup(permissions = ['document.host_preview_tasks.read']) {
  const context = createShareSession(permissions);
  wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context;
}
async function lookup(value: string) { await wrapper.get('[data-testid="document-preview-exact-id"]').setValue(value); await wrapper.get('form.document-preview-lookup').trigger('submit'); await flushPromises(); }
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getDocumentPreviewCapabilities).mockResolvedValue({ enabled: true, providerKey: 'fixture', allowedMimeTypes: [], maxInputBytes: 1024, maxAttempts: 3 });
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, versionId: otherId });
  vi.mocked(api.listDocumentPreviewTasks).mockImplementation(async (query = {}) => ({ items: [], page: query.page ?? 1, pageSize: query.pageSize ?? 20, total: 40 }));
});
afterEach(() => wrapper?.unmount());

it('只读 Host 在空页外按 ID 读取并核对完整文件身份，不打开或写入', async () => {
  await setup(); await lookup(` ${task.id.toUpperCase()} `);
  expect(api.getDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith(task.id, expect.any(AbortSignal));
  for (const [field, expected] of Object.entries({ id: task.id, 'version-id': otherId, 'source-file': task.sourceFileId, 'output-file': task.outputFileId, requester: task.requestedByUserId })) {
    expect(wrapper.get(`[data-testid="document-preview-${field === 'id' ? 'detail-id' : field}"]`).text()).toBe(expected);
  }
  expect(api.openDocumentPreviewTaskContent).not.toHaveBeenCalled(); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled(); expect(api.retryDocumentPreviewTask).not.toHaveBeenCalled(); expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
  expect(wrapper.find('[data-testid="document-preview-detail-cancel"]').exists()).toBe(false);
  const availability = wrapper.get('[data-testid="document-preview-result-availability"]');
  expect(availability.text()).toContain('待核对');
  vi.mocked(api.openDocumentPreviewTaskContent).mockResolvedValue(undefined);
  await wrapper.get('[data-testid="document-preview-detail-open-pdf"]').trigger('click');
  await flushPromises();
  expect(availability.text()).toBe('最近一次读取成功。');
});
it.each(['bad', '', '00000000-0000-0000-0000-000000000000'])('非法 ID %j 不读取、不触发列表回退', async value => {
  await setup(); await lookup(value); expect(api.getDocumentPreviewTask).not.toHaveBeenCalled(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(1);
  expect(wrapper.get('[data-testid="document-preview-lookup-problem"]').text()).toContain('请输入非零预览任务 UUID');
});
it('未找到保留目标 ID，刷新不借其他任务展示', async () => {
  vi.mocked(api.getDocumentPreviewTask).mockRejectedValue({ status: 404, code: 'document.host_preview_task.not_found', title: '未找到' });
  await setup(); await lookup(otherId); expect(wrapper.get('[data-testid="document-preview-detail-id"]').text()).toBe(otherId);
  expect(wrapper.find('[data-testid="document-preview-source-file"]').exists()).toBe(false);
  await wrapper.get('[data-testid="document-preview-detail-refresh"]').trigger('click'); await flushPromises();
  expect(api.getDocumentPreviewTask).toHaveBeenLastCalledWith(otherId, expect.any(AbortSignal));
});
it('替换精确目标取消旧读取，迟到旧内容不能覆盖', async () => {
  const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise);
  await setup(); await lookup(task.id); const signal = vi.mocked(api.getDocumentPreviewTask).mock.calls[0]![1]!;
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...task, id: otherId, documentTitle: '新目标' }); await lookup(otherId);
  pending.resolve({ ...task, documentTitle: '旧内容' }); await flushPromises();
  expect(signal.aborted).toBe(true); expect(wrapper.text()).toContain('新目标'); expect(wrapper.text()).not.toContain('旧内容');
});
it.each(['session', 'account', 'tenant', 'permissions'] as const)('%s 变化清输入并取消详情，旧响应不重开', async change => {
  const pending = deferred<typeof task>(); vi.mocked(api.getDocumentPreviewTask).mockReturnValueOnce(pending.promise);
  const { session } = await setup(); await lookup(task.id); const signal = vi.mocked(api.getDocumentPreviewTask).mock.calls[0]![1]!;
  const staleSubmit = (wrapper.findComponent(View).vm as unknown as { lookupControls: { submit: () => void } }).lookupControls.submit;
  if (change === 'session') session.currentUser!.sessionId = otherId;
  if (change === 'account') session.currentUser!.id = otherId;
  if (change === 'tenant') Object.assign(session.currentUser!, { tenantId: otherId, scope: `tenant:${otherId.replaceAll('-', '')}`, actorScope: `tenant:${otherId.replaceAll('-', '')}` });
  if (change === 'permissions') session.currentUser!.permissions = [];
  await flushPromises(); staleSubmit(); pending.resolve({ ...task, documentTitle: '失效内容' }); await flushPromises();
  expect(signal.aborted).toBe(true); expect(wrapper.find('[data-testid="document-preview-detail-id"]').exists()).toBe(false); expect(wrapper.text()).not.toContain('失效内容');
  if (change === 'session' || change === 'account') expect((wrapper.get('[data-testid="document-preview-exact-id"]').element as HTMLInputElement).value).toBe('');
});
it('分页和文档筛选草稿不替换精确目标，空输入在新会话中仍可操作', async () => {
  const { session } = await setup(); session.currentUser!.sessionId = otherId; await flushPromises();
  await lookup(task.id); wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
  expect(api.listDocumentPreviewTasks).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, documentItemId: undefined, statusKey: undefined }, expect.any(AbortSignal));
  expect(wrapper.get('[data-testid="document-preview-detail-id"]').text()).toBe(task.id); expect(api.getDocumentPreviewTask).toHaveBeenCalledTimes(1);
});
it.each(['documentItemId', 'sourceFileId', 'versionId', 'requestedByUserId', 'version'] as const)('拒绝原行 %s 漂移或任务版本倒退', async field => {
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [{ ...task, versionId: task.id, version: 4 }], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, versionId: task.id, version: 4, [field]: field === 'version' ? 3 : otherId, documentTitle: '错误来源' });
  await setup(); await lookup(task.id); expect(wrapper.text()).not.toContain('错误来源'); expect(wrapper.find('[data-testid="document-preview-source-file"]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="document-preview-detail-problem"]').exists()).toBe(true);
});
it('当前文件和空结果身份明确显示，刷新失败标记保留快照', async () => {
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValueOnce({ ...task, versionId: null, outputFileId: null, requestedByUserId: null });
  await setup(); await lookup(task.id); expect(wrapper.get('[data-testid="document-preview-version-id"]').text()).toContain('创建时的当前文件');
  expect(wrapper.get('[data-testid="document-preview-output-file"]').text()).toBe('未提供'); expect(wrapper.get('[data-testid="document-preview-requester"]').text()).toBe('未提供');
  vi.mocked(api.getDocumentPreviewTask).mockRejectedValueOnce({ status: 503, title: '读取失败' });
  await wrapper.get('[data-testid="document-preview-detail-refresh"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="document-preview-stale-snapshot"]').text()).toContain('上次读取的快照');
  expect(wrapper.get('[data-testid="document-preview-detail-id"]').text()).toBe(task.id);
});
