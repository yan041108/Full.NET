import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import AiWorkflowApprovalPanel from './AiWorkflowApprovalPanel.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/ai-agent-approvals';
vi.mock('../api/ai-agent-approvals', () => ({ getAgentWorkflowProposal: vi.fn(), createAgentApproval: vi.fn(), getAgentApproval: vi.fn(), decideAgentApproval: vi.fn() }));
const id = '01900000-0000-7000-8000-000000000001';
const proposal = { runId: id, operationId: id, targetChatSessionId: id, currentTitle: 'Original', targetVersion: 1,
  proposedTitle: 'Reviewed title', toolName: 'ai.chat.sessions.rename', toolVersion: 1, argumentsJson: '{}' };
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); setActivePinia(createPinia()); vi.spyOn(useSessionStore(), 'can').mockReturnValue(true); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
it('切换运行立即清除旧提案并中止原请求，迟到响应不能恢复提交入口', async () => {
  let finish!: (value: typeof proposal) => void;
  vi.mocked(api.getAgentWorkflowProposal).mockReturnValue(new Promise(resolve => { finish = resolve; }));
  wrapper = mount(AiWorkflowApprovalPanel, { props: { runId: id } });
  await wrapper.get('[data-testid="ai-workflow-read-proposal"]').trigger('click');
  const signal = vi.mocked(api.getAgentWorkflowProposal).mock.calls[0]![1]!;
  await wrapper.setProps({ runId: '01900000-0000-7000-8000-000000000002' });
  expect(signal.aborted).toBe(true); finish(proposal); await flushPromises();
  expect(wrapper.text()).not.toContain('Reviewed title'); expect(wrapper.find('[data-testid="ai-workflow-request-approval"]').exists()).toBe(false);
});
it('上下文切换清除原提案并阻止沿用原审批身份', async () => {
  vi.mocked(api.getAgentWorkflowProposal).mockResolvedValue(proposal);
  wrapper = mount(AiWorkflowApprovalPanel, { props: { runId: id } });
  await wrapper.get('[data-testid="ai-workflow-read-proposal"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).toContain('Reviewed title');
  useSessionStore().currentContextName = 'Tenant B'; await flushPromises();
  expect(wrapper.text()).not.toContain('Reviewed title'); expect(api.createAgentApproval).not.toHaveBeenCalled();
});
it('提案读取被拒绝后清除旧数据，不保留可提交的陈旧参数', async () => {
  vi.mocked(api.getAgentWorkflowProposal).mockResolvedValueOnce(proposal).mockRejectedValueOnce(new Error('revoked'));
  wrapper = mount(AiWorkflowApprovalPanel, { props: { runId: id } });
  await wrapper.get('[data-testid="ai-workflow-read-proposal"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).toContain('Reviewed title');
  await wrapper.get('[data-testid="ai-workflow-read-proposal"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).not.toContain('Reviewed title'); expect(wrapper.find('[data-testid="ai-workflow-request-approval"]').exists()).toBe(false);
});
it('缺少读取权限时不显示原审批及提案内容', () => {
  vi.mocked(useSessionStore().can).mockReturnValue(false);
  wrapper = mount(AiWorkflowApprovalPanel, { props: { runId: id } });
  expect(wrapper.find('[data-testid="ai-workflow-approval"]').exists()).toBe(false); expect(api.getAgentWorkflowProposal).not.toHaveBeenCalled();
});
it('日程审批使用通用目标标签并展示服务器冻结的准确版本', async () => {
  vi.mocked(api.getAgentApproval).mockResolvedValue({ id, runId: id, operationId: id,
    toolName: 'calendar.personal_schedules.complete', toolVersion: 1, argumentsHash: 'a'.repeat(64),
    decisionKey: 'pending', actionSummary: 'Complete own personal schedule', targetSummary: id,
    changeSummary: 'pending -> completed; expected version: 7', scopeSummary: 'Owned personal schedule; independent approval required',
    costCeiling: null, currency: null, expiresAtUtc: '2099-01-01T00:00:00Z', consumedAtUtc: null,
    version: 1, createdAtUtc: '2026-10-31T00:00:00Z' });
  wrapper = mount(AiWorkflowApprovalPanel);
  await wrapper.get('input').setValue(id);
  await wrapper.get('[data-testid="ai-workflow-lookup-approval"]').trigger('click'); await flushPromises();
  expect(wrapper.text()).toContain('操作目标'); expect(wrapper.text()).toContain('变更内容');
  expect(wrapper.text()).toContain('expected version: 7'); expect(wrapper.text()).not.toContain('当前标题');
});
