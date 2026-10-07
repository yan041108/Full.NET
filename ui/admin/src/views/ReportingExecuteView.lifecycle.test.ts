import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import ReportingExecuteView from './ReportingExecuteView.vue';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { executeReportingDefinition } from '../api/reporting-executions';
import { createOutputSession, deferred, publishedReportDefinition, reportResult } from '../test/data-output-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
vi.mock('../api/reporting-executions', () => ({ executeReportingDefinition: vi.fn() }));
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([publishedReportDefinition]); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; });
const mountView = (permissions = ['reporting.definitions.read', 'reporting.executions.run']) => { const context = createOutputSession(permissions); wrapper = mount(ReportingExecuteView, { global: { plugins: [context.pinia] } }); return context.session; };
describe('报表执行授权与生命周期', () => {
  it('只有执行权限也能读取发布目录，并固定目录给出的版本号', async () => {
    vi.mocked(executeReportingDefinition).mockResolvedValue(reportResult);
    mountView(['reporting.executions.run']); await flushPromises();
    expect(listReportingPublishedDefinitions).toHaveBeenCalledTimes(1);
    await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises();
    expect(vi.mocked(executeReportingDefinition).mock.calls[0]?.[1].versionNumber).toBe(1);
  });
  it('查询互斥，失败后可重试且撤权立即清空已展示结果', async () => {
    const pending = deferred<typeof reportResult>(); vi.mocked(executeReportingDefinition).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(reportResult);
    const session = mountView(); await flushPromises(); const button = wrapper!.get('[data-testid="reporting-execute-run"]');
    await button.trigger('click'); await button.trigger('click'); expect(executeReportingDefinition).toHaveBeenCalledTimes(1);
    pending.reject(new Error('failed')); await flushPromises(); await button.trigger('click'); await flushPromises();
    expect(wrapper!.find('.result-card').exists()).toBe(true); session.currentUser!.permissions = ['reporting.definitions.read']; await flushPromises();
    expect(wrapper!.find('.result-card').exists()).toBe(false);
  });
  it('无执行权限不显示执行入口，无读取权限不拉目录', async () => { mountView([]); await flushPromises(); expect(wrapper!.find('[data-testid="reporting-execute-run"]').exists()).toBe(false); expect(listReportingPublishedDefinitions).not.toHaveBeenCalled(); });
  it('切租户取消查询并丢弃旧租户结果', async () => {
    const pending = deferred<typeof reportResult>(); vi.mocked(executeReportingDefinition).mockReturnValue(pending.promise);
    const session = mountView(); await flushPromises(); await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click');
    const signal = vi.mocked(executeReportingDefinition).mock.calls[0]?.[4]; session.currentUser!.tenantId = 'new-tenant'; await flushPromises();
    pending.resolve(reportResult); await flushPromises(); expect(signal?.aborted).toBe(true); expect(wrapper!.find('.result-card').exists()).toBe(false);
  });
  it('切换定义丢弃在途旧结果并清空已展示结果', async () => {
    const pending = deferred<typeof reportResult>(); vi.mocked(executeReportingDefinition).mockReturnValue(pending.promise);
    mountView(); await flushPromises(); await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click');
    const select = wrapper!.findComponent(ElSelect); select.vm.$emit('update:modelValue', 'new-definition'); select.vm.$emit('change');
    await flushPromises(); pending.resolve(reportResult); await flushPromises(); expect(wrapper!.find('.result-card').exists()).toBe(false);
  });
  it('离开页面取消查询，迟到失败不写回页面', async () => {
    const pending = deferred<typeof reportResult>(); vi.mocked(executeReportingDefinition).mockReturnValue(pending.promise);
    mountView(); await flushPromises(); await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click'); const signal = vi.mocked(executeReportingDefinition).mock.calls[0]?.[4];
    wrapper!.unmount(); wrapper = undefined; pending.reject(new Error('late')); await flushPromises(); expect(signal?.aborted).toBe(true);
  });
});
