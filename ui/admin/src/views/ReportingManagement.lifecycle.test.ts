import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElForm, ElDrawer, ElMessage, ElMessageBox } from 'element-plus';
import type { ReportingDataSource } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ReportingDataSourcesView from './ReportingDataSourcesView.vue';
import ReportingDefinitionsView from './ReportingDefinitionsView.vue';
import * as sources from '../api/reporting-data-sources';
import * as definitions from '../api/reporting-definitions';
import { createOutputSession, deferred, outputId, reportDefinition } from '../test/data-output-fixtures';

vi.mock('../api/reporting-data-sources', () => ({ listReportingDataSources: vi.fn(), getReportingDataSource: vi.fn(),
  createReportingDataSource: vi.fn(), updateReportingDataSource: vi.fn(), disableReportingDataSource: vi.fn(),
  deleteReportingDataSource: vi.fn(), testReportingDataSource: vi.fn() }));
vi.mock('../api/reporting-definitions', () => ({ listReportingGroups: vi.fn(), createReportingGroup: vi.fn(),
  updateReportingGroup: vi.fn(), deleteReportingGroup: vi.fn(), listReportingQueryPorts: vi.fn(),
  listReportingDefinitions: vi.fn(), createReportingDefinition: vi.fn(), updateReportingDefinition: vi.fn(),
  deleteReportingDefinition: vi.fn(), publishReportingDefinition: vi.fn(), listReportingDefinitionVersions: vi.fn() }));

const source: ReportingDataSource = { id: outputId, tenantId: null, name: '原会话数据源', providerKey: 'sql_server',
  serverHost: 'private-db', port: 1433, databaseName: 'private-database', username: 'private-user',
  hasPassword: true, trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null,
  lastTestStatusKey: null, lastTestMessage: null, createdAtUtc: '2026-10-10T00:00:00Z', updatedAtUtc: null, version: 1 };
const page = { items: [{ ...source, maskedDatabaseName: 'private-***', maskedServerEndpoint: 'private-***',
  maskedUsername: 'private-***' }], page: 1, pageSize: 20, total: 1 };
const group = { id: outputId, parentId: null, name: '原会话分组', sortOrder: 0, isEnabled: true,
  createdAtUtc: source.createdAtUtc, updatedAtUtc: null, version: 1 };
const permissions = ['reporting.data_sources.read', 'reporting.data_sources.create', 'reporting.data_sources.update',
  'reporting.data_sources.delete', 'reporting.data_sources.test', 'reporting.definitions.read',
  'reporting.definitions.create', 'reporting.definitions.update', 'reporting.definitions.delete',
  'reporting.definitions.publish', 'reporting.groups.read', 'reporting.groups.create', 'reporting.groups.update',
  'reporting.groups.delete', 'reporting.query_ports.read'];
let wrapper: VueWrapper | undefined;
function mountPage(component: typeof ReportingDataSourcesView | typeof ReportingDefinitionsView, allowed = permissions) {
  const context = createOutputSession(allowed);
  Object.assign(context.session.currentUser!, { scope: 'host', actorScope: 'host', tenantId: null });
  wrapper = mount(component, { global: { plugins: [context.pinia], directives: { loading: () => {} } } });
  return context.session;
}
function switchTenant(session: ReturnType<typeof createOutputSession>['session']) {
  Object.assign(session.currentUser!, { tenantId: outputId, scope: `tenant:${outputId.replaceAll('-', '')}` });
}
const rowAction = async (text: string) => {
  const action = { 删除: 'delete', 版本: 'versions', 发布: 'publish', 编辑: 'edit' }[text];
  expect(action).toBeDefined();
  const selector = `[data-testid="reporting-definition-${action}"]`;
  if (!wrapper!.find(selector).exists()) { await wrapper!.get('[data-testid="art-table-action-more"]').trigger('click'); await flushPromises(); }
  const button = wrapper!.find(selector);
  if (button.exists()) await button.trigger('click');
  else document.querySelector(selector)!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await flushPromises();
};
beforeEach(() => {
  vi.clearAllMocks();
  for (const value of [...Object.values(sources), ...Object.values(definitions)]) vi.mocked(value).mockReset();
  vi.mocked(sources.listReportingDataSources).mockResolvedValue(page);
  vi.mocked(definitions.listReportingGroups).mockResolvedValue([group]);
  vi.mocked(definitions.listReportingDefinitions).mockResolvedValue([reportDefinition]);
  vi.mocked(definitions.listReportingQueryPorts).mockResolvedValue([]);
  vi.mocked(definitions.listReportingDefinitionVersions).mockResolvedValue([]);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; ElMessageBox.close(); ElMessage.closeAll(); vi.restoreAllMocks(); });

describe('Reporting 管理页授权代次', () => {
  it.each(['sources', 'definitions'] as const)('切租户同步取消 %s 读取，迟到结果不能重新出现', async kind => {
    const pending = deferred<any>();
    const read = kind === 'sources' ? sources.listReportingDataSources : definitions.listReportingDefinitions;
    vi.mocked(read).mockReturnValueOnce(pending.promise);
    const session = mountPage(kind === 'sources' ? ReportingDataSourcesView : ReportingDefinitionsView);
    await flushPromises();
    const calls = vi.mocked(read).mock.calls[0]!;
    const signal = calls[calls.length - 1] as AbortSignal;
    switchTenant(session); await flushPromises();
    pending.resolve(kind === 'sources' ? page : [reportDefinition]); await flushPromises();
    expect(wrapper!.text()).not.toContain(kind === 'sources' ? source.name : reportDefinition.name);
    expect(signal?.aborted).toBe(true);
    expect(read).toHaveBeenCalledOnce();
  });
  it.each(['sources', 'definitions'] as const)('无读取权限不加载 %s 管理目录', async kind => {
    mountPage(kind === 'sources' ? ReportingDataSourcesView : ReportingDefinitionsView, []); await flushPromises();
    expect(kind === 'sources' ? sources.listReportingDataSources : definitions.listReportingDefinitions).not.toHaveBeenCalled();
  });
  it('同页刷新取消旧列表，旧结果和 finally 不覆盖新请求', async () => {
    const old = deferred<typeof page>(); const fresh = deferred<typeof page>();
    vi.mocked(sources.listReportingDataSources).mockReturnValueOnce(old.promise).mockReturnValueOnce(fresh.promise);
    mountPage(ReportingDataSourcesView); await flushPromises();
    wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await flushPromises();
    old.resolve(page); await flushPromises();
    expect(wrapper!.text()).not.toContain(source.name);
    fresh.resolve({ ...page, items: [{ ...page.items[0]!, name: '最新目录' }] }); await flushPromises();
    expect(wrapper!.text()).toContain('最新目录');
    expect(vi.mocked(sources.listReportingDataSources).mock.calls[0]![1]!.aborted).toBe(true);
  });
  it('迟到编辑详情不能重新打开已失效数据源编辑器', async () => {
    const pending = deferred<ReportingDataSource>(); vi.mocked(sources.getReportingDataSource).mockReturnValue(pending.promise);
    const session = mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises();
    switchTenant(session); pending.resolve(source); await flushPromises();
    expect(wrapper!.findAllComponents(ArtFormDialog).some(dialog => dialog.props('open'))).toBe(false);
    expect(vi.mocked(sources.getReportingDataSource).mock.calls[0]![1]!.aborted).toBe(true);
  });
  it('连接测试迟到不通知、不刷新，也不恢复已清空内容', async () => {
    const pending = deferred<any>(); vi.mocked(sources.testReportingDataSource).mockReturnValue(pending.promise);
    const success = vi.spyOn(ElMessage, 'success');
    const session = mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-test"]').trigger('click'); await flushPromises();
    switchTenant(session); pending.resolve({ succeeded: true, message: '原连接成功' }); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(sources.listReportingDataSources).toHaveBeenCalledOnce();
  });
  it.each(['disable', 'delete'] as const)('数据源 %s 确认期间失效，提示立即关闭且不能提交', async kind => {
    const session = mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get(`[data-testid="reporting-data-source-${kind}"]`).trigger('click'); await flushPromises();
    expect(document.body.textContent).toContain(source.name);
    switchTenant(session); await flushPromises();
    expect(document.body.textContent).not.toContain(source.name);
    expect(kind === 'disable' ? sources.disableReportingDataSource : sources.deleteReportingDataSource).not.toHaveBeenCalled();
  });
  it('报表删除确认在撤权时关闭且不提交', async () => {
    const session = mountPage(ReportingDefinitionsView); await flushPromises(); await rowAction('删除'); await flushPromises();
    expect(document.body.textContent).toContain(reportDefinition.name);
    session.currentUser!.permissions = []; await flushPromises();
    expect(document.body.textContent).not.toContain(reportDefinition.name);
    expect(definitions.deleteReportingDefinition).not.toHaveBeenCalled();
  });
  it('切租户取消待处理版本读取，迟到版本不重新打开抽屉', async () => {
    const pending = deferred<any[]>(); vi.mocked(definitions.listReportingDefinitionVersions).mockReturnValue(pending.promise);
    const session = mountPage(ReportingDefinitionsView); await flushPromises(); await rowAction('版本'); await flushPromises();
    switchTenant(session); pending.resolve([]); await flushPromises();
    expect(wrapper!.findAllComponents({ name: 'ElDrawer' }).some(drawer => drawer.props('modelValue'))).toBe(false);
    expect(vi.mocked(definitions.listReportingDefinitionVersions).mock.calls[0]![1]!.aborted).toBe(true);
  });
  it('发布期间重复点击只提交一次，切会话后迟到结果不触发补读', async () => {
    const pending = deferred<any>(); vi.mocked(definitions.publishReportingDefinition).mockReturnValue(pending.promise);
    const session = mountPage(ReportingDefinitionsView); await flushPromises();
    await rowAction('发布'); await rowAction('发布'); await flushPromises();
    expect(definitions.publishReportingDefinition).toHaveBeenCalledOnce();
    switchTenant(session); pending.resolve({}); await flushPromises();
    expect(definitions.listReportingDefinitions).toHaveBeenCalledOnce();
  });
  it('关闭版本抽屉立即取消读取，迟到结果不接入', async () => {
    const pending = deferred<any[]>(); vi.mocked(definitions.listReportingDefinitionVersions).mockReturnValue(pending.promise);
    mountPage(ReportingDefinitionsView); await flushPromises(); await rowAction('版本');
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await flushPromises();
    pending.resolve([]); await flushPromises();
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
    expect(vi.mocked(definitions.listReportingDefinitionVersions).mock.calls[0]![1]!.aborted).toBe(true);
  });
  it.each(['sources', 'groups'] as const)('%s 校验等待时关闭编辑器，不能继续提交', async kind => {
    mountPage(kind === 'sources' ? ReportingDataSourcesView : ReportingDefinitionsView); await flushPromises();
    if (kind === 'sources') await wrapper!.get('[data-testid="reporting-data-source-create"]').trigger('click');
    else await wrapper!.get('[data-testid="reporting-group-create"]').trigger('click');
    await flushPromises();
    const dialog = wrapper!.findAllComponents(ArtFormDialog).find(item => item.props('open'))!;
    const validation = deferred<boolean>(); const form = dialog.getComponent(ElForm);
    vi.spyOn(form.vm.$.exposed!, 'validate').mockImplementation(() => validation.promise as any);
    dialog.vm.$emit('confirm'); await flushPromises();
    expect(dialog.props('saving')).toBe(true);
    dialog.vm.$emit('update:open', false); validation.resolve(true); await flushPromises();
    expect(kind === 'sources' ? sources.createReportingDataSource : definitions.createReportingGroup).not.toHaveBeenCalled();
  });
  it('数据源真实模型校验允许正常创建，重复确认只写一次', async () => {
    const pending = deferred<ReportingDataSource>(); vi.mocked(sources.createReportingDataSource).mockReturnValue(pending.promise);
    mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-create"]').trigger('click'); await flushPromises();
    Object.assign(wrapper!.getComponent(ArtFormDialog).getComponent(ElForm).props('model')!, { name: '新增', serverHost: 'localhost',
      databaseName: 'app', username: 'user', password: 'not-a-real-secret' });
    const dialog = wrapper!.getComponent(ArtFormDialog); dialog.vm.$emit('confirm'); dialog.vm.$emit('confirm'); await flushPromises();
    expect(sources.createReportingDataSource).toHaveBeenCalledOnce();
    expect(vi.mocked(sources.createReportingDataSource).mock.calls[0]![1]).toBeInstanceOf(AbortSignal);
    pending.resolve(source); await flushPromises(); expect(dialog.props('open')).toBe(false);
    expect(sources.listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it('数据源确认取消允许重试，确认后提交一次且刷新目录', async () => {
    vi.mocked(sources.deleteReportingDataSource).mockResolvedValue(true);
    mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-delete"]').trigger('click'); await flushPromises();
    wrapper!.findAllComponents(ArtFormDialog).find(dialog => dialog.props('open'))!.vm.$emit('update:open', false);
    await flushPromises(); expect(sources.deleteReportingDataSource).not.toHaveBeenCalled();
    await wrapper!.get('[data-testid="reporting-data-source-delete"]').trigger('click'); await flushPromises();
    document.querySelector('[data-testid="reporting-data-source-confirm"]')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await flushPromises(); expect(sources.deleteReportingDataSource).toHaveBeenCalledOnce();
    expect(sources.listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it('版本读取失败关闭抽屉，让页面错误可见', async () => {
    vi.mocked(definitions.listReportingDefinitionVersions).mockRejectedValue({ status: 503, code: 'fixture.unavailable', title: '版本服务不可用' });
    mountPage(ReportingDefinitionsView); await flushPromises(); await rowAction('版本');
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
    expect(wrapper!.text()).toContain('版本服务不可用');
  });
  it('关闭报表编辑器清空备注，随后发布不携带上一草稿', async () => {
    vi.mocked(definitions.publishReportingDefinition).mockResolvedValue({} as any);
    mountPage(ReportingDefinitionsView); await flushPromises(); await rowAction('编辑');
    const noteElement = document.querySelector('[data-testid="reporting-publish-note"]')!;
    const note = (noteElement.matches('textarea') ? noteElement : noteElement.querySelector('textarea')) as HTMLTextAreaElement;
    note.value = '未提交的旧备注'; note.dispatchEvent(new Event('input', { bubbles: true })); await flushPromises();
    wrapper!.findAllComponents(ArtFormDialog).find(dialog => dialog.props('open'))!.vm.$emit('update:open', false);
    await flushPromises(); await rowAction('发布');
    expect(definitions.publishReportingDefinition).toHaveBeenCalledWith(outputId, { changeNote: null, version: 1 }, expect.any(AbortSignal));
  });
  it('同 Host 会话更换取消连接测试，迟到失败不能显示', async () => {
    const pending = deferred<any>(); vi.mocked(sources.testReportingDataSource).mockReturnValue(pending.promise);
    const session = mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-test"]').trigger('click'); await flushPromises();
    session.currentUser!.sessionId = '019bc2b1-2a40-7cc3-8992-a80de51bf298'; await flushPromises();
    pending.reject({ status: 500, code: 'fixture.old', title: '旧会话测试错误' }); await flushPromises();
    expect(vi.mocked(sources.testReportingDataSource).mock.calls[0]![1]!.aborted).toBe(true);
    expect(wrapper!.text()).not.toContain('旧会话测试错误');
    expect(sources.listReportingDataSources).toHaveBeenCalledTimes(2);
  });
  it.each(['close', 'revoke'] as const)('数据源编辑器 %s 清除已读凭据和手动输入密码', async kind => {
    vi.mocked(sources.getReportingDataSource).mockResolvedValue(source);
    const session = mountPage(ReportingDataSourcesView); await flushPromises();
    await wrapper!.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises();
    const model = wrapper!.getComponent(ArtFormDialog).getComponent(ElForm).props('model')!;
    model.password = 'not-a-real-secret'; expect(model.username).toBe(source.username);
    if(kind === 'close') wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open', false);
    else session.currentUser!.permissions = [];
    await flushPromises();
    expect(model.password).toBe(''); expect(model.username).toBe(''); expect(model.serverHost).toBe('');
    expect(wrapper!.findAllComponents(ArtFormDialog).some(dialog => dialog.props('open'))).toBe(false);
  });
  it.each(['sources', 'definitions'] as const)('%s KeepAlive 停用取消读取，恢复仅加载一次', async kind => {
    const pending = deferred<any>();
    const read = kind === 'sources' ? sources.listReportingDataSources : definitions.listReportingDefinitions;
    vi.mocked(read).mockReturnValueOnce(pending.promise);
    const context = createOutputSession(permissions);
    Object.assign(context.session.currentUser!, { scope: 'host', actorScope: 'host', tenantId: null });
    const active = ref(true); const component = kind === 'sources' ? ReportingDataSourcesView : ReportingDefinitionsView;
    const parent = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => active.value ? h(component) : null }) });
    wrapper = mount(parent, { global: { plugins: [context.pinia], directives: { loading: () => {} } } });
    await flushPromises(); const call = vi.mocked(read).mock.calls[0]!; const signal = call.at(-1) as AbortSignal;
    active.value = false; await flushPromises(); expect(signal.aborted).toBe(true);
    pending.resolve(kind === 'sources' ? page : [reportDefinition]); await flushPromises(); expect(read).toHaveBeenCalledOnce();
    active.value = true; await flushPromises(); expect(read).toHaveBeenCalledTimes(2);
    expect(wrapper!.text()).toContain(kind === 'sources' ? source.name : reportDefinition.name);
  });

});
