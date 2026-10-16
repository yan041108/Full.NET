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

beforeEach(() => { vi.mocked(api.listHostFiles).mockImplementation(async (page = 1, pageSize = 20, options) => ({ items: [{ ...file, folderId: options?.folderId || null }, { ...other, folderId: options?.folderId || null }], page, pageSize, total: 200 })); });
function state() { return wrapper!.vm as unknown as { page: number; pageSize: number; items: typeof file[]; total: number; selectedRows: typeof file[]; searchForm: Record<string, string | undefined>; appliedFilters: { fileName: string }; selectedFolder: typeof folder | null; previewDialogVisible: boolean }; }
const pager = () => wrapper!.findComponent({ name: 'ElPagination' });
describe('文件目录与分页查询意图', () => {
  it('查询修剪条件并回第一页，未应用草稿不进入刷新请求', async () => {
    await setup(); state().page = 3; await call('handleSearch', { fileName: '  private  ' }); await flushPromises();
    expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.slice(0, 3)).toEqual([1, 20, { folderId: '', fileNameContains: 'private' }]);
    state().searchForm = { fileName: 'draft' }; await call('load'); expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.[2]?.fileNameContains).toBe('private');
  });
  it('查询改变立即清旧文件和选择，等待中不能删除旧行', async () => {
    await setup(); select([file]); const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise);
    await call('handleSearch', { fileName: 'new' }); expect(state().items).toEqual([]); expect(state().selectedRows).toEqual([]); await call('remove', file); expect(api.deleteHostFile).not.toHaveBeenCalled();
    pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await flushPromises();
  });
  it('同一查询补读失败保留已确认目录内容', async () => {
    await setup(); vi.mocked(api.listHostFiles).mockRejectedValueOnce(new Error('offline')); await call('load'); expect(state().items.map(item => item.id)).toEqual([file.id, other.id]);
  });
  it('目录事件取权威节点，切换时先清除旧列表', async () => {
    await setup(); const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise);
    await call('onFolderSelected', { ...node, id: folder.id.toUpperCase(), folder: { ...folder, id: folder.id.toUpperCase(), name: 'forged' } });
    expect(state().selectedFolder?.name).toBe(folder.name); expect(state().items).toEqual([]); expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.[2]?.folderId).toBe(folder.id);
    pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await flushPromises();
  });
  it('不是根节点的空目录载荷不能偷偷切回根', async () => {
    await setup(); await call('onFolderSelected', node); await flushPromises(); const count = vi.mocked(api.listHostFiles).mock.calls.length;
    await call('onFolderSelected', { id: 'unknown', label: 'forged', folder: null }); expect(state().selectedFolder?.id).toBe(folder.id); expect(api.listHostFiles).toHaveBeenCalledTimes(count);
  });
  it('选中目录从新树消失时回根第一页并清空旧目录行', async () => {
    await setup(); await call('onFolderSelected', node); await flushPromises(); state().page = 3; await call('load');
    const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise); vi.mocked(api.listHostFolderTree).mockResolvedValueOnce([]);
    const action = call('loadFolders'); await flushPromises(); expect(state().selectedFolder).toBeNull(); expect(state().page).toBe(1); expect(state().items).toEqual([]);
    pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await action;
  });
  it('分页大小变化仅从change查询一次并回第一页', async () => {
    await setup(); state().page = 3; await call('load'); const count = vi.mocked(api.listHostFiles).mock.calls.length; const component = pager();
    component.vm.$emit('update:page-size', 50); component.vm.$emit('update:current-page', 2); component.vm.$emit('size-change', 50); component.vm.$emit('current-change', 2); component.vm.$emit('change', 2, 50); await flushPromises();
    expect(api.listHostFiles).toHaveBeenCalledTimes(count + 1); expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.slice(0, 2)).toEqual([1, 50]);
    pager().vm.$emit('change', 1, 50); await flushPromises(); expect(api.listHostFiles).toHaveBeenCalledTimes(count + 1);
  });
  it('普通翻页使用目标页且不重复查询', async () => {
    vi.mocked(api.listHostFiles).mockImplementation(async (page = 1, pageSize = 20) => ({ items: [file, other], page, pageSize, total: 60 }));
    await setup(); const component = pager(); component.vm.$emit('update:current-page', 2); component.vm.$emit('current-change', 2); component.vm.$emit('change', 2, 20); await flushPromises();
    expect(api.listHostFiles).toHaveBeenCalledTimes(2); expect(state().page).toBe(2); expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.[0]).toBe(2);
  });
  it('旧查询分页回调不能改变新筛选的页码或大小', async () => {
    await setup(); const props = pager().vm.$.vnode.props!; const update = props['onUpdate:current-page'] as (page: number) => void, change = props.onChange as (page: number, size: number) => void;
    await call('handleSearch', { fileName: 'next' }); await flushPromises(); const count = vi.mocked(api.listHostFiles).mock.calls.length; update(4); change(4, 50); await flushPromises();
    expect(state().page).toBe(1); expect(state().pageSize).toBe(20); expect(api.listHostFiles).toHaveBeenCalledTimes(count);
  });
  it('旧查询栏回调不能在新主体应用旧筛选', async () => {
    const session = await setup(); const search = wrapper!.findComponent({ name: 'ArtSearchBar' }).vm.$.vnode.props!.onSearch as (params: Record<string, string>) => void;
    session.currentUser!.sessionId = 'next'; await flushPromises(); const count = vi.mocked(api.listHostFiles).mock.calls.length; search({ fileName: 'old' }); await flushPromises();
    expect(state().appliedFilters.fileName).toBe(''); expect(api.listHostFiles).toHaveBeenCalledTimes(count);
  });
  it('空越界页只向当前末页恢复一次', async () => {
    await setup(); vi.mocked(api.listHostFiles).mockResolvedValueOnce({ items: [], page: 4, pageSize: 20, total: 21 }); state().page = 4; await call('load');
    expect(state().page).toBe(2); expect(api.listHostFiles).toHaveBeenCalledTimes(3); expect(vi.mocked(api.listHostFiles).mock.calls.at(-1)?.[0]).toBe(2);
  });
  it('恢复页仍为空且总数继续变化时不循环读取', async () => {
    await setup(); vi.mocked(api.listHostFiles).mockResolvedValueOnce({ items: [], page: 4, pageSize: 20, total: 21 }).mockResolvedValueOnce({ items: [], page: 2, pageSize: 20, total: 0 }); state().page = 4; await call('load');
    expect(api.listHostFiles).toHaveBeenCalledTimes(3); expect(state().items).toEqual([]);
    expect(state().page).toBe(2); expect(pager().props('total')).toBe(0); expect(pager().props('pageCount')).toBe(2);
  });
  it('错误页回包不污染当前查询且不解除未知上传限制', async () => {
    await setup(); await call('onFileSelected', { target: { files: [new File(['x'], 'a.txt')] } }); vi.mocked(api.uploadHostFile).mockRejectedValueOnce(new Error('lost')); await call('upload');
    vi.mocked(api.listHostFiles).mockResolvedValueOnce({ items: [other], page: 2, pageSize: 20, total: 1 }); await call('load'); await call('upload'); expect(api.uploadHostFile).toHaveBeenCalledOnce(); expect(state().items.map(item => item.id)).toEqual([file.id, other.id]);
  });
  it('显式新查询立即关闭已打开预览并回收URL', async () => {
    await setup(); vi.mocked(api.previewHostFileContent).mockResolvedValueOnce(new Blob(['plain'], { type: 'text/plain' })); await call('preview', file);
    const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise); await call('handleSearch', { fileName: 'new' });
    expect(state().previewDialogVisible).toBe(false); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:file'); pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await flushPromises();
  });
  it('查询改变后旧删除确认不再写入', async () => {
    await setup(); const action = call('remove', file); const pending = deferred<Awaited<ReturnType<typeof api.listHostFiles>>>(); vi.mocked(api.listHostFiles).mockReturnValueOnce(pending.promise);
    await call('handleSearch', { fileName: 'new' }); await call('finishConfirmation', true); await action; expect(api.deleteHostFile).not.toHaveBeenCalled();
    pending.resolve({ items: [], page: 1, pageSize: 20, total: 0 }); await flushPromises();
  });
  it('撤权后直接调用查询重置也不能写入新筛选', async () => {
    const session = await setup(); session.currentUser!.permissions = []; await flushPromises(); await call('handleSearch', { fileName: 'forged' }); await call('resetSearch');
    expect(state().appliedFilters.fileName).toBe(''); expect(api.listHostFiles).toHaveBeenCalledOnce();
  });
  it('主分页与表体同处卡片测量容器且使用固定底部类', async () => {
    await setup(); const main = wrapper!.get('.art-table-card .art-crud-table-main'); expect(main.find('.el-table').exists()).toBe(true); expect(main.find('.art-table-pagination').exists()).toBe(true);
    expect(main.find('.custom-pagination').exists()).toBe(false);
  });
});
