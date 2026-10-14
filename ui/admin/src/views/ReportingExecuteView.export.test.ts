import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElInput, ElSelect } from 'element-plus';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import ReportingExecuteView from './ReportingExecuteView.vue';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import { executeReportingDefinition } from '../api/reporting-executions';
import { createReportingExportTask } from '../api/reporting-export-tasks';
import { createOutputSession, deferred, exportTask, publishedReportDefinition, reportResult } from '../test/data-output-fixtures';
vi.mock('../api/reporting-definitions', () => ({ listReportingPublishedDefinitions: vi.fn() }));
vi.mock('../api/reporting-executions', () => ({ executeReportingDefinition: vi.fn() }));
vi.mock('../api/reporting-export-tasks', () => ({ createReportingExportTask: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn() }));
const permissions = ['reporting.executions.run', 'reporting.export_tasks.create', 'reporting.export_tasks.read'];
const definition = { ...publishedReportDefinition, parameterSchema: [{ parameterKey: 'filter', displayName: '筛选', dataTypeKey: 'string', isRequired: false, defaultValue: null }] };
let wrapper: VueWrapper | undefined; let router: Router;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([definition]);
  vi.mocked(executeReportingDefinition).mockResolvedValue(reportResult); vi.mocked(createReportingExportTask).mockResolvedValue(exportTask);
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(granted = permissions) {
  const context = createOutputSession(granted); router = createRouter({ history: createMemoryHistory(), routes: [
    { name: 'reporting-execute', path: '/execute', component: { render: () => null } },
    { name: 'reporting-export-tasks', path: '/exports', component: { render: () => null } }
  ] }); await router.push({ name: 'reporting-execute' });
  wrapper = mount(ReportingExecuteView, { global: { plugins: [context.pinia, router], directives: { loading: {} } } });
  await flushPromises(); return context.session;
}
describe('查询页按当前版本和参数创建导出', () => {
  it.each(permissions)('缺少 %s 不显示导出入口', async missing => {
    await setup(permissions.filter(value => value !== missing)); expect(wrapper!.find('[data-testid="reporting-execute-export"]').exists()).toBe(false);
  });
  it('与查询使用同样的规范参数，创建成功定位任务但不下载', async () => {
    await setup(); await wrapper!.getComponent(ElInput).setValue('  current filter  ');
    await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click'); await flushPromises();
    const parameters = [{ parameterKey: 'filter', value: 'current filter' }];
    expect(executeReportingDefinition).toHaveBeenCalledWith(definition.definitionId, { versionNumber: 1, parameters }, 1, 50, expect.any(AbortSignal));
    await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click'); await flushPromises();
    expect(createReportingExportTask).toHaveBeenCalledExactlyOnceWith({ definitionId: definition.definitionId, versionNumber: 1, formatKey: 'excel', parameters }, expect.any(AbortSignal));
    expect(router.currentRoute.value.name).toBe('reporting-export-tasks'); expect(router.currentRoute.value.query.taskId).toBe(exportTask.id);
  });
  it('使用选择的精确版本，不回退首个版本', async () => {
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue([definition, { ...definition, versionNumber: 2 }]);
    vi.mocked(createReportingExportTask).mockResolvedValue({ ...exportTask, versionNumber: 2 }); await setup();
    const select = wrapper!.getComponent(ElSelect); select.vm.$emit('update:modelValue', definition.definitionId + ':2'); select.vm.$emit('change'); await flushPromises();
    await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click'); await flushPromises();
    expect(vi.mocked(createReportingExportTask).mock.calls[0]![0].versionNumber).toBe(2);
  });
  it.each(['definition', 'version', 'format'])('创建返回%s错配时不导航', async mismatch => {
    await setup(); vi.mocked(createReportingExportTask).mockResolvedValue({ ...exportTask,
      definitionId: mismatch === 'definition' ? '01980000-0000-7000-8000-000000000003' : exportTask.definitionId,
      versionNumber: mismatch === 'version' ? 9 : 1, formatKey: mismatch === 'format' ? 'other' : 'excel' });
    await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click'); await flushPromises();
    expect(router.currentRoute.value.name).toBe('reporting-execute');
  });
  it('创建等待中互斥查询和重复导出，撤权取消并拒绝迟到导航', async () => {
    const pending = deferred<typeof exportTask>(); vi.mocked(createReportingExportTask).mockReturnValueOnce(pending.promise);
    const session = await setup(); await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click');
    await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click'); await wrapper!.get('[data-testid="reporting-execute-run"]').trigger('click');
    expect(createReportingExportTask).toHaveBeenCalledOnce(); expect(executeReportingDefinition).not.toHaveBeenCalled();
    const signal = vi.mocked(createReportingExportTask).mock.calls[0]![1]!;
    session.currentUser!.permissions = ['reporting.executions.run']; await flushPromises(); pending.resolve(exportTask); await flushPromises();
    expect(signal.aborted).toBe(true); expect(router.currentRoute.value.name).toBe('reporting-execute');
  });
  it('更换定义取消旧导出，迟到完成不能接管新选择', async () => {
    const pending = deferred<typeof exportTask>(); vi.mocked(createReportingExportTask).mockReturnValueOnce(pending.promise); await setup();
    await wrapper!.get('[data-testid="reporting-execute-export"]').trigger('click');
    const select = wrapper!.getComponent(ElSelect); select.vm.$emit('update:modelValue', 'unknown'); select.vm.$emit('change'); await flushPromises();
    pending.resolve(exportTask); await flushPromises(); expect(router.currentRoute.value.name).toBe('reporting-execute');
    expect(vi.mocked(createReportingExportTask).mock.calls[0]![1]!.aborted).toBe(true);
  });
});
