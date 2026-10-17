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
  vi.mocked(api.updateHostFileMetadata).mockResolvedValue({ ...file, revision: 2 }); vi.mocked(api.deleteHostFile).mockResolvedValue(file);
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
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return context.session;
}
async function call(name: string, ...args: unknown[]) {
  const vm = wrapper!.findComponent(HostFilesView).vm as unknown as Record<string, (...values: unknown[]) => unknown>;
  return await vm[name]!(...args);
}
function select(rows: typeof file[]) {
  const vm = wrapper!.findComponent(HostFilesView).vm as unknown as { selectedRows: typeof file[] }; vm.selectedRows = rows;
}

const sources = () => [new File(['abc'], 'a.txt'), new File(['xy'], 'b.txt')];
beforeEach(() => { vi.mocked(api.uploadHostFile).mockImplementation(async source => ({ ...file, originalFileName: source.name, sizeBytes: source.size })); });
const claim = { id: outputId, idempotencyKey: 'claim', consumerModule: 'documents', consumerReferenceId: outputId, state: 'active', createdAtUtc: file.createdAtUtc, updatedAtUtc: file.createdAtUtc, confirmedAtUtc: null, releasedAtUtc: null };
function state() { return wrapper!.vm as unknown as { items: typeof file[]; previewDialogVisible: boolean; referencesDrawerVisible: boolean; referencesPage: number; referencesTotal: number; references: typeof claim[]; uploadUnconfirmed: boolean; uploadReceipt?: { items: { outcome: string }[] }; selectedFiles: File[] }; }
describe('Host 上传回执与内容读取归属', () => {
  it('批量部分成功显示逐项回执和警告，不泄露未知消息', async () => {
    await setup(); const files = sources(); await call('onFileSelected', { target: { files } });
    vi.mocked(api.batchUploadHostFiles).mockResolvedValueOnce({ succeededCount: 1, results: [
      { originalFileName: files[0]!.name, succeeded: true, file: { ...file, originalFileName: files[0]!.name, sizeBytes: files[0]!.size }, errorCode: null, message: null },
      { originalFileName: files[1]!.name, succeeded: false, file: null, errorCode: 'files.file_too_large', message: 'private backend' }
    ] }); const success = vi.spyOn(ElMessage, 'success'), warning = vi.spyOn(ElMessage, 'warning'); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline'));
    await call('upload'); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(warning).toHaveBeenCalledOnce();
    expect(state().uploadReceipt?.items.map(item => item.outcome)).toEqual(['succeeded', 'failed']); expect(wrapper!.text()).not.toContain('private backend'); expect(state().selectedFiles).toEqual([]);
  });
  it('上传等待期间回执不能提前标成功', async () => {
    await setup(); await call('onFileSelected', { target: { files: [sources()[0]] } }); const pending = deferred<typeof file>(); vi.mocked(api.uploadHostFile).mockReturnValueOnce(pending.promise);
    const action = call('upload'); await flushPromises(); expect(state().uploadReceipt?.items[0]?.outcome).toBe('processing'); pending.resolve({ ...file, originalFileName: 'a.txt', sizeBytes: 3 }); await action;
    expect(state().uploadReceipt?.items[0]?.outcome).toBe('succeeded');
  });
  it('未知上传结果刷新失败不能重交，成功刷新后允许显式重交', async () => {
    await setup(); await call('onFileSelected', { target: { files: [sources()[0]] } }); vi.mocked(api.uploadHostFile).mockRejectedValueOnce(new Error('lost')); await call('upload');
    expect(state().uploadReceipt?.items[0]?.outcome).toBe('unconfirmed'); await call('upload'); expect(api.uploadHostFile).toHaveBeenCalledOnce();
    vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline')); await call('load'); await call('upload'); expect(api.uploadHostFile).toHaveBeenCalledOnce();
    await call('load'); await call('upload'); expect(api.uploadHostFile).toHaveBeenCalledTimes(2);
  });
  it('批量错配结果不能反馈成功而是保留未确认回执', async () => {
    await setup(); await call('onFileSelected', { target: { files: sources() } }); vi.mocked(api.batchUploadHostFiles).mockResolvedValueOnce({ succeededCount: 0, results: [] }); const success = vi.spyOn(ElMessage, 'success');
    await call('upload'); expect(success).not.toHaveBeenCalled(); expect(state().uploadUnconfirmed).toBe(true); expect(state().uploadReceipt?.items).toHaveLength(2);
  });
  it('确认上传清除原生输入值，允许再次选择同名文件', async () => {
    await setup(); const input = wrapper!.get('[data-testid="host-files-file-input"]').element as HTMLInputElement;
    const setter = vi.spyOn(input, 'value', 'set'); await call('onFileSelected', { target: { files: [sources()[0]] } }); await call('upload'); expect(setter).toHaveBeenCalledWith('');
  });
  it('上传未知标记不能被早于提交结果的旧读取清除', async () => {
    await setup(); const old = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(old.promise); const reading = call('load');
    await call('onFileSelected', { target: { files: [sources()[0]] } }); vi.mocked(api.uploadHostFile).mockRejectedValueOnce(new Error('lost')); await call('upload');
    old.resolve({ items: [file], page: 1, pageSize: 20, total: 1 }); await reading; expect(state().uploadUnconfirmed).toBe(true);
  });
  it.each(['preview', 'download', 'openReferences'])('%s 使用权威目录身份和快照而非伪造事件内容', async name => {
    await setup(); vi.mocked(api.previewHostFileContent).mockResolvedValueOnce(new Blob(['ok'], { type: 'text/plain' })); vi.mocked(api.downloadHostFileContent).mockResolvedValueOnce(new Blob(['ok']));
    await call(name, { ...file, id: file.id.toUpperCase(), originalFileName: 'forged', contentType: 'text/html' });
    const operation = name === 'preview' ? api.previewHostFileContent : name === 'download' ? api.downloadHostFileContent : api.listHostFileReferences;
    expect(vi.mocked(operation).mock.calls[0]?.[0]).toBe(file.id); if (name === 'download') expect(api.openHostFileBlob).toHaveBeenCalledWith(expect.any(Blob), file.originalFileName);
  });
  it('预览等待中行修订失效会取消且不能创建旧 URL', async () => {
    await setup(); const pending = deferred<Blob>(); vi.mocked(api.previewHostFileContent).mockReturnValueOnce(pending.promise); const action = call('preview', file);
    const signal = vi.mocked(api.previewHostFileContent).mock.calls[0]?.[1]; state().items = [{ ...file, revision: 2 }]; pending.resolve(new Blob(['old'], { type: 'text/plain' })); await action;
    expect(signal?.aborted).toBe(true); expect(URL.createObjectURL).not.toHaveBeenCalled(); expect(state().previewDialogVisible).toBe(false);
  });
  it('已打开预览在同修订内容元数据变化时也释放URL', async () => {
    await setup(); vi.mocked(api.previewHostFileContent).mockResolvedValueOnce(new Blob(['ok'], { type: 'text/plain' })); await call('preview', file);
    state().items = [{ ...file, contentType: 'image/png' }, other]; await flushPromises(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:file'); expect(state().previewDialogVisible).toBe(false);
  });
  it('预览拒绝实际返回的主动内容类型', async () => {
    await setup(); vi.mocked(api.previewHostFileContent).mockResolvedValueOnce(new Blob(['<script/>'], { type: 'text/html' })); await call('preview', file);
    expect(URL.createObjectURL).not.toHaveBeenCalled(); expect(state().previewDialogVisible).toBe(false);
  });
  it('旧预览关闭回调不能关闭新文件预览', async () => {
    await setup(); vi.mocked(api.previewHostFileContent).mockResolvedValue(new Blob(['ok'], { type: 'text/plain' })); await call('preview', file); await flushPromises();
    const dialog = wrapper!.findAllComponents({ name: 'ElDialog' }).find(item => item.attributes('data-testid') === 'host-files-preview')!;
    const update = dialog.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    await call('closePreviewDialog'); await call('preview', other); update(false); await flushPromises(); expect(state().previewDialogVisible).toBe(true);
  });
  it('下载等待中目标退出目录后不打开旧内容', async () => {
    await setup(); const pending = deferred<Blob>(); vi.mocked(api.downloadHostFileContent).mockReturnValueOnce(pending.promise); const action = call('download', file); state().items = [other];
    pending.resolve(new Blob(['old'])); await action; expect(api.openHostFileBlob).not.toHaveBeenCalled(); expect(vi.mocked(api.downloadHostFileContent).mock.calls[0]?.[1]?.aborted).toBe(true);
  });
  it('引用抽屉页码查询原文件并展示总数', async () => {
    await setup(); vi.mocked(api.listHostFileReferences).mockResolvedValue({ items: [claim], page: 1, pageSize: 20, total: 25 }); await call('openReferences', file);
    expect(state().referencesTotal).toBe(25); vi.mocked(api.listHostFileReferences).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 25 });
    state().referencesPage = 2; await call('loadReferences'); expect(api.listHostFileReferences).toHaveBeenLastCalledWith(file.id, 2, 20, expect.any(AbortSignal));
  });
  it('引用目标失效清理抽屉且迟到结果不接入', async () => {
    await setup(); const pending = deferred<Awaited<ReturnType<typeof api.listHostFileReferences>>>(); vi.mocked(api.listHostFileReferences).mockReturnValueOnce(pending.promise);
    const action = call('openReferences', file); state().items = [other]; pending.resolve({ items: [claim], page: 1, pageSize: 20, total: 1 }); await action;
    expect(state().referencesDrawerVisible).toBe(false); expect(state().references).toEqual([]);
  });
  it('旧引用关闭回调不能关闭新目标抽屉', async () => {
    await setup(); await call('openReferences', file); await flushPromises(); const drawer = wrapper!.findComponent({ name: 'ElDrawer' });
    const update = drawer.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    state().referencesDrawerVisible = false; await call('openReferences', other); update(false); await flushPromises(); expect(state().referencesDrawerVisible).toBe(true);
  });
  it('旧引用分页回调不能改变新目标页码或发起读取', async () => {
    await setup(); await call('openReferences', file); await flushPromises();
    const pagination = wrapper!.findAllComponents({ name: 'ElPagination' }).at(-1)!;
    const update = pagination.vm.$.vnode.props!['onUpdate:currentPage'] as (page: number) => void;
    const change = pagination.vm.$.vnode.props!.onChange as (page: number) => void;
    state().referencesDrawerVisible = false; await call('openReferences', other); const count = vi.mocked(api.listHostFileReferences).mock.calls.length;
    update(2); change(2); await flushPromises(); expect(state().referencesPage).toBe(1); expect(api.listHostFileReferences).toHaveBeenCalledTimes(count);
  });
  it('引用下一页失败清空旧行但保留明确错误和刷新恢复入口', async () => {
    await setup(); vi.mocked(api.listHostFileReferences).mockResolvedValueOnce({ items: [claim], page: 1, pageSize: 20, total: 25 }); await call('openReferences', file);
    vi.mocked(api.listHostFileReferences).mockRejectedValueOnce(new Error('offline')); state().referencesPage = 2; await call('loadReferences'); await flushPromises();
    expect(state().references).toEqual([]); expect(wrapper!.get('[data-testid="host-files-references"]').find('[role="alert"]').exists()).toBe(true);
    vi.mocked(api.listHostFileReferences).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 25 }); await call('loadReferences');
    expect(state().referencesDrawerVisible).toBe(true); expect(state().referencesPage).toBe(2);
  });
  it('孤立引用读取权限不能绕过父Read打开抽屉', async () => {
    await setup(['files.file_references.read']); await call('openReferences', file);
    expect(api.listHostFileReferences).not.toHaveBeenCalled(); expect(state().referencesDrawerVisible).toBe(false);
  });
  it('主体切换清理上传回执，旧失败不恢复它', async () => {
    const session = await setup(); await call('onFileSelected', { target: { files: [sources()[0]] } }); const pending = deferred<typeof file>(); vi.mocked(api.uploadHostFile).mockReturnValueOnce(pending.promise);
    const action = call('upload'); session.currentUser!.sessionId = 'next'; await flushPromises(); pending.reject(new Error('old')); await action;
    expect(state().uploadReceipt).toBeUndefined(); expect(state().uploadUnconfirmed).toBe(false);
  });
});
