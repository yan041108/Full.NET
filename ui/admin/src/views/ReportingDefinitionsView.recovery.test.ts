import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElForm, ElInput, ElDrawer, ElMessage } from 'element-plus';
import View from './ReportingDefinitionsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import * as api from '../api/reporting-definitions';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { dataSourceRow } from '../test/reporting-data-source-fixtures';
import { reportingDefinition as definition, reportingGroup as group, reportingPort as port, reportingVersion as version, reportingInitialVersion as initialVersion } from '../test/reporting-definition-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingGroups: vi.fn(), createReportingGroup: vi.fn(), updateReportingGroup: vi.fn(), deleteReportingGroup: vi.fn(), listReportingDefinitions: vi.fn(), createReportingDefinition: vi.fn(), updateReportingDefinition: vi.fn(), deleteReportingDefinition: vi.fn(), listReportingQueryPorts: vi.fn(), publishReportingDefinition: vi.fn(), listReportingDefinitionVersions: vi.fn() }));
vi.mock('../api/reporting-data-sources', () => ({ listReportingDataSources: vi.fn() }));
const permissions = ['reporting.definitions.read', 'reporting.definitions.create', 'reporting.definitions.update', 'reporting.definitions.delete', 'reporting.definitions.publish', 'reporting.groups.read', 'reporting.groups.create', 'reporting.groups.update', 'reporting.groups.delete', 'reporting.query_ports.read', 'reporting.data_sources.read'];
const failure = { title: '目录暂时不可用', code: 'fixture.unavailable', status: 503 };
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
let wrapper: VueWrapper;
interface State {
  groups: typeof group[]; definitions: typeof definition[]; queryPorts: typeof port[]; dataSourceOptions: { value: string }[];
  selectedGroupId?: string; groupEditorOpen: boolean; definitionEditorOpen: boolean; versionsDrawerOpen: boolean; acting: boolean;
  groupForm: Record<string, unknown>; definitionForm: typeof definition & { dataSourceId: string }; versions: typeof version[];
  problem?: unknown; versionsProblem?: unknown; catalogProblems: Record<string, unknown>; publishNote: string;
  receipt?: { resource: string; kind: string; outcome: string; id?: string; versionNumber?: number }; creationBlocks: Set<string>; rowBlocks: Map<string, number>;
  loadPage: () => Promise<void>; openCreateGroup: () => void; openEditGroup: (target: typeof group) => void;
  submitGroup: () => Promise<void>; openCreateDefinition: () => void; openEditDefinition: (target: typeof definition) => void;
  submitDefinition: () => Promise<void>; publishCurrentDefinition: (target: typeof definition) => Promise<void>;
  confirmDeleteGroup: (target: typeof group) => Promise<void>; confirmDeleteDefinition: (target: typeof definition) => Promise<void>;
  openVersions: (target: typeof definition) => Promise<void>; retryVersions: () => Promise<void>; finishConfirmation: (value: boolean) => void;
  acknowledgeRecovery: (resource: 'group' | 'definition', id?: string) => Promise<void>;
}
const state = () => wrapper.vm as unknown as State;
function prepareCreate(resource: 'group' | 'definition'): void {
  if (resource === 'group') { state().openCreateGroup(); state().groupForm.name = '新分组'; }
  else { state().openCreateDefinition(); Object.assign(state().definitionForm, { definitionKey: 'new-fixture', name: '新报表' }); }
}
async function setup(route?: { name: string }, allowed = permissions) {
  const context = createShareSession(allowed); wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: {} }, provide: route ? { [routeLocationKey as symbol]: route } : {} } }); await flushPromises(); return context.session;
}
const dialog = () => wrapper.findAllComponents(ArtFormDialog).find(item => item.props('open') && item.findComponent(ElForm).exists())!;
async function validate(promise = Promise.resolve(true)) { await flushPromises(); vi.spyOn(dialog().getComponent(ElForm).vm.$.exposed!, 'validate').mockImplementation(() => promise); }
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listReportingGroups).mockResolvedValue([{ ...group }]); vi.mocked(api.listReportingDefinitions).mockResolvedValue([{ ...definition, parameterSchema: definition.parameterSchema.map(p => ({ ...p })) }]);
  vi.mocked(api.listReportingQueryPorts).mockResolvedValue([port]); vi.mocked(listReportingDataSources).mockResolvedValue({ items: [dataSourceRow], page: 1, pageSize: 100, total: 1 });
  vi.mocked(api.createReportingGroup).mockImplementation(async body => ({ ...group, ...body, parentId: body.parentId ?? null, id: otherId, version: 1 }));
  vi.mocked(api.createReportingDefinition).mockImplementation(async body => ({ ...definition, ...body, description: body.description ?? null, layoutConfigJson: body.layoutConfigJson ?? '{}', id: otherId, version: 1, latestPublishedVersionNumber: 0 }));
  vi.mocked(api.updateReportingGroup).mockImplementation(async (id, body) => ({ ...group, ...body, parentId: body.parentId ?? null, id, version: body.version + 1 }));
  vi.mocked(api.updateReportingDefinition).mockImplementation(async (id, body) => ({ ...definition, ...body, description: body.description ?? null, layoutConfigJson: body.layoutConfigJson ?? '{}', id, version: body.version + 1 }));
  vi.mocked(api.deleteReportingDefinition).mockResolvedValue(true); vi.mocked(api.deleteReportingGroup).mockResolvedValue(true);
  vi.mocked(api.publishReportingDefinition).mockResolvedValue(version); vi.mocked(api.listReportingDefinitionVersions).mockResolvedValue([initialVersion, version]);
});
afterEach(() => { wrapper?.unmount(); ElMessage.closeAll(); vi.restoreAllMocks(); });
describe('报表分组、定义与发布恢复', () => {
  it('辅助目录失败不吞掉定义目录，重试保留原筛选与草稿', async () => {
    vi.mocked(api.listReportingQueryPorts).mockRejectedValueOnce(failure); await setup();
    expect(state().definitions).toHaveLength(1); expect(state().groups).toHaveLength(1); expect(state().catalogProblems.ports).toEqual(failure);
    await state().loadPage(); expect(state().catalogProblems.ports).toBeUndefined(); expect(state().queryPorts).toEqual([port]);
  });
  it('同修订目录重读不覆盖已编辑参数，端口内容变化停止提交', async () => {
    await setup(); state().openEditDefinition(definition); state().definitionForm.parameterSchema[0]!.displayName = '手工名称';
    await state().loadPage(); expect(state().definitionForm.parameterSchema[0]!.displayName).toBe('手工名称');
    vi.mocked(api.listReportingQueryPorts).mockResolvedValueOnce([{ ...port, supportedProviderKeys: ['mysql'] }]); await state().loadPage(); await validate(); await state().submitDefinition();
    expect(api.updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('目录就绪失效后恢复同内容，也不能重放旧确认或继续旧校验', async () => {
    await setup(); state().openEditDefinition(definition); const pending = deferred<boolean>(); await validate(pending.promise);
    const old = dialog().vm.$.vnode.props!.onConfirm as () => void, action = state().submitDefinition();
    vi.mocked(listReportingDataSources).mockRejectedValueOnce(failure); await state().loadPage(); await state().loadPage();
    pending.resolve(true); await action; old(); await flushPromises(); expect(api.updateReportingDefinition).not.toHaveBeenCalled();
    expect(state().definitionEditorOpen).toBe(true); expect(state().definitionForm.name).toBe(definition.name);
  });
  it('允许把端口的可选参数加强为必填，不能把必填参数降级', async () => {
    vi.mocked(api.listReportingQueryPorts).mockResolvedValueOnce([{ ...port, parameters: [{ ...port.parameters[0]!, isRequired: false }] }]);
    await setup(); state().openEditDefinition(definition); await validate(); await state().submitDefinition(); expect(api.updateReportingDefinition).toHaveBeenCalledOnce();
  });
  it('必填端口参数不能用可选草稿绕过保存门槛', async () => {
    await setup(); state().openEditDefinition(definition); state().definitionForm.parameterSchema[0]!.isRequired = false; await validate(); await state().submitDefinition();
    expect(api.updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('读取全部数据源页而非截断第一个 100 条，去重并保留可用选择', async () => {
    const first = Array.from({ length: 100 }, (_, index) => ({ ...dataSourceRow, id: '019bc2b1-2a40-7cc3-8992-' + (index + 1000).toString(16).padStart(12, '0') }));
    vi.mocked(listReportingDataSources).mockImplementation(async query => ({ items: query?.page === 1 ? first : [{ ...dataSourceRow, id: otherId }], page: query?.page ?? 1, pageSize: 100, total: 101 }));
    await setup(); expect(state().dataSourceOptions).toHaveLength(101); expect(listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it('没有辅助目录权限不预取也不允许绕过选择提交', async () => {
    await setup(undefined, ['reporting.definitions.read', 'reporting.definitions.create', 'reporting.groups.read']); state().openCreateDefinition(); await validate(); await state().submitDefinition();
    expect(listReportingDataSources).not.toHaveBeenCalled(); expect(api.listReportingQueryPorts).not.toHaveBeenCalled(); expect(api.createReportingDefinition).not.toHaveBeenCalled();
  });
  it('数据源欠量页不能冒充完整目录或开放保存', async () => {
    vi.mocked(listReportingDataSources).mockResolvedValue({ items: [dataSourceRow], page: 1, pageSize: 100, total: 101 });
    await setup(); expect(state().catalogProblems.sources).toBeDefined(); expect(state().dataSourceOptions).toEqual([]);
    state().openCreateDefinition(); await validate(); await state().submitDefinition(); expect(api.createReportingDefinition).not.toHaveBeenCalled();
  });
  it.each(['group', 'definition'] as const)('%s 健康创建确认结果并清理表单，不被自己的关闭取消', async resource => {
    await setup(); if (resource === 'group') { state().openCreateGroup(); state().groupForm.name = '新分组'; }
    else { state().openCreateDefinition(); Object.assign(state().definitionForm, { definitionKey: 'new-fixture', name: '新报表' }); }
    await validate(); await (resource === 'group' ? state().submitGroup() : state().submitDefinition());
    expect(state().receipt).toMatchObject({ resource, kind: 'create', outcome: 'succeeded', id: otherId });
    expect(resource === 'group' ? state().groupEditorOpen : state().definitionEditorOpen).toBe(false); expect(state().acting).toBe(false);
  });
  it.each(['group', 'definition'] as const)('%s 创建已发送后关闭保留未知门槛，迟到结果不修改新编辑器', async resource => {
    const pending = deferred<any>(); await setup();
    prepareCreate(resource);
    if (resource === 'group') vi.mocked(api.createReportingGroup).mockReturnValueOnce(pending.promise);
    else vi.mocked(api.createReportingDefinition).mockReturnValueOnce(pending.promise);
    await validate(); const action = resource === 'group' ? state().submitGroup() : state().submitDefinition(); await flushPromises();
    if (resource === 'group') { state().groupEditorOpen = false; state().openCreateGroup(); } else { state().definitionEditorOpen = false; state().openCreateDefinition(); }
    expect(state().receipt?.outcome).toBe('unconfirmed'); pending.resolve(resource === 'group' ? { ...group, id: otherId } : { ...definition, id: otherId }); await action;
    expect(state().creationBlocks.has(resource)).toBe(true); expect(resource === 'group' ? state().groupEditorOpen : state().definitionEditorOpen).toBe(true); expect(ElMessage.success).not.toHaveBeenCalled();
  });
  it.each(['group', 'definition'] as const)('%s 校验前冻结草稿，校验中输入变化取消旧提交', async resource => {
    await setup(); const pending = deferred<boolean>(); if (resource === 'group') state().openEditGroup(group); else state().openEditDefinition(definition); await validate(pending.promise);
    const action = resource === 'group' ? state().submitGroup() : state().submitDefinition();
    if (resource === 'group') state().groupForm.name = '后改'; else state().definitionForm.parameterSchema[0]!.defaultValue = '2027'; pending.resolve(true); await action;
    expect(resource === 'group' ? api.updateReportingGroup : api.updateReportingDefinition).not.toHaveBeenCalled(); expect(state().creationBlocks.size).toBe(0);
  });
  it('分组编辑保持现有父组，不因表单未显示父组而隐式移动到根', async () => {
    const child = { ...group, parentId: otherId }; vi.mocked(api.listReportingGroups).mockResolvedValueOnce([child, { ...group, id: otherId }]);
    await setup(); state().openEditGroup(child); await validate(); await state().submitGroup();
    expect(api.updateReportingGroup).toHaveBeenCalledWith(child.id, expect.objectContaining({ parentId: otherId }), expect.any(AbortSignal));
  });
  it.each(['group', 'definition'] as const)('%s 更新先确认再关闭和补读，补读失败不降级成功', async resource => {
    await setup(); if (resource === 'group') state().openEditGroup(group); else state().openEditDefinition(definition); await validate();
    vi.mocked(api.listReportingGroups).mockRejectedValueOnce(failure); vi.mocked(api.listReportingDefinitions).mockRejectedValueOnce(failure);
    await (resource === 'group' ? state().submitGroup() : state().submitDefinition());
    expect(state().receipt).toMatchObject({ resource, kind: 'update', outcome: 'succeeded' }); expect(state().acting).toBe(false);
    expect(resource === 'group' ? state().groupEditorOpen : state().definitionEditorOpen).toBe(false);
  });
  it.each(['group', 'definition'] as const)('%s 未知创建关闭重开仍受保护，解锁确认不自动创建', async resource => {
    await setup(); prepareCreate(resource); await validate();
    vi.mocked(resource === 'group' ? api.createReportingGroup : api.createReportingDefinition).mockRejectedValueOnce(failure);
    await (resource === 'group' ? state().submitGroup() : state().submitDefinition());
    if (resource === 'group') state().groupEditorOpen = false; else state().definitionEditorOpen = false;
    prepareCreate(resource);
    await validate(); await (resource === 'group' ? state().submitGroup() : state().submitDefinition());
    expect(resource === 'group' ? api.createReportingGroup : api.createReportingDefinition).toHaveBeenCalledOnce();
    const recovery = state().acknowledgeRecovery(resource); state().finishConfirmation(true); await recovery;
    expect(state().creationBlocks.has(resource)).toBe(false); expect(resource === 'group' ? api.createReportingGroup : api.createReportingDefinition).toHaveBeenCalledOnce();
  });
  it('发布成功补读失败仍保留精确发布版本，不自动重发发布', async () => {
    await setup(); vi.mocked(api.listReportingDefinitions).mockRejectedValueOnce(failure); await state().publishCurrentDefinition(definition);
    expect(state().receipt).toMatchObject({ resource: 'definition', kind: 'publish', outcome: 'succeeded', versionNumber: 2 });
    expect(state().definitions[0]!.latestPublishedVersionNumber).toBe(2); expect(state().definitions[0]!.version).toBe(2);
    await state().publishCurrentDefinition(definition); expect(api.publishReportingDefinition).toHaveBeenCalledOnce();
  });
  it('发布响应错配不能反馈成功或补读，保留未知结果保护', async () => {
    await setup(); vi.mocked(api.publishReportingDefinition).mockResolvedValueOnce({ ...version, definitionId: otherId }); await state().publishCurrentDefinition(definition);
    expect(state().receipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled(); expect(api.listReportingDefinitions).toHaveBeenCalledOnce();
  });
  it('发布结果未知阻止立即重复，新修订目录读取后再由用户操作', async () => {
    await setup(); vi.mocked(api.publishReportingDefinition).mockRejectedValueOnce(failure); await state().publishCurrentDefinition(definition);
    await state().publishCurrentDefinition(definition); expect(api.publishReportingDefinition).toHaveBeenCalledOnce();
    vi.mocked(api.listReportingDefinitions).mockResolvedValueOnce([{ ...definition, version: 2, latestPublishedVersionNumber: 2 }]); await state().loadPage(); expect(state().rowBlocks.size).toBe(0);
  });
  it('版本读取失败保留可关闭抽屉，重试只读取原定义', async () => {
    await setup(); vi.mocked(api.listReportingDefinitionVersions).mockRejectedValueOnce(failure); await state().openVersions(definition);
    expect(state().versionsDrawerOpen).toBe(true); expect(state().versionsProblem).toEqual(failure); await state().retryVersions();
    expect(state().versions).toEqual([initialVersion, version]); expect(state().versionsProblem).toBeUndefined(); expect(api.publishReportingDefinition).not.toHaveBeenCalled();
  });
  it('已确认发布版本不能在版本重读中丢失或换身份', async () => {
    await setup(); await state().publishCurrentDefinition(definition); const current = state().definitions[0]!;
    vi.mocked(api.listReportingDefinitionVersions).mockResolvedValueOnce([{ ...version, id: otherId }]); await state().openVersions(current);
    expect(state().versions).toEqual([]); expect(state().versionsProblem).toBeDefined();
  });
  it('存在更高版本也不能掩盖已确认版本被遗漏', async () => {
    await setup(); await state().publishCurrentDefinition(definition);
    vi.mocked(api.listReportingDefinitionVersions).mockResolvedValueOnce([{ ...version, id: otherId, versionNumber: 3 }]); await state().openVersions(state().definitions[0]!);
    expect(state().versions).toEqual([]); expect(state().versionsProblem).toBeDefined();
  });
  it.each(['group', 'definition'] as const)('%s 删除成功后旧目录不复活删除资源', async resource => {
    if (resource === 'group') vi.mocked(api.listReportingDefinitions).mockResolvedValue([]);
    await setup(); const action = resource === 'group' ? state().confirmDeleteGroup(group) : state().confirmDeleteDefinition(definition); state().finishConfirmation(true); await action;
    expect(resource === 'group' ? state().groups : state().definitions).toEqual([]); expect(state().receipt).toMatchObject({ resource, kind: 'delete', outcome: 'succeeded' });
  });
  it('目录已经观察到新修订后，后续旧目录不能回退', async () => {
    await setup(); vi.mocked(api.listReportingDefinitions).mockResolvedValueOnce([{ ...definition, version: 4, latestPublishedVersionNumber: 3 }]);
    await state().loadPage(); await state().loadPage(); expect(state().definitions[0]!.version).toBe(4); expect(state().catalogProblems.definitions).toBeDefined();
  });
  it('旧刷新、行按钮以及编辑器真实事件不能借用新代次', async () => {
    await setup(); const refresh = wrapper.getComponent(ArtTableHeader).vm.$.vnode.props!.onRefresh as () => void;
    const edit = wrapper.findAllComponents(ArtTableActionButton).find(item => item.props('testId') === 'reporting-definition-edit')!.vm.$.vnode.props!.onClick as () => void;
    await state().loadPage(); refresh(); edit(); await flushPromises(); expect(api.listReportingDefinitions).toHaveBeenCalledTimes(2); expect(state().definitionEditorOpen).toBe(false);
    state().openEditDefinition(definition); await validate(); const old = dialog(), submit = old.vm.$.vnode.props!.onConfirm as () => void;
    const input = old.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    state().definitionEditorOpen = false; state().openEditDefinition(definition); await validate(); input('旧名称'); submit(); await flushPromises();
    expect(state().definitionForm.name).toBe(definition.name); expect(api.updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('旧确认不能确认下一次删除，旧抽屉关闭回调不能关闭新抽屉', async () => {
    await setup(); const first = state().confirmDeleteDefinition(definition); await flushPromises(); const prompt = wrapper.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === 'reporting-management-confirm')!;
    const oldConfirm = prompt.vm.$.vnode.props!.onConfirm as () => void; state().finishConfirmation(false); await first;
    const second = state().confirmDeleteDefinition(definition); await flushPromises(); oldConfirm(); expect(api.deleteReportingDefinition).not.toHaveBeenCalled(); state().finishConfirmation(false); await second;
    await state().openVersions(definition); await flushPromises(); const close = wrapper.getComponent(ElDrawer).vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    state().versionsDrawerOpen = false; await state().openVersions(definition); close(false); expect(state().versionsDrawerOpen).toBe(true);
  });
  it('离开路由同步清理草稿并取消写入接入，旧结果不恢复页面', async () => {
    const route = reactive({ name: 'reporting-definitions' }), pending = deferred<typeof definition>(); await setup(route);
    state().openEditDefinition(definition); await validate(); vi.mocked(api.updateReportingDefinition).mockReturnValueOnce(pending.promise);
    const action = state().submitDefinition(); await flushPromises(); route.name = 'home'; expect(state().definitionForm.parameterSchema).toEqual([]);
    pending.resolve({ ...definition, version: 2 }); await action; expect(ElMessage.success).not.toHaveBeenCalled(); expect(state().definitions).toEqual([]);
  });
});
