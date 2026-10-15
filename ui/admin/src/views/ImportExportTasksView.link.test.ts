import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDrawer } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router, type LocationQueryRaw } from 'vue-router';
import ImportExportTasksView from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';

vi.mock('../api/import-export-tasks', () => ({ listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), cancelImportExportTask: vi.fn(),
  executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn(),
  listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
let wrapper: VueWrapper | undefined; let router: Router; let visible = ref(true);
let activeSession: ReturnType<typeof createOutputSession>['session'] | undefined;
const otherId = '01980000-0000-7000-8000-000000000003';
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listImportExportTasks).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.getImportExportTask).mockImplementation(async id => ({ ...importTask, id, tenantId: activeSession?.currentUser?.tenantId ?? importTask.tenantId, schemaDisplayName: id === otherId ? '新任务' : '链接任务' }));
});
afterEach(() => { wrapper?.unmount(); activeSession = undefined; vi.restoreAllMocks(); });
async function setup(query: LocationQueryRaw = { taskId: outputId }, permissions = ['import_export.import_tasks.read'], keepAlive = false) {
  const context = createOutputSession(permissions); activeSession = context.session; visible = ref(true);
  router = createRouter({ history: createMemoryHistory(), routes: [
    { name: 'import-export-tasks', path: '/tasks', component: { render: () => null } },
    { name: 'elsewhere', path: '/elsewhere', component: { render: () => null } }
  ] }); await router.push({ name: 'import-export-tasks', query });
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(ImportExportTasksView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : ImportExportTasksView, { global: { plugins: [context.pinia, router], directives: { loading: {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return context.session;
}
describe('导入任务详情链接', () => {
  it('直接读取不在列表页的任务，入口不执行任务或预取模板', async () => {
    await setup(); expect(api.getImportExportTask).toHaveBeenCalledExactlyOnceWith(outputId, expect.any(AbortSignal));
    expect(wrapper!.text()).toContain('链接任务'); expect(api.executeImportExportTask).not.toHaveBeenCalled(); expect(api.listStaticImportSchemas).not.toHaveBeenCalled();
  });
  it.each(['bad', ['01980000-0000-7000-8000-000000000003'], '00000000-0000-0000-0000-000000000000'])('非法任务参数 %j 不读取', async taskId => {
    await setup({ taskId }); expect(api.getImportExportTask).not.toHaveBeenCalled();
  });
  it('无读取权限不打开详情', async () => { await setup(undefined, []); expect(api.getImportExportTask).not.toHaveBeenCalled(); });
  it('切换链接取消旧详情并拒绝迟到结果', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.getImportExportTask).mockReturnValueOnce(pending.promise);
    await setup(); const signal = vi.mocked(api.getImportExportTask).mock.calls[0]![1]!;
    await router.replace({ query: { taskId: otherId } }); await flushPromises();
    pending.resolve({ ...importTask, schemaDisplayName: '旧敏感任务' }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).toContain('新任务'); expect(wrapper!.text()).not.toContain('旧敏感任务');
  });
  it('关闭详情不因刷新或无关参数变化重开', async () => {
    await setup(); wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await flushPromises();
    await router.replace({ query: { taskId: outputId, page: '2' } }); await flushPromises();
    expect(api.getImportExportTask).toHaveBeenCalledOnce(); expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
  });
  it('租户变化不重放旧链接，显式新链接才能打开', async () => {
    const session = await setup(); session.currentUser = { ...session.currentUser!, tenantId: otherId, scope: `tenant:${otherId.replaceAll('-', '')}` };
    await flushPromises(); expect(api.getImportExportTask).toHaveBeenCalledOnce();
    expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(wrapper!.text()).toContain('新任务');
  });
  it('离开路由取消在途详情，不再交付旧任务', async () => {
    const pending = deferred<typeof importTask>(); vi.mocked(api.getImportExportTask).mockReturnValueOnce(pending.promise);
    await setup(); const signal = vi.mocked(api.getImportExportTask).mock.calls[0]![1]!;
    await router.push({ name: 'elsewhere' }); await flushPromises(); pending.resolve(importTask); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
  });
  it('KeepAlive 恢复不重放旧任务，新链接恢复定位', async () => {
    await setup(undefined, undefined, true); visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(api.getImportExportTask).toHaveBeenCalledOnce(); expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(wrapper!.text()).toContain('新任务');
  });
  it('停用期间显式新链接在恢复后读取一次', async () => {
    await setup(undefined, undefined, true); visible.value = false; await flushPromises();
    await router.replace({ query: { taskId: otherId } }); await flushPromises(); expect(api.getImportExportTask).toHaveBeenCalledOnce();
    visible.value = true; await flushPromises(); expect(api.getImportExportTask).toHaveBeenCalledTimes(2);
    expect(wrapper!.text()).toContain('新任务');
  });
});
