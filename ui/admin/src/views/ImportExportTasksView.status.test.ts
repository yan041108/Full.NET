import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElPagination } from 'element-plus';
import View from './ImportExportTasksView.vue';
import * as api from '../api/import-export-tasks';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask as task } from '../test/task-lifecycle-fixtures';
vi.mock('../api/import-export-tasks', () => ({ listImportExportTasks: vi.fn(), getImportExportTask: vi.fn(), cancelImportExportTask: vi.fn(), executeImportExportTask: vi.fn(), resumeImportExportTask: vi.fn(), retryImportExportTask: vi.fn(), downloadImportExportTaskErrorReceipt: vi.fn(), listStaticImportSchemas: vi.fn(), createImportExportTask: vi.fn(), downloadStaticImportTemplate: vi.fn() }));
const otherId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
let wrapper: VueWrapper;
interface State { schemaDraft: string; statusDraft: string; appliedSchema?: string; appliedStatus?: string; page: number; items: typeof task[]; selectedTask?: typeof task; actionReceipt?: { outcome: string }; createOpen: boolean; createHandlers: { created: (value: typeof task) => void }; applyFilter(): Promise<void>; load(): Promise<boolean>; refreshResults(): void; openDetail(id: string): Promise<void>; requestCancellation(): void; confirmCancellation(): Promise<void>; runAction(kind: string): Promise<void> }
const state = () => wrapper.vm as unknown as State;
async function setup(permissions = ['import_export.import_tasks.read']) {
  const context = createOutputSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: {
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' },
    ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }, ImportTaskCreateDialog: true
  } } }); await flushPromises(); return context;
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listImportExportTasks).mockImplementation(async (query = {}) => ({ items: [{ ...task, schemaKey: query.schemaKey ?? task.schemaKey, statusKey: query.statusKey ?? task.statusKey }], page: query.page ?? 1, pageSize: query.pageSize ?? 20, total: 60 }));
  vi.mocked(api.getImportExportTask).mockResolvedValue({ ...task });
});
afterEach(() => wrapper?.unmount());
async function apply(status: string) { state().statusDraft = status; await state().applyFilter(); await flushPromises(); }

it('只读提交 Schema 与状态，分页/刷新沿用已提交条件而非草稿', async () => {
  await setup(); state().schemaDraft = ' retired.fixture '; await apply('execution_failed'); state().schemaDraft = 'other'; state().statusDraft = 'canceled';
  wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); state().refreshResults(); await flushPromises();
  expect(api.listImportExportTasks).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, schemaKey: 'retired.fixture', statusKey: 'execution_failed' }, expect.any(AbortSignal));
  state().schemaDraft = ''; await apply('all'); expect(state().appliedSchema).toBeUndefined(); expect(state().appliedStatus).toBeUndefined(); expect(state().page).toBe(1); expect(api.listStaticImportSchemas).not.toHaveBeenCalled();
});
it.each(['status', 'schema'])('非法 %s 不请求，刷新/分页不回退全部', async kind => {
  await setup(); if (kind === 'schema') state().schemaDraft = 'x'.repeat(129); await apply(kind === 'status' ? 'unknown' : 'execution_failed');
  state().refreshResults(); wrapper.getComponent(ElPagination).vm.$emit('change', 2, 20); await flushPromises(); expect(api.listImportExportTasks).toHaveBeenCalledTimes(1); expect(state().items).toEqual([]);
});
it('错配状态拒绝整页，不本地筛选伪造总数', async () => {
  await setup(); vi.mocked(api.listImportExportTasks).mockResolvedValueOnce({ items: [task], page: 1, pageSize: 20, total: 60 }); await apply('execution_failed'); expect(state().items).toEqual([]); expect(wrapper.find('.el-alert--error').exists()).toBe(true);
});
it('新查询取消旧页，迟到源不能污染新状态', async () => {
  await setup(); const pending = deferred<Awaited<ReturnType<typeof api.listImportExportTasks>>>(); vi.mocked(api.listImportExportTasks).mockReturnValueOnce(pending.promise);
  state().page = 2; const old = state().load(); const signal = vi.mocked(api.listImportExportTasks).mock.calls.at(-1)![1]!;
  await apply('execution_failed'); pending.resolve({ items: [{ ...task, schemaDisplayName: '旧页' }], page: 2, pageSize: 20, total: 60 }); await old; expect(signal.aborted).toBe(true); expect(state().items[0]?.statusKey).toBe('execution_failed'); expect(wrapper.text()).not.toContain('旧页');
});
it('会话变化清空已提交状态与 Schema，恢复为全部', async () => {
  const { session } = await setup(); state().schemaDraft = 'retired.fixture'; await apply('execution_failed'); session.currentUser!.sessionId = otherId; await flushPromises();
  expect(state().schemaDraft).toBe(''); expect(state().statusDraft).toBe('all'); expect(api.listImportExportTasks).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, schemaKey: undefined, statusKey: undefined }, expect.any(AbortSignal));
});
it('独立精确详情可查看其他状态，筛选页保持原状态', async () => {
  await setup(); await apply('execution_failed'); await state().openDetail(task.id); expect(state().selectedTask?.statusKey).toBe('preview_succeeded'); expect(state().items[0]?.statusKey).toBe('execution_failed');
});
it.each(['cancel', 'execute'])('确认 %s 后移出旧状态页，陈旧源不能撤销成功回执', async kind => {
  await setup(['import_export.import_tasks.read', 'import_export.import_tasks.execute', 'import_export.import_tasks.cancel']); await apply('preview_succeeded'); await state().openDetail(task.id);
  const accepted = { ...task, statusKey: kind === 'cancel' ? 'canceled' : 'queued', version: 2 };
  vi.mocked(api.cancelImportExportTask).mockResolvedValue(accepted); vi.mocked(api.executeImportExportTask).mockResolvedValue(accepted);
  await state().runAction(kind); expect(state().actionReceipt?.outcome).toBe('succeeded'); expect(state().selectedTask?.statusKey).toBe(accepted.statusKey); expect(state().items).toEqual([]); expect(wrapper.find('.el-alert--error').exists()).toBe(true);
});
it('新建回执保留独立详情，不改变失败筛选或注入不同状态', async () => {
  await setup(['import_export.import_tasks.read', 'import_export.import_tasks.create', 'import_export.static_schemas.read']); await apply('execution_failed'); state().createOpen = true; await flushPromises(); state().createHandlers.created({ ...task, id: otherId }); await flushPromises();
  expect(state().selectedTask?.id).toBe(otherId); expect(state().items.every(row => row.statusKey === 'execution_failed')).toBe(true); expect(api.listImportExportTasks).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, schemaKey: undefined, statusKey: 'execution_failed' }, expect.any(AbortSignal));
});
it('确认回执不覆盖同修订源页中新鲜的来源保留状态', async () => {
  await setup(['import_export.import_tasks.read', 'import_export.import_tasks.cancel']); await state().openDetail(task.id);
  const accepted = { ...task, statusKey: 'canceled', version: 2 };
  vi.mocked(api.cancelImportExportTask).mockResolvedValue(accepted);
  vi.mocked(api.listImportExportTasks).mockResolvedValueOnce({ items: [{ ...accepted, sourceRetention: { expiresAtUtc: '2026-01-01T00:00:00Z', stateKey: 'released', releasedAtUtc: '2026-01-02T00:00:00Z' } }], page: 1, pageSize: 20, total: 1 });
  await state().runAction('cancel'); expect(state().actionReceipt?.outcome).toBe('succeeded'); expect(state().items[0]?.sourceRetention.stateKey).toBe('released');
});
