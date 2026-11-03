import { mount, flushPromises } from '@vue/test-utils';
import { defineComponent, reactive, ref } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WorkflowOperations from './WorkflowOperations.vue';
const api = vi.hoisted(() => ({ steps: vi.fn(), backlog: vi.fn(), push: vi.fn() }));
const id = '0198f955-899d-7000-8000-000000000001';
const rights = ['workflow.operations.read', 'workflow.instances.read', 'workflow.recovery_tasks.read'];
const session = reactive({ switching: false, currentUser: { id, sessionId: id, scope: 'host', tenantId: null as string | null, profileVersion: 1, permissions: [...rights] }, can(code: string) { return this.currentUser.permissions.includes(code); } });
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('../i18n/adminI18n', () => ({ useAdminI18n: () => ({ t: (key: string) => key, locale: ref('zh-CN') }) }));
vi.mock('../api/workflow-observability', () => ({ listWorkflowOperationsSteps: api.steps, listWorkflowOperationsBacklog: api.backlog }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: api.push }) }));
const steps = () => ({ observedAtUtc: '2026-10-29T00:00:00Z', total: 21, page: 1, pageSize: 20, items: [{ id, instanceId: id, nodeKey: 'approve', nodeTypeKey: 'human.approval', definitionKey: 'expense', instanceStatusKey: 'active', statusKey: 'active', dwellSeconds: 600, dueAtUtc: '2026-10-28T23:00:00Z', pausedAtUtc: null, slaStatusKey: 'waiting', isOverdue: true }] });
const wrappers: ReturnType<typeof mount>[] = []; function create() { const wrapper = mount(WorkflowOperations); wrappers.push(wrapper); return wrapper; }
beforeEach(() => { vi.resetAllMocks(); session.currentUser.tenantId = null; session.currentUser.permissions = [...rights]; session.switching = false; api.steps.mockResolvedValue(steps()); api.backlog.mockResolvedValue({ observedAtUtc: '2026-10-29T00:00:00Z', total: 0, page: 1, pageSize: 20, items: [] }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('运营读取与恢复导航', () => {
  it('没有运营权限不创建入口，也不自动读取', () => { session.currentUser.permissions = ['workflow.instances.read']; const wrapper = create(); expect(wrapper.find('button').exists()).toBe(false); expect(api.steps).not.toHaveBeenCalled(); });
  it('KeepAlive切离页面后销毁运营投影，回来必须重新读取', async () => {
    const harness = defineComponent({ components: { WorkflowOperations }, setup: () => ({ active: ref(true) }),
      template: '<button data-testid="toggle" @click="active = !active">toggle</button><KeepAlive><WorkflowOperations v-if="active"/><div v-else>other</div></KeepAlive>' });
    const wrapper = mount(harness); wrappers.push(wrapper); await wrapper.get('[data-testid="workflow-operations-open"]').trigger('click'); await flushPromises();
    expect(wrapper.find('table').exists()).toBe(true); await wrapper.get('[data-testid="toggle"]').trigger('click');
    await wrapper.get('[data-testid="toggle"]').trigger('click'); expect(wrapper.find('[data-testid="workflow-operations-panel"]').exists()).toBe(false);
    expect(api.steps).toHaveBeenCalledTimes(1);
  });
  it('读取有界页并导航原实例/恢复页，不能直接发恢复写入', async () => {
    const wrapper = create(); expect(api.steps).not.toHaveBeenCalled(); await wrapper.get('[data-testid="workflow-operations-open"]').trigger('click'); await flushPromises();
    expect(api.steps).toHaveBeenCalledWith({ page: 1, pageSize: 20, minimumDwellSeconds: 0, onlyOverdue: false }, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('600s'); expect(wrapper.text()).toContain('workflowR5.overdue');
    await wrapper.get('[data-testid="workflow-operations-inspect"]').trigger('click'); expect(api.push).toHaveBeenLastCalledWith({ path: '/workflow/instances', query: { resourceId: id } });
    await wrapper.get('[data-testid="workflow-operations-recovery"]').trigger('click'); expect(api.push).toHaveBeenLastCalledWith('/workflow/recovery-tasks');
  });
  it('筛选变化先清空旧结果，重新读取后切换到独立积压', async () => {
    const wrapper = create(); await wrapper.get('[data-testid="workflow-operations-open"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-operations-minimum"]').setValue(300); expect(wrapper.find('table').exists()).toBe(false);
    await wrapper.get('[data-testid="workflow-operations-refresh"]').trigger('click'); await flushPromises(); expect(api.steps.mock.calls.at(-1)![0].minimumDwellSeconds).toBe(300);
    await wrapper.get('select').setValue('backlog'); await wrapper.get('[data-testid="workflow-operations-refresh"]').trigger('click'); await flushPromises();
    expect(api.backlog).toHaveBeenCalledWith({ page: 1, pageSize: 20 }, expect.any(AbortSignal));
  });
  it('身份变化取消旧读取，403不会恢复旧列表或放出恢复按钮', async () => {
    let resolve!: (value: ReturnType<typeof steps>) => void; api.steps.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = create(); await wrapper.get('[data-testid="workflow-operations-open"]').trigger('click'); const signal = api.steps.mock.calls[0]![1] as AbortSignal;
    session.currentUser.tenantId = id; session.currentUser.permissions = ['workflow.operations.read', 'workflow.instances.read']; await flushPromises();
    expect(signal.aborted).toBe(true); resolve(steps()); await flushPromises(); expect(wrapper.find('table').exists()).toBe(false);
    api.steps.mockRejectedValue({ status: 403 }); await wrapper.get('[data-testid="workflow-operations-open"]').trigger('click'); await flushPromises();
    expect(wrapper.find('table').exists()).toBe(false); expect(wrapper.find('[data-testid="workflow-operations-recovery"]').exists()).toBe(false);
  });
});
