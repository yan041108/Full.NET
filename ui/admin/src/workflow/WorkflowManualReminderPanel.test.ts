import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { listWorkflowReminderTargets, requestWorkflowReminder } from '../api/workflow-reminders';
import WorkflowManualReminderPanel from './WorkflowManualReminderPanel.vue';

vi.mock('../api/workflow-reminders', () => ({ listWorkflowReminderTargets: vi.fn(), requestWorkflowReminder: vi.fn() }));
const id = '01912345-6789-7abc-8def-0123456789ab';
const target = { stepId: id, nodeKey: 'review', nodeTypeKey: 'human.task', stepRevision: 1, activeTodoCount: 2, manualCount: 0, lastRequestedAtUtc: null, canRemind: true };
function setup(permissions = ['workflow.instances.remind']) {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = { id, username: 'starter', displayName: 'Starter', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { wrapper: mount(WorkflowManualReminderPanel, { props: { instanceId: id, revision: 1, statusKey: 'active' }, global: { plugins: [pinia] } }), session };
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowReminderTargets).mockResolvedValue({ instanceRevision: 1, items: [target] }); vi.mocked(requestWorkflowReminder).mockResolvedValue({
  requestId: id, stepId: id, instanceRevision: 1, stepRevision: 1, manualSequence: 1, recipientCount: 2, acceptedAtUtc: '2026-10-25T00:00:00Z' }); });
describe('发起人催办入口', () => {
  it('没有精确权限不请求或显示接收目标', async () => {
    const { wrapper } = setup([]); await flushPromises(); expect(listWorkflowReminderTargets).not.toHaveBeenCalled(); expect(wrapper.find('section').exists()).toBe(false); wrapper.unmount();
  });
  it('未知结果重试保留同一请求身份且不声称已送达', async () => {
    vi.mocked(requestWorkflowReminder).mockRejectedValueOnce(new TypeError('network'));
    const { wrapper } = setup(); await flushPromises();
    await wrapper.get('[data-testid="workflow-manual-remind"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-manual-remind"]').trigger('click'); await flushPromises();
    expect(requestWorkflowReminder).toHaveBeenCalledTimes(2);
    expect(vi.mocked(requestWorkflowReminder).mock.calls[0]?.[1]).toEqual(vi.mocked(requestWorkflowReminder).mock.calls[1]?.[1]); wrapper.unmount();
  });
  it('身份切换立即清除旧目标并忽略迟到响应', async () => {
    let resolve!: (value: { instanceRevision: number; items: typeof target[] }) => void;
    vi.mocked(listWorkflowReminderTargets).mockImplementationOnce(() => new Promise(result => { resolve = result; }));
    const { wrapper, session } = setup(); session.currentUser = { ...session.currentUser!, permissions: [] };
    resolve({ instanceRevision: 1, items: [target] }); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-manual-remind"]').exists()).toBe(false); wrapper.unmount();
  });
  it('受理后刷新失败仍保留已知限频状态', async () => {
    vi.mocked(listWorkflowReminderTargets).mockResolvedValueOnce({ instanceRevision: 1, items: [target] }).mockRejectedValueOnce(new TypeError('network'));
    const { wrapper } = setup(); await flushPromises();
    await wrapper.get('[data-testid="workflow-manual-remind"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="workflow-manual-remind"]').attributes('disabled')).toBeDefined();
    expect(wrapper.text()).toContain('1/20'); wrapper.unmount();
  });
});
