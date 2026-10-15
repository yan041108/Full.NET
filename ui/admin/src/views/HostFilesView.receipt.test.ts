import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { ElMessage } from 'element-plus';
import HostFilesView from './HostFilesView.vue';
import * as api from '../api/host-files';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, outputId } from '../test/data-output-fixtures';
vi.mock('../api/host-files', () => ({ listHostFiles: vi.fn(), listHostFolderTree: vi.fn(), uploadHostFile: vi.fn(), batchUploadHostFiles: vi.fn(),
  deleteHostFile: vi.fn(), batchDeleteHostFiles: vi.fn(), previewHostFileContent: vi.fn(), downloadHostFileContent: vi.fn(), openHostFileBlob: vi.fn(),
  createHostFolder: vi.fn(), updateHostFolder: vi.fn(), deleteHostFolder: vi.fn(), updateHostFileMetadata: vi.fn(), listHostFileReferences: vi.fn() }));
const file = { id: outputId, originalFileName: 'private.txt', contentType: 'text/plain', sizeBytes: 12, contentHash: null,
  folderId: null, revision: 1, createdAtUtc: '2026-10-15T00:00:00Z', createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const other = { ...file, id: '01980000-0000-7000-8000-000000000003', originalFileName: 'other.txt' };
const folder = { id: '01980000-0000-7000-8000-000000000004', parentId: null, name: 'Private folder', displayOrder: 0, revision: 1, children: [] };
const node = { id: folder.id, label: folder.name, folder };
const permissions = ['files.files.read', 'files.files.upload', 'files.files.download', 'files.files.update', 'files.files.delete',
  'files.file_references.read', 'files.folders.create', 'files.folders.update', 'files.folders.delete'];
let wrapper: VueWrapper | undefined; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listHostFiles).mockResolvedValue({ items: [file, other], page: 1, pageSize: 20, total: 2 });
  vi.mocked(api.listHostFolderTree).mockResolvedValue([folder]); vi.mocked(api.uploadHostFile).mockResolvedValue(file);
  vi.mocked(api.updateHostFileMetadata).mockResolvedValue(file); vi.mocked(api.deleteHostFile).mockResolvedValue(file);
  vi.mocked(api.batchDeleteHostFiles).mockResolvedValue({ succeededCount: 1, results: [{ fileId: file.id, succeeded: true, errorCode: null, message: null }] });
  vi.mocked(api.listHostFileReferences).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:file'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(granted = permissions, keepAlive = false) {
  const context = createShareSession(granted); visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(HostFilesView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : HostFilesView, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return context.session;
}
async function call(name: string, ...args: unknown[]) {
  const vm = wrapper!.findComponent(HostFilesView).vm as unknown as Record<string, (...values: unknown[]) => unknown>;
  return await vm[name]!(...args);
}
function select(rows: typeof file[]) {
  const vm = wrapper!.findComponent(HostFilesView).vm as unknown as { selectedRows: typeof file[] }; vm.selectedRows = rows;
}
describe('Host 文件单项与批量删除回执', () => {
  it('孤立上传删除权限不能读取或发起写入', async () => {
    await setup(['files.files.upload', 'files.files.delete']); await call('onFileSelected', { target: { files: [new File(['x'], 'a.txt')] } }); await call('upload');
    expect(api.listHostFiles).not.toHaveBeenCalled(); expect(api.uploadHostFile).not.toHaveBeenCalled();
  });
  it('批量选择按UUID去重并取权威目录中的标识', async () => {
    await setup(); select([file, { ...file, id: file.id.toUpperCase() }]); const action = call('removeSelected'); await call('finishConfirmation', true); await action;
    expect(api.batchDeleteHostFiles).toHaveBeenCalledExactlyOnceWith([file.id], expect.any(AbortSignal));
  });
  it('部分成功保留逐项回执，补读失败仍移除确认删除项', async () => {
    await setup(); select([file, other]); vi.mocked(api.batchDeleteHostFiles).mockResolvedValueOnce({ succeededCount: 1, results: [
      { fileId: other.id, succeeded: false, errorCode: 'files.file.in_use', message: '内部敏感文本' }, { fileId: file.id.toUpperCase(), succeeded: true, errorCode: null, message: null }
    ] }); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline'));
    const action = call('removeSelected'); await call('finishConfirmation', true); await action; await flushPromises();
    const state = wrapper!.vm as unknown as { items: typeof file[]; total: number }; expect(state.items.map(row => row.id)).toEqual([other.id]); expect(state.total).toBe(1);
    expect(wrapper!.text()).toContain('files.file.in_use'); expect(wrapper!.text()).not.toContain('内部敏感文本'); expect(wrapper!.find('[data-testid="host-files-delete-receipt"]').exists()).toBe(true);
  });
  it('批量在途回执仅表示等待，不能提前显示成功', async () => {
    await setup(); select([file]); const pending = deferred<Awaited<ReturnType<typeof api.batchDeleteHostFiles>>>(); vi.mocked(api.batchDeleteHostFiles).mockReturnValueOnce(pending.promise);
    const action = call('removeSelected'); await call('finishConfirmation', true); await flushPromises();
    expect((wrapper!.vm as unknown as { deleteReceipt: { items: { outcome: string }[] } }).deleteReceipt.items[0]?.outcome).toBe('processing');
    pending.resolve({ succeededCount: 1, results: [{ fileId: file.id, succeeded: true, errorCode: null, message: null }] }); await action;
  });
  it('未知批量结果全部标记未确认，成功刷新前不能重交原项目', async () => {
    await setup(); select([file]); vi.mocked(api.batchDeleteHostFiles).mockRejectedValueOnce(new Error('response lost'));
    const action = call('removeSelected'); await call('finishConfirmation', true); await action;
    expect((wrapper!.vm as unknown as { deleteReceipt: { items: { outcome: string }[] } }).deleteReceipt.items[0]?.outcome).toBe('unconfirmed');
    select([file]); await call('removeSelected'); expect(api.batchDeleteHostFiles).toHaveBeenCalledOnce(); await call('load');
    const retry = call('removeSelected'); await call('finishConfirmation', true); await retry; expect(api.batchDeleteHostFiles).toHaveBeenCalledTimes(2);
  });
  it('确认取消不生成回执也不删除', async () => {
    await setup(); select([file]); const action = call('removeSelected'); await call('finishConfirmation', false); await action;
    expect(api.batchDeleteHostFiles).not.toHaveBeenCalled(); expect((wrapper!.vm as unknown as { deleteReceipt: unknown }).deleteReceipt).toBeUndefined();
  });
  it('旧确认与关闭回调不能批准或关闭下一次删除', async () => {
    await setup(); const first = call('remove', file); await flushPromises();
    const buttons = () => wrapper!.findAllComponents({ name: 'ElButton' });
    const accept = buttons().find(button => button.attributes('data-testid') === 'host-files-confirm-accept')!.vm.$.vnode.props!.onClick as () => void;
    const dismiss = buttons().find(button => button.attributes('data-testid') === 'host-files-confirm-dismiss')!.vm.$.vnode.props!.onClick as () => void;
    await call('finishConfirmation', false); await first; const next = call('remove', other); await flushPromises(); accept(); dismiss(); await flushPromises();
    expect(api.deleteHostFile).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="host-files-confirm-accept"]').exists()).toBe(true);
    vi.mocked(api.deleteHostFile).mockResolvedValueOnce(other); await call('finishConfirmation', true); await next; expect(api.deleteHostFile).toHaveBeenCalledExactlyOnceWith(other.id, expect.any(AbortSignal));
  });
  it('单项成功删除在列表补读失败后仍退出当前目录', async () => {
    await setup(); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline')); const action = call('remove', { ...file, id: file.id.toUpperCase() }); await call('finishConfirmation', true); await action;
    expect((wrapper!.vm as unknown as { items: typeof file[] }).items.map(item => item.id)).toEqual([other.id]);
  });
  it('删除身份错误回包不反馈成功或移除文件', async () => {
    await setup(); vi.mocked(api.deleteHostFile).mockResolvedValueOnce(other); const success = vi.spyOn(ElMessage, 'success'); const action = call('remove', file); await call('finishConfirmation', true); await action;
    expect(success).not.toHaveBeenCalled(); expect((wrapper!.vm as unknown as { items: typeof file[] }).items).toHaveLength(2);
  });
  it('未知单项结果后失败刷新不能解除该项删除限制', async () => {
    await setup(); vi.mocked(api.deleteHostFile).mockRejectedValueOnce(new Error('lost')); const action = call('remove', file); await call('finishConfirmation', true); await action;
    vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline')); await call('load'); await call('remove', file); expect(api.deleteHostFile).toHaveBeenCalledOnce();
  });
  it('较早列表不能在批量删除确认后恢复已删除项', async () => {
    await setup(); const old = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(old.promise); const reading = call('load'); const signal = vi.mocked(api.listHostFiles).mock.calls.at(-1)?.[3];
    select([file]); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('fresh offline')); const action = call('removeSelected'); await call('finishConfirmation', true); await action; expect(signal?.aborted).toBe(true);
    old.resolve({ items: [file, other], page: 1, pageSize: 20, total: 2 }); await reading; expect((wrapper!.vm as unknown as { items: typeof file[] }).items).toEqual([other]);
  });
  it('作用域变更清理回执且旧失败不能污染新页面', async () => {
    const session = await setup(); select([file]); const pending = deferred<Awaited<ReturnType<typeof api.batchDeleteHostFiles>>>(); vi.mocked(api.batchDeleteHostFiles).mockReturnValueOnce(pending.promise);
    const action = call('removeSelected'); await call('finishConfirmation', true); session.currentUser!.sessionId = 'next'; await flushPromises(); pending.reject(new Error('old')); await action;
    expect((wrapper!.vm as unknown as { deleteReceipt: unknown }).deleteReceipt).toBeUndefined();
  });
});
