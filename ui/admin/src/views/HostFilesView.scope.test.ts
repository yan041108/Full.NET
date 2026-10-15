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
describe('Host 文件整页请求与意图归属', () => {
  it('无读取权限不预取列表或目录', async () => {
    await setup([]); expect(api.listHostFiles).not.toHaveBeenCalled(); expect(api.listHostFolderTree).not.toHaveBeenCalled();
  });
  it('切入租户清理 Host 内容，不带原权限重读', async () => {
    const session = await setup(); session.currentUser = { ...session.currentUser!, tenantId: other.id, scope: `tenant:${other.id.replaceAll('-', '')}` };
    await flushPromises(); expect(wrapper!.text()).not.toContain(file.originalFileName); expect(api.listHostFiles).toHaveBeenCalledOnce();
  });
  it('列表读取与目录并发完成顺序不改变根目录条件', async () => {
    const pending = deferred<typeof folder[]>(); vi.mocked(api.listHostFolderTree).mockReturnValueOnce(pending.promise); await setup();
    expect(vi.mocked(api.listHostFiles).mock.calls[0]![2]?.folderId).toBe(''); pending.resolve([folder]); await flushPromises();
  });
  it('最新搜索取消旧页，迟到数据不覆盖当前列表', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise); await setup();
    const signal = vi.mocked(api.listHostFiles).mock.calls[0]![3]!; await call('load'); pending.resolve({ items: [{ ...file, originalFileName: 'stale-name' }], page: 1, pageSize: 20, total: 1 });
    await flushPromises(); expect(signal.aborted).toBe(true); expect(wrapper!.text()).not.toContain('stale-name');
  });
  it.each(['download', 'preview'])('%s 撤权后不打开迟到文件或重开预览', async kind => {
    const pending = deferred<Blob>(); if (kind === 'download') vi.mocked(api.downloadHostFileContent).mockReturnValueOnce(pending.promise); else vi.mocked(api.previewHostFileContent).mockReturnValueOnce(pending.promise);
    const session = await setup(); const action = call(kind, file); session.currentUser!.permissions = []; pending.resolve(new Blob(['private'])); await action;
    expect(api.openHostFileBlob).not.toHaveBeenCalled(); expect(URL.createObjectURL).not.toHaveBeenCalled();
  });
  it('关闭正在加载的预览同步取消，旧结果不能恢复 URL', async () => {
    const pending = deferred<Blob>(); vi.mocked(api.previewHostFileContent).mockReturnValueOnce(pending.promise); await setup();
    const action = call('preview', file); await call('closePreviewDialog'); const signal = vi.mocked(api.previewHostFileContent).mock.calls[0]![1]!;
    pending.resolve(new Blob(['private'])); await action; expect(signal.aborted).toBe(true); expect(URL.createObjectURL).not.toHaveBeenCalled();
  });
  it('批量删除确认冻结原选择，不删除后来选择的文件', async () => {
    await setup(); select([file]); const action = call('removeSelected'); await flushPromises(); select([other]);
    await call('finishConfirmation', true); await action;
    expect(api.batchDeleteHostFiles).toHaveBeenCalledExactlyOnceWith([file.id], expect.any(AbortSignal));
  });
  it.each(['remove', 'removeSelected', 'removeFolder', 'renameFolder'])('%s 确认等待跨撤权不发写请求', async name => {
    const session = await setup(); select([file]); const action = call(name, name === 'remove' ? file : node); await flushPromises();
    session.currentUser!.permissions = []; await action;
    expect(api.deleteHostFile).not.toHaveBeenCalled(); expect(api.batchDeleteHostFiles).not.toHaveBeenCalled(); expect(api.deleteHostFolder).not.toHaveBeenCalled(); expect(api.updateHostFolder).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="host-files-confirm-message"]').exists()).toBe(false);
  });
  it('放弃删除不发写请求', async () => {
    await setup(); const action = call('remove', file); await call('finishConfirmation', false); await action; expect(api.deleteHostFile).not.toHaveBeenCalled();
  });
  it('元数据关闭后旧成功不能关闭新编辑或刷新列表', async () => {
    const pending = deferred<typeof file>(); vi.mocked(api.updateHostFileMetadata).mockReturnValueOnce(pending.promise); await setup(); await call('openMetadataDialog', file);
    const action = call('saveMetadata'); const vm = wrapper!.findComponent(HostFilesView).vm as unknown as { metadataDialogVisible: boolean };
    vm.metadataDialogVisible = false; await call('openMetadataDialog', other); pending.resolve(file); await action;
    expect(vm.metadataDialogVisible).toBe(true); expect(api.listHostFiles).toHaveBeenCalledOnce();
  });
  it('列表修订变化关闭旧元数据表单，不提交旧内容', async () => {
    await setup(); await call('openMetadataDialog', file); vi.mocked(api.listHostFiles).mockResolvedValueOnce({ items: [{ ...file, revision: 2 }], page: 1, pageSize: 20, total: 1 });
    await call('load'); await call('saveMetadata'); expect(api.updateHostFileMetadata).not.toHaveBeenCalled();
  });
  it('上传等待中撤权拒绝迟到回执和反馈', async () => {
    const pending = deferred<typeof file>(); vi.mocked(api.uploadHostFile).mockReturnValueOnce(pending.promise); const session = await setup();
    await call('onFileSelected', { target: { files: [new File(['fixture'], 'a.txt')] } }); const success = vi.spyOn(ElMessage, 'success'); const action = call('upload');
    session.currentUser!.permissions = []; pending.resolve(file); await action; expect(success).not.toHaveBeenCalled(); expect(api.listHostFiles).toHaveBeenCalledOnce();
  });
  it('引用抽屉关闭取消等待，不在迟到响应后重开', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listHostFileReferences>>>(); vi.mocked(api.listHostFileReferences).mockReturnValueOnce(pending.promise); await setup();
    const action = call('openReferences', file); const vm = wrapper!.findComponent(HostFilesView).vm as unknown as { referencesDrawerVisible: boolean }; vm.referencesDrawerVisible = false;
    pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await action; expect(vm.referencesDrawerVisible).toBe(false);
    expect(vi.mocked(api.listHostFileReferences).mock.calls[0]![3]!.aborted).toBe(true);
  });
  it('KeepAlive 停用释放预览，恢复仅重读当前列表和目录', async () => {
    vi.mocked(api.previewHostFileContent).mockResolvedValue(new Blob(['plain'], { type: 'text/plain' })); await setup(permissions, true); await call('preview', file);
    visible.value = false; await flushPromises(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:file'); visible.value = true; await flushPromises();
    expect(api.listHostFiles).toHaveBeenCalledTimes(2); expect(api.listHostFolderTree).toHaveBeenCalledTimes(2);
  });
  it('稳定会话上传保留文件与取消信号，完成后刷新列表', async () => {
    await setup(); const source = new File(['fixture'], 'source.txt'); await call('onFileSelected', { target: { files: [source] } }); await call('upload');
    expect(api.uploadHostFile).toHaveBeenCalledWith(source, undefined, expect.any(AbortSignal)); expect(api.listHostFiles).toHaveBeenCalledTimes(2);
  });
  it('稳定元数据编辑使用打开时修订并传递取消信号', async () => {
    await setup(); await call('openMetadataDialog', file); await call('saveMetadata');
    expect(api.updateHostFileMetadata).toHaveBeenCalledWith(file.id, { expectedRevision: 1, originalFileName: file.originalFileName, folderId: null }, expect.any(AbortSignal));
    expect(api.listHostFiles).toHaveBeenCalledTimes(2);
  });
  it('稳定目录重命名使用确认输入与原修订', async () => {
    vi.mocked(api.updateHostFolder).mockResolvedValue({ ...folder, createdAtUtc: file.createdAtUtc, createdByUserId: file.createdByUserId, updatedAtUtc: null, updatedByUserId: null });
    await setup(); const action = call('renameFolder', node); await flushPromises();
    await wrapper!.get('[data-testid="host-files-confirm-input"]').setValue(' Renamed '); await call('finishConfirmation', true); await action;
    expect(api.updateHostFolder).toHaveBeenCalledWith(folder.id, { expectedRevision: 1, name: 'Renamed', displayOrder: 0 }, expect.any(AbortSignal));
  });
  it('目录确认期间修订改变不提交旧意图', async () => {
    await setup(); const action = call('removeFolder', node); vi.mocked(api.listHostFolderTree).mockResolvedValueOnce([{ ...folder, revision: 2 }]);
    await call('loadFolders'); await call('finishConfirmation', true); await action; expect(api.deleteHostFolder).not.toHaveBeenCalled();
  });
  it('目录名为空不能确认，放弃仍结束等待', async () => {
    await setup(); const action = call('renameFolder', node); await flushPromises();
    await wrapper!.get('[data-testid="host-files-confirm-input"]').setValue(' '); await call('finishConfirmation', true);
    expect(api.updateHostFolder).not.toHaveBeenCalled(); await call('finishConfirmation', false); await action;
  });
});
