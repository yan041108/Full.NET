import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElMessage } from 'element-plus';
import type { ReportingExportTaskDetail } from '@fullnet/client-contracts';
import View from './ReportingExportTasksView.vue';
import * as api from '../api/reporting-export-tasks';
import { listReportingPublishedDefinitions } from '../api/reporting-publications';
import { createOutputSession, deferred, exportTask, publishedExportDefinition } from '../test/data-output-fixtures';
vi.mock('../api/reporting-export-tasks', () => ({ getReportingExportTask: vi.fn(), listReportingExportTasks: vi.fn(), createReportingExportTask: vi.fn(), cancelReportingExportTask: vi.fn(), downloadReportingExportTask: vi.fn() }));
vi.mock('../api/reporting-publications', () => ({ listReportingPublishedDefinitions: vi.fn() }));
const read = 'reporting.export_tasks.read', create = 'reporting.export_tasks.create', catalog = 'reporting.published_definitions.read', download = 'reporting.export_tasks.download';
const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const original: ReportingExportTaskDetail = { ...exportTask, versionNumber: 3, parameters: [{ parameterKey: 'year', value: '2026' }, { parameterKey: 'department', value: null }, { parameterKey: 'empty', value: '' }, { parameterKey: 'note', value: '  原筛选\nA&B  ' }] };
let wrapper: VueWrapper;
interface State {
  selectedTask?: ReportingExportTaskDetail; detailVisible: boolean; createDialogVisible: boolean; regeneration?: { taskId: string; versionNumber: number; parameters: unknown[] };
  createBlocked: boolean; receipt?: { outcome: string; taskId?: string }; detailProblem?: { code: string };
  openDetail(id: string): Promise<void>; downloadTask(task: ReportingExportTaskDetail): Promise<void>; prepareRegeneration(task: ReportingExportTaskDetail): Promise<void>;
  submitCreate(): Promise<void>; openCreateDialog(): Promise<void>; selectedDefinitionVersionKey: string; parameterValues: Map<string, string>;
}
const state = () => wrapper.vm as unknown as State;
beforeEach(() => { vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listReportingExportTasks).mockResolvedValue({ items: [original], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.getReportingExportTask).mockResolvedValue({ ...original, parameters: original.parameters.map(value => ({ ...value })) });
  vi.mocked(api.downloadReportingExportTask).mockRejectedValue({ type: 'about:blank', status: 404, code: 'files.file.not_found', title: 'missing' });
  vi.mocked(api.createReportingExportTask).mockImplementation(async body => ({ ...original, id: nextId, definitionId: body.definitionId, versionNumber: body.versionNumber!, parameters: body.parameters,
    statusKey: 'queued', outputFileId: null, outputFileName: null, completedAtUtc: null, version: 1 }));
  vi.mocked(listReportingPublishedDefinitions).mockResolvedValue({ items: [{ ...publishedExportDefinition, versionNumber: 9 }], page: 1, pageSize: 20, total: 1 });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(permissions = [read, create, catalog, download]) {
  const context = createOutputSession(permissions); const route = reactive({ name: 'reporting-export-tasks', query: {} });
  wrapper = mount(View, { global: { plugins: [context.pinia], provide: { [routeLocationKey as symbol]: route }, directives: { loading: () => {} }, stubs: {
    ElDialog: { name: 'ElDialog', props: ['modelValue'], emits: ['update:modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); await state().openDetail(original.id); await state().downloadTask(original); await flushPromises(); return { ...context, route };
}
describe('报表原结果独立重新生成', () => {
  it('原文件明确缺失后先重读再冻结版本与参数，确认前没有 POST 或最新目录读取', async () => {
    await setup(); await state().prepareRegeneration(original); await flushPromises();
    expect(api.getReportingExportTask).toHaveBeenCalledTimes(2); expect(api.createReportingExportTask).not.toHaveBeenCalled(); expect(listReportingPublishedDefinitions).not.toHaveBeenCalled();
    expect(state().regeneration).toMatchObject({ taskId: original.id, versionNumber: 3, parameters: original.parameters });
    expect(wrapper.find('[data-testid="reporting-export-definition"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="reporting-export-regeneration-snapshot"]').text()).toContain(original.id);
    expect(wrapper.get('[data-testid="reporting-export-regeneration-snapshot"]').text()).toContain('未提供（null）');
    expect(wrapper.get('[data-testid="reporting-export-regeneration-snapshot"]').text()).toContain('空字符串');
    await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledExactlyOnceWith({ definitionId: original.definitionId, versionNumber: 3, formatKey: 'excel', parameters: original.parameters }, expect.any(AbortSignal));
    expect(state().selectedTask?.id).toBe(nextId); expect(state().receipt?.outcome).toBe('succeeded'); expect(original.statusKey).toBe('succeeded');
  });
  it.each([403, 500])('文件读取 %s 不是明确缺失，不提供重新生成', async status => {
    vi.mocked(api.downloadReportingExportTask).mockRejectedValue({ type: 'about:blank', code: 'test.failed', status, title: 'failed' });
    await setup(); await state().prepareRegeneration(original); expect(state().regeneration).toBeUndefined(); expect(api.getReportingExportTask).toHaveBeenCalledTimes(1); expect(api.createReportingExportTask).not.toHaveBeenCalled();
  });
  it.each([create, catalog, download])('缺少 %s 不创建新任务', async permission => {
    await setup([read, create, catalog, download].filter(value => value !== permission)); await state().prepareRegeneration(original);
    expect(state().regeneration).toBeUndefined(); expect(api.createReportingExportTask).not.toHaveBeenCalled();
  });
  it.each(['queued', 'processing'])('原状态 %s 不能另建原参数任务', async statusKey => {
    await setup(); await state().prepareRegeneration({ ...original, statusKey }); expect(state().regeneration).toBeUndefined();
  });
  it.each(['failed', 'canceled'])('本人 %s 原任务可在当前授权下另建，不要求下载或取消权限', async statusKey => {
    const source = { ...original, statusKey, outputFileId: null, outputFileName: null, rowCount: 0 };
    vi.mocked(api.getReportingExportTask).mockResolvedValue(source); await setup([read, create, catalog]); await state().prepareRegeneration(source);
    expect(state().regeneration).toMatchObject({ taskId: source.id, versionNumber: 3 }); expect(api.createReportingExportTask).not.toHaveBeenCalled();
    await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledTimes(1); expect(state().selectedTask?.id).toBe(nextId); expect(source.statusKey).toBe(statusKey);
  });
  it.each(['definition', 'version', 'parameters', 'file', 'owner', 'state'])('重读原来源 %s 变化拒绝继续', async field => {
    await setup(); const changed = { ...original, parameters: original.parameters.map(value => ({ ...value })) };
    if (field === 'definition') changed.definitionId = nextId; else if (field === 'version') changed.versionNumber = 9;
    else if (field === 'parameters') changed.parameters[0]!.value = '2027'; else if (field === 'file') changed.outputFileId = nextId; else if (field === 'state') changed.statusKey = 'processing'; else changed.requestedByUserId = nextId;
    vi.mocked(api.getReportingExportTask).mockResolvedValue(changed); await state().prepareRegeneration(original);
    expect(state().regeneration).toBeUndefined(); expect(state().detailProblem?.code).toBe('client.original_export_changed'); expect(api.createReportingExportTask).not.toHaveBeenCalled();
  });
  it('重读期间会话切换取消，迟到结果不打开确认', async () => {
    const { session } = await setup(); const pending = deferred<ReportingExportTaskDetail>(); vi.mocked(api.getReportingExportTask).mockReturnValue(pending.promise);
    const reading = state().prepareRegeneration(original); const signal = vi.mocked(api.getReportingExportTask).mock.lastCall![1]!;
    session.currentUser!.sessionId = nextId; await flushPromises(); expect(signal.aborted).toBe(true); pending.resolve(original); await reading;
    expect(state().regeneration).toBeUndefined(); expect(state().createDialogVisible).toBe(false);
  });
  it('新任务回执不能复用原任务 ID', async () => {
    await setup(); await state().prepareRegeneration(original); vi.mocked(api.createReportingExportTask).mockResolvedValue(original); await state().submitCreate();
    expect(state().receipt?.outcome).toBe('unconfirmed'); expect(state().createBlocked).toBe(true);
  });
  it('错误参数回执保留未知门禁，不能再次自动创建', async () => {
    await setup(); await state().prepareRegeneration(original); vi.mocked(api.createReportingExportTask).mockResolvedValue({ ...original, id: nextId, parameters: [] });
    await state().submitCreate(); await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledTimes(1); expect(state().createBlocked).toBe(true);
  });
  it('创建未知门禁与普通创建共用规范参数身份，换参数顺序不能绕过', async () => {
    await setup(); await state().prepareRegeneration(original); vi.mocked(api.createReportingExportTask).mockRejectedValue(new Error('lost')); await state().submitCreate();
    state().createDialogVisible = false; await flushPromises();
    vi.mocked(listReportingPublishedDefinitions).mockResolvedValue({ items: [{ ...publishedExportDefinition, versionNumber: 3, parameterSchema: [
      { parameterKey: 'department', displayName: '部门', dataTypeKey: 'string', isRequired: false, defaultValue: null },
      { parameterKey: 'empty', displayName: '空文本', dataTypeKey: 'string', isRequired: false, defaultValue: '' },
      { parameterKey: 'note', displayName: '备注', dataTypeKey: 'string', isRequired: false, defaultValue: '  原筛选\nA&B  ' },
      { parameterKey: 'year', displayName: '年度', dataTypeKey: 'string', isRequired: false, defaultValue: '2026' }
    ] }], page: 1, pageSize: 20, total: 1 });
    await state().openCreateDialog(); state().parameterValues.delete('department'); await flushPromises(); expect(state().createBlocked).toBe(true);
    await state().submitCreate(); expect(api.createReportingExportTask).toHaveBeenCalledTimes(1);
  });
});
