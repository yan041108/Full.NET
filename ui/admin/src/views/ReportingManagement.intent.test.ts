import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElForm, ElMessage } from 'element-plus';
import type { ReportingDataSource, ReportingDefinition } from '@fullnet/client-contracts';
import Sources from './ReportingDataSourcesView.vue';
import Definitions from './ReportingDefinitionsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as sources from '../api/reporting-data-sources';
import * as definitions from '../api/reporting-definitions';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, outputId, reportDefinition } from '../test/data-output-fixtures';

vi.mock('../api/reporting-data-sources', () => ({ listReportingDataSources: vi.fn(), getReportingDataSource: vi.fn(),
  createReportingDataSource: vi.fn(), updateReportingDataSource: vi.fn(), disableReportingDataSource: vi.fn(),
  deleteReportingDataSource: vi.fn(), testReportingDataSource: vi.fn() }));
vi.mock('../api/reporting-definitions', () => ({ listReportingGroups: vi.fn(), createReportingGroup: vi.fn(), updateReportingGroup: vi.fn(),
  deleteReportingGroup: vi.fn(), listReportingQueryPorts: vi.fn(), listReportingDefinitions: vi.fn(), createReportingDefinition: vi.fn(),
  updateReportingDefinition: vi.fn(), deleteReportingDefinition: vi.fn(), publishReportingDefinition: vi.fn(), listReportingDefinitionVersions: vi.fn() }));
const otherId = '01980000-0000-7000-8000-000000000003';
const source: ReportingDataSource = { id: outputId, tenantId: null, name: '数据源', providerKey: 'sql_server',
  serverHost: 'fixture-db', port: 1433, databaseName: 'app', username: 'fixture-user', hasPassword: true,
  trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null,
  createdAtUtc: '2026-10-15T00:00:00Z', updatedAtUtc: null, version: 1 };
const sourceRow = { ...source, maskedServerEndpoint: 'fixture-***', maskedDatabaseName: 'app-***', maskedUsername: 'user-***' };
const otherSource = { ...sourceRow, id: otherId, providerKey: 'mysql' };
const group = { id: outputId, parentId: null, name: '分组一', sortOrder: 0, isEnabled: true, createdAtUtc: source.createdAtUtc, updatedAtUtc: null, version: 1 };
const otherGroup = { ...group, id: otherId, name: '分组二' };
const parameter = { parameterKey: 'schema', displayName: '默认名称', dataTypeKey: 'string', isRequired: true, defaultValue: 'default' };
const port = { queryPortKey: reportDefinition.queryPortKey, displayName: '目录', description: '', supportedProviderKeys: ['mysql'],
  parameters: [{ ...parameter, minimum: null, maximum: null }] };
const definition = { ...reportDefinition, parameterSchema: [{ ...parameter, displayName: '已保存名称', defaultValue: 'saved' }] };
const all = ['reporting.data_sources.read', 'reporting.data_sources.create', 'reporting.data_sources.update', 'reporting.data_sources.delete',
  'reporting.data_sources.test', 'reporting.definitions.read', 'reporting.definitions.create', 'reporting.definitions.update',
  'reporting.definitions.delete', 'reporting.definitions.publish', 'reporting.groups.read', 'reporting.groups.create',
  'reporting.groups.update', 'reporting.groups.delete', 'reporting.query_ports.read'];
type State = { items: typeof sourceRow[]; groups: typeof group[]; definitions: ReportingDefinition[]; acting: boolean; changing: boolean;
  editorOpen: boolean; groupEditorOpen: boolean; definitionEditorOpen: boolean; versionsDrawerOpen: boolean;
  selectedGroupId: string; editorForm: Record<string, unknown>; groupForm: Record<string, unknown>;
  definitionForm: { name: string; queryPortKey: string; dataSourceId: string; parameterSchema: typeof parameter[] } };
let wrapper: VueWrapper | undefined;
function state() { return wrapper!.vm as unknown as State; }
function run(name: string, ...args: unknown[]) { return (wrapper!.vm as unknown as Record<string, (...values: unknown[]) => Promise<unknown>>)[name]!(...args); }
async function setup(component: typeof Sources | typeof Definitions, permissions = all) {
  const context = createShareSession(permissions); wrapper = mount(component, { global: { plugins: [context.pinia], directives: { loading: {} } } });
  await flushPromises(); return context.session;
}
async function validateOpen(result = Promise.resolve(true)) {
  await flushPromises(); const dialog = wrapper!.findAllComponents(ArtFormDialog).find(item => item.props('open'))!;
  vi.spyOn(dialog.getComponent(ElForm).vm.$.exposed!, 'validate').mockImplementation(() => result);
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(sources.listReportingDataSources).mockResolvedValue({ items: [sourceRow, otherSource], page: 1, pageSize: 20, total: 2 });
  vi.mocked(sources.getReportingDataSource).mockResolvedValue(source); vi.mocked(sources.updateReportingDataSource).mockResolvedValue(source);
  vi.mocked(sources.deleteReportingDataSource).mockResolvedValue(true); vi.mocked(sources.disableReportingDataSource).mockResolvedValue(source);
  vi.mocked(definitions.listReportingGroups).mockResolvedValue([group, otherGroup]); vi.mocked(definitions.listReportingDefinitions).mockResolvedValue([definition]);
  vi.mocked(definitions.listReportingQueryPorts).mockResolvedValue([port]); vi.mocked(definitions.listReportingDefinitionVersions).mockResolvedValue([]);
  vi.mocked(definitions.deleteReportingDefinition).mockResolvedValue(true); vi.mocked(definitions.deleteReportingGroup).mockResolvedValue(true);
  vi.mocked(definitions.updateReportingDefinition).mockResolvedValue({ ...definition, version: 2 }); vi.mocked(definitions.updateReportingGroup).mockResolvedValue({ ...group, version: 2 });
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; ElMessage.closeAll(); vi.restoreAllMocks(); });

describe('报表定义编辑与确认意图', () => {
  it('缺少页面 Read 即使有其他目录权限也不预取或显示入口', async () => {
    await setup(Definitions, all.filter(code => code !== 'reporting.definitions.read'));
    expect(definitions.listReportingGroups).not.toHaveBeenCalled(); expect(sources.listReportingDataSources).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="reporting-group-create"]').exists()).toBe(false); await run('openCreateGroup');
    expect(state().groupEditorOpen).toBe(false);
  });
  it('打开已有定义后保留保存过的参数，不被端口默认值覆盖', async () => {
    await setup(Definitions); await run('openEditDefinition', definition); await flushPromises();
    expect(state().definitionForm.parameterSchema).toEqual(definition.parameterSchema);
    state().definitionForm.parameterSchema[0]!.displayName = '本地输入'; expect(definition.parameterSchema[0]!.displayName).toBe('已保存名称');
  });
  it('新建先选查询端口，再选兼容的提供程序数据源', async () => {
    await setup(Definitions); await run('openCreateDefinition'); expect(state().definitionForm.queryPortKey).toBe(port.queryPortKey);
    expect(state().definitionForm.dataSourceId).toBe(otherSource.id);
  });
  it('编辑期间切换分组关闭旧表单，不能把旧输入提交到新分组', async () => {
    await setup(Definitions); await run('openEditDefinition', definition); await flushPromises(); state().selectedGroupId = otherGroup.id;
    await run('submitDefinition'); expect(state().definitionEditorOpen).toBe(false); expect(definitions.updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('校验期间切换分组取消创建，晚校验不能提交', async () => {
    await setup(Definitions); await run('openCreateDefinition'); const pending = deferred<boolean>(); await validateOpen(pending.promise);
    const action = run('submitDefinition'); state().selectedGroupId = otherGroup.id; pending.resolve(true); await action;
    expect(definitions.createReportingDefinition).not.toHaveBeenCalled();
  });
  it('目录出现新修订时关闭旧定义编辑器', async () => {
    await setup(Definitions); await run('openEditDefinition', definition); state().definitions = [{ ...definition, version: 9 }];
    expect(state().definitionEditorOpen).toBe(false); await run('submitDefinition'); expect(definitions.updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('目录出现新修订时关闭旧分组编辑器', async () => {
    await setup(Definitions); await run('openEditGroup', group); state().groups = [{ ...group, version: 9 }, otherGroup];
    expect(state().groupEditorOpen).toBe(false);
  });
  it('打开另一类编辑器关闭原编辑器，版本抽屉不替换编辑目标', async () => {
    await setup(Definitions); await run('openEditDefinition', definition); await run('openEditGroup', group);
    expect(state().definitionEditorOpen).toBe(false); expect(state().groupEditorOpen).toBe(true);
    await run('openVersions', definition); expect(state().groupEditorOpen).toBe(false); expect(state().versionsDrawerOpen).toBe(true);
  });
  it.each(['group', 'definition'] as const)('%s 删除返回 false 不显示成功或移除本地目录', async kind => {
    const success = vi.spyOn(ElMessage, 'success'); await setup(Definitions);
    if (kind === 'group') vi.mocked(definitions.deleteReportingGroup).mockResolvedValueOnce(false);
    else vi.mocked(definitions.deleteReportingDefinition).mockResolvedValueOnce(false);
    const action = run(kind === 'group' ? 'confirmDeleteGroup' : 'confirmDeleteDefinition', kind === 'group' ? group : definition);
    await run('finishConfirmation', true); await action;
    expect(success).not.toHaveBeenCalled(); expect(state().groups).toHaveLength(2); expect(state().definitions).toHaveLength(1);
  });
  it.each(['group', 'definition'] as const)('%s 确认期间修订变化不能继续删除', async kind => {
    await setup(Definitions); const action = run(kind === 'group' ? 'confirmDeleteGroup' : 'confirmDeleteDefinition', kind === 'group' ? group : definition);
    if (kind === 'group') state().groups = [{ ...group, version: 9 }, otherGroup]; else state().definitions = [{ ...definition, version: 9 }];
    await run('finishConfirmation', true); await action;
    expect(kind === 'group' ? definitions.deleteReportingGroup : definitions.deleteReportingDefinition).not.toHaveBeenCalled();
  });
  it('版本响应属于另一报表时不展示且关闭抽屉', async () => {
    vi.mocked(definitions.listReportingDefinitionVersions).mockResolvedValueOnce([{ id: otherId, definitionId: otherId, versionNumber: 1,
      dataSourceId: outputId, queryPortKey: port.queryPortKey, parameterSchema: [], layoutConfigJson: '{}', changeNote: '其他定义',
      publishedByUserId: outputId, publishedAtUtc: source.createdAtUtc }]); await setup(Definitions); await run('openVersions', definition);
    expect(state().versionsDrawerOpen).toBe(false); expect(wrapper!.text()).not.toContain('其他定义');
  });
  it('健康编辑定义保留原分组、版本与自定义参数', async () => {
    await setup(Definitions); await run('openEditDefinition', definition); await validateOpen(); await run('submitDefinition');
    expect(definitions.updateReportingDefinition).toHaveBeenCalledWith(definition.id, expect.objectContaining({ groupId: definition.groupId,
      version: definition.version, parameterSchema: definition.parameterSchema }), expect.any(AbortSignal));
  });
  it('健康分组更新保留原修订并关闭表单，不被自身目录更新误判', async () => {
    await setup(Definitions); await run('openEditGroup', group); await validateOpen(); await run('submitGroup');
    expect(definitions.updateReportingGroup).toHaveBeenCalledWith(group.id, expect.objectContaining({ version: group.version }), expect.any(AbortSignal));
    expect(state().groupEditorOpen).toBe(false); expect(state().groups[0]!.version).toBe(2); expect(state().acting).toBe(false);
  });
  it('删除确认固定原始标识，调用者改写原对象不能改目标', async () => {
    await setup(Definitions); const target = { ...definition }; const action = run('confirmDeleteDefinition', target);
    target.id = otherId; await run('finishConfirmation', true); await action;
    expect(definitions.deleteReportingDefinition).toHaveBeenCalledWith(definition.id, expect.any(AbortSignal));
  });
  it('关闭后重开新编辑，旧更新结果不能关闭新表单或清除忙状态', async () => {
    const old = deferred<ReportingDefinition>(); const next = deferred<ReportingDefinition>();
    vi.mocked(definitions.updateReportingDefinition).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    await setup(Definitions); await run('openEditDefinition', definition); await validateOpen(); const first = run('submitDefinition'); await flushPromises();
    state().definitionEditorOpen = false; await run('openEditDefinition', definition); await validateOpen(); const second = run('submitDefinition'); await flushPromises();
    old.resolve({ ...definition, version: 2 }); await first; expect(state().definitionEditorOpen).toBe(true); expect(state().acting).toBe(true);
    state().definitionEditorOpen = false; next.resolve({ ...definition, version: 2 }); await second;
  });
  it('健康新建定义使用打开时分组和兼容数据源', async () => {
    vi.mocked(definitions.createReportingDefinition).mockResolvedValueOnce({ ...definition, dataSourceId: otherId });
    await setup(Definitions); await run('openCreateDefinition'); await validateOpen(); await run('submitDefinition');
    expect(definitions.createReportingDefinition).toHaveBeenCalledWith(expect.objectContaining({ groupId: group.id, dataSourceId: otherId }), expect.any(AbortSignal));
    expect(state().definitionEditorOpen).toBe(false);
  });
  it('关闭闲置编辑器不释放另一发布操作的锁，重复发布仍被阻止', async () => {
    const pending = deferred<Awaited<ReturnType<typeof definitions.publishReportingDefinition>>>(); vi.mocked(definitions.publishReportingDefinition).mockReturnValueOnce(pending.promise);
    await setup(Definitions); await run('openEditGroup', group); const action = run('publishCurrentDefinition', definition);
    state().groupEditorOpen = false; expect(state().acting).toBe(true); await run('publishCurrentDefinition', definition);
    expect(definitions.publishReportingDefinition).toHaveBeenCalledOnce(); wrapper!.unmount(); wrapper = undefined;
    pending.reject(new Error('late')); await action;
  });
});

describe('报表数据源编辑与操作意图', () => {
  it('缺少父 Read 不能显示创建入口或直接创建', async () => {
    await setup(Sources, ['reporting.data_sources.create']); await run('openCreate');
    expect(state().editorOpen).toBe(false); expect(wrapper!.find('[data-testid="reporting-data-source-create"]').exists()).toBe(false);
  });
  it('编辑详情等待时即打开可关闭容器，关闭取消并拒绝迟到结果', async () => {
    const pending = deferred<ReportingDataSource>(); vi.mocked(sources.getReportingDataSource).mockReturnValueOnce(pending.promise);
    await setup(Sources); const action = run('openEdit', sourceRow); expect(state().editorOpen).toBe(true);
    const signal = vi.mocked(sources.getReportingDataSource).mock.calls[0]![1]!; state().editorOpen = false; expect(signal.aborted).toBe(true);
    pending.resolve(source); await action; expect(state().editorOpen).toBe(false); expect(state().editorForm.username).toBe('');
  });
  it('详情身份错配不接入其它数据源凭据', async () => {
    vi.mocked(sources.getReportingDataSource).mockResolvedValueOnce({ ...source, id: otherId }); await setup(Sources); await run('openEdit', sourceRow);
    expect(state().editorForm.username).toBe(''); expect(state().editorForm.serverHost).toBe('');
  });
  it('刷新发现新版本关闭旧数据源编辑器并清除凭据', async () => {
    await setup(Sources); await run('openEdit', sourceRow); state().editorForm.password = 'fixture-only';
    state().items = [{ ...sourceRow, version: 9 }]; expect(state().editorOpen).toBe(false); expect(state().editorForm.password).toBe('');
  });
  it.each(['disable', 'delete'] as const)('%s 确认期间列表修订变化不写入', async kind => {
    await setup(Sources); const action = run(kind === 'disable' ? 'runDisable' : 'runDelete', sourceRow);
    state().items = [{ ...sourceRow, version: 9 }]; await run('finishConfirmation', true); await action;
    expect(kind === 'disable' ? sources.disableReportingDataSource : sources.deleteReportingDataSource).not.toHaveBeenCalled();
  });
  it('删除 false 不显示成功或刷新', async () => {
    vi.mocked(sources.deleteReportingDataSource).mockResolvedValueOnce(false); const success = vi.spyOn(ElMessage, 'success');
    await setup(Sources); const action = run('runDelete', sourceRow); await run('finishConfirmation', true); await action;
    expect(success).not.toHaveBeenCalled(); expect(sources.listReportingDataSources).toHaveBeenCalledOnce();
  });
  it('健康删除仍只提交冻结目标一次并刷新', async () => {
    await setup(Sources); const action = run('runDelete', sourceRow); await run('finishConfirmation', true); await action;
    expect(sources.deleteReportingDataSource).toHaveBeenCalledWith(sourceRow.id, expect.any(AbortSignal)); expect(sources.listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it('健康编辑沿用原详情修订，空密码保持 null', async () => {
    await setup(Sources); await run('openEdit', sourceRow); await validateOpen(); await run('submitEditor');
    expect(sources.updateReportingDataSource).toHaveBeenCalledWith(source.id, expect.objectContaining({ version: source.version, password: null }), expect.any(AbortSignal));
    expect(state().editorOpen).toBe(false); expect(sources.listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it('权威详情版本更新于旧列表仍使用详情版本提交', async () => {
    vi.mocked(sources.getReportingDataSource).mockResolvedValueOnce({ ...source, version: 4 });
    await setup(Sources); await run('openEdit', sourceRow); await validateOpen(); await run('submitEditor');
    expect(sources.updateReportingDataSource).toHaveBeenCalledWith(source.id, expect.objectContaining({ version: 4 }), expect.any(AbortSignal));
  });
  it('停用响应身份错配不反馈成功或刷新目录', async () => {
    vi.mocked(sources.disableReportingDataSource).mockResolvedValueOnce({ ...source, id: otherId }); const success = vi.spyOn(ElMessage, 'success');
    await setup(Sources); const action = run('runDisable', sourceRow); await run('finishConfirmation', true); await action;
    expect(success).not.toHaveBeenCalled(); expect(sources.listReportingDataSources).toHaveBeenCalledOnce();
  });
  it('关闭闲置数据源编辑器不释放连接测试的锁', async () => {
    const pending = deferred<Awaited<ReturnType<typeof sources.testReportingDataSource>>>(); vi.mocked(sources.testReportingDataSource).mockReturnValueOnce(pending.promise);
    await setup(Sources); await run('openEdit', sourceRow); const action = run('runTest', sourceRow); state().editorOpen = false;
    expect(state().changing).toBe(true); await run('runTest', sourceRow); expect(sources.testReportingDataSource).toHaveBeenCalledOnce();
    wrapper!.unmount(); wrapper = undefined; pending.reject(new Error('late')); await action;
  });
  it('未知目录项不能直接进入编辑、测试或删除', async () => {
    await setup(Sources); const fake = { ...sourceRow, id: '01980000-0000-7000-8000-000000000099' };
    await run('openEdit', fake); await run('runTest', fake); await run('runDelete', fake);
    expect(sources.getReportingDataSource).not.toHaveBeenCalled(); expect(sources.testReportingDataSource).not.toHaveBeenCalled(); expect(sources.deleteReportingDataSource).not.toHaveBeenCalled();
  });
});
