import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import type { ReportingGroup, ReportingDefinitionVersion, ReportingQueryPortDefinition } from '@fullnet/client-contracts';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDefinitionsView from './ReportingDefinitionsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import {
  listReportingDefinitions,
  listReportingGroups,
  listReportingQueryPorts, getReportingDefinition, updateReportingDefinition, publishReportingDefinition, listReportingDefinitionVersions, deleteReportingDefinition, deleteReportingGroup, createReportingDefinition, createReportingGroup
} from '../api/reporting-definitions';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { useSessionStore } from '../auth/session';
import { ElButton, ElDialog, ElDrawer, ElForm, ElMessage, ElPagination, ElSelect } from 'element-plus';
import { dataSourceRow } from '../test/reporting-data-source-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { updateReportingGroup } from '../api/reporting-definitions';

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
function mountView(grants: string[] = [], route?: { name: string }) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().state = 'authenticated';
  useSessionStore().currentUser = { id, username: 'reviewer', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: grants, sessionId: groupId, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(ReportingDefinitionsView, { global: { plugins: [pinia], provide: route ? { [routeLocationKey as symbol]: route } : {}, directives: { loading: {} }, stubs: {
    ArtFormDialog: { props: ['open', 'title', 'saving', 'showConfirm', 'confirmTestId'], emits: ['confirm', 'update:open'], template: '<section v-if="open" :data-title="title"><slot/><button @click="$emit(\'update:open\', false)" data-testid="test-editor-close">关闭</button><button v-if="showConfirm" :disabled="saving" :data-testid="confirmTestId" @click="$emit(\'confirm\')">保存</button></section>' },
    ElDialog: { props: ['modelValue', 'title'], template: '<aside v-if="modelValue" :data-title="title"><slot/><slot name="footer"/></aside>' },
    ElDrawer: { props: ['modelValue'], template: '<aside v-if="modelValue"><slot/></aside>' },
    ReportingDefinitionGrantsDialog: true,
    ArtTableActionGroup: { template: '<div><slot/></div>' }
  } } }); wrappers.push(wrapper); return wrapper;
}
type OwnedState = {
  confirmation?: { confirm: () => void; update: (open: boolean) => void };
  definitions: ReturnType<typeof draft>[]; groups: ReportingGroup[]; writeUnconfirmed: boolean;
  confirmDeleteGroup: (group: ReportingGroup) => Promise<void>;
  openEditGroup: (group: ReportingGroup) => void;
  groupForm: { name: string }; definitionForm: { name: string }; definitionEditorOpen: boolean;
  loadPage: () => Promise<void>;
};
const ownedState = (wrapper: VueWrapper) => wrapper.vm as unknown as OwnedState;

describe('ReportingDefinitionsView', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    groupsMock.mockResolvedValue([]);
    definitionsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    queryPortsMock.mockResolvedValue([]);
    dataSourcesMock.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 });
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
    dataSourcesMock.mockResolvedValue({ items: [{ ...dataSourceRow, id: groupId }], page: 1, pageSize: 100, total: 1 });
    queryPortsMock.mockResolvedValue([{ queryPortKey: 'metadata.schemas', displayName: '目录', description: '', supportedProviderKeys: ['sql_server'],
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
    const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-config-name"]').setValue('保留本地输入');
    await wrapper.get('[data-testid="reporting-config-draft-save"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises(); expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('保留本地输入');
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    vi.mocked(getReportingDefinition).mockResolvedValue({ ...draft(9), name: '最新草稿' }); await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
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
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click');
    const confirm = ownedState(wrapper).confirmation!.confirm; useSessionStore().currentUser = undefined; confirm(); await flushPromises();
    expect(deleteReportingDefinition).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="reporting-management-confirm"]').exists()).toBe(false);
  });
  it('删除返回未应用时保留定义，不提示成功或自动补读', async () => {
    withDefinition(); vi.mocked(deleteReportingDefinition).mockResolvedValue(false); const success = vi.spyOn(ElMessage, 'success');
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click');
    await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    expect(ownedState(wrapper).definitions).toHaveLength(1); expect(ownedState(wrapper).writeUnconfirmed).toBe(true);
    expect(success).not.toHaveBeenCalled(); expect(definitionsMock).toHaveBeenCalledOnce(); expect(wrapper.text()).toContain('服务端未确认删除');
  });
  it('确认删除的定义不能被陈旧补读或刷新复活', async () => {
    withDefinition(); vi.mocked(deleteReportingDefinition).mockResolvedValue(true);
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click');
    await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    expect(ownedState(wrapper).definitions).toEqual([]); await ownedState(wrapper).loadPage(); expect(ownedState(wrapper).definitions).toEqual([]);
    expect(deleteReportingDefinition).toHaveBeenCalledOnce();
  });
  it.each([false, true])('分组删除结果 %s 只接受明确成功，陈旧目录不能恢复已删除组', async deleted => {
    withDefinition(); vi.mocked(deleteReportingGroup).mockResolvedValue(deleted); const success = vi.spyOn(ElMessage, 'success');
    const wrapper = mountView([...permissions, 'reporting.groups.delete']); await flushPromises(); const vm = ownedState(wrapper);
    const deleting = vm.confirmDeleteGroup(vm.groups[0]!); await flushPromises(); await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await deleting;
    expect(vm.groups).toHaveLength(deleted ? 0 : 1); expect(vm.definitions).toHaveLength(deleted ? 0 : 1);
    if (deleted) { await vm.loadPage(); expect(vm.groups).toEqual([]); expect(vm.definitions).toEqual([]); }
    else { expect(success).not.toHaveBeenCalled(); expect(vm.writeUnconfirmed).toBe(true); }
    expect(deleteReportingGroup).toHaveBeenCalledOnce();
  });
  it('取消的确认回调不能批准后来打开的删除确认', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click');
    const old = ownedState(wrapper).confirmation!; old.update(false); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click'); old.confirm(); await flushPromises();
    expect(deleteReportingDefinition).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="reporting-management-confirm"]').exists()).toBe(true);
  });
  it.each(['refresh', 'route'])('%s 销毁原确认，保留的回调不能删除', async change => {
    withDefinition(); const route = reactive({ name: 'reporting-definitions' }); const wrapper = mountView(permissions, route); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click'); const old = ownedState(wrapper).confirmation!;
    if (change === 'route') route.name = 'dashboard'; else await ownedState(wrapper).loadPage();
    old.confirm(); await flushPromises(); expect(deleteReportingDefinition).not.toHaveBeenCalled(); expect(ownedState(wrapper).confirmation).toBeUndefined();
  });
  it('等待确认期间目标修订变化，不能删除较新的定义', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-delete"]').trigger('click');
    ownedState(wrapper).definitions = [draft(9)]; await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    expect(deleteReportingDefinition).not.toHaveBeenCalled();
  });
  it.each(['confirmation', 'response', 'reopen'])('重新载入草稿在 %s 期间失去原窗口或输入归属，不覆盖新输入', async phase => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises();
    const vm = ownedState(wrapper); vm.definitionForm.name = '原本地输入'; await wrapper.vm.$nextTick(); await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click');
    const pending = deferred<ReturnType<typeof draft>>(); vi.mocked(getReportingDefinition).mockReturnValueOnce(pending.promise);
    if (phase === 'confirmation') vm.definitionForm.name = '后来输入';
    await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    if (phase === 'reopen') { vm.definitionEditorOpen = false; await flushPromises(); vi.mocked(getReportingDefinition).mockResolvedValueOnce({ ...draft(), name: '重开输入' }); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); }
    else vm.definitionForm.name = '后来输入';
    pending.resolve({ ...draft(9), name: '迟到服务端' }); await flushPromises();
    expect(vm.definitionForm.name).toBe(phase === 'reopen' ? '重开输入' : '后来输入');
    expect(getReportingDefinition).toHaveBeenCalledTimes(phase === 'confirmation' ? 1 : phase === 'reopen' ? 3 : 2);
  });
  it.each(['confirmation', 'response', 'fresh'])('重新载入分组在 %s 时只接受原输入，保留父组关系', async phase => {
    const child: ReportingGroup = { id, parentId: groupId, name: '子分组', sortOrder: 3, isEnabled: true, createdAtUtc: draft().createdAtUtc, updatedAtUtc: null, version: 4 };
    groupsMock.mockResolvedValue([child]); const wrapper = mountView([...permissions, 'reporting.groups.update']); await flushPromises();
    const vm = ownedState(wrapper); vm.openEditGroup(child); await flushPromises(); await wrapper.get('[data-testid="reporting-config-group-reload"]').trigger('click');
    const pending = deferred<ReportingGroup[]>(); groupsMock.mockReturnValueOnce(pending.promise);
    if (phase === 'confirmation') vm.groupForm.name = '后来输入'; await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    if (phase === 'response') vm.groupForm.name = '后来输入'; pending.resolve([{ ...child, name: '服务端新输入', version: 5 }]); await flushPromises();
    expect(vm.groupForm.name).toBe(phase === 'fresh' ? '服务端新输入' : '后来输入'); expect(vm.groups[0]?.parentId).toBe(groupId);
    expect(groupsMock).toHaveBeenCalledTimes(phase === 'confirmation' ? 1 : 2);
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
    await wrapper.get('[data-testid="reporting-config-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    expect((wrapper.vm as unknown as { definitionGroupId: string }).definitionGroupId).toBe(groupId);
    expect(createReportingDefinition).not.toHaveBeenCalled(); expect(publishReportingDefinition).not.toHaveBeenCalled();
  });
  it('完整读取跨页数据源摘要，第一页不会截断后续可用选择', async () => {
    const first = Array.from({ length: 100 }, (_, index) => ({ ...dataSourceRow, id: '019bc2b1-2a40-7cc3-8992-' + (index + 1000).toString(16).padStart(12, '0') }));
    dataSourcesMock.mockImplementation(async query => ({ items: query?.page === 1 ? first : [dataSourceRow], page: query?.page ?? 1, pageSize: 100, total: 101 }));
    const wrapper = mountView(permissions); await flushPromises();
    const vm = wrapper.vm as unknown as { dataSourceOptions: { value: string }[] };
    expect(vm.dataSourceOptions).toHaveLength(101);
    expect(dataSourcesMock).toHaveBeenNthCalledWith(2, { page: 2, pageSize: 100, isEnabled: true }, expect.any(AbortSignal));
    expect(vm.dataSourceOptions.at(-1)?.value).toBe(dataSourceRow.id);
  });
  it.each(['short', 'duplicate', 'changed', 'limit'] as const)('数据源目录%s不能作为完整选择，定义列表仍独立可读', async failure => {
    withDefinition();
    const first = Array.from({ length: 100 }, (_, index) => ({ ...dataSourceRow, id: '019bc2b1-2a40-7cc3-8992-' + (index + 1000).toString(16).padStart(12, '0') }));
    dataSourcesMock.mockImplementation(async query => ({ items: query?.page === 1 ? failure === 'short' ? [dataSourceRow] : first : [failure === 'duplicate' ? first[0]! : dataSourceRow], page: query?.page ?? 1, pageSize: 100, total: failure === 'limit' ? 10001 : failure === 'changed' && query?.page === 2 ? 102 : 101 }));
    const wrapper = mountView([...permissions, 'reporting.definitions.create']); await flushPromises();
    const vm = wrapper.vm as unknown as { dataSourceOptions: unknown[]; catalogProblems: { sources?: unknown }; definitionEditorOpen: boolean };
    expect(vm.dataSourceOptions).toEqual([]); expect(vm.catalogProblems.sources).toBeDefined();
    expect(wrapper.text()).toContain('配置验收');
    await wrapper.get('[data-testid="reporting-definition-create"]').trigger('click'); await flushPromises();
    expect(vm.definitionEditorOpen).toBe(false); expect(createReportingDefinition).not.toHaveBeenCalled();
  });
  it('切会话取消在途数据源页，不继续获取后页或恢复旧选项', async () => {
    const pending = deferred<Awaited<ReturnType<typeof listReportingDataSources>>>(); dataSourcesMock.mockReturnValueOnce(pending.promise);
    const wrapper = mountView(permissions); await flushPromises(); const signal = dataSourcesMock.mock.calls[0]![1]!;
    useSessionStore().currentUser = undefined;
    expect(signal.aborted).toBe(true); pending.resolve({ items: [], page: 1, pageSize: 100, total: 0 }); await flushPromises();
    expect(dataSourcesMock).toHaveBeenCalledOnce(); expect((wrapper.vm as unknown as { dataSourceOptions: unknown[] }).dataSourceOptions).toEqual([]);
  });
  it('编辑子分组保留父组标识和并发版本', async () => {
    const child = { id, parentId: groupId, name: '子分组', sortOrder: 3, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 4 };
    groupsMock.mockResolvedValue([child]); vi.mocked(updateReportingGroup).mockResolvedValue({ ...child, name: '新名称', version: 5 });
    const wrapper = mountView([...permissions, 'reporting.groups.update']); await flushPromises();
    const vm = wrapper.vm as unknown as { openEditGroup: (row: typeof child) => void; groupForm: { name: string }; submitGroup: () => Promise<void> };
    vm.openEditGroup(child); await flushPromises(); vm.groupForm.name = '新名称'; await vm.submitGroup();
    expect(updateReportingGroup).toHaveBeenCalledWith(id, { name: '新名称', sortOrder: 3, isEnabled: true, parentId: groupId, version: 4 }, expect.any(AbortSignal));
  });
  it('分组校验等待期间草稿变化，原提交失效而不保存后来的内容', async () => {
    const child = { id, parentId: groupId, name: '子分组', sortOrder: 3, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 4 };
    groupsMock.mockResolvedValue([child]); const wrapper = mountView([...permissions, 'reporting.groups.update']); await flushPromises();
    const vm = wrapper.vm as unknown as { openEditGroup: (row: typeof child) => void; groupForm: { name: string }; submitGroup: () => Promise<void> };
    vm.openEditGroup(child); await flushPromises(); const pending = deferred<boolean>();
    vi.spyOn(wrapper.findAllComponents(ElForm)[0]!.vm.$.exposed!, 'validate').mockImplementation(() => pending.promise);
    const submitting = vm.submitGroup(); vm.groupForm.name = '后来输入'; pending.resolve(true); await submitting;
    expect(updateReportingGroup).not.toHaveBeenCalled();
  });

  it('辅助目录重读使校验中的旧草稿提交失效，即使目录内容相同', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises();
    const pending = deferred<boolean>(); vi.spyOn(wrapper.findAllComponents(ElForm)[0]!.vm.$.exposed!, 'validate').mockImplementation(() => pending.promise);
    const vm = wrapper.vm as unknown as { submitDefinition: () => Promise<void>; loadPage: () => Promise<void> };
    const submitting = vm.submitDefinition(); await vm.loadPage(); pending.resolve(true); await submitting;
    expect(updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('分组校验等待期间切换编辑目标，原提交不能写入任一分组', async () => {
    const child: ReportingGroup = { id, parentId: groupId, name: '子分组', sortOrder: 3, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 4 };
    const root = { ...child, id: groupId, parentId: null, name: '父分组' };
    groupsMock.mockResolvedValue([child, root]); const wrapper = mountView([...permissions, 'reporting.groups.update']); await flushPromises();
    const vm = wrapper.vm as unknown as { openEditGroup: (row: ReportingGroup) => void; submitGroup: () => Promise<void>; groupForm: { name: string } };
    vm.openEditGroup(child); await flushPromises(); const pending = deferred<boolean>();
    vi.spyOn(wrapper.findAllComponents(ElForm)[0]!.vm.$.exposed!, 'validate').mockImplementation(() => pending.promise);
    const submitting = vm.submitGroup(); vm.openEditGroup(root); pending.resolve(true); await submitting;
    expect(updateReportingGroup).not.toHaveBeenCalled(); expect(vm.groupForm.name).toBe('父分组');
  });

  it('数据源目录仍在刷新时不能借旧候选提交草稿', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises();
    const pending = deferred<Awaited<ReturnType<typeof listReportingDataSources>>>(); dataSourcesMock.mockReturnValueOnce(pending.promise);
    const vm = wrapper.vm as unknown as { submitDefinition: () => Promise<void>; loadPage: () => Promise<void> };
    const reading = vm.loadPage(); await vm.submitDefinition(); expect(updateReportingDefinition).not.toHaveBeenCalled();
    pending.resolve({ items: [], page: 1, pageSize: 100, total: 0 }); await reading;
  });
  it.each(['cancel', 'fresh', 'input', 'catalog'])('端口参数重置在 %s 时只处理当前草稿，其他输入保留', async phase => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-name"]').setValue('其他输入保留');
    const port: ReportingQueryPortDefinition = (await queryPortsMock.mock.results[0]!.value)[0]!; queryPortsMock.mockResolvedValue([{ ...port, parameters: port.parameters.map(row => ({ ...row, isRequired: true })) }]);
    await wrapper.get('[data-testid="reporting-editor-catalog-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    const vm = wrapper.vm as unknown as { definitionForm: ReturnType<typeof draft> }; const original = JSON.stringify(vm.definitionForm.parameterSchema);
    await wrapper.get('[data-testid="reporting-parameters-reset"]').trigger('click'); const prompt = ownedState(wrapper).confirmation!;
    if (phase === 'catalog') { await ownedState(wrapper).loadPage(); prompt.confirm(); }
    else { if (phase === 'input') vm.definitionForm.name = '后来输入'; if (phase === 'cancel') prompt.update(false); else prompt.confirm(); }
    await flushPromises(); expect(updateReportingDefinition).not.toHaveBeenCalled();
    expect(vm.definitionForm.name).toBe(phase === 'input' ? '后来输入' : '其他输入保留');
    if (phase === 'fresh') { expect(vm.definitionForm.parameterSchema[0]).toMatchObject({ displayName: '目录默认名称', defaultValue: 'catalog-default', isRequired: true }); expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(true); }
    else expect(JSON.stringify(vm.definitionForm.parameterSchema)).toBe(original);
  });
  it('详情等待即提供可关闭编辑容器，关闭取消请求且晚结果不重开', async () => {
    withDefinition(); const pending = deferred<ReturnType<typeof draft>>(); vi.mocked(getReportingDefinition).mockReturnValueOnce(pending.promise);
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click');
    expect(wrapper.get('[data-testid="reporting-draft-loading"]').text()).toContain('可关闭'); expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="reporting-config-name"]').attributes('disabled')).toBeDefined();
    const signal = vi.mocked(getReportingDefinition).mock.lastCall![1]!; await wrapper.get('[data-testid="test-editor-close"]').trigger('click'); expect(signal.aborted).toBe(true);
    pending.resolve(draft()); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-name"]').exists()).toBe(false);
  });
  it('详情失败保留可重试窗口，确认重读后恢复且不自动保存', async () => {
    withDefinition(); vi.mocked(getReportingDefinition).mockRejectedValueOnce(new Error('offline')); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-name"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-config-name"]').setValue('临时输入'); await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click');
    await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises(); expect(getReportingDefinition).toHaveBeenCalledTimes(2);
    expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe(draft().name); expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(true); expect(updateReportingDefinition).not.toHaveBeenCalled();
  });
  it('打开分组意图取消旧定义读取，迟到详情不并排打开编辑器', async () => {
    withDefinition(); const pending = deferred<ReturnType<typeof draft>>(); vi.mocked(getReportingDefinition).mockReturnValueOnce(pending.promise);
    const wrapper = mountView([...permissions, 'reporting.groups.update']); await flushPromises(); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click');
    const signal = vi.mocked(getReportingDefinition).mock.lastCall![1]!; ownedState(wrapper).openEditGroup(ownedState(wrapper).groups[0]!); expect(signal.aborted).toBe(true);
    pending.resolve(draft()); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-name"]').exists()).toBe(false); expect(wrapper.find('[data-testid="reporting-config-group-save"]').exists()).toBe(true);
  });
  it('目录新修订保留本地定义但禁止保存，旧详情也不能解除，确认新详情后恢复', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-name"]').setValue('保留本地'); definitionsMock.mockResolvedValue({ items: [draft(8)], total: 1, page: 1, pageSize: 20 });
    await wrapper.get('[data-testid="reporting-editor-catalog-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false);
    expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('保留本地');
    await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click'); await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(false); expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('保留本地');
    vi.mocked(getReportingDefinition).mockResolvedValueOnce({ ...draft(9), name: '最新草稿' }); await wrapper.get('[data-testid="reporting-config-draft-reload"]').trigger('click'); await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="reporting-config-draft-save"]').exists()).toBe(true); expect((wrapper.get('[data-testid="reporting-config-name"]').element as HTMLInputElement).value).toBe('最新草稿'); expect(updateReportingDefinition).not.toHaveBeenCalled();
  });

  type SavingState = OwnedState & { draftReceipt?: { outcome: string; version?: number; resource: string }; creationBlocks: Set<string>;
    openCreateGroup: () => void; openCreateDefinition: () => void; groupEditorOpen: boolean; acting: boolean;
    submitGroup: () => Promise<void>; submitDefinition: () => Promise<void>; acknowledgeRecovery: (resource: 'group' | 'definition') => Promise<void> };
  async function prepareSave(resource: 'group' | 'definition', mode: 'create' | 'update' = 'create') {
    withDefinition(); dataSourcesMock.mockResolvedValue({ items: [{ ...dataSourceRow, id: groupId }], page: 1, pageSize: 100, total: 1 });
    vi.mocked(createReportingGroup).mockImplementation(async body => ({ ...body, parentId: null, id, createdAtUtc: draft().createdAtUtc, updatedAtUtc: null, version: 1 }));
    vi.mocked(createReportingDefinition).mockImplementation(async body => ({ ...draft(1), ...body, id: '019bc2b1-2a40-7cc3-8992-a80de51bf297', description: body.description ?? null, layoutConfigJson: body.layoutConfigJson ?? '{}', latestPublishedVersionNumber: 0 }));
    vi.mocked(updateReportingGroup).mockImplementation(async (target, body) => ({ id: target, ...body, parentId: body.parentId ?? null, createdAtUtc: draft().createdAtUtc, updatedAtUtc: null, version: body.version + 1 }));
    vi.mocked(updateReportingDefinition).mockImplementation(async (target, body) => ({ ...draft(), ...body, id: target, description: body.description ?? null, layoutConfigJson: body.layoutConfigJson ?? '{}', version: body.version + 1 }));
    const wrapper = mountView([...permissions, 'reporting.groups.create', 'reporting.groups.update', 'reporting.definitions.create']); await flushPromises();
    const vm = wrapper.vm as unknown as SavingState;
    if (resource === 'group') { if (mode === 'create') vm.openCreateGroup(); else vm.openEditGroup(vm.groups[0]!); vm.groupForm.name = '保存原分组'; }
    else { if (mode === 'create') vm.openCreateDefinition(); else { await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); await flushPromises(); }
      Object.assign(vm.definitionForm, { definitionKey: 'report-review', name: '保存原定义' }); }
    await flushPromises(); return { wrapper, vm, submit: () => resource === 'group' ? vm.submitGroup() : vm.submitDefinition() };
  }
  it.each(['group', 'definition'] as const)('%s 保存成功先保留回执，补读失败不降级或重发', async resource => {
    const { vm, submit } = await prepareSave(resource, 'update'); definitionsMock.mockRejectedValueOnce(new Error('read failed'));
    await submit(); expect(vm.draftReceipt).toMatchObject({ resource, outcome: 'confirmed', version: resource === 'group' ? 2 : 8 });
    expect(resource === 'group' ? vm.groupEditorOpen : vm.definitionEditorOpen).toBe(false); expect(vm.acting).toBe(false);
    expect(resource === 'group' ? updateReportingGroup : updateReportingDefinition).toHaveBeenCalledOnce();
  });
  it.each(['group', 'definition'] as const)('%s 新建成功不会被自己的关闭降级为未知', async resource => {
    const { vm, submit } = await prepareSave(resource); await submit(); expect(vm.draftReceipt).toMatchObject({ resource, outcome: 'confirmed', version: 1 });
    expect(vm.creationBlocks.has(resource)).toBe(false); expect(vm.writeUnconfirmed).toBe(false); expect(vm.acting).toBe(false);
  });
  it.each(['group', 'definition'] as const)('%s 校验期间输入变化，不能借用新输入提交', async resource => {
    const { wrapper, vm, submit } = await prepareSave(resource, 'update'); const pending = deferred<boolean>();
    vi.spyOn(wrapper.findAllComponents(ElForm)[0]!.vm.$.exposed!, 'validate').mockImplementation(() => pending.promise);
    const saving = submit(); (resource === 'group' ? vm.groupForm : vm.definitionForm).name = '后来输入'; pending.resolve(true); await saving;
    expect(resource === 'group' ? updateReportingGroup : updateReportingDefinition).not.toHaveBeenCalled();
  });
  it.each(['group', 'definition'] as const)('%s 在途新建关闭取消接入，刷新重开仍保留原输入和防重', async resource => {
    const { wrapper, vm, submit } = await prepareSave(resource); const pending = deferred<never>();
    const api = resource === 'group' ? createReportingGroup : createReportingDefinition; vi.mocked(api).mockReturnValueOnce(pending.promise);
    const saving = submit(); await flushPromises(); const signal = vi.mocked(api).mock.lastCall![1]!;
    if (resource === 'group') vm.groupEditorOpen = false; else vm.definitionEditorOpen = false;
    expect(signal.aborted).toBe(true); expect(vm.draftReceipt?.outcome).toBe('unknown'); expect(vm.acting).toBe(false);
    await vm.loadPage(); if (resource === 'group') vm.openCreateGroup(); else vm.openCreateDefinition(); await flushPromises();
    expect((resource === 'group' ? vm.groupForm : vm.definitionForm).name).toBe(resource === 'group' ? '保存原分组' : '保存原定义');
    await submit(); expect(api).toHaveBeenCalledOnce(); expect(vm.creationBlocks.has(resource)).toBe(true);
    pending.reject(new Error('late response')); await saving; expect(vm.draftReceipt?.outcome).toBe('unknown');
    const recovery = vm.acknowledgeRecovery(resource); await flushPromises(); await wrapper.get('[data-testid="reporting-management-confirm"]').trigger('click'); await recovery;
    expect(vm.creationBlocks.has(resource)).toBe(false); expect(api).toHaveBeenCalledOnce(); await submit(); expect(api).toHaveBeenCalledTimes(2); expect(vm.draftReceipt?.outcome).toBe('confirmed');
  });
  it.each(['group', 'definition'] as const)('%s 新建回包错配按未知结果保护，不提示成功', async resource => {
    const { vm, submit } = await prepareSave(resource); const success = vi.spyOn(ElMessage, 'success');
    if (resource === 'group') vi.mocked(createReportingGroup).mockResolvedValueOnce({ ...vm.groups[0]!, name: '错配' }); else vi.mocked(createReportingDefinition).mockResolvedValueOnce(draft());
    await submit(); expect(vm.draftReceipt?.outcome).toBe('unknown'); expect(vm.creationBlocks.has(resource)).toBe(true); expect(success).not.toHaveBeenCalled();
  });
  it('新建定义不继承上次编辑在相同端口下的私有参数输入', async () => {
    const { wrapper, vm } = await prepareSave('definition', 'update');
    const form = vm.definitionForm as ReturnType<typeof draft>; form.parameterSchema[0]!.displayName = '上次私有名称'; form.parameterSchema[0]!.defaultValue = '上次私有值';
    vm.definitionEditorOpen = false; vm.openCreateDefinition(); await flushPromises();
    expect(form.parameterSchema[0]).toMatchObject({ displayName: '目录默认名称', defaultValue: 'catalog-default' });
    expect(wrapper.text()).not.toContain('上次私有值');
  });
  it('新建回包不得冒用当前已知定义的标识', async () => {
    const { vm, submit } = await prepareSave('definition');
    vi.mocked(createReportingDefinition).mockImplementationOnce(async body => ({ ...draft(1), ...body, description: body.description ?? null, layoutConfigJson: body.layoutConfigJson ?? '{}', latestPublishedVersionNumber: 0 }));
    await submit(); expect(vm.draftReceipt?.outcome).toBe('unknown'); expect(vm.creationBlocks.has('definition')).toBe(true);
  });
  it.each(['group', 'definition'] as const)('%s 原窗口输入、关闭与保存回调不能修改后来重开的编辑器', async resource => {
    const { wrapper, vm } = await prepareSave(resource, 'update'); const dialogs = wrapper.findAllComponents(ArtFormDialog);
    const editor = dialogs.find(item => item.props('open'))!; const props = editor.vm.$.vnode.props!;
    const oldClose = props['onUpdate:open'] as (open: boolean) => void, oldSave = props.onConfirm as () => void;
    const oldInput = editor.getComponent({ name: 'ElInput' }).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    if (resource === 'group') { vm.groupEditorOpen = false; vm.openEditGroup(vm.groups[0]!); }
    else { vm.definitionEditorOpen = false; await wrapper.get('[data-testid="reporting-config-edit"]').trigger('click'); }
    await flushPromises(); oldInput('旧回调'); oldClose(false); oldSave(); await flushPromises();
    expect(resource === 'group' ? vm.groupEditorOpen : vm.definitionEditorOpen).toBe(true);
    expect((resource === 'group' ? vm.groupForm : vm.definitionForm).name).not.toBe('旧回调'); expect(resource === 'group' ? updateReportingGroup : updateReportingDefinition).not.toHaveBeenCalled();
  });
  it.each(['group', 'definition'] as const)('%s 确定拒绝可以修正后人工重试，冲突需要重新读取', async resource => {
    const { vm, submit } = await prepareSave(resource, 'update'); const api = resource === 'group' ? updateReportingGroup : updateReportingDefinition;
    vi.mocked(api).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: 'Invalid' }); await submit();
    expect(vm.draftReceipt?.outcome).toBe('rejected'); expect(vm.writeUnconfirmed).toBe(false); await submit(); expect(vm.draftReceipt?.outcome).toBe('confirmed');
  });

  const publication = (change: Partial<ReportingDefinitionVersion> = {}): ReportingDefinitionVersion => ({ id: groupId, definitionId: id, dataSourceId: groupId,
    versionNumber: 2, queryPortKey: draft().queryPortKey, parameterSchema: draft().parameterSchema, layoutConfigJson: draft().layoutConfigJson,
    changeNote: null, publishedByUserId: id, publishedAtUtc: draft().createdAtUtc, ...change });
  type PublicationState = { publishOpen: boolean; publicationReceipt?: { outcome: string; versionNumber?: number }; definitions: ReturnType<typeof draft>[]; problem?: unknown;
    loadPage: () => Promise<void>; confirmPublish: () => Promise<void> };
  const publicationState = (wrapper: VueWrapper) => wrapper.vm as unknown as PublicationState;
  const publicationButton = (wrapper: VueWrapper) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'reporting-config-publish-confirm')!;
  type HistoryState = { versionsDrawerOpen: boolean; versions: { id: string; versionNumber: number }[]; versionsProblem?: unknown; versionsLoading: boolean; versionPage: number; openVersions: (row: ReturnType<typeof draft>) => Promise<void>; loadVersions: () => Promise<void> };
  const historyState = (wrapper: VueWrapper) => wrapper.vm as unknown as HistoryState;
  const historyRow = (number: number) => { const { parameterSchema: _, layoutConfigJson: __, ...row } = publication({ versionNumber: number,
    id: '019bc2b1-2a40-7cc3-8992-' + (1000 + number).toString(16).padStart(12, '0') }); return row; };
  it('历史读取失败只在抽屉中提示，重试原定义恢复且不发布', async () => {
    withDefinition(); vi.mocked(listReportingDefinitionVersions).mockRejectedValueOnce({ status: 503, code: 'fixture.history', title: '历史暂不可用' });
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); await flushPromises();
    expect(historyState(wrapper).versionsDrawerOpen).toBe(true); expect(historyState(wrapper).versionsLoading).toBe(false);
    expect(wrapper.get('[data-testid="reporting-history-problem"]').text()).toContain('历史暂不可用'); expect(publicationState(wrapper).problem).toBeUndefined();
    vi.mocked(listReportingDefinitionVersions).mockResolvedValueOnce({ items: [historyRow(1)], total: 1, page: 1, pageSize: 20 });
    await wrapper.get('[data-testid="reporting-history-retry"]').trigger('click'); await flushPromises();
    expect(historyState(wrapper).versions).toHaveLength(1); expect(historyState(wrapper).versionsProblem).toBeUndefined(); expect(publishReportingDefinition).not.toHaveBeenCalled();
  });
  it('关闭历史同步取消读取，迟到结果和旧关闭回调不能影响重开抽屉', async () => {
    withDefinition(); const pending = deferred<Awaited<ReturnType<typeof listReportingDefinitionVersions>>>(); vi.mocked(listReportingDefinitionVersions).mockReturnValueOnce(pending.promise);
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); const vm = historyState(wrapper);
    const oldClose = wrapper.getComponent(ElDrawer).vm.$.vnode.props!['onUpdate:modelValue'] as (value: boolean) => void;
    const signal = vi.mocked(listReportingDefinitionVersions).mock.lastCall![1]!; vm.versionsDrawerOpen = false; expect(signal.aborted).toBe(true);
    vi.mocked(listReportingDefinitionVersions).mockResolvedValueOnce({ items: [historyRow(1)], total: 1, page: 1, pageSize: 20 });
    await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); await flushPromises(); oldClose(false);
    pending.resolve({ items: [], total: 0, page: 1, pageSize: 20 }); await flushPromises(); expect(vm.versionsDrawerOpen).toBe(true); expect(vm.versions).toHaveLength(1);
  });
  it.each(['changed-id', 'missing', 'older-total'])('已确认发布的历史不能 %s，失败后仍可重试核对', async change => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockResolvedValueOnce(publication()); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises(); await publicationState(wrapper).confirmPublish();
    const actual = { ...historyRow(2), id: groupId };
    vi.mocked(listReportingDefinitionVersions).mockResolvedValueOnce({ items: change === 'older-total' ? [historyRow(1)] : change === 'missing' ? [historyRow(3), historyRow(1)] : [historyRow(2), historyRow(1)],
      total: change === 'older-total' ? 1 : change === 'missing' ? 3 : 2, page: 1, pageSize: 20 });
    await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); await flushPromises(); expect(historyState(wrapper).versions).toEqual([]); expect(historyState(wrapper).versionsProblem).toBeDefined();
    vi.mocked(listReportingDefinitionVersions).mockResolvedValueOnce({ items: [actual, historyRow(1)], total: 2, page: 1, pageSize: 20 });
    await wrapper.get('[data-testid="reporting-history-retry"]').trigger('click'); await flushPromises(); expect(historyState(wrapper).versions).toHaveLength(2); expect(publishReportingDefinition).toHaveBeenCalledOnce();
  });
  it('翻页立即清除旧摘要，旧分页回调不能操作新页', async () => {
    withDefinition(); vi.mocked(listReportingDefinitionVersions).mockResolvedValueOnce({ items: Array.from({ length: 20 }, (_, index) => historyRow(21 - index)), total: 21, page: 1, pageSize: 20 });
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-history"]').trigger('click'); await flushPromises();
    const pagination = wrapper.findAllComponents(ElPagination).at(-1)!; const oldPage = pagination.vm.$.vnode.props!['onUpdate:currentPage'] as (value: number) => void;
    const pending = deferred<Awaited<ReturnType<typeof listReportingDefinitionVersions>>>(); vi.mocked(listReportingDefinitionVersions).mockReturnValueOnce(pending.promise);
    oldPage(2); expect(historyState(wrapper).versions).toEqual([]); oldPage(1); expect(historyState(wrapper).versionPage).toBe(2);
    pending.resolve({ items: [historyRow(1)], total: 21, page: 2, pageSize: 20 }); await flushPromises(); expect(historyState(wrapper).versions[0]?.versionNumber).toBe(1);
    expect(listReportingDefinitionVersions).toHaveBeenCalledTimes(2);
  });
  it('发布成功先保存精确版本，补读失败不撤销确认或自动再次发布', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockResolvedValue(publication()); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="reporting-publication-target-version"]').text()).toContain('7');
    definitionsMock.mockRejectedValueOnce(new Error('offline')); await publicationState(wrapper).confirmPublish();
    expect(publicationState(wrapper).publicationReceipt).toMatchObject({ outcome: 'confirmed', versionNumber: 2 }); expect(publicationState(wrapper).definitions[0]?.version).toBe(8);
    expect(publicationState(wrapper).publishOpen).toBe(false); await publicationState(wrapper).confirmPublish(); expect(publishReportingDefinition).toHaveBeenCalledOnce();
  });
  it('已确认发布修订不能被后续陈旧目录回退', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockResolvedValue(publication());
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    await publicationState(wrapper).confirmPublish(); await publicationState(wrapper).loadPage();
    expect(publicationState(wrapper).definitions[0]).toMatchObject({ version: 8, latestPublishedVersionNumber: 2 }); expect(publicationState(wrapper).problem).toBeDefined();
    expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('confirmed'); expect(publishReportingDefinition).toHaveBeenCalledOnce();
  });
  it('观察到更高目录修订后不再回退，保留最后可信摘要', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises();
    definitionsMock.mockResolvedValueOnce({ items: [{ ...draft(9), latestPublishedVersionNumber: 3 }], page: 1, pageSize: 20, total: 1 });
    await publicationState(wrapper).loadPage(); await publicationState(wrapper).loadPage();
    expect(publicationState(wrapper).definitions[0]).toMatchObject({ version: 9, latestPublishedVersionNumber: 3 }); expect(publicationState(wrapper).problem).toBeDefined();
  });
  it.each([{ definitionId: groupId }, { dataSourceId: id }, { versionNumber: 1 }, { queryPortKey: 'other' }, { parameterSchema: [] }, { layoutConfigJson: '{"other":true}' }, { publishedByUserId: groupId }, { changeNote: 'other' }])('发布快照错配%j按未知保护，不反馈成功', async change => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockResolvedValue(publication(change)); const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises(); await publicationState(wrapper).confirmPublish();
    expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('unknown'); expect(success).not.toHaveBeenCalled(); expect(definitionsMock).toHaveBeenCalledOnce();
  });
  it('等价UUID大小写和规范布局可以确认发布', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockResolvedValue(publication({ definitionId: id.toUpperCase(), publishedByUserId: id.toUpperCase(), layoutConfigJson: ' { } ' }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises(); await publicationState(wrapper).confirmPublish();
    expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('confirmed');
  });
  it('说明变化使旧确认失效，新确认冻结当前说明', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockImplementation(async (_key, body) => publication({ changeNote: body.changeNote ?? null }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    const old = publicationButton(wrapper).vm.$.vnode.props!.onClick as () => void;
    await wrapper.get('[data-testid="reporting-config-publish-note"]').setValue('Reviewed'); old(); await flushPromises(); expect(publishReportingDefinition).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="reporting-config-publish-confirm"]').trigger('click'); await flushPromises();
    expect(publishReportingDefinition).toHaveBeenCalledWith(id, { version: 7, changeNote: 'Reviewed' }, expect.any(AbortSignal)); expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('confirmed');
  });
  it('旧窗口关闭和确认不能影响同一资源的新窗口', async () => {
    withDefinition(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    const old = publicationButton(wrapper).vm.$.vnode.props!.onClick as () => void;
    const close = wrapper.findAllComponents(ElDialog).find(item => item.props('modelValue'))!.vm.$.vnode.props!['onUpdate:modelValue'] as (value: boolean) => void;
    close(false); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises(); close(false); old(); await flushPromises();
    expect(publicationState(wrapper).publishOpen).toBe(true); expect(publishReportingDefinition).not.toHaveBeenCalled();
  });
  it('关闭在途发布中止接入并保留未知，晚成功不改写回执', async () => {
    withDefinition(); const pending = deferred<ReportingDefinitionVersion>(); vi.mocked(publishReportingDefinition).mockReturnValueOnce(pending.promise);
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    const action = publicationState(wrapper).confirmPublish(), signal = vi.mocked(publishReportingDefinition).mock.lastCall![2]!; publicationState(wrapper).publishOpen = false;
    expect(signal.aborted).toBe(true); pending.resolve(publication()); await action; expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('unknown');
    expect(definitionsMock).toHaveBeenCalledOnce();
  });
  it('明确发布拒绝保留确认窗口，允许修正后手动提交', async () => {
    withDefinition(); vi.mocked(publishReportingDefinition).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: 'Invalid' }).mockResolvedValue(publication());
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    await publicationState(wrapper).confirmPublish(); expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('rejected'); expect(publicationState(wrapper).publishOpen).toBe(true);
    await publicationState(wrapper).confirmPublish(); expect(publishReportingDefinition).toHaveBeenCalledTimes(2); expect(publicationState(wrapper).publicationReceipt?.outcome).toBe('confirmed');
  });
  it('路由离开取消发布并销毁本作用域回执，返回不重放', async () => {
    withDefinition(); const route = reactive({ name: 'reporting-definitions' }), pending = deferred<ReportingDefinitionVersion>(); vi.mocked(publishReportingDefinition).mockReturnValueOnce(pending.promise);
    const wrapper = mountView(permissions, route); await flushPromises(); await wrapper.get('[data-testid="reporting-config-publish"]').trigger('click'); await flushPromises();
    const action = publicationState(wrapper).confirmPublish(), signal = vi.mocked(publishReportingDefinition).mock.lastCall![2]!; route.name = 'home'; expect(signal.aborted).toBe(true);
    pending.resolve(publication()); await action; expect(publicationState(wrapper).publicationReceipt).toBeUndefined(); route.name = 'reporting-definitions'; await flushPromises(); expect(publishReportingDefinition).toHaveBeenCalledOnce();
  });

});
