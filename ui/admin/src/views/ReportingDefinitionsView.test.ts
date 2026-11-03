import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDefinitionsView from './ReportingDefinitionsView.vue';
import {
  listReportingDefinitions,
  listReportingGroups,
  listReportingQueryPorts, getReportingDefinition, updateReportingDefinition, publishReportingDefinition, listReportingDefinitionVersions, deleteReportingDefinition, createReportingDefinition
} from '../api/reporting-definitions';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { useSessionStore } from '../auth/session';
import { ElMessageBox, ElSelect } from 'element-plus';

vi.mock('../api/reporting-definitions', () => ({
  listReportingGroups: vi.fn(),
  createReportingGroup: vi.fn(),
  updateReportingGroup: vi.fn(),
  deleteReportingGroup: vi.fn(),
  listReportingQueryPorts: vi.fn(),
  listReportingDefinitions: vi.fn(),
  getReportingDefinition: vi.fn(),
  createReportingDefinition: vi.fn(),
  updateReportingDefinition: vi.fn(),
  deleteReportingDefinition: vi.fn(),
  publishReportingDefinition: vi.fn(),
  listReportingDefinitionVersions: vi.fn()
}));

vi.mock('../api/reporting-data-sources', () => ({
  listReportingDataSources: vi.fn()
}));

const groupsMock = vi.mocked(listReportingGroups);
const definitionsMock = vi.mocked(listReportingDefinitions);
const queryPortsMock = vi.mocked(listReportingQueryPorts);
const dataSourcesMock = vi.mocked(listReportingDataSources);

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', groupId = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const draft = (version = 7) => ({ id, groupId, dataSourceId: groupId, definitionKey: 'report-review', name: '配置验收', description: '',
  queryPortKey: 'metadata.schemas', layoutConfigJson: '{}', isEnabled: true, latestPublishedVersionNumber: 1, version,
  parameterSchema: [{ parameterKey: 'name', displayName: '保留草稿名称', dataTypeKey: 'string', isRequired: false, defaultValue: 'draft-default' }],
  createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null });
const permissions = ['reporting.definitions.read', 'reporting.definitions.update', 'reporting.definitions.publish', 'reporting.definitions.delete', 'reporting.groups.read', 'reporting.query_ports.read', 'reporting.data_sources.read'];
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });
function mountView(grants: string[] = []) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = { id, username: 'reviewer', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: grants, sessionId: groupId, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(ReportingDefinitionsView, { global: { plugins: [pinia], directives: { loading: {} }, stubs: {
    ArtFormDialog: { props: ['open', 'title', 'saving', 'showConfirm', 'confirmTestId'], emits: ['confirm', 'update:open'], template: '<section v-if="open" :data-title="title"><slot/><button @click="$emit(\'update:open\', false)" data-testid="test-editor-close">关闭</button><button v-if="showConfirm" :disabled="saving" :data-testid="confirmTestId" @click="$emit(\'confirm\')">保存</button></section>' },
    ElDialog: { props: ['modelValue', 'title'], template: '<aside v-if="modelValue" :data-title="title"><slot/><slot name="footer"/></aside>' },
    ElDrawer: { props: ['modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' },
    ReportingDefinitionGrantsDialog: true,
    ArtTableActionGroup: { template: '<div><slot/></div>' }
  } } }); wrappers.push(wrapper); return wrapper;
}

describe('ReportingDefinitionsView', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    groupsMock.mockResolvedValue([]);
    definitionsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    queryPortsMock.mockResolvedValue([]);
    dataSourcesMock.mockResolvedValue({ items: [], page: 1, pageSize: 200, total: 0 });
    vi.mocked(getReportingDefinition).mockResolvedValue(draft());
    vi.mocked(listReportingDefinitionVersions).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="reporting-definition-create"]').exists()).toBe(false);
  });
  function withDefinition() {
    const { layoutConfigJson: _, parameterSchema: __, ...summary } = draft(4);
    definitionsMock.mockResolvedValue({ items: [summary], page: 1, pageSize: 20, total: 1 });
    groupsMock.mockResolvedValue([{ id: groupId, parentId: null, name: '分组', sortOrder: 0, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 1 }]);
    queryPortsMock.mockResolvedValue([{ queryPortKey: 'metadata.schemas', displayName: '目录', description: '', supportedProviderKeys: ['sqlserver'],
      parameters: [{ parameterKey: 'name', displayName: '目录默认名称', dataTypeKey: 'string', isRequired: false, defaultValue: 'catalog-default', minimum: null, maximum: null }] }]);
  }
  it('只读角色只请求定义目录，不因缺少辅助目录权限而失败', async () => {
    const wrapper = mountView(['reporting.definitions.read']); await flushPromises(); expect(definitionsMock).toHaveBeenCalledTimes(1);
    expect(groupsMock).not.toHaveBeenCalled(); expect(queryPortsMock).not.toHaveBeenCalled(); expect(dataSourcesMock).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="reporting-config-publish"]').exists()).toBe(false);
  });
  it('租户上下文不显示 Host 配置或调用维护目录', async () => {
    const wrapper = mountView(permissions); useSessionStore().currentUser = { ...useSessionStore().currentUser!, actorScope: 'tenant', scope: 'tenant', tenantId: groupId }; await flushPromises();
    expect(wrapper.find('[data-testid="reporting-config-refresh"]').exists()).toBe(false); expect(definitionsMock).not.toHaveBeenCalled();
  });
  it('切会话取消目录并拒绝迟到 Host 定义', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listReportingDefinitions>>) => void; definitionsMock.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(['reporting.definitions.read']); await flushPromises(); const signal = definitionsMock.mock.calls[0]![1]!;
    useSessionStore().currentUser = undefined; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve({ items: [draft()], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper.text()).not.toContain('配置验收');
  });
  it('草稿保存使用详情版本，保留自定义参数而不被目录默认值覆盖，不自动发布', async () => {
    withDefinition(); vi.mocked(updateReportingDefinition).mockResolvedValue(draft(8)); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-name"]').setValue('修改草稿');
    await wrapper.get('[data-testid="reporting-config-draft-save"]').trigger('click'); await flushPromises();
    expect(updateReportingDefinition).toHaveBeenCalledWith(id, expect.objectContaining({ version: 7, name: '修改草稿', parameterSchema: [expect.objectContaining({ displayName: '保留草稿名称', defaultValue: 'draft-default' })] }), expect.any(AbortSignal)); expect(publishReportingDefinition).not.toHaveBeenCalled();
  });
  it('发布前读取详情并单独确认新鲜版本与说明，未确认不写入', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    expect(publishReportingDefinition).not.toHaveBeenCalled(); await wrapper.get('[data-testid="reporting-config-publish-note"]').setValue('发布说明');
    await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); await flushPromises();
    expect(publishReportingDefinition).toHaveBeenCalledWith(id, { version: 7, changeNote: '发布说明' }, expect.any(AbortSignal));
  });
  it('草稿冲突保留输入和旧版本，目录刷新不能解锁，确认重新读取后使用新版本', async () => {
    withDefinition(); vi.mocked(updateReportingDefinition).mockRejectedValueOnce({ status: 409, code: 'reporting.definition.version_conflict', title: 'Conflict' });
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(undefined as never); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-name"]').setValue('保留本地输入');
    await wrapper.get('[data-testid="reporting-config-draft-save"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('保留本地输入');
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    vi.mocked(getReportingDefinition).mockResolvedValue({ ...draft(9), name: '最新草稿' }); await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('最新草稿');
    vi.mocked(updateReportingDefinition).mockResolvedValue(draft(10)); await wrapper.get('[data-testid="reporting-config-draft-save"]').trigger('click'); await flushPromises();
    expect(vi.mocked(updateReportingDefinition).mock.lastCall?.[1].version).toBe(9);
  });
  it.each(['edit', 'publish'])('迟到的 %s 读取在撤权后不能重新打开对话框', async action => {
    withDefinition(); let resolve!: (value: ReturnType<typeof draft>) => void; vi.mocked(getReportingDefinition).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get(`[data-testid="reporting-config-${action}"]`).trigger('click'); const signal = vi.mocked(getReportingDefinition).mock.calls[0]![1]!;
    useSessionStore().currentUser!.permissions = []; await flushPromises(); expect(signal.aborted).toBe(true); resolve(draft()); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-name"]').exists()).toBe(false);
  });
  it('未知发布锁定操作，写前读取迟到结果不得解锁，只有显式刷新恢复', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockRejectedValue(new Error('lost response'));
    const wrapper = mountView(permissions); await flushPromises();
    let resolve!: (value: Awaited<ReturnType<typeof listReportingDefinitions>>) => void; definitionsMock.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); const signal = definitionsMock.mock.lastCall![1]!;
    await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-unconfirmed"]').exists()).toBe(true);
    expect(signal.aborted).toBe(true); resolve({ items: [draft(4)], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-unconfirmed"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); expect(publishReportingDefinition).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-unconfirmed"]').exists()).toBe(false); expect(publishReportingDefinition).toHaveBeenCalledTimes(1);
  });
  it('删除确认期间切会话不得向新上下文发起旧定义删除', async () => {
    withDefinition(); let confirm!: () => void; vi.spyOn(ElMessageBox, 'confirm').mockImplementationOnce(() => new Promise(done => { confirm = () => done(undefined as never); }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click'); useSessionStore().currentUser = undefined; confirm(); await flushPromises(); expect(deleteReportingDefinition).not.toHaveBeenCalled();
  });
  it('历史抽屉按二十条摘要读取', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); await flushPromises();
    expect(listReportingDefinitionVersions).toHaveBeenCalledWith(id, expect.any(AbortSignal), 1, 20);
  });
  it('辅助目录失败仍可读取授权定义', async () => {
    withDefinition(); groupsMock.mockRejectedValueOnce(new Error('catalog offline')); const wrapper = mountView(permissions); await flushPromises(); expect(wrapper.text()).toContain('配置验收'); expect(definitionsMock).toHaveBeenCalledTimes(1);
  });
  it('新的筛选取消旧列表，迟到旧条件不能覆盖当前结果', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listReportingDefinitions>>) => void; definitionsMock.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(['reporting.definitions.read']); await flushPromises(); const signal = definitionsMock.mock.calls[0]![1]!;
    definitionsMock.mockResolvedValue({ items: [{ ...draft(), name: '新条件结果' }], page: 1, pageSize: 20, total: 1 });
    wrapper.findComponent({ name: 'ArtSearchBar' }).vm.$emit('search', { name: '新的条件' }); await flushPromises(); expect(signal.aborted).toBe(true);
    resolve({ items: [{ ...draft(), name: '迟到旧条件' }], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper.text()).toContain('新条件结果'); expect(wrapper.text()).not.toContain('迟到旧条件');
  });
  it('发布期间失去会话，中止写入请求且迟到成功不能刷新旧配置', async () => {
    withDefinition(); let resolve!: (value: Awaited<ReturnType<typeof publishReportingDefinition>>) => void;
    vi.mocked(publishReportingDefinition).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); const signal = vi.mocked(publishReportingDefinition).mock.calls[0]![2]!;
    useSessionStore().currentUser = undefined; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(undefined as never); await flushPromises(); expect(definitionsMock).toHaveBeenCalledTimes(1); expect(wrapper.find('[data-testid="reporting-config-publish"]').exists()).toBe(false);
  });
  it('未知结果后辅助目录失败不能解除写入锁，完整人工刷新后才恢复', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockRejectedValue(new Error('lost response')); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); await flushPromises();
    groupsMock.mockRejectedValueOnce(new Error('groups unavailable')); await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-unconfirmed"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-unconfirmed"]').exists()).toBe(false); expect(publishReportingDefinition).toHaveBeenCalledTimes(1);
  });
  it('新建草稿冻结打开时的分组，目录刷新不能静默改挂到另一个分组', async () => {
    withDefinition(); vi.mocked(createReportingDefinition).mockResolvedValue(draft()); const wrapper = mountView([...permissions, 'reporting.definitions.create']); await flushPromises();
    await wrapper.get('[data-testid="reporting-definition-create"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-key"]').setValue('new-report'); await wrapper.get('[data-testid="reporting-config-name"]').setValue('新草稿');
    wrapper.findAllComponents(ElSelect)[1]!.vm.$emit('update:modelValue', groupId); await flushPromises();
    const group = (await groupsMock.mock.results[0]!.value)[0]!; groupsMock.mockResolvedValue([{ ...group, id, name: '其他分组' }]);
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-draft-save"]').trigger('click'); await flushPromises();
    expect(createReportingDefinition).toHaveBeenCalledWith(expect.objectContaining({ groupId, definitionKey: 'new-report' }), expect.any(AbortSignal)); expect(publishReportingDefinition).not.toHaveBeenCalled();
  });
});
