import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingDataSourcesView from './ReportingDataSourcesView.vue';
import { listReportingDataSources, getReportingDataSource, createReportingDataSource, updateReportingDataSource, disableReportingDataSource, deleteReportingDataSource, testReportingDataSource } from '../api/reporting-data-sources';
import { useSessionStore } from '../auth/session';
import { ElMessageBox } from 'element-plus';

vi.mock('../api/reporting-data-sources', () => ({
  listReportingDataSources: vi.fn(),
  getReportingDataSource: vi.fn(),
  createReportingDataSource: vi.fn(),
  updateReportingDataSource: vi.fn(),
  disableReportingDataSource: vi.fn(),
  deleteReportingDataSource: vi.fn(),
  testReportingDataSource: vi.fn()
}));

const listMock = vi.mocked(listReportingDataSources);
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', sessionId = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const detail = (version = 7) => ({ id, tenantId: null, name: '受控数据源', providerKey: 'sql_server', serverHost: 'reports.example.com', port: 1433, databaseName: 'reports', username: 'readonly',
  hasPassword: true, trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version });
const summary = () => { const { serverHost: _, port: __, databaseName: ___, username: ____, ...value } = detail(4); return { ...value, maskedServerEndpoint: 'r***:1433', maskedDatabaseName: 'r***', maskedUsername: 'r***' }; };
const permissions = ['read', 'create', 'update', 'delete', 'test'].map(action => `reporting.data_sources.${action}`);
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); });

function mountView(grants: string[] = []) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = { id, username: 'reviewer', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host', permissions: grants,
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(ReportingDataSourcesView, { global: { plugins: [pinia], directives: { loading: {} }, stubs: {
    ArtFormDialog: { props: ['open', 'title', 'saving', 'showConfirm', 'confirmTestId'], emits: ['confirm', 'update:open'], template: '<section v-if="open" :data-title="title"><slot/><button data-testid="test-source-close" @click="$emit(\'update:open\', false)">关闭</button><button v-if="showConfirm" :disabled="saving" :data-testid="confirmTestId" @click="$emit(\'confirm\')">保存</button></section>' },
    ArtTableActionGroup: { template: '<div><slot/></div>' }
  } } }); wrappers.push(wrapper); return wrapper;
}
async function fillCreate(wrapper: VueWrapper) {
  await wrapper.get('[data-testid="reporting-data-source-create"]').trigger('click');
  for (const [key, value] of Object.entries({ name: '新数据源', host: 'reports.example.com', port: '1433', database: 'reports', username: 'readonly', password: 'test-fixture-password' })) await wrapper.get(`[data-testid="reporting-source-${key}"]`).setValue(value);
}

describe('ReportingDataSourcesView', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    listMock.mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });
    vi.mocked(getReportingDataSource).mockResolvedValue(detail()); vi.mocked(createReportingDataSource).mockResolvedValue(detail(1));
    vi.mocked(updateReportingDataSource).mockResolvedValue(detail(8)); vi.mocked(disableReportingDataSource).mockResolvedValue({ ...detail(8), isEnabled: false });
    vi.mocked(testReportingDataSource).mockResolvedValue({ succeeded: true, message: '受控连接成功' });
  });

  it('hides create action without permission', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-testid="reporting-data-source-create"]').exists()).toBe(false);
  });
  function existing() { listMock.mockResolvedValue({ items: [summary()], page: 1, pageSize: 20, total: 1 }); }
  it('只读 Host 只读取脱敏列表，不请求连接正文或测试', async () => {
    existing(); const wrapper = mountView(['reporting.data_sources.read']); await flushPromises(); expect(wrapper.text()).toContain('r***:1433');
    expect(wrapper.find('[data-testid="reporting-data-source-edit"]').exists()).toBe(false); expect(getReportingDataSource).not.toHaveBeenCalled(); expect(testReportingDataSource).not.toHaveBeenCalled();
  });
  it('新建入口与搜索在现行工具栏显示，保存固定提供程序和安全连接选项', async () => {
    const wrapper = mountView(permissions); await flushPromises(); expect(wrapper.findComponent({ name: 'ArtSearchBar' }).exists()).toBe(true); await fillCreate(wrapper);
    await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises();
    expect(createReportingDataSource).toHaveBeenCalledWith(expect.objectContaining({ tenantId: null, providerKey: 'sql_server', port: 1433, trustServerCertificate: false }), expect.any(AbortSignal));
  });
  it('非法端口不写入，修正后才提交', async () => {
    const wrapper = mountView(permissions); await flushPromises(); await fillCreate(wrapper); await wrapper.get('[data-testid="reporting-source-port"]').setValue('65536');
    await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises(); expect(createReportingDataSource).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('1–65535');
  });
  it('编辑使用详情版本，密码留空传 null 保留原凭据', async () => {
    existing(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="reporting-source-password"]').element as HTMLInputElement).value).toBe('');
    await wrapper.get('[data-testid="reporting-source-name"]').setValue('修改连接名称'); await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises();
    expect(updateReportingDataSource).toHaveBeenCalledWith(id, expect.objectContaining({ name: '修改连接名称', version: 7, password: null }), expect.any(AbortSignal));
  });
  it('关闭并重新打开时不保留本次输入的密码', async () => {
    existing(); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-source-password"]').setValue('new-local-password'); await wrapper.get('[data-testid="test-source-close"]').trigger('click');
    await wrapper.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises(); expect((wrapper.get('[data-testid="reporting-source-password"]').element as HTMLInputElement).value).toBe('');
  });
  it('冲突保留本地输入，列表刷新不解锁，确认重新读取后清空密码并使用新版本', async () => {
    existing(); vi.mocked(updateReportingDataSource).mockRejectedValueOnce({ status: 409, code: 'reporting.data_source.version_conflict', title: 'Conflict' }); vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(undefined as never);
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-edit"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="reporting-source-name"]').setValue('保留本地名称'); await wrapper.get('[data-testid="reporting-source-password"]').setValue('new-local-password');
    await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="reporting-source-refresh"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="reporting-source-name"]').element as HTMLInputElement).value).toBe('保留本地名称'); expect(wrapper.find('[data-testid="reporting-data-source-editor-submit"]').exists()).toBe(false);
    vi.mocked(getReportingDataSource).mockResolvedValue(detail(9)); await wrapper.get('[data-testid="reporting-source-reload"]').trigger('click'); await flushPromises();
    expect((wrapper.get('[data-testid="reporting-source-password"]').element as HTMLInputElement).value).toBe('');
    await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises(); expect(vi.mocked(updateReportingDataSource).mock.lastCall?.[1].version).toBe(9);
  });
  it('切租户取消连接详情读取，迟到正文不能重新进入编辑器', async () => {
    existing(); let resolve!: (value: ReturnType<typeof detail>) => void; vi.mocked(getReportingDataSource).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-edit"]').trigger('click'); const signal = vi.mocked(getReportingDataSource).mock.calls[0]![1]!;
    useSessionStore().currentUser = { ...useSessionStore().currentUser!, actorScope: 'tenant', scope: 'tenant', tenantId: sessionId }; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(detail()); await flushPromises(); expect(wrapper.find('[data-testid="reporting-source-name"]').exists()).toBe(false); expect(wrapper.text()).not.toContain('受控数据源');
  });
  it('失去会话取消创建请求，迟到成功不刷新旧列表', async () => {
    let resolve!: (value: ReturnType<typeof detail>) => void; vi.mocked(createReportingDataSource).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(permissions); await flushPromises(); await fillCreate(wrapper); await wrapper.get('[data-testid="reporting-data-source-editor-submit"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(createReportingDataSource).mock.calls[0]![1]!; useSessionStore().currentUser = undefined; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(detail()); await flushPromises(); expect(listMock).toHaveBeenCalledTimes(1); expect(wrapper.find('[data-testid="reporting-source-name"]').exists()).toBe(false);
  });
  it('连接测试结果不明禁止重放，只有人工列表刷新后才能再次操作', async () => {
    existing(); vi.mocked(testReportingDataSource).mockRejectedValue(new Error('lost response')); const wrapper = mountView(permissions); await flushPromises();
    await wrapper.get('[data-testid="reporting-data-source-test"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-source-unconfirmed"]').exists()).toBe(true);
    await wrapper.get('[data-testid="reporting-data-source-test"]').trigger('click'); expect(testReportingDataSource).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="reporting-source-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.find('[data-testid="reporting-source-unconfirmed"]').exists()).toBe(false); expect(testReportingDataSource).toHaveBeenCalledTimes(1);
  });
  it('禁用确认期间换会话，不能在新上下文执行旧禁用', async () => {
    existing(); let confirm!: () => void; vi.spyOn(ElMessageBox, 'confirm').mockImplementationOnce(() => new Promise(done => { confirm = () => done(undefined as never); }));
    const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-disable"]').trigger('click'); useSessionStore().currentUser = undefined; confirm(); await flushPromises(); expect(disableReportingDataSource).not.toHaveBeenCalled();
  });
  it('取消删除确认正常结束，不产生请求或未知结果警告', async () => {
    existing(); vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue('cancel'); const wrapper = mountView(permissions); await flushPromises(); await wrapper.get('[data-testid="reporting-data-source-delete"]').trigger('click'); await flushPromises();
    expect(deleteReportingDataSource).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="reporting-source-unconfirmed"]').exists()).toBe(false);
  });
  it('测试前的迟到列表读取不能解除未知结果锁', async () => {
    existing(); const wrapper = mountView(permissions); await flushPromises(); let resolve!: (value: Awaited<ReturnType<typeof listReportingDataSources>>) => void;
    listMock.mockImplementationOnce(() => new Promise(done => { resolve = done; })); await wrapper.get('[data-testid="reporting-source-refresh"]').trigger('click'); const signal = listMock.mock.lastCall![1]!;
    vi.mocked(testReportingDataSource).mockRejectedValue(new Error('lost response')); await wrapper.get('[data-testid="reporting-data-source-test"]').trigger('click'); await flushPromises(); expect(signal.aborted).toBe(true);
    resolve({ items: [summary()], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(wrapper.find('[data-testid="reporting-source-unconfirmed"]').exists()).toBe(true);
  });
});
