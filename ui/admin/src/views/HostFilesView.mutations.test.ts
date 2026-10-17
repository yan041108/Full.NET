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

const folderResponse = { ...folder, createdAtUtc: file.createdAtUtc, createdByUserId: file.createdByUserId, updatedAtUtc: null, updatedByUserId: null };
function state() { return wrapper!.vm as unknown as { items: typeof file[]; total: number; metadataDialogVisible: boolean; metadataForm: { originalFileName: string; folderId: string }; newFolderName: string; selectedFolder: typeof folder | null; folderTree: { children?: { folder: typeof folder; label: string }[] }[] }; }
describe('Host 文件元数据和目录确认结果', () => {
  it('元数据成功先更新本地名称修订，补读失败仍保留', async () => {
    await setup(); await call('openMetadataDialog', file); state().metadataForm.originalFileName = 'new.txt';
    vi.mocked(api.updateHostFileMetadata).mockResolvedValueOnce({ ...file, originalFileName: 'new.txt', revision: 2 }); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline'));
    await call('saveMetadata'); expect(state().items[0]).toMatchObject({ originalFileName: 'new.txt', revision: 2 }); expect(state().metadataDialogVisible).toBe(false);
  });
  it('确认移到其他目录的文件先退出当前目录和选择', async () => {
    await setup(); await call('openMetadataDialog', file); state().metadataForm.folderId = folder.id; select([file]);
    vi.mocked(api.updateHostFileMetadata).mockResolvedValueOnce({ ...file, folderId: folder.id, revision: 2 }); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline'));
    await call('saveMetadata'); expect(state().items.map(row => row.id)).toEqual([other.id]); expect(state().total).toBe(1);
  });
  it('元数据错误身份不反馈成功，刷新前不能重交', async () => {
    await setup(); await call('openMetadataDialog', file); vi.mocked(api.updateHostFileMetadata).mockResolvedValueOnce({ ...other, revision: 2 }); const success = vi.spyOn(ElMessage, 'success');
    await call('saveMetadata'); await call('openMetadataDialog', file); await call('saveMetadata');
    expect(success).not.toHaveBeenCalled(); expect(api.updateHostFileMetadata).toHaveBeenCalledOnce(); expect(state().metadataDialogVisible).toBe(false);
  });
  it('元数据未知结果失败刷新不解锁，成功刷新后需重新打开', async () => {
    await setup(); await call('openMetadataDialog', file); vi.mocked(api.updateHostFileMetadata).mockRejectedValueOnce(new Error('lost')); await call('saveMetadata');
    vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline')); await call('load'); await call('openMetadataDialog', file); expect(state().metadataDialogVisible).toBe(false);
    await call('load'); await call('openMetadataDialog', file); expect(state().metadataDialogVisible).toBe(true); expect(api.updateHostFileMetadata).toHaveBeenCalledOnce();
  });
  it('关闭在途元数据不能直接重开并重写同一旧修订', async () => {
    await setup(); await call('openMetadataDialog', file); const pending = deferred<typeof file>(); vi.mocked(api.updateHostFileMetadata).mockReturnValueOnce(pending.promise);
    const action = call('saveMetadata'); state().metadataDialogVisible = false; await call('openMetadataDialog', file); expect(state().metadataDialogVisible).toBe(false);
    pending.resolve({ ...file, revision: 2 }); await action; expect(api.listHostFiles).toHaveBeenCalledOnce();
  });
  it('元数据从权威行冻结而不是采用事件中的文件名目录', async () => {
    await setup(); await call('openMetadataDialog', { ...file, id: file.id.toUpperCase(), originalFileName: 'forged', folderId: folder.id });
    expect(state().metadataForm).toEqual({ originalFileName: file.originalFileName, folderId: '' });
  });
  it('旧编辑保存和关闭回调不能作用于新文件表单', async () => {
    await setup(); await call('openMetadataDialog', file); await flushPromises();
    const button = (id: string) => wrapper!.findAllComponents({ name: 'ElButton' }).find(item => item.attributes('data-testid') === id)!;
    const close = button('host-files-metadata-back').vm.$.vnode.props!.onClick as () => void;
    const save = button('host-files-metadata-save').vm.$.vnode.props!.onClick as () => void;
    state().metadataDialogVisible = false; await call('openMetadataDialog', other); close(); save(); await flushPromises();
    expect(state().metadataDialogVisible).toBe(true); expect(api.updateHostFileMetadata).not.toHaveBeenCalled();
  });
  it('在途编辑被较新目录修订关闭时未知提交限制不被同轮读取清除', async () => {
    await setup(); await call('openMetadataDialog', file); const pending = deferred<typeof file>(); vi.mocked(api.updateHostFileMetadata).mockReturnValueOnce(pending.promise); const action = call('saveMetadata');
    vi.mocked(api.listHostFiles).mockResolvedValueOnce({ items: [{ ...file, revision: 2 }], page: 1, pageSize: 20, total: 1 }); await call('load');
    await call('openMetadataDialog', { ...file, revision: 2 }); expect(state().metadataDialogVisible).toBe(false);
    pending.resolve({ ...file, revision: 3 }); await action; expect(state().items[0]?.revision).toBe(2);
  });
  it('目录重命名成功后补读失败保留新名称与修订', async () => {
    await setup(); await call('onFolderSelected', node); await flushPromises();
    vi.mocked(api.updateHostFolder).mockResolvedValueOnce({ ...folderResponse, name: 'Renamed', revision: 2 }); vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline'));
    const action = call('renameFolder', node); await flushPromises(); await wrapper!.get('[data-testid="host-files-confirm-input"]').setValue('Renamed'); await call('finishConfirmation', true); await action;
    expect(state().selectedFolder).toMatchObject({ name: 'Renamed', revision: 2 }); expect(state().folderTree[0]?.children?.[0]?.label).toBe('Renamed');
  });
  it('目录在途改名不覆盖已经读取的更高修订', async () => {
    await setup(); const pending = deferred<Awaited<ReturnType<typeof api.updateHostFolder>>>(); vi.mocked(api.updateHostFolder).mockReturnValueOnce(pending.promise);
    const action = call('renameFolder', node); await call('finishConfirmation', true); await flushPromises();
    vi.mocked(api.listHostFolderTree).mockResolvedValueOnce([{ ...folder, name: 'Newer', revision: 3 }]); await call('loadFolders');
    vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline')); pending.resolve({ ...folderResponse, name: 'Own', revision: 2 }); await action;
    expect(state().folderTree[0]?.children?.[0]?.folder).toMatchObject({ name: 'Newer', revision: 3 });
  });
  it('较早目录读取取消后不能恢复确认删除的节点', async () => {
    await setup(); const old = deferred<typeof folder[]>(); vi.mocked(api.listHostFolderTree).mockReturnValueOnce(old.promise); const reading = call('loadFolders');
    const signal = vi.mocked(api.listHostFolderTree).mock.calls.at(-1)?.[0]; vi.mocked(api.deleteHostFolder).mockResolvedValueOnce(folderResponse);
    vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline')); const action = call('removeFolder', node); await call('finishConfirmation', true); await action;
    expect(signal?.aborted).toBe(true); old.resolve([folder]); await reading; expect(state().folderTree[0]?.children).toEqual([]);
  });
  it('目录删除成功先移除节点并切回根，补读失败不恢复旧选择', async () => {
    await setup(); await call('onFolderSelected', node); await flushPromises(); vi.mocked(api.deleteHostFolder).mockResolvedValueOnce(folderResponse);
    vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline')); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline'));
    const action = call('removeFolder', node); await call('finishConfirmation', true); await action;
    expect(state().selectedFolder).toBeNull(); expect(state().folderTree[0]?.children).toEqual([]); expect(state().items).toEqual([]); expect(state().total).toBe(0);
  });
  it('目录错误身份不能作为删除成功', async () => {
    await setup(); vi.mocked(api.deleteHostFolder).mockResolvedValueOnce({ ...folderResponse, id: other.id }); const success = vi.spyOn(ElMessage, 'success');
    const action = call('removeFolder', node); await call('finishConfirmation', true); await action;
    expect(success).not.toHaveBeenCalled(); expect(state().folderTree[0]?.children).toHaveLength(1);
  });
  it('目录未知结果成功重读树前不能再次删除或改名', async () => {
    await setup(); vi.mocked(api.deleteHostFolder).mockRejectedValueOnce(new Error('lost')); const action = call('removeFolder', node); await call('finishConfirmation', true); await action;
    await call('renameFolder', node); vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline')); await call('loadFolders'); await call('removeFolder', node);
    expect(api.deleteHostFolder).toHaveBeenCalledOnce(); expect(wrapper!.find('[data-testid="host-files-confirm-accept"]').exists()).toBe(false);
    await wrapper!.get('[data-testid="host-files-folder-refresh"]').trigger('click'); await flushPromises();
    const retry = call('removeFolder', node); await flushPromises(); expect(wrapper!.find('[data-testid="host-files-confirm-accept"]').exists()).toBe(true); await call('finishConfirmation', false); await retry;
  });
  it('刷新文件列表不能解除目录未知结果门槛', async () => {
    await setup(); vi.mocked(api.deleteHostFolder).mockRejectedValueOnce(new Error('lost')); const action = call('removeFolder', node); await call('finishConfirmation', true); await action;
    await call('load'); await call('removeFolder', node); expect(api.deleteHostFolder).toHaveBeenCalledOnce();
    expect(wrapper!.find('[data-testid="host-files-confirm-accept"]').exists()).toBe(false);
  });
  it('目录事件大小写等价但名称和修订取当前节点快照', async () => {
    await setup(); const action = call('renameFolder', { ...node, folder: { ...folder, id: folder.id.toUpperCase(), name: 'forged', displayOrder: 99 } }); await flushPromises();
    expect(wrapper!.get('[data-testid="host-files-confirm-input"]').element).toHaveProperty('value', folder.name); await call('finishConfirmation', false); await action;
  });
  it('创建确认结果进入原父目录，保留后来输入的创建名称', async () => {
    await setup(); await call('onFolderSelected', node); await flushPromises(); state().newFolderName = 'Child';
    const pending = deferred<Awaited<ReturnType<typeof api.createHostFolder>>>(); vi.mocked(api.createHostFolder).mockReturnValueOnce(pending.promise); const action = call('createFolder'); state().newFolderName = 'Next';
    vi.mocked(api.listHostFolderTree).mockRejectedValueOnce(new Error('offline')); pending.resolve({ ...folderResponse, id: other.id, parentId: folder.id, name: 'Child', revision: 0 }); await action;
    expect(state().newFolderName).toBe('Next'); expect(JSON.stringify(state().folderTree)).toContain('Child');
  });
  it('创建未知结果禁止自动重建，成功刷新目录后才能显式创建', async () => {
    await setup(); state().newFolderName = 'Child'; vi.mocked(api.createHostFolder).mockRejectedValueOnce(new Error('lost')); await call('createFolder'); await call('createFolder');
    expect(api.createHostFolder).toHaveBeenCalledOnce(); await call('loadFolders'); vi.mocked(api.createHostFolder).mockResolvedValueOnce({ ...folderResponse, id: other.id, name: 'Child', revision: 0 }); await call('createFolder');
    expect(api.createHostFolder).toHaveBeenCalledTimes(2);
  });
  it('主体切换丢弃在途创建结果，不污染新目录', async () => {
    const session = await setup(); state().newFolderName = 'Private'; const pending = deferred<Awaited<ReturnType<typeof api.createHostFolder>>>(); vi.mocked(api.createHostFolder).mockReturnValueOnce(pending.promise);
    const action = call('createFolder'); session.currentUser!.sessionId = 'new'; await flushPromises(); pending.resolve({ ...folderResponse, id: other.id, name: 'Private', revision: 0 }); await action;
    expect(JSON.stringify(state().folderTree)).not.toContain('Private"'); expect(state().newFolderName).toBe('');
  });
});
