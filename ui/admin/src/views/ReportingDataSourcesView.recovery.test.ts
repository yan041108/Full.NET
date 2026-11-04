import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElForm, ElInput, ElPagination, ElMessage } from 'element-plus';
import View from './ReportingDataSourcesView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtSearchBar from '../framework/art-design/components/ArtSearchBar.vue';
import * as api from '../api/reporting-data-sources';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { dataSource as source, dataSourceRow as row } from '../test/reporting-data-source-fixtures';

vi.mock('../api/reporting-data-sources', () => ({ listReportingDataSources: vi.fn(), getReportingDataSource: vi.fn(),
  createReportingDataSource: vi.fn(), updateReportingDataSource: vi.fn(), disableReportingDataSource: vi.fn(),
  deleteReportingDataSource: vi.fn(), testReportingDataSource: vi.fn() }));
const permissions = ['read', 'create', 'update', 'delete', 'test'].map(action => `reporting.data_sources.${action}`);
const result = (items = [row], page = 1, total = items.length, pageSize = 20) => ({ items, page, pageSize, total });
const failure = { title: '暂时不可用', code: 'fixture.unavailable', status: 503 };
let wrapper: VueWrapper;
interface Controls { submit: () => void; update: (open: boolean) => void; input: (key: string, value: unknown) => void; retry: () => void }
interface State {
  items: typeof row[]; page: number; pageSize: number; total: number; loading: boolean; changing: boolean;
  problem?: unknown; editorProblem?: unknown; editorOpen: boolean; editorForm: Record<string, unknown>;
  createBlocked: boolean; rowBlocks: Map<string, number>; receipt?: { kind: string; outcome: string; id?: string };
  editorControls: Controls; listControls: { refresh: () => void; change: (page: number, size: number) => void };
  tableRows: (typeof row & { edit: () => void })[];
  load: () => Promise<unknown>; openCreate: () => void; openEdit: (target: typeof row) => Promise<void>;
  submitEditor: () => Promise<void>; retryEditor: () => Promise<void>; acknowledgeRecovery: (id?: string) => Promise<void>;
  runDisable: (target: typeof row) => Promise<void>; runDelete: (target: typeof row) => Promise<void>;
  runTest: (target: typeof row) => Promise<void>; finishConfirmation: (confirmed: boolean) => void;
}
const state = () => wrapper.vm as unknown as State;
async function setup(route?: { name: string }) {
  const context = createShareSession(permissions);
  wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: {} },
    provide: route ? { [routeLocationKey as symbol]: route } : {} } });
  await flushPromises(); return context.session;
}
async function validate(promise = Promise.resolve(true)) {
  await flushPromises(); const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('open') && item.props('confirmTestId') === 'reporting-data-source-editor-submit')!;
  vi.spyOn(dialog.getComponent(ElForm).vm.$.exposed!, 'validate').mockImplementation(() => promise);
}
async function create(promise = Promise.resolve(true)) { state().openCreate(); Object.assign(state().editorForm, { name: source.name, serverHost: source.serverHost, databaseName: source.databaseName, username: source.username, password: 'fixture-only' }); await validate(promise); }
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listReportingDataSources).mockResolvedValue(result()); vi.mocked(api.getReportingDataSource).mockResolvedValue(source);
  vi.mocked(api.createReportingDataSource).mockResolvedValue(source); vi.mocked(api.updateReportingDataSource).mockResolvedValue({ ...source, version: 2 });
  vi.mocked(api.disableReportingDataSource).mockResolvedValue({ ...source, version: 2, isEnabled: false });
  vi.mocked(api.deleteReportingDataSource).mockResolvedValue(true); vi.mocked(api.testReportingDataSource).mockResolvedValue({ succeeded: true, message: '连接成功' });
});
afterEach(() => { wrapper?.unmount(); ElMessage.closeAll(); vi.restoreAllMocks(); });

describe('报表数据源完整流程恢复', () => {
  it('同查询补读失败保留目录并提供刷新，换页失败清除旧页', async () => {
    await setup(); vi.mocked(api.listReportingDataSources).mockRejectedValue(failure); await state().load();
    expect(state().items).toEqual([row]); expect(state().problem).toEqual(failure);
    state().page = 2; await state().load(); expect(state().items).toEqual([]);
  });
  it('拒绝错页响应，重试成功清除列表错误', async () => {
    await setup(); vi.mocked(api.listReportingDataSources).mockResolvedValueOnce(result([row], 2)); await state().load();
    expect(state().problem).toBeDefined(); await state().load(); expect(state().problem).toBeUndefined();
  });
  it('真实分页下一页保持新页并且只发一个读取', async () => {
    vi.mocked(api.listReportingDataSources).mockImplementation(async query => result([row], query?.page ?? 1, 60, query?.pageSize ?? 20));
    await setup(); await wrapper.getComponent(ElPagination).get('button.btn-next').trigger('click'); await flushPromises();
    expect(state().page).toBe(2); expect(api.listReportingDataSources).toHaveBeenCalledTimes(2);
    expect(api.listReportingDataSources).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }), expect.any(AbortSignal));
  });
  it('页大小事件集中发出时回第一页并去重', async () => {
    await setup(); const pagination = wrapper.getComponent(ElPagination); pagination.vm.$emit('update:page-size', 50);
    pagination.vm.$emit('change', 2, 50); pagination.vm.$emit('change', 1, 50); await flushPromises();
    expect(api.listReportingDataSources).toHaveBeenCalledTimes(2); expect(state().page).toBe(1);
  });
  it('空末页只回退一次，非空页不会因 COUNT 变化丢失', async () => {
    await setup(); state().page = 3; vi.mocked(api.listReportingDataSources).mockResolvedValueOnce(result([], 3, 21)).mockResolvedValueOnce(result([row], 2, 0));
    await state().load(); expect(state().page).toBe(2); expect(state().items).toEqual([row]); expect(api.listReportingDataSources).toHaveBeenCalledTimes(3);
  });
  it('旧列表和行按钮回调在刷新后不能重放', async () => {
    await setup(); const refresh = wrapper.getComponent(ArtTableHeader).vm.$.vnode.props!.onRefresh as () => void;
    const edit = wrapper.findAllComponents(ArtTableActionButton).find(button => button.props('testId') === 'reporting-data-source-edit')!.vm.$.vnode.props!.onClick as () => void;
    await state().load(); refresh(); edit(); await flushPromises(); expect(api.listReportingDataSources).toHaveBeenCalledTimes(2); expect(api.getReportingDataSource).not.toHaveBeenCalled();
  });
  it('搜索组件内部旧输入事件不能修改刷新后的查询模型', async () => {
    await setup(); const oldInput = wrapper.getComponent(ArtSearchBar).getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    await state().load(); await flushPromises(); oldInput('旧名称');
    wrapper.getComponent(ArtSearchBar).vm.$emit('search'); await flushPromises();
    expect(api.listReportingDataSources).toHaveBeenLastCalledWith(expect.objectContaining({ nameContains: undefined }), expect.any(AbortSignal));
  });
  it('详情读取失败保留可关闭容器，重试只重读原目标', async () => {
    vi.mocked(api.getReportingDataSource).mockRejectedValueOnce(failure); await setup(); await state().openEdit(row);
    expect(state().editorOpen).toBe(true); expect(state().editorProblem).toEqual(failure); expect(state().editorForm.serverHost).toBe('');
    await state().retryEditor(); expect(state().editorProblem).toBeUndefined(); expect(state().editorForm.serverHost).toBe(source.serverHost);
    expect(api.updateReportingDataSource).not.toHaveBeenCalled();
  });
  it('详情重读不能低于此前已知修订，也不能恢复旧连接元数据', async () => {
    vi.mocked(api.getReportingDataSource).mockResolvedValueOnce({ ...source, version: 4, serverHost: 'latest-fixture' })
      .mockResolvedValueOnce({ ...source, version: 2, serverHost: 'stale-fixture' });
    await setup(); await state().openEdit(row); await state().retryEditor();
    expect(state().editorForm.serverHost).toBe('latest-fixture'); expect(state().editorProblem).toBeDefined();
    await validate(); await state().submitEditor(); expect(api.updateReportingDataSource).not.toHaveBeenCalled();
  });
  it('校验期间输入改动取消旧提交，不把新密码带入旧请求', async () => {
    await setup(); const pending = deferred<boolean>(); await create(pending.promise); const action = state().submitEditor();
    state().editorForm.password = 'changed-fixture'; pending.resolve(true); await action;
    expect(api.createReportingDataSource).not.toHaveBeenCalled(); expect(state().createBlocked).toBe(false);
  });
  it('旧编辑器输入和提交回调不能作用于重开的表单', async () => {
    await setup(); await create(); const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'reporting-data-source-editor-submit')!;
    const submit = dialog.vm.$.vnode.props!.onConfirm as () => void, close = dialog.vm.$.vnode.props!['onUpdate:open'] as (open: boolean) => void;
    const input = dialog.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    state().editorOpen = false; await create(); input('旧输入'); submit(); close(false); await flushPromises();
    expect(state().editorOpen).toBe(true); expect(state().editorForm.name).toBe(source.name); expect(api.createReportingDataSource).not.toHaveBeenCalled();
  });
  it.each(['create', 'update'] as const)('%s 确认成功后先保存回执再关闭，补读失败不会改成失败', async kind => {
    await setup(); if (kind === 'create') await create(); else { await state().openEdit(row); await validate(); }
    vi.mocked(api.listReportingDataSources).mockRejectedValueOnce(failure); await state().submitEditor();
    expect(state().receipt).toMatchObject({ kind, id: source.id, outcome: 'succeeded' }); expect(state().editorOpen).toBe(false);
    expect(state().editorForm.password).toBe(''); expect(state().changing).toBe(false); expect(state().problem).toEqual(failure);
  });
  it('创建响应丢失禁止自动重发，关闭重开仍要求明确确认且确认不发 POST', async () => {
    await setup(); await create(); vi.mocked(api.createReportingDataSource).mockRejectedValueOnce(failure); await state().submitEditor();
    expect(state().receipt?.outcome).toBe('unconfirmed'); state().editorOpen = false; await create(); await state().submitEditor();
    expect(api.createReportingDataSource).toHaveBeenCalledOnce(); state().acknowledgeRecovery(); state().finishConfirmation(true); await flushPromises();
    expect(state().createBlocked).toBe(false); expect(api.createReportingDataSource).toHaveBeenCalledOnce();
    expect(JSON.stringify(state().receipt)).not.toContain('fixture-only'); expect(JSON.stringify(state().receipt)).not.toContain(source.serverHost);
  });
  it('创建发送后关闭标记结果未确认，迟到结果不关闭新表单', async () => {
    const pending = deferred<typeof source>(); vi.mocked(api.createReportingDataSource).mockReturnValueOnce(pending.promise);
    await setup(); await create(); const action = state().submitEditor(); await flushPromises(); state().editorOpen = false;
    expect(state().receipt?.outcome).toBe('unconfirmed'); await create(); pending.resolve(source); await action;
    expect(state().editorOpen).toBe(true); expect(state().createBlocked).toBe(true); expect(api.listReportingDataSources).toHaveBeenCalledOnce();
  });
  it('确定拒绝的创建允许修正后再手动提交', async () => {
    await setup(); await create(); vi.mocked(api.createReportingDataSource).mockRejectedValueOnce({ title: '输入无效', status: 400, code: 'fixture.invalid' });
    await state().submitEditor(); expect(state().receipt?.outcome).toBe('failed'); expect(state().createBlocked).toBe(false);
    await state().submitEditor(); expect(api.createReportingDataSource).toHaveBeenCalledTimes(2);
  });
  it('成功更新后的旧修订补读不能重新显示或重新编辑旧凭据', async () => {
    await setup(); await state().openEdit(row); await validate(); await state().submitEditor();
    expect(state().receipt?.outcome).toBe('succeeded'); expect(state().items).toEqual([]); expect(state().problem).toBeDefined();
    await state().openEdit(row); expect(api.getReportingDataSource).toHaveBeenCalledOnce();
    vi.mocked(api.listReportingDataSources).mockResolvedValueOnce(result([{ ...row, version: 2 }])); await state().load(); expect(state().items[0]!.version).toBe(2);
  });
  it.each(['disable', 'delete'] as const)('%s 成功后的补读失败保留成功回执且不重发', async kind => {
    await setup(); vi.mocked(api.listReportingDataSources).mockRejectedValueOnce(failure);
    const action = kind === 'disable' ? state().runDisable(row) : state().runDelete(row); state().finishConfirmation(true); await action;
    expect(state().receipt).toMatchObject({ kind, outcome: 'succeeded' }); expect(state().items).toEqual([]); expect(state().changing).toBe(false);
    expect(kind === 'disable' ? api.disableReportingDataSource : api.deleteReportingDataSource).toHaveBeenCalledOnce();
  });
  it('删除成功后旧列表不能复活已删行', async () => {
    await setup(); const action = state().runDelete(row); state().finishConfirmation(true); await action;
    expect(state().items).toEqual([]); expect(state().receipt?.outcome).toBe('succeeded'); await state().runDelete(row); expect(api.deleteReportingDataSource).toHaveBeenCalledOnce();
  });
  it('连接测试返回业务失败仍显示已完成回执和结果，补读失败不会重试连接', async () => {
    await setup(); vi.mocked(api.testReportingDataSource).mockResolvedValueOnce({ succeeded: false, message: '连接被拒绝' });
    vi.mocked(api.listReportingDataSources).mockRejectedValueOnce(failure); await state().runTest(row);
    expect(state().receipt).toMatchObject({ kind: 'test', outcome: 'succeeded' }); expect(wrapper.text()).toContain('连接被拒绝'); expect(api.testReportingDataSource).toHaveBeenCalledOnce();
  });
  it('未知停用结果需要新修订权威读取或明确解锁，旧页读取不解除保护', async () => {
    await setup(); vi.mocked(api.disableReportingDataSource).mockRejectedValueOnce(failure); const action = state().runDisable(row); state().finishConfirmation(true); await action;
    await state().load(); expect(state().rowBlocks.has(row.id)).toBe(true); await state().runTest(row); expect(api.testReportingDataSource).not.toHaveBeenCalled();
    vi.mocked(api.listReportingDataSources).mockResolvedValueOnce(result([{ ...row, version: 2, isEnabled: false }])); await state().load(); expect(state().rowBlocks.has(row.id)).toBe(false);
  });
  it('更新结果未知可在编辑器重读新修订，原密码不进入恢复记录', async () => {
    await setup(); await state().openEdit(row); state().editorForm.password = 'fixture-only'; await validate();
    vi.mocked(api.updateReportingDataSource).mockRejectedValueOnce(failure); await state().submitEditor();
    await flushPromises(); expect(state().rowBlocks.has(row.id)).toBe(true);
    expect(wrapper.findAllComponents(ElButton).some(button => button.attributes('data-testid') === 'reporting-data-source-create-recover')).toBe(true);
    vi.mocked(api.getReportingDataSource).mockResolvedValueOnce({ ...source, version: 2 }); await state().retryEditor();
    expect(state().rowBlocks.has(row.id)).toBe(false); expect(state().editorForm.password).toBe(''); expect(JSON.stringify(state().receipt)).not.toContain('fixture-only');
  });
  it('未知行结果的人工确认只解除保护，不再次调用停用接口', async () => {
    await setup(); vi.mocked(api.disableReportingDataSource).mockRejectedValueOnce(failure); const action = state().runDisable(row); state().finishConfirmation(true); await action;
    const recovery = state().acknowledgeRecovery(row.id); state().finishConfirmation(true); await recovery;
    expect(state().rowBlocks.has(row.id)).toBe(false); expect(api.disableReportingDataSource).toHaveBeenCalledOnce();
  });
  it('旧确认回调不能确认下一次删除或恢复提示', async () => {
    await setup(); const first = state().runDelete(row); await flushPromises(); const dialog = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'reporting-data-source-confirm')!;
    const old = dialog.vm.$.vnode.props!.onConfirm as () => void; state().finishConfirmation(false); await first;
    const second = state().runDelete(row); await flushPromises(); old(); await flushPromises(); expect(api.deleteReportingDataSource).not.toHaveBeenCalled();
    state().finishConfirmation(false); await second;
  });
  it('离开路由立即清空凭据并取消写入接入，迟到结果不刷新或提示', async () => {
    const route = reactive({ name: 'reporting-data-sources' }); const pending = deferred<typeof source>(); vi.mocked(api.createReportingDataSource).mockReturnValueOnce(pending.promise);
    await setup(route); await create(); const action = state().submitEditor(); await flushPromises(); route.name = 'home';
    expect(state().editorForm.password).toBe(''); expect(vi.mocked(api.createReportingDataSource).mock.calls[0]![1]!.aborted).toBe(true);
    pending.resolve(source); await action; expect(ElMessage.success).not.toHaveBeenCalled(); expect(api.listReportingDataSources).toHaveBeenCalledOnce();
  });
});
