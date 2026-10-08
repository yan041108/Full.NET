import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listMyWorkflowInstances,
  listWorkflowInstanceExecutionLogs,
  listWorkflowInstances,
  pauseWorkflowInstance,
  recoverWorkflowInstance,
  reassignWorkflowInstance,
  resumeWorkflowInstance
} from '../api/workflow-instances';
import WorkflowInstancesView from './WorkflowInstancesView.vue';

vi.mock('../api/workflow-instances', () => ({
  cancelWorkflowInstance: vi.fn(),
  getWorkflowInstance: vi.fn(),
  listMyWorkflowInstances: vi.fn(),
  listWorkflowInstanceExecutionLogs: vi.fn(),
  listWorkflowInstances: vi.fn(),
  pauseWorkflowInstance: vi.fn(),
  recoverWorkflowInstance: vi.fn(),
  reassignWorkflowInstance: vi.fn(),
  resumeWorkflowInstance: vi.fn()
}));

const instanceId = '01912345-6789-7abc-8def-0123456789ab';

function mountView(permissions = ['workflow.instances.read']) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.state = 'authenticated';
  session.currentUser = {
    id: '01912345-6789-7abc-8def-0123456789aa',
    username: 'reader',
    displayName: '流程观察员',
    tenantId: '01912345-6789-7abc-8def-0123456789a1',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '01912345-6789-7abc-8def-0123456789a2',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(WorkflowInstancesView, { global: { plugins: [pinia] } });
}

describe('WorkflowInstancesView', () => {
  beforeEach(() => {
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(undefined as never);
    vi.mocked(listMyWorkflowInstances).mockReset().mockResolvedValue({
      items: [{
        id: instanceId,
        definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
        definitionKey: 'purchase',
        businessTitle: null,
        businessType: 'purchase',
        businessId: 'PO-001',
        statusKey: 'active',
        startedById: '01912345-6789-7abc-8def-0123456789aa',
        startedAtUtc: '2026-08-30T00:00:00Z',
        completedAtUtc: null
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
    vi.mocked(listWorkflowInstances).mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });
    vi.mocked(cancelWorkflowInstance).mockReset().mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'cancelled',
      revision: 4,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(getWorkflowInstance).mockReset().mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 3,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z',
      dueAtUtc: '2026-08-30T00:30:00Z',
      timeoutStatusKey: 'overdue',
      reminderCount: 2,
      escalatedAtUtc: null
    });
    vi.mocked(listWorkflowInstanceExecutionLogs).mockReset().mockResolvedValue([
      {
        id: '01912345-6789-7abc-8def-0123456789b1',
        instanceId,
        stepId: null,
        transitionKey: 'instance.started',
        fromStatusKey: null,
        toStatusKey: 'running',
        createdAtUtc: '2026-08-30T00:00:00Z'
      },
      {
        id: '01912345-6789-7abc-8def-0123456789b2',
        instanceId,
        stepId: '01912345-6789-7abc-8def-0123456789af',
        transitionKey: 'todo.created',
        fromStatusKey: 'running',
        toStatusKey: 'waiting',
        createdAtUtc: '2026-08-30T00:01:00Z'
      }
    ]);
    vi.mocked(pauseWorkflowInstance).mockReset();
    vi.mocked(resumeWorkflowInstance).mockReset();
    vi.mocked(recoverWorkflowInstance).mockReset();
    vi.mocked(reassignWorkflowInstance).mockReset();
  });

  it('撤销读取权限后取消请求并丢弃迟到的列表和详情', async () => {
    let resolveList!: (value: Awaited<ReturnType<typeof listMyWorkflowInstances>>) => void;
    const list = vi.mocked(listMyWorkflowInstances).getMockImplementation()!();
    vi.mocked(listMyWorkflowInstances).mockImplementationOnce(() => new Promise(resolve => { resolveList = resolve; }));
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    const session = useSessionStore();
    session.currentUser!.permissions = [];
    await flushPromises();
    resolveList(await list);
    await flushPromises();
    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-instance-list-item"]')).toHaveLength(0);
    expect(vi.mocked(listMyWorkflowInstances).mock.calls[0]?.[1]?.aborted).toBe(true);
    wrapper.unmount();
  });

  it('取消确认期间切换租户不能写入旧实例', async () => {
    let confirm!: (value: never) => void;
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
    const wrapper = mountView(['workflow.instances.read', 'workflow.instances.cancel']);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-instance-cancel"]').trigger('click');
    useSessionStore().currentUser!.tenantId = '01912345-6789-7abc-8def-0123456789bb';
    await flushPromises();
    confirm(undefined as never);
    await flushPromises();
    expect(cancelWorkflowInstance).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('切换账号后丢弃旧动作的迟到成功和执行轨迹', async () => {
    let complete!: (value: Awaited<ReturnType<typeof cancelWorkflowInstance>>) => void;
    const cancelled = await vi.mocked(cancelWorkflowInstance).getMockImplementation()!(instanceId, { expectedRevision: 3, reason: null, idempotencyKey: 'probe' });
    vi.mocked(cancelWorkflowInstance).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const wrapper = mountView(['workflow.instances.read', 'workflow.instances.cancel']);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-instance-cancel"]').trigger('click');
    await flushPromises();
    useSessionStore().currentUser!.id = '01912345-6789-7abc-8def-0123456789bb';
    await flushPromises();
    complete(cancelled);
    await flushPromises();
    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(0);
    wrapper.unmount();
  });

  it('有精确恢复权限时提供活动待办改派入口', async () => {
    const wrapper = mountView(['workflow.instances.read', 'workflow.instances.recover']);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="workflow-instance-reassign"]').exists()).toBe(true);
    wrapper.unmount();
  });

  it.each(['pause', 'resume', 'recover'] as const)('%s 确认期间撤权不能继续写入', async kind => {
    let confirm!: (value: never) => void;
    if (kind === 'recover') vi.spyOn(ElMessageBox, 'prompt').mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
    else vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
    const snapshot = await vi.mocked(getWorkflowInstance).getMockImplementation()!(instanceId);
    vi.mocked(getWorkflowInstance).mockResolvedValueOnce({ ...snapshot, statusKey: kind === 'pause' ? 'active' : 'suspended' });
    const wrapper = mountView(['workflow.instances.read', 'workflow.instances.' + kind]);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-instance-' + kind + '"]').trigger('click');
    useSessionStore().currentUser!.permissions = ['workflow.instances.read']; await flushPromises();
    confirm((kind === 'recover' ? { value: '核对恢复' } : undefined) as never); await flushPromises();
    expect({ pause: pauseWorkflowInstance, resume: resumeWorkflowInstance, recover: recoverWorkflowInstance }[kind]).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('新查询完成后旧详情不能回填，旧请求同步取消', async () => {
    let complete!: (value: Awaited<ReturnType<typeof getWorkflowInstance>>) => void;
    const old = await vi.mocked(getWorkflowInstance).getMockImplementation()!(instanceId);
    vi.mocked(getWorkflowInstance).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    const nextId = '01912345-6789-7abc-8def-0123456789bc';
    vi.mocked(getWorkflowInstance).mockResolvedValueOnce({ ...old, id: nextId, businessId: 'NEW' });
    vi.mocked(listWorkflowInstanceExecutionLogs).mockResolvedValueOnce([]);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(nextId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    expect(vi.mocked(getWorkflowInstance).mock.calls[0]?.[1]?.aborted).toBe(true);
    complete(old); await flushPromises();
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('NEW');
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).not.toContain('PO-001'); wrapper.unmount();
  });

  it('错误实例的执行轨迹不得显示在当前详情', async () => {
    const logs = await vi.mocked(listWorkflowInstanceExecutionLogs).getMockImplementation()!(instanceId);
    vi.mocked(listWorkflowInstanceExecutionLogs).mockResolvedValueOnce(logs.map(log => ({ ...log, instanceId: 'different' })));
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(0); wrapper.unmount();
  });

  it('默认加载我发起的列表并在点击行后展开详情', async () => {
    const wrapper = mountView();
    await flushPromises();

    expect(listMyWorkflowInstances).toHaveBeenCalled();
    expect(wrapper.find('[data-testid="workflow-instance-list-item"]').text()).toContain('PO-001');
    await wrapper.get('[data-testid="workflow-instance-list-item"]').trigger('click');
    await flushPromises();

    expect(getWorkflowInstance).toHaveBeenCalledWith(instanceId, expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('PO-001');
  });

  it.each(['success', 'error'] as const)('重叠列表的旧 %s 与 finally 不影响新请求', async outcome => {
    let resolveOld!: (value: Awaited<ReturnType<typeof listMyWorkflowInstances>>) => void;
    let rejectOld!: (reason: unknown) => void;
    let resolveNew!: (value: Awaited<ReturnType<typeof listMyWorkflowInstances>>) => void;
    const result = await vi.mocked(listMyWorkflowInstances).getMockImplementation()!();
    vi.mocked(listMyWorkflowInstances)
      .mockImplementationOnce(() => new Promise((resolve, reject) => { resolveOld = resolve; rejectOld = reject; }))
      .mockImplementationOnce(() => new Promise(resolve => { resolveNew = resolve; }));
    const wrapper = mountView(); await flushPromises();
    await wrapper.get('.workflow-instances__filters').trigger('submit'); await flushPromises();
    expect(vi.mocked(listMyWorkflowInstances).mock.calls[0]?.[1]?.aborted).toBe(true);
    if (outcome === 'success') resolveOld(result); else rejectOld({ status: 500, code: 'old.failure', title: '旧错误' });
    await flushPromises();
    expect(wrapper.get('.workflow-instances__list-card').attributes('aria-busy')).toBe('true');
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-instance-list-item"]')).toHaveLength(0);
    resolveNew({ ...result, items: [{ ...result.items[0]!, businessId: 'NEW-LIST' }] }); await flushPromises();
    expect(wrapper.get('.workflow-instances__list-card').attributes('aria-busy')).toBe('false');
    expect(wrapper.get('[data-testid="workflow-instance-list-item"]').text()).toContain('NEW-LIST'); wrapper.unmount();
  });

  it('旧取消报错不能结束新实例仍在执行的取消', async () => {
    let rejectOld!: (reason: unknown) => void;
    let resolveNew!: (value: Awaited<ReturnType<typeof cancelWorkflowInstance>>) => void;
    const snapshot = await vi.mocked(getWorkflowInstance).getMockImplementation()!(instanceId);
    vi.mocked(cancelWorkflowInstance)
      .mockImplementationOnce(() => new Promise((_, reject) => { rejectOld = reject; }))
      .mockImplementationOnce(() => new Promise(resolve => { resolveNew = resolve; }));
    const wrapper = mountView(['workflow.instances.read', 'workflow.instances.cancel']);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-instance-cancel"]').trigger('click'); await flushPromises();
    const nextId = '01912345-6789-7abc-8def-0123456789bc';
    vi.mocked(getWorkflowInstance).mockResolvedValueOnce({ ...snapshot, id: nextId });
    vi.mocked(listWorkflowInstanceExecutionLogs).mockResolvedValue([]);
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(nextId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-instance-cancel"]').trigger('click'); await flushPromises();
    rejectOld({ status: 500, code: 'old.action.failure', title: '旧操作错误' }); await flushPromises();
    expect(wrapper.get('[data-testid="workflow-instance-search"]').attributes('disabled')).toBeDefined();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    expect(vi.mocked(cancelWorkflowInstance).mock.calls[0]?.[2]?.aborted).toBe(true);
    resolveNew({ ...snapshot, id: nextId, statusKey: 'cancelled', activeTodoId: null, revision: 4 }); await flushPromises();
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('cancelled'); wrapper.unmount();
  });

  it('仅向具有独立取消权限的用户展示并执行活动实例取消', async () => {
    const reader = mountView();
    await reader.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await reader.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(reader.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);

    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.cancel'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-cancel"]').trigger('click');
    await flushPromises();

    expect(cancelWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 3,
        reason: null,
        idempotencyKey: expect.any(String)
      }),
      expect.any(AbortSignal)
    );
    expect(operator.get('[data-testid="workflow-instance-summary"]').text())
      .toContain('cancelled');
    expect(operator.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);
  });

  it('按实例标识同时加载只读概要与顺序执行轨迹', async () => {
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();

    expect(getWorkflowInstance).toHaveBeenCalledWith(instanceId, expect.any(AbortSignal));
    expect(listWorkflowInstanceExecutionLogs).toHaveBeenCalledWith(
      instanceId,
      expect.any(AbortSignal)
    );
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('PO-001');
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('active');
    expect(wrapper.get('[data-testid="workflow-instance-timeout-status"]').text())
      .toContain('已逾期');
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('2');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(2);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[0]?.text())
      .toContain('instance.started');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[1]?.text())
      .toContain('todo.created');
    expect(wrapper.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-pause"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);
  });

  it('展示活动多人审批步骤的权威票数进度', async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 3,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z',
      activeNodeKey: 'review',
      approvalModeKey: 'nOfM',
      requiredApprovalCount: 2,
      approvedCount: 1,
      rejectedCount: 0,
      pendingCount: 2
    });
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();

    const progress = wrapper.get('[data-testid="workflow-instance-approval-progress"]');
    expect(progress.text()).toContain('review');
    expect(progress.text()).toContain('同意 1');
    expect(progress.text()).toContain('通过门槛 2');
  });

  it('查询失败时清除旧实例并展示 ProblemDetails', async () => {
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    vi.mocked(getWorkflowInstance).mockRejectedValueOnce({
      status: 403,
      code: 'authorization.permission_denied',
      title: 'Forbidden',
      traceId: 'trace-workflow-instance'
    });

    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(0);
    expect(wrapper.get('[role="alert"]').text()).toContain('authorization.permission_denied');
    expect(wrapper.get('[role="alert"]').text()).toContain('trace-workflow-instance');
  });

  it('仅向具有暂停权限的用户展示活动实例暂停入口并提交修订号与新幂等键', async () => {
    vi.mocked(pauseWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'suspended',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.pause'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-pause"]').trigger('click');
    await flushPromises();

    expect(pauseWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 3,
        reason: null,
        idempotencyKey: expect.stringMatching(/^pause-/)
      }),
      expect.any(AbortSignal)
    );
    expect(listWorkflowInstanceExecutionLogs).toHaveBeenCalledTimes(2);
    expect(operator.get('[data-testid="workflow-instance-summary"]').text())
      .toContain('suspended');
    expect(operator.find('[data-testid="workflow-instance-pause"]').exists()).toBe(false);
  });

  it('暂停实例仅向 resume 与 recover 权限分别创建恢复入口，强制恢复必须提交原因', async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'suspended',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(resumeWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 5,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(recoverWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 5,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.spyOn(ElMessageBox, 'prompt').mockResolvedValue({ value: '卡住后强制恢复' } as never);

    const reader = mountView();
    await reader.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await reader.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(reader.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    expect(reader.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);

    const resumer = mountView([
      'workflow.instances.read',
      'workflow.instances.resume'
    ]);
    await resumer.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await resumer.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(resumer.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);
    await resumer.get('[data-testid="workflow-instance-resume"]').trigger('click');
    await flushPromises();
    expect(resumeWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 4,
        idempotencyKey: expect.stringMatching(/^resume-/)
      }),
      expect.any(AbortSignal)
    );

    const recoverer = mountView([
      'workflow.instances.read',
      'workflow.instances.recover'
    ]);
    await recoverer.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await recoverer.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(recoverer.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    await recoverer.get('[data-testid="workflow-instance-recover"]').trigger('click');
    await flushPromises();
    expect(recoverWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 4,
        reason: '卡住后强制恢复',
        idempotencyKey: expect.stringMatching(/^recover-/)
      }),
      expect.any(AbortSignal)
    );
  });

  it('409 冲突展示 ProblemDetails 并提供刷新入口', async () => {
    vi.mocked(pauseWorkflowInstance).mockRejectedValueOnce({
      status: 409,
      code: 'workflow.revision.conflict',
      title: 'The workflow instance was updated by another request.',
      traceId: 'trace-workflow-conflict'
    });
    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.pause'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-pause"]').trigger('click');
    await flushPromises();

    expect(operator.get('[role="alert"]').text()).toContain('workflow.revision.conflict');
    expect(operator.find('[data-testid="workflow-instance-conflict-refresh"]').exists()).toBe(true);
    await operator.get('[data-testid="workflow-instance-conflict-refresh"]').trigger('click');
    await flushPromises();
    expect(getWorkflowInstance).toHaveBeenCalledTimes(2);
  });
});
