import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import type { HostDocumentPreviewTaskResponse } from '@fullnet/client-contracts';
import View from './DocumentPreviewTasksView.vue';
import * as api from '../api/document-preview-tasks';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/document-preview-tasks', () => ({ getDocumentPreviewCapabilities: vi.fn(), retryDocumentPreviewTask: vi.fn(), getDocumentPreviewTask: vi.fn(), listDocumentPreviewTasks: vi.fn(), createDocumentPreviewTask: vi.fn(), cancelDocumentPreviewTask: vi.fn(), openDocumentPreviewTaskContent: vi.fn() }));
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
let wrapper: VueWrapper;
interface State { statusDraft: string; appliedStatus?: string; filterDocumentItemId: string; page: number; total: number; items: HostDocumentPreviewTaskResponse[]; receipt?: { outcome: string }; editorForm: { documentItemId: string; versionId: string }; selectedTask?: HostDocumentPreviewTaskResponse; applyFilter(): Promise<void>; load(): Promise<boolean>; refreshResults(): Promise<void>; openDetail(id: string): Promise<void>; openCreate(): void; submitCreate(): Promise<void>; requestCancellation(row: HostDocumentPreviewTaskResponse): void; confirmCancellation(): Promise<void> }
const state = () => wrapper.vm as unknown as State;
async function setup(permissions = ['document.host_preview_tasks.read']) {
  const context = createShareSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ArtFormDialog: { name: 'ArtFormDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context;
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.getDocumentPreviewCapabilities).mockResolvedValue({ enabled: true, providerKey: 'fixture', allowedMimeTypes: [], maxInputBytes: 1024, maxAttempts: 3 });
  vi.mocked(api.listDocumentPreviewTasks).mockImplementation(async (query = {}) => ({ items: [{ ...task, statusKey: query.statusKey ?? 'succeeded' }], page: query.page ?? 1, pageSize: query.pageSize ?? 20, total: 60 }));
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task });
});
afterEach(() => wrapper?.unmount());
async function apply(status: string) { state().statusDraft = status; await state().applyFilter(); await flushPromises(); }

it('文档和状态一起提交，分页/刷新保留已提交状态而不使用草稿', async () => {
  await setup(); state().filterDocumentItemId = task.documentItemId; await apply('failed'); state().statusDraft = 'cancelled';
  wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
  expect(api.listDocumentPreviewTasks).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, documentItemId: task.documentItemId, statusKey: 'failed' }, expect.any(AbortSignal));
  await state().refreshResults(); expect(state().appliedStatus).toBe('failed'); expect(state().page).toBe(2);
  await apply('all'); expect(state().appliedStatus).toBeUndefined(); expect(state().page).toBe(1);
  expect(api.createDocumentPreviewTask).not.toHaveBeenCalled(); expect(api.cancelDocumentPreviewTask).not.toHaveBeenCalled();
});
it.each(['invalid-status', 'invalid-document'])('%s 拒绝后刷新和分页不能退回全部查询', async kind => {
  await setup(); if (kind === 'invalid-document') state().filterDocumentItemId = 'bad';
  await apply(kind === 'invalid-status' ? 'unknown' : 'failed'); await state().refreshResults(); wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises();
  expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(1); expect(state().items).toEqual([]);
});
it('状态不符拒绝整页而不做本地筛选伪造总数', async () => {
  await setup(); vi.mocked(api.listDocumentPreviewTasks).mockResolvedValueOnce({ items: [task], page: 1, pageSize: 20, total: 60 });
  await apply('failed'); expect(state().items).toEqual([]); expect(state().total).toBe(0); expect(wrapper.find('.el-alert--error').exists()).toBe(true);
});
it('第二页在途保留总数，失败保留请求页并只显式重读原页', async () => {
  await setup(); await apply('failed'); const pending = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise);
  wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); expect(state().page).toBe(2); expect(state().total).toBe(60);
  wrapper.getComponent(ElPagination).vm.$emit('change', 1, 20); expect(state().page).toBe(2);
  pending.reject({ status: 503, title: '读取失败' }); await flushPromises(); expect(state().page).toBe(2); expect(state().total).toBe(0); expect(state().items).toEqual([]);
  wrapper.getComponent(ElPagination).vm.$emit('change', 1, 20); await flushPromises(); expect(api.listDocumentPreviewTasks).toHaveBeenCalledTimes(3);
  await state().refreshResults(); expect(api.listDocumentPreviewTasks).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, documentItemId: undefined, statusKey: 'failed' }, expect.any(AbortSignal));
});
it('新状态取消旧读取，迟到成功页不能污染失败页', async () => {
  await setup(); const pending = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise);
  state().page = 2; const old = state().load(); const signal = vi.mocked(api.listDocumentPreviewTasks).mock.calls.at(-1)![1]!;
  await apply('failed'); pending.resolve({ items: [{ ...task, documentTitle: '旧页' }], page: 2, pageSize: 20, total: 60 }); await old;
  expect(signal.aborted).toBe(true); expect(state().items[0]?.statusKey).toBe('failed'); expect(wrapper.text()).not.toContain('旧页');
});
it('会话变化清空状态条件，恢复使用全部状态', async () => {
  const { session } = await setup(); await apply('failed'); state().statusDraft = 'cancelled'; session.currentUser!.sessionId = otherId; await flushPromises();
  expect(state().statusDraft).toBe('all'); expect(state().appliedStatus).toBeUndefined(); expect(api.listDocumentPreviewTasks).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, documentItemId: undefined, statusKey: undefined }, expect.any(AbortSignal));
});
it('精确详情允许读取其他状态，列表状态保持不变', async () => {
  await setup(); await apply('failed'); await state().openDetail(task.id); expect(state().selectedTask?.statusKey).toBe('succeeded'); expect(state().items[0]?.statusKey).toBe('failed'); expect(state().appliedStatus).toBe('failed');
});
it('确认取消从待执行页移除，陈旧待执行源不能恢复任务', async () => {
  await setup(['document.host_preview_tasks.read', 'document.host_preview_tasks.cancel']); await apply('pending');
  const pending = state().items[0]!; vi.mocked(api.cancelDocumentPreviewTask).mockResolvedValue({ ...pending, statusKey: 'cancelled', version: 2 });
  state().requestCancellation(pending); await state().confirmCancellation(); expect(state().receipt?.outcome).toBe('succeeded'); expect(state().items).toEqual([]); expect(wrapper.find('.el-alert--error').exists()).toBe(true);
});
it('新排队创建回执保留独立详情，不混入失败历史', async () => {
  await setup(['document.host_preview_tasks.read', 'document.host_preview_tasks.create']); await apply('failed'); state().openCreate(); state().editorForm.documentItemId = task.documentItemId;
  vi.mocked(api.createDocumentPreviewTask).mockResolvedValue({ ...task, id: otherId, statusKey: 'pending' });
  const pending = deferred<Awaited<ReturnType<typeof api.listDocumentPreviewTasks>>>(); vi.mocked(api.listDocumentPreviewTasks).mockReturnValueOnce(pending.promise);
  vi.mocked(api.getDocumentPreviewTask).mockResolvedValue({ ...task, id: otherId, statusKey: 'pending' }); const submitting = state().submitCreate(); await flushPromises();
  expect(state().items.every(row => row.statusKey === 'failed')).toBe(true); expect(state().selectedTask?.id).toBe(otherId); expect(state().receipt?.outcome).toBe('succeeded');
  pending.resolve({ items: [{ ...task, statusKey: 'failed' }], page: 1, pageSize: 20, total: 60 }); await submitting;
});
