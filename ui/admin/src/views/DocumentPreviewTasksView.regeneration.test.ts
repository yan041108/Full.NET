import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { documentPreviewTask } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewCapabilities: vi.fn(), listDocumentPreviewTasks: vi.fn(),
  getDocumentPreviewTask: vi.fn(), createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), retryDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const source = { ...documentPreviewTask, versionId: '01912345-6789-7abc-8def-0123456789ab', outputFileId: '01912345-6789-7abc-8def-0123456789ac' };
const created = { ...source, id: '01912345-6789-7abc-8def-0123456789ad', outputFileId: null, statusKey: 'pending' };
const read = 'document.host_preview_tasks.read', create = 'document.host_preview_tasks.create';
let wrapper: VueWrapper;
type State = { items: typeof source[]; editorOpen: boolean; editorForm: { documentItemId: string; versionId: string }; receipt?: { outcome: string }; selectedTask?: typeof created;
  retryTask: (row: typeof source) => Promise<void>; reviewRetry: (row: typeof source) => void; recoveryConfirmation?: { confirm: () => void };
  openPdf: (row: typeof source) => Promise<void>; openRegenerate: (row: typeof source) => void; submitCreate: () => Promise<void> };
const state = () => wrapper.vm as unknown as State;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.getDocumentPreviewCapabilities).mockResolvedValue({ enabled: true, allowedMimeTypes: [], maxInputBytes: 1024, maxAttempts: 3, providerKey: 'external_http' });
  vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [source], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getDocumentPreviewTask).mockImplementation(async id => id === created.id ? created : source);
  vi.mocked(api.createDocumentPreviewTask).mockResolvedValue(created);
  vi.mocked(api.openDocumentPreviewTaskContent).mockRejectedValue({ status: 404, code: 'files.file.not_found', title: 'Missing' });
});
afterEach(() => wrapper?.unmount());
async function setup(permissions = [read, create]) { const context = createShareSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: {} },
  stubs: { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } }); await flushPromises(); return context; }
it('结果明确缺失后固定原文档版本，确认才创建新任务并保留旧事实', async () => {
  await setup(); await state().openPdf(source); state().openRegenerate(source); await flushPromises();
  expect(state().editorForm).toEqual({ documentItemId: source.documentItemId, versionId: source.versionId });
  expect(wrapper.get('[data-testid="document-preview-regeneration-hint"]').text()).toContain('原任务及其状态保留');
  expect(wrapper.findAll('[data-testid="document-preview-task-editor-form"] input').every(input => input.attributes('disabled') !== undefined)).toBe(true);
  expect(api.createDocumentPreviewTask).not.toHaveBeenCalled(); await state().submitCreate();
  expect(api.createDocumentPreviewTask).toHaveBeenCalledExactlyOnceWith({ documentItemId: source.documentItemId, versionId: source.versionId }, expect.any(AbortSignal));
  expect(state().receipt?.outcome).toBe('succeeded'); expect(state().selectedTask?.id).toBe(created.id); expect(source.statusKey).toBe('succeeded');
});
it.each([{ status: 403, code: 'files.file.not_found' }, { status: 503, code: 'files.storage.unavailable' }, { status: 404, code: 'document.preview_task.not_found' }])('权限或未知故障不伪报结果过期 %j', async error => {
  vi.mocked(api.openDocumentPreviewTaskContent).mockRejectedValue(error); await setup(); await state().openPdf(source); state().openRegenerate(source);
  expect(state().editorOpen).toBe(false); expect(api.createDocumentPreviewTask).not.toHaveBeenCalled();
});
it('只有读取权限可以看到缺失提示，不能借结果缺失取得创建权限', async () => {
  await setup([read]); await state().openPdf(source); state().openRegenerate(source); expect(state().editorOpen).toBe(false);
});
it('旧任务快照和缺少原版本的历史任务不能打开固定版本再生成', async () => {
  await setup(); await state().openPdf(source); state().items = [{ ...source, version: 2 }]; state().openRegenerate(source); expect(state().editorOpen).toBe(false);
  state().items = [{ ...source, versionId: null } as unknown as typeof source]; state().openRegenerate(state().items[0]!); expect(state().editorOpen).toBe(false);
});
it('旧任务冒充创建结果时保留未知门槛，不接纳为新任务或自动重发', async () => {
  vi.mocked(api.createDocumentPreviewTask).mockResolvedValue(source); await setup(); await state().openPdf(source); state().openRegenerate(source);
  await state().submitCreate(); expect(state().receipt?.outcome).toBe('unconfirmed'); await state().submitCreate(); expect(api.createDocumentPreviewTask).toHaveBeenCalledOnce();
});
it('转换能力关闭时不能从缺失结果绕过服务端能力门禁', async () => {
  vi.mocked(api.getDocumentPreviewCapabilities).mockResolvedValue({ enabled: false, allowedMimeTypes: [], maxInputBytes: 1024, maxAttempts: 3, providerKey: 'disabled' });
  await setup(); await state().openPdf(source); state().openRegenerate(source); expect(state().editorOpen).toBe(false);
});
it('独立重试权限可创建新任务，未知结果必须明确核对且确认不自动重发', async () => {
  const failed = { ...source, statusKey: 'failed' }; vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [failed], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.retryDocumentPreviewTask).mockRejectedValueOnce(new Error('lost')).mockResolvedValue(created);
  await setup([read, 'document.host_preview_tasks.retry']); await state().retryTask(failed); await state().retryTask(failed);
  expect(api.retryDocumentPreviewTask).toHaveBeenCalledOnce(); expect(state().receipt?.outcome).toBe('unconfirmed');
  state().reviewRetry(failed); state().recoveryConfirmation?.confirm(); expect(api.retryDocumentPreviewTask).toHaveBeenCalledOnce();
  await state().retryTask(failed); expect(api.retryDocumentPreviewTask).toHaveBeenCalledTimes(2); expect(state().receipt?.outcome).toBe('succeeded');
  expect(api.createDocumentPreviewTask).not.toHaveBeenCalled();
});
it('重试核对确认撤权后失效，恢复权限也不能重放旧确认', async () => {
  const failed = { ...source, statusKey: 'failed' }; vi.mocked(api.listDocumentPreviewTasks).mockResolvedValue({ items: [failed], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.retryDocumentPreviewTask).mockRejectedValue(new Error('lost')); const { session } = await setup([read, 'document.host_preview_tasks.retry']);
  await state().retryTask(failed); state().reviewRetry(failed); const confirm = state().recoveryConfirmation!.confirm;
  session.currentUser!.permissions = [read]; await flushPromises(); confirm(); expect(api.retryDocumentPreviewTask).toHaveBeenCalledOnce();
  expect(state().recoveryConfirmation).toBeUndefined();
});
