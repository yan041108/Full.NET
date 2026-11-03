import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReportingExecuteView from './ReportingExecuteView.vue';
import { listReportingDefinitions, getReportingDefinition } from '../api/reporting-definitions';
import { executeReportingDefinition, getReportingExecutionVersion } from '../api/reporting-executions';
import { useSessionStore } from '../auth/session';
import { ElPagination, ElSelect } from 'element-plus';

vi.mock('../api/reporting-definitions', () => ({
  listReportingDefinitions: vi.fn(), getReportingDefinition: vi.fn()
}));

vi.mock('../api/reporting-executions', () => ({
  executeReportingDefinition: vi.fn(), getReportingExecutionVersion: vi.fn()
}));

const definitionsMock = vi.mocked(listReportingDefinitions);
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', other = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const summary = () => ({ id, groupId: other, dataSourceId: other, definitionKey: 'published-query', name: '版本查询', description: null, queryPortKey: 'reporting.schema_inventory',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 7 });
const published = (definitionId = id, versionNumber = 1) => ({ id: other, definitionId, versionNumber, dataSourceId: other, queryPortKey: 'reporting.schema_inventory', layoutConfigJson: '{}', changeNote: null,
  parameterSchema: [{ parameterKey: 'topN', displayName: '已发布条数', dataTypeKey: 'integer', isRequired: true, defaultValue: '20' }], publishedByUserId: id, publishedAtUtc: '2026-11-03T00:00:00Z' });
const result = (page = 1) => ({ definitionId: id, definitionKey: 'published-query', definitionName: '版本查询', versionNumber: 1, queryPortKey: 'reporting.schema_inventory',
  columns: [{ columnKey: 'SchemaName', displayName: 'Schema' }], rows: [{ values: { SchemaName: 'controlled-proof' } }], page, pageSize: 50, hasMore: true, totalRows: null, commandTimeoutSeconds: 30, executedAtUtc: '2026-11-03T00:00:00Z' });
const permissions = ['reporting.executions.run', 'reporting.definitions.read'];
const wrappers: VueWrapper[] = [];
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });

function mountView(grants = permissions) {
  const pinia = createPinia();
  setActivePinia(pinia);
  useSessionStore().currentUser = { id, username: 'reviewer', displayName: '验收', tenantId: null, actorScope: 'host', scope: 'host', permissions: grants,
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: other, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(ReportingExecuteView, { global: { plugins: [pinia], directives: { loading: {} } } }); wrappers.push(wrapper); return wrapper;
}

describe('ReportingExecuteView', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    definitionsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
    vi.mocked(getReportingExecutionVersion).mockResolvedValue(published()); vi.mocked(executeReportingDefinition).mockResolvedValue(result());
  });

  it('disables run action without definitions', async () => {
    const wrapper = mountView();
    await wrapper.vm.$nextTick();
    const button = wrapper.find('[data-testid="reporting-execute-run"]');
    expect(button.attributes('disabled')).toBeDefined();
  });
  function existing() { definitionsMock.mockResolvedValue({ items: [summary()], page: 1, pageSize: 20, total: 1 }); }
  it.each([['reporting.executions.run'], ['reporting.definitions.read']])('缺少独立权限时不读取或执行：%s', async permission => {
    const wrapper = mountView([permission]); await flushPromises(); expect(definitionsMock).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="reporting-execute-run"]').exists()).toBe(false);
  });
  it('按目录明确发布号加载已发布参数，不读取草稿，目录每页二十条', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); expect(getReportingExecutionVersion).toHaveBeenCalledWith(id, 1, expect.any(AbortSignal)); expect(getReportingDefinition).not.toHaveBeenCalled();
    expect(definitionsMock).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }), expect.any(AbortSignal)); expect(wrapper.text()).toContain('已发布条数');
    expect((wrapper.get('[data-parameter-key="topN"]').element as HTMLInputElement).value).toBe('20');
    await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises();
    expect(executeReportingDefinition).toHaveBeenCalledWith(id, { versionNumber: 1, parameters: [{ parameterKey: 'topN', value: '20' }] }, 1, 50, expect.any(AbortSignal));
  });
  it('修改输入后翻页沿用首次执行快照，重新执行才采用新输入并回第一页', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-parameter-key="topN"]').setValue('30'); expect(wrapper.find('[data-testid="reporting-execute-input-changed"]').exists()).toBe(true);
    vi.mocked(executeReportingDefinition).mockResolvedValue(result(2)); wrapper.findComponent(ElPagination).vm.$emit('current-change', 2); await flushPromises();
    expect(vi.mocked(executeReportingDefinition).mock.lastCall).toEqual([id, { versionNumber: 1, parameters: [{ parameterKey: 'topN', value: '20' }] }, 2, 50, expect.any(AbortSignal)]);
    vi.mocked(executeReportingDefinition).mockResolvedValue(result()); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises();
    expect(vi.mocked(executeReportingDefinition).mock.lastCall?.[1].parameters[0]?.value).toBe('30'); expect(vi.mocked(executeReportingDefinition).mock.lastCall?.[2]).toBe(1);
    expect(wrapper.find('[data-testid="reporting-execute-input-changed"]').exists()).toBe(false);
  });
  it('目录出现新发布版本不会改变已载入输入，显式重载后才采用新版', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); definitionsMock.mockResolvedValue({ items: [{ ...summary(), latestPublishedVersionNumber: 2 }], page: 1, pageSize: 20, total: 1 });
    await wrapper.findComponent(ElSelect).props('remoteMethod')!('版本'); await flushPromises(); expect(getReportingExecutionVersion).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(vi.mocked(executeReportingDefinition).mock.lastCall?.[1].versionNumber).toBe(1);
    vi.mocked(getReportingExecutionVersion).mockResolvedValue(published(id, 2)); await wrapper.get('[data-testid="reporting-execute-reload"]').trigger('click'); await flushPromises();
    expect(getReportingExecutionVersion).toHaveBeenLastCalledWith(id, 2, expect.any(AbortSignal)); expect(executeReportingDefinition).toHaveBeenCalledTimes(1); expect(wrapper.get('[data-testid="reporting-execute-version"]').text()).toContain('2');
  });
  it('切定义取消旧发布版本，迟到参数不污染新的选择', async () => {
    definitionsMock.mockResolvedValue({ items: [summary(), { ...summary(), id: other, latestPublishedVersionNumber: 2 }], page: 1, pageSize: 20, total: 2 });
    let resolve!: (value: ReturnType<typeof published>) => void; vi.mocked(getReportingExecutionVersion).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(); await flushPromises(); const signal = vi.mocked(getReportingExecutionVersion).mock.calls[0]![2]!;
    vi.mocked(getReportingExecutionVersion).mockResolvedValue({ ...published(other, 2), parameterSchema: [] }); wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', other); await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(published()); await flushPromises(); expect(wrapper.find('[data-parameter-key="topN"]').exists()).toBe(false); expect(wrapper.get('[data-testid="reporting-execute-version"]').text()).toContain('2');
  });
  it('执行中切租户取消请求并清空结果，迟到成功不得显示旧数据', async () => {
    existing(); let resolve!: (value: ReturnType<typeof result>) => void; vi.mocked(executeReportingDefinition).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mountView(); await flushPromises(); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); const signal = vi.mocked(executeReportingDefinition).mock.calls[0]![4]!;
    useSessionStore().currentUser = { ...useSessionStore().currentUser!, actorScope: 'tenant', scope: 'tenant', tenantId: other }; await flushPromises(); expect(signal.aborted).toBe(true);
    resolve(result()); await flushPromises(); expect(wrapper.text()).not.toContain('controlled-proof'); expect(wrapper.find('[data-testid="reporting-execute-run"]').exists()).toBe(false);
  });
  it('发布版本读取失败不退回草稿，也不允许执行', async () => {
    existing(); vi.mocked(getReportingExecutionVersion).mockRejectedValueOnce(new Error('offline')); const wrapper = mountView(); await flushPromises();
    expect(getReportingDefinition).not.toHaveBeenCalled(); expect(wrapper.get('[data-testid="reporting-execute-run"]').attributes('disabled')).toBeDefined(); expect(executeReportingDefinition).not.toHaveBeenCalled();
  });
  it('必填参数为空不执行，补齐后只发起一次查询', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); await wrapper.get('[data-parameter-key="topN"]').setValue(''); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(executeReportingDefinition).not.toHaveBeenCalled();
    await wrapper.get('[data-parameter-key="topN"]').setValue('10'); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(executeReportingDefinition).toHaveBeenCalledTimes(1);
  });
  it('执行失败清空旧结果，不自动重放，用户可手动再次执行', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('controlled-proof');
    vi.mocked(executeReportingDefinition).mockRejectedValueOnce(new Error('offline')); await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(wrapper.text()).not.toContain('controlled-proof'); expect(executeReportingDefinition).toHaveBeenCalledTimes(2);
    await wrapper.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises(); expect(executeReportingDefinition).toHaveBeenCalledTimes(3);
  });
  it('远程检索失败保留已载入版本，撤权则取消读取并清空', async () => {
    existing(); const wrapper = mountView(); await flushPromises(); definitionsMock.mockRejectedValueOnce(new Error('offline')); await wrapper.findComponent(ElSelect).props('remoteMethod')!('目录'); await flushPromises();
    expect(wrapper.get('[data-testid="reporting-execute-version"]').text()).toContain('1');
    useSessionStore().currentUser!.permissions = ['reporting.definitions.read']; await flushPromises(); expect(wrapper.find('[data-testid="reporting-execute-version"]').exists()).toBe(false);
  });
});
