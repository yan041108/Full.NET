import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../../auth/session';
import { reassignWorkflowInstance, type WorkflowInstanceResponse } from '../../api/workflow-instances';
import WorkflowInstanceReassignDialog from './WorkflowInstanceReassignDialog.vue';

vi.mock('../../api/workflow-instances', () => ({ reassignWorkflowInstance: vi.fn() }));
const instance: WorkflowInstanceResponse = {
  id: '01912345-6789-7abc-8def-0123456789ab', definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
  formVersionId: '01912345-6789-7abc-8def-0123456789ad', businessTitle: null,
  businessType: 'demo.enterprise_request', businessId: '01912345-6789-7abc-8def-0123456789af',
  statusKey: 'active', revision: 3, activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
  startedAtUtc: '2026-08-30T00:00:00Z'
};
const target = '01912345-6789-7abc-8def-0123456789bb';
async function mountDialog(permissions = ['workflow.instances.read', 'workflow.instances.recover']) {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore(); session.state = 'authenticated';
  session.currentUser = { id: target, username: 'operator', displayName: '操作者', tenantId: target,
    actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions, sessionId: instance.id, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(WorkflowInstanceReassignDialog, { props: { instance }, global: { plugins: [pinia], stubs: { teleport: true } } });
  await flushPromises(); return wrapper;
}
describe('WorkflowInstanceReassignDialog', () => {
  beforeEach(() => {
    vi.spyOn(ElMessageBox, 'confirm').mockReset().mockResolvedValue(undefined as never);
    vi.mocked(reassignWorkflowInstance).mockReset().mockResolvedValue({ ...instance, revision: 4 });
  });
  it('提交当前修订号、规范目标和新幂等键，成功后通知权威回读', async () => {
    const wrapper = await mountDialog();
    await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(' ' + target + ' ');
    await wrapper.get('[data-testid="workflow-reassign-reason"]').setValue(' 已核对人员 ');
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(reassignWorkflowInstance).toHaveBeenCalledWith(instance.id,
      { assigneeUserId: target, expectedRevision: 3, reason: '已核对人员', idempotencyKey: expect.stringMatching(/^reassign-/) },
      expect.any(AbortSignal));
    expect(wrapper.emitted('saved')).toHaveLength(1); wrapper.unmount();
  });
  it.each(['invalid', '00000000-0000-0000-0000-000000000000', ''])('非法用户 %s 不进入确认或 HTTP', async value => {
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(value);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(ElMessageBox.confirm).not.toHaveBeenCalled(); expect(reassignWorkflowInstance).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it.each(['x'.repeat(501), 'invalid\u0000reason', 'invalid\nreason'])('越界或控制字符原因不能写入', async value => {
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-reason"]').setValue(value);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(reassignWorkflowInstance).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('只有读取权限不能提交改派', async () => {
    const wrapper = await mountDialog(['workflow.instances.read']);
    await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(reassignWorkflowInstance).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('撤权发生在确认期间时不发送旧改派', async () => {
    let confirm!: (value: never) => void;
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click');
    useSessionStore().currentUser!.permissions = ['workflow.instances.read']; await flushPromises();
    confirm(undefined as never); await flushPromises();
    expect(reassignWorkflowInstance).not.toHaveBeenCalled(); expect(wrapper.emitted('close')).toHaveLength(1); wrapper.unmount();
  });
  it('关闭后取消在途改派并丢弃迟到成功', async () => {
    let complete!: (value: WorkflowInstanceResponse) => void;
    vi.mocked(reassignWorkflowInstance).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-reassign-close"]').trigger('click');
    expect(vi.mocked(reassignWorkflowInstance).mock.calls[0]?.[2]?.aborted).toBe(true);
    complete({ ...instance, revision: 4 }); await flushPromises();
    expect(wrapper.emitted('saved')).toBeUndefined(); wrapper.unmount();
  });
  it('错误身份的成功不能回填到原实例，输入保持', async () => {
    vi.mocked(reassignWorkflowInstance).mockResolvedValueOnce({ ...instance, id: target, revision: 4 });
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(wrapper.emitted('saved')).toBeUndefined(); expect(wrapper.find('[role="alert"]').exists()).toBe(true);
    expect((wrapper.get('[data-testid="workflow-reassign-user"]').element as HTMLInputElement).value).toBe(target); wrapper.unmount();
  });
  it('版本冲突保留编辑输入且不报告成功', async () => {
    vi.mocked(reassignWorkflowInstance).mockRejectedValueOnce({ status: 409, code: 'workflow.revision.conflict', title: '版本冲突' });
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    expect(wrapper.emitted('saved')).toBeUndefined(); expect(wrapper.get('[role="alert"]').text()).toContain('workflow.revision.conflict'); wrapper.unmount();
  });

  it.each(['revision', 'activeTodoId'] as const)('确认期间同实例的 %s 改变使旧改派失效', async key => {
    let confirm!: (value: never) => void;
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click');
    await wrapper.setProps({ instance: { ...instance, [key]: key === 'revision' ? 4 : target } });
    confirm(undefined as never); await flushPromises();
    expect(reassignWorkflowInstance).not.toHaveBeenCalled(); expect(wrapper.emitted('close')).toHaveLength(1); wrapper.unmount();
  });

  it('关闭后迟到的 HTTP 错误不显示在弹窗', async () => {
    let reject!: (reason: unknown) => void;
    vi.mocked(reassignWorkflowInstance).mockImplementationOnce(() => new Promise((_, fail) => { reject = fail; }));
    const wrapper = await mountDialog(); await wrapper.get('[data-testid="workflow-reassign-user"]').setValue(target);
    await wrapper.get('[data-testid="workflow-reassign-save"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-reassign-close"]').trigger('click');
    reject({ status: 500, code: 'old.reassign.failure', title: '旧错误' }); await flushPromises();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false); expect(wrapper.emitted('saved')).toBeUndefined(); wrapper.unmount();
  });
});
