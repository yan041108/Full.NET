import { createRouter, createMemoryHistory } from 'vue-router';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowTodoDetail, WorkflowTodoListItemResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { ElMessage, ElPagination } from 'element-plus';

import {
  listWorkflowTodoCountersignCandidates,
  approveWorkflowTodo,
  completeWorkflowTodo,
  voteWorkflowTodo,
  countersignWorkflowTodo,
  cancelWorkflowTodoCountersign,
  getWorkflowTodoCountersignChain,
  getWorkflowTodo,
  listMyWorkflowTodoHistory,
  listWorkflowTodoReturnTargets,
  listMyWorkflowTodos,
  rejectWorkflowTodo,
  returnWorkflowTodo,
  uploadWorkflowTodoSignature,
  listWorkflowTodoTransferCandidates,
  transferWorkflowTodo
} from '../api/workflow-todos';
import WorkflowTodosView from './WorkflowTodosView.vue';
import { delegateWorkflowTodo, getWorkflowTodoDelegation, recallWorkflowTodoDelegation,
  listWorkflowTodoDelegationCandidates } from '../api/workflow-delegations';
vi.mock('../api/workflow-delegations', () => ({ delegateWorkflowTodo: vi.fn(), getWorkflowTodoDelegation: vi.fn(),
  recallWorkflowTodoDelegation: vi.fn(), listWorkflowTodoDelegationCandidates: vi.fn() }));

vi.mock('../api/workflow-todos', () => ({
  listWorkflowTodoCountersignCandidates: vi.fn(),
  listWorkflowTodoTransferCandidates: vi.fn(),
  transferWorkflowTodo: vi.fn(),
  approveWorkflowTodo: vi.fn(),
  completeWorkflowTodo: vi.fn(),
  voteWorkflowTodo: vi.fn(),
  uploadWorkflowTodoSignature: vi.fn(),
  countersignWorkflowTodo: vi.fn(),
  cancelWorkflowTodoCountersign: vi.fn(),
  getWorkflowTodoCountersignChain: vi.fn(),
  getWorkflowTodo: vi.fn(),
  listMyWorkflowTodoHistory: vi.fn(),
  listWorkflowTodoReturnTargets: vi.fn(),
  listMyWorkflowTodos: vi.fn(),
  rejectWorkflowTodo: vi.fn(),
  returnWorkflowTodo: vi.fn()
}));


function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}

const todo: WorkflowTodoListItemResponse = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  instanceId: '01912345-6789-7abc-8def-0123456789ac',
  stepId: '01912345-6789-7abc-8def-0123456789ad',
  statusKey: 'active',
  arrivedAtUtc: '2026-08-30T00:00:00Z',
  completedAtUtc: null,
  resultActionKey: null,
  revision: 3,
  businessTitle: null,
  businessType: 'purchase',
  businessId: 'PO-001',
  instanceStatusKey: 'active',
  definitionKey: 'purchase-approval',
  nodeKey: 'manager-approval'
};

const historyTodo = {
  ...todo,
  id: '01912345-6789-7abc-8def-0123456789b0',
  statusKey: 'completed',
  completedAtUtc: '2026-08-31T00:00:00Z',
  resultActionKey: 'approve'
};

const paged = (items: WorkflowTodoListItemResponse[]) => ({
  items,
  page: 1,
  pageSize: 20,
  total: items.length
});

const detail: WorkflowTodoDetail = {
  id: todo.id,
  instanceId: todo.instanceId,
  stepId: todo.stepId,
  assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
  statusKey: 'active',
  revision: 3,
  formVersionId: '01912345-6789-7abc-8def-0123456789af',
  formSchemaHash: 'a'.repeat(64),
  allowedActions: ['approve', 'reject', 'return', 'countersign', 'transfer', 'remind'], signRequired: false, commentRequired: false,
  formSchema: {
    schemaVersion: 1,
    adapterVersion: 1,
    sections: [{
      sectionKey: 'request',
      fields: [{
        fieldKey: 'summary',
        fieldTypeKey: 'text',
        required: true,
        constraints: {}
      }]
    }]
  },
  submission: { summary: '采购审批' },
  fieldPolicies: { summary: 'editable' },
  submissionRevision: 4,
  approvalModeKey: 'single',
  requiredApprovalCount: 1,
  approvedCount: 0,
  rejectedCount: 0,
  pendingCount: 1
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '01912345-6789-7abc-8def-0123456789ae',
    username: 'approver',
    displayName: '审批人',
    tenantId: '01912345-6789-7abc-8def-0123456789aa',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '01912345-6789-7abc-8def-0123456789a1',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  const router = createRouter({ history: createMemoryHistory(), routes: ['/', '/workflow/todos'].map(path => ({ path, component: { template: '<div />' } })) });
  return mount(WorkflowTodosView, { global: { plugins: [pinia, router] } });
}

describe('WorkflowTodosView', () => {
  it.each([false, true])('历史原待办只按独立收回权限及最新代理修订操作：%s', async allowed => {
    vi.mocked(listMyWorkflowTodoHistory).mockResolvedValue(paged([{ ...historyTodo, resultActionKey: 'delegate' }]));
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, id: historyTodo.id, statusKey: 'completed', allowedActions: ['delegate'], commentRequired: true });
    vi.mocked(getWorkflowTodoDelegation).mockResolvedValue({ originTodoId: historyTodo.id, proxyTodoId: todo.id,
      originalAssigneeUserId: detail.assigneeUserId, proxyUserId: '01912345-6789-7abc-8def-0123456789b3', statusKey: 'active',
      proxyRevision: 7, formRevision: 9, canRecall: true, createdAtUtc: '2026-08-30T00:00:00Z' });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.delegate', ...(allowed ? ['workflow.todos.recall_delegation'] : [])]);
    try {
      await flushPromises(); await wrapper.findAll('[data-testid="workflow-todo-tabs"] .el-tabs__item')[1]!.trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-todo-delegate-recall"]').exists()).toBe(allowed);
      expect(wrapper.find('[data-testid="workflow-todo-delegate-target"]').exists()).toBe(false);
      if (allowed) {
        expect(wrapper.get('[data-testid="workflow-todo-delegate-recall"]').attributes('disabled')).toBeDefined();
        await wrapper.get('[data-testid="workflow-todo-comment"]').setValue('收回办理');
        await wrapper.get('[data-testid="workflow-todo-delegate-recall"]').trigger('click'); await flushPromises();
        expect(recallWorkflowTodoDelegation).toHaveBeenCalledWith(historyTodo.id, 7, 9, '收回办理', expect.any(String));
      } else expect(recallWorkflowTodoDelegation).not.toHaveBeenCalled();
      expect(delegateWorkflowTodo).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it.each([false, true])('代理使用独立精确权限和20人真实候选分页：%s', async allowed => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, allowedActions: ['approve', 'delegate'] });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.transfer', ...(allowed ? ['workflow.todos.delegate'] : [])]);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-todo-delegate-submit"]').exists()).toBe(allowed);
      if (allowed) {
        expect(listWorkflowTodoDelegationCandidates).toHaveBeenCalledWith(detail.id, 1, 20, expect.any(AbortSignal));
        await wrapper.get('[data-testid="workflow-todo-delegate-target"]').setValue('01912345-6789-7abc-8def-0123456789b3');
        await wrapper.get('[data-testid="workflow-todo-delegate-submit"]').trigger('click'); await flushPromises();
        expect(delegateWorkflowTodo).toHaveBeenCalledWith(detail.id, '01912345-6789-7abc-8def-0123456789b3', detail.revision,
          detail.submissionRevision, null, expect.any(String), undefined);
      } else { expect(listWorkflowTodoDelegationCandidates).not.toHaveBeenCalled(); expect(delegateWorkflowTodo).not.toHaveBeenCalled(); }
      expect(transferWorkflowTodo).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it.each([false, true])('投票使用独立权限、修订与私有答案：%s', async allowed => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, activityKindKey: 'human.vote', approvalModeKey: 'none',
      requiredApprovalCount: 0, approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['vote'],
      fieldPolicies: { summary: 'readOnly' }, voteForm: { formVersionId: detail.formVersionId, formSchemaHash: detail.formSchemaHash,
        formSchema: detail.formSchema, values: { summary: 'private-answer' }, fieldPolicies: { summary: 'required' }, revision: 7,
        choiceKey: null, readOnly: false, allowAbstain: true }, voteProgress: { memberCount: 3, requiredParticipationCount: 2,
        submittedCount: 0, pendingCount: 3, cancelledCount: 0, statusKey: 'active', closesAtUtc: null,
        yesCount: null, noCount: null, abstainCount: null, unsubmittedCount: null } });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', ...(allowed ? ['workflow.todos.vote'] : [])]);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.get('[data-testid="workflow-vote-private-form"]').text()).toContain('本人的独立投票表单');
      expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-todo-vote"]').exists()).toBe(allowed);
      if (allowed) {
        expect(wrapper.get('[data-testid="workflow-todo-vote"]').attributes('disabled')).toBeDefined();
        await wrapper.get('[data-testid="workflow-vote-choice"]').setValue('yes');
        await wrapper.get('[data-testid="workflow-todo-vote"]').trigger('click'); await flushPromises();
        expect(voteWorkflowTodo).toHaveBeenCalledWith(detail.id, detail.revision, 7, 'yes', { summary: 'private-answer' }, null, expect.any(String), undefined);
      } else expect(voteWorkflowTodo).not.toHaveBeenCalled();
      expect(approveWorkflowTodo).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it.each([false, true])('办理提交只使用独立完成权限，不能借审批权限：%s', async allowed => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, activityKindKey: 'human.task', approvalModeKey: 'none',
      requiredApprovalCount: 0, approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['complete'],
      taskProgress: { completionModeKey: 'all', memberCount: 3, requiredCompletionCount: 3, completedCount: 1, pendingCount: 2, cancelledCount: 0 } });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', ...(allowed ? ['workflow.todos.complete'] : [])]);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.get('[data-testid="workflow-task-progress"]').text()).toContain('已完成 1 人');
      expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-todo-complete"]').exists()).toBe(allowed);
      if (allowed) { await wrapper.get('[data-testid="workflow-todo-complete"]').trigger('click'); await flushPromises();
        expect(completeWorkflowTodo).toHaveBeenCalledWith(detail.id, detail.revision, detail.submissionRevision, {}, null, expect.any(String)); }
      else expect(completeWorkflowTodo).not.toHaveBeenCalled();
      expect(approveWorkflowTodo).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });

  it('节点禁用动作时不请求辅助能力，签名和意见齐全才提交绑定文件', async () => {
    const fileId = '01912345-6789-7abc-8def-0123456789b1';
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, allowedActions: ['approve'], signRequired: true, commentRequired: true });
    vi.mocked(uploadWorkflowTodoSignature).mockResolvedValue({ fileId, sizeBytes: 68, contentHash: 'a'.repeat(64) });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.reject',
      'workflow.todos.return', 'workflow.todos.countersign', 'workflow.todos.sign']);
    await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-todo-reject"]').exists()).toBe(false);
    expect(listWorkflowTodoReturnTargets).not.toHaveBeenCalled(); expect(listWorkflowTodoCountersignCandidates).not.toHaveBeenCalled();
    expect(wrapper.get('[data-testid="workflow-todo-approve"]').attributes('disabled')).toBeDefined();
    wrapper.findComponent({ name: 'WorkflowSignatureCapture' }).vm.$emit('capture', new File([new Uint8Array(68)], 'signature.png', { type: 'image/png' }));
    await flushPromises();
    expect(wrapper.get('[data-testid="workflow-todo-approve"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="workflow-todo-comment"]').setValue('确认同意');
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click'); await flushPromises();
    expect(approveWorkflowTodo).toHaveBeenCalledWith(detail.id, detail.revision, detail.submissionRevision, {}, '确认同意', expect.any(String), fileId);
    wrapper.unmount();
  });
  it('原版渲染器尚未就绪时禁止办理，即使实例已有满足必填的值', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, webRenderJson: { widgetList: [], formConfig: {} } });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve']); await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="workflow-todo-approve"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    expect(approveWorkflowTodo).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('缓存页面切换深链时重新授权读取，并拒绝旧资源的迟到响应', async () => {
    const secondId = '01912345-6789-7abc-8def-0123456789b2';
    let finishOld!: (value: WorkflowTodoDetail) => void;
    const old = new Promise<WorkflowTodoDetail>(resolve => { finishOld = resolve; });
    vi.mocked(getWorkflowTodo).mockImplementation(id => id === todo.id ? old
      : Promise.resolve({ ...detail, id: secondId, submission: { summary: '第二条深链资源' } }));
    const wrapper = mountWithPermissions(['workflow.todos.read']);
    await flushPromises();
    await wrapper.vm.$router.push({ path: '/workflow/todos', query: { resourceId: todo.id } });
    await flushPromises();
    expect(getWorkflowTodo).toHaveBeenCalledWith(todo.id, expect.any(AbortSignal));
    await wrapper.vm.$router.push({ path: '/workflow/todos', query: { resourceId: secondId } });
    await flushPromises();
    expect(getWorkflowTodo).toHaveBeenCalledWith(secondId, expect.any(AbortSignal));
    finishOld(detail);
    await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).props('submission')).toEqual({ summary: '第二条深链资源' });
    wrapper.unmount();
  });
  beforeEach(() => {
    vi.mocked(getWorkflowTodoDelegation).mockReset().mockResolvedValue(null);
    vi.mocked(delegateWorkflowTodo).mockReset(); vi.mocked(recallWorkflowTodoDelegation).mockReset();
    vi.mocked(listWorkflowTodoDelegationCandidates).mockReset().mockResolvedValue({ items: [
      { id: '01912345-6789-7abc-8def-0123456789b3', username: 'proxy', displayName: '代理人' }
    ], page: 1, pageSize: 20, total: 1 });
    vi.mocked(voteWorkflowTodo).mockReset().mockResolvedValue({ id: todo.instanceId, definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: detail.formVersionId, businessTitle: null, businessType: 'purchase', businessId: 'PO-001', statusKey: 'active',
      revision: 4, activeTodoId: null, startedAtUtc: '2026-08-30T00:00:00Z' });
    vi.mocked(completeWorkflowTodo).mockReset().mockResolvedValue({ id: todo.instanceId, definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: detail.formVersionId, businessTitle: null, businessType: 'purchase', businessId: 'PO-001', statusKey: 'completed',
      revision: 5, activeTodoId: null, startedAtUtc: '2026-08-30T00:00:00Z' });
    vi.mocked(transferWorkflowTodo).mockReset();
    vi.mocked(listWorkflowTodoTransferCandidates).mockReset().mockResolvedValue({ items: [
      { id: '01912345-6789-7abc-8def-0123456789b3', username: 'next', displayName: '接收人' },
      { id: '01912345-6789-7abc-8def-0123456789b4', username: 'other', displayName: '另一接收人' }
    ], page: 1, pageSize: 100, total: 2 });
    vi.mocked(uploadWorkflowTodoSignature).mockReset();
    vi.mocked(countersignWorkflowTodo).mockReset();
    vi.mocked(cancelWorkflowTodoCountersign).mockReset();
    vi.mocked(getWorkflowTodoCountersignChain).mockReset();
    vi.mocked(listWorkflowTodoCountersignCandidates).mockReset();
    vi.mocked(getWorkflowTodoCountersignChain).mockRejectedValue({ status: 404 });
    vi.mocked(listWorkflowTodoCountersignCandidates).mockResolvedValue({
      items: [{ id: '01912345-6789-7abc-8def-0123456789b3', username: 'reviewer', displayName: '加签人' }],
      page: 1, pageSize: 20, total: 1
    });
    vi.mocked(listMyWorkflowTodos).mockReset().mockResolvedValue(paged([todo]));
    vi.mocked(listMyWorkflowTodoHistory).mockReset().mockResolvedValue(paged([historyTodo]));
    vi.mocked(getWorkflowTodo).mockReset().mockResolvedValue(detail);
    vi.mocked(listWorkflowTodoReturnTargets).mockReset().mockResolvedValue([{
      stepId: '01912345-6789-7abc-8def-0123456789a3',
      nodeKey: 'manager-approval',
      assigneeUserId: '01912345-6789-7abc-8def-0123456789a4',
      completedAtUtc: '2026-08-29T00:00:00Z'
    }]);
    vi.mocked(approveWorkflowTodo).mockReset().mockResolvedValue({
      id: todo.instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: detail.formVersionId,
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'completed',
      revision: 5,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(rejectWorkflowTodo).mockReset();
    vi.mocked(returnWorkflowTodo).mockReset().mockResolvedValue({
      id: todo.instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: detail.formVersionId,
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 5,
      activeTodoId: '01912345-6789-7abc-8def-0123456789a5',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
  });

  it('仅转办权限可读取本人候选，失败重试复用幂等键，变更目标使用新键', async () => {
    vi.mocked(transferWorkflowTodo).mockRejectedValue(new Error('network'));
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.transfer']);
    await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    expect(listWorkflowTodoTransferCandidates).toHaveBeenCalledWith(detail.id, 1, 20, expect.any(AbortSignal));
    expect(listWorkflowTodoCountersignCandidates).not.toHaveBeenCalled();
    const target = wrapper.get('[data-testid="workflow-todo-transfer-target"]');
    await target.setValue('01912345-6789-7abc-8def-0123456789b3');
    const submit = wrapper.get('[data-testid="workflow-todo-transfer-submit"]');
    await submit.trigger('click'); await flushPromises(); await submit.trigger('click'); await flushPromises();
    const calls = vi.mocked(transferWorkflowTodo).mock.calls;
    expect(calls).toHaveLength(2); expect(calls[0]).toEqual(calls[1]);
    expect(calls[0]?.slice(0, 5)).toEqual([detail.id, '01912345-6789-7abc-8def-0123456789b3', detail.revision, detail.submissionRevision, null]);
    await target.setValue('01912345-6789-7abc-8def-0123456789b4');
    await submit.trigger('click'); await flushPromises();
    expect(calls[2]?.[5]).not.toBe(calls[0]?.[5]);
    wrapper.unmount();
  });

  it('节点禁用转办时不读取候选也不显示操作', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, allowedActions: ['approve'] });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.transfer']);
    await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    expect(listWorkflowTodoTransferCandidates).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="workflow-todo-transfer-submit"]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('有未提交字段修改时阻止转办并解释原因', async () => {
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.transfer']);
    await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-transfer-target"]').setValue('01912345-6789-7abc-8def-0123456789b3');
    wrapper.findComponent({ name: 'WorkflowFormRenderer' }).vm.$emit('update:patch', { summary: '尚未提交' });
    await flushPromises();
    expect(wrapper.get('[data-testid="workflow-todo-transfer-submit"]').attributes('disabled')).toBeDefined();
    expect(wrapper.text()).toContain('转办不提交字段修改');
    expect(transferWorkflowTodo).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it.each(['tenant', 'account', 'session', 'scope', 'read', 'approve', 'reject', 'return', 'countersign', 'switching'] as const)('上下文 %s 变化立即移除旧表单和列表', async change => {
    const permissions = ['workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.reject', 'workflow.todos.return', 'workflow.todos.countersign'];
    const wrapper = mountWithPermissions(permissions);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(true);
    vi.mocked(listMyWorkflowTodos).mockResolvedValue(paged([]));
    const session = useSessionStore();
    if (change === 'switching') session.switching = true;
    else session.currentUser = {
      ...session.currentUser!,
      ...(change === 'tenant' ? { tenantId: '01912345-6789-7abc-8def-0123456789b1' }
        : change === 'account' ? { id: '01912345-6789-7abc-8def-0123456789b2' }
        : change === 'session' ? { sessionId: '01912345-6789-7abc-8def-0123456789b4' }
        : change === 'scope' ? { scope: 'host', actorScope: 'host', tenantId: null }
        : { permissions: change === 'read' ? [] : permissions.filter(permission => permission !== `workflow.todos.${change}`) })
    };
    await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-todo-open"]').exists()).toBe(false);
    expect(listMyWorkflowTodos).toHaveBeenCalledTimes(change === 'read' || change === 'switching' ? 1 : 2);
    wrapper.unmount();
  });

  it('普通资料刷新保留未提交的审批意见', async () => {
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-comment"]').setValue('未提交意见');
    const session = useSessionStore();
    session.currentUser = { ...session.currentUser!, displayName: '更新姓名', profileVersion: 2 };
    await flushPromises();
    expect((wrapper.get('[data-testid="workflow-todo-comment"]').element as HTMLTextAreaElement).value).toBe('未提交意见');
    expect(listMyWorkflowTodos).toHaveBeenCalledTimes(1);
    wrapper.unmount();
  });

  it('旧抽屉的迟到关闭事件不能清空深链切换后的详情', async () => {
    const wrapper = mountWithPermissions(['workflow.todos.read']); await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    const oldDrawer = wrapper.findComponent({ name: 'ElDrawer' });
    expect(oldDrawer.exists()).toBe(true);
    const secondId = '01912345-6789-7abc-8def-0123456789b2';
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, id: secondId, submission: { summary: '新详情' } });
    await wrapper.vm.$router.push({ path: '/workflow/todos', query: { resourceId: secondId } }); await flushPromises();
    oldDrawer.vm.$emit('close'); await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).props('submission')).toEqual({ summary: '新详情' });
    wrapper.findComponent({ name: 'ElDrawer' }).vm.$emit('close'); await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(false);
    wrapper.unmount();
  });

  it('旧分页请求的失败和 finally 不污染新请求', async () => {
    const old = deferred<ReturnType<typeof paged>>();
    const next = deferred<ReturnType<typeof paged>>();
    vi.mocked(listMyWorkflowTodos).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    const wrapper = mountWithPermissions(['workflow.todos.read']);
    await flushPromises();
    const session = useSessionStore();
    session.currentUser = { ...session.currentUser!, tenantId: '01912345-6789-7abc-8def-0123456789b1' };
    await flushPromises();
    old.reject({ status: 500, code: 'old.list_failed', title: '旧列表错误' });
    await flushPromises();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    expect(wrapper.get('section').attributes('aria-busy')).toBe('true');
    next.resolve(paged([])); await flushPromises();
    expect(wrapper.get('section').attributes('aria-busy')).toBe('false');
    wrapper.unmount();
  });

  it('切换上下文取消旧详情且不启动后续辅助读取', async () => {
    const old = deferred<WorkflowTodoDetail>();
    vi.mocked(getWorkflowTodo).mockReturnValueOnce(old.promise);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.return']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    const signal = vi.mocked(getWorkflowTodo).mock.calls[0][1];
    useSessionStore().switching = true; await flushPromises();
    expect(signal?.aborted).toBe(true);
    old.resolve(detail); await flushPromises();
    expect(listWorkflowTodoReturnTargets).not.toHaveBeenCalled();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(false);
    useSessionStore().switching = false; await flushPromises();
    expect(listMyWorkflowTodos).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });

  it.each(['approve', 'reject', 'return', 'countersign', 'cancel'] as const)('旧 %s 回执不会关闭新详情或显示旧提示', async action => {
    const old = deferred<Awaited<ReturnType<typeof approveWorkflowTodo>>>();
    const execute = action === 'approve' ? approveWorkflowTodo : action === 'reject' ? rejectWorkflowTodo
      : action === 'return' ? returnWorkflowTodo : action === 'countersign' ? countersignWorkflowTodo : cancelWorkflowTodoCountersign;
    vi.mocked(execute).mockReturnValueOnce(old.promise);
    if (action === 'cancel') vi.mocked(getWorkflowTodoCountersignChain).mockResolvedValue({
      chainId: todo.id, directionKey: 'before', statusKey: 'active', items: []
    });
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.reject', 'workflow.todos.return', 'workflow.todos.countersign']);
    try {
      await flushPromises();
      await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      if (action === 'return') {
        await wrapper.get('[data-testid="workflow-todo-return-target"]').setValue('01912345-6789-7abc-8def-0123456789a3');
        await wrapper.get('[data-testid="workflow-todo-comment"]').setValue('退回原因');
      }
      if (action === 'countersign') await wrapper.get('[data-testid="workflow-todo-countersign-assignees"]').setValue(['01912345-6789-7abc-8def-0123456789b3']);
      const button = action === 'cancel' ? 'countersign-cancel' : action === 'countersign' ? 'countersign-submit' : action;
      await wrapper.get(`[data-testid="workflow-todo-${button}"]`).trigger('click'); await flushPromises();
      expect(execute).toHaveBeenCalledTimes(1);
      // 同一账号切换资源也必须隔离回执，不能只检查租户。
      const secondId = '01912345-6789-7abc-8def-0123456789b2';
      vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, id: secondId });
      await wrapper.vm.$router.push({ path: '/workflow/todos', query: { resourceId: secondId } }); await flushPromises();
      old.resolve({} as Awaited<ReturnType<typeof approveWorkflowTodo>>); await flushPromises();
      expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(true);
      expect(wrapper.find('[role="alert"]').exists()).toBe(false);
      expect(success).not.toHaveBeenCalled();
      expect(listMyWorkflowTodos).toHaveBeenCalledTimes(1);
      expect(wrapper.get('section').attributes('aria-busy')).toBe('false');
    } finally { wrapper.unmount(); success.mockRestore(); }
  });

  it('切换上下文后旧审批冲突不关闭新详情、不回填错误、不释放新审批 busy', async () => {
    const old = deferred<Awaited<ReturnType<typeof approveWorkflowTodo>>>();
    const next = deferred<Awaited<ReturnType<typeof approveWorkflowTodo>>>();
    vi.mocked(approveWorkflowTodo).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    const session = useSessionStore();
    session.currentUser = { ...session.currentUser!, tenantId: '01912345-6789-7abc-8def-0123456789b1' }; await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    expect(approveWorkflowTodo).toHaveBeenCalledTimes(2);
    old.reject({ status: 409, code: 'old.conflict', title: '旧上下文冲突' }); await flushPromises();
    expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(true);
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    expect(wrapper.get('section').attributes('aria-busy')).toBe('true');
    expect(listMyWorkflowTodos).toHaveBeenCalledTimes(2);
    wrapper.unmount();
    next.reject(new Error('卸载后失败')); await flushPromises();
  });

  it('撤销权限取消正在读取的加签候选和链，并忽略迟到数据', async () => {
    const candidates = deferred<Awaited<ReturnType<typeof listWorkflowTodoCountersignCandidates>>>();
    const chain = deferred<Awaited<ReturnType<typeof getWorkflowTodoCountersignChain>>>();
    vi.mocked(listWorkflowTodoCountersignCandidates).mockReturnValueOnce(candidates.promise);
    vi.mocked(getWorkflowTodoCountersignChain).mockReturnValueOnce(chain.promise);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.countersign']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(listWorkflowTodoCountersignCandidates).mock.calls[0][3];
    expect(getWorkflowTodoCountersignChain).toHaveBeenCalledWith(todo.id, signal);
    const session = useSessionStore();
    session.currentUser = { ...session.currentUser!, permissions: [] }; await flushPromises();
    expect(signal?.aborted).toBe(true);
    candidates.resolve({ items: [], total: 0, page: 1, pageSize: 100 });
    chain.resolve({ chainId: todo.id, directionKey: 'before', statusKey: 'active', items: [] }); await flushPromises();
    expect(wrapper.find('[data-testid="workflow-todo-countersign-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('前加签等待中仅展示只读表单与取消链，取消后刷新恢复的新待办', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, statusKey: 'before_countersign' });
    vi.mocked(getWorkflowTodoCountersignChain).mockResolvedValue({ chainId: todo.id, directionKey: 'before', statusKey: 'active', items: [] });
    vi.mocked(cancelWorkflowTodoCountersign).mockResolvedValue({} as Awaited<ReturnType<typeof cancelWorkflowTodoCountersign>>);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.return', 'workflow.todos.countersign']);
    try {
      await flushPromises();
      await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.get('[data-testid="workflow-todo-waiting-countersign"]').text()).toContain('等待前加签');
      expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-todo-countersign-submit"]').exists()).toBe(false);
      expect(listWorkflowTodoReturnTargets).not.toHaveBeenCalled();
      expect(listWorkflowTodoCountersignCandidates).not.toHaveBeenCalled();
      expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).props('fieldPolicies')).toEqual({ summary: 'readOnly' });
      await wrapper.get('[data-testid="workflow-todo-countersign-cancel"]').trigger('click'); await flushPromises();
      expect(cancelWorkflowTodoCountersign).toHaveBeenCalledWith(detail.id, detail.revision, null, expect.any(String));
      expect(listMyWorkflowTodos).toHaveBeenCalledTimes(2);
    } finally { wrapper.unmount(); }
  });

  it('附属签署人即使具备平台管理权限也只显示资源允许的办理动作', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, allowedActions: ['approve', 'reject', 'remind'] });
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.approve', 'workflow.todos.reject', 'workflow.todos.return', 'workflow.todos.countersign', 'workflow.todos.transfer']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(true);
      expect(wrapper.find('[data-testid="workflow-todo-return"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-todo-countersign-submit"]').exists()).toBe(false);
      expect(listWorkflowTodoReturnTargets).not.toHaveBeenCalled(); expect(listWorkflowTodoCountersignCandidates).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });

  it('多人加签候选真实翻页并保留跨页选择', async () => {
    const first = '01912345-6789-7abc-8def-0123456789b3'; const second = '01912345-6789-7abc-8def-0123456789b4';
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, approvalModeKey: 'all', requiredApprovalCount: 2, pendingCount: 2 });
    vi.mocked(listWorkflowTodoCountersignCandidates).mockResolvedValueOnce({ items: [{ id: first, username: 'first', displayName: 'First' }], page: 1, pageSize: 20, total: 21 })
      .mockResolvedValueOnce({ items: [{ id: second, username: 'second', displayName: 'Second' }], page: 2, pageSize: 20, total: 21 });
    vi.mocked(countersignWorkflowTodo).mockResolvedValue({} as Awaited<ReturnType<typeof countersignWorkflowTodo>>);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.countersign']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-todo-countersign-assignees"]').setValue([first]);
      wrapper.findAllComponents(ElPagination).find(component => component.attributes('data-testid') === 'workflow-todo-countersign-pages')!.vm.$emit('current-change', 2);
      await flushPromises();
      expect(listWorkflowTodoCountersignCandidates).toHaveBeenLastCalledWith(detail.id, 2, 20, expect.any(AbortSignal));
      expect(wrapper.get('[data-testid="workflow-todo-countersign-assignees"]').findAll('option').map(option => option.attributes('value'))).toEqual([first, second]);
      await wrapper.get('[data-testid="workflow-todo-countersign-assignees"]').setValue([first, second]);
      await wrapper.get('[data-testid="workflow-todo-countersign-submit"]').trigger('click'); await flushPromises();
      expect(countersignWorkflowTodo).toHaveBeenCalledWith(detail.id, 'before', [first, second], detail.revision, null, expect.any(String));
    } finally { wrapper.unmount(); }
  });

  it('历史原待办可取消尚未结束的后加签，不枚举候选或开放新的审批动作', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({ ...detail, statusKey: 'completed' });
    vi.mocked(getWorkflowTodoCountersignChain).mockResolvedValue({ chainId: todo.id, directionKey: 'after', statusKey: 'active', items: [] });
    vi.mocked(cancelWorkflowTodoCountersign).mockResolvedValue({} as Awaited<ReturnType<typeof cancelWorkflowTodoCountersign>>);
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.countersign', 'workflow.todos.approve']);
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      expect(listWorkflowTodoCountersignCandidates).not.toHaveBeenCalled();
      expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="workflow-todo-countersign-submit"]').exists()).toBe(false);
      await wrapper.get('[data-testid="workflow-todo-countersign-cancel"]').trigger('click'); await flushPromises();
      expect(cancelWorkflowTodoCountersign).toHaveBeenCalledWith(detail.id, detail.revision, null, expect.any(String));
    } finally { wrapper.unmount(); }
  });

  it.each(['countersign', 'cancel'] as const)('当前 %s 成功后关闭详情、刷新列表并只提示一次', async action => {
    const execute = action === 'countersign' ? countersignWorkflowTodo : cancelWorkflowTodoCountersign;
    vi.mocked(execute).mockResolvedValue({} as Awaited<ReturnType<typeof approveWorkflowTodo>>);
    if (action === 'cancel') vi.mocked(getWorkflowTodoCountersignChain).mockResolvedValue({ chainId: todo.id, directionKey: 'before', statusKey: 'active', items: [] });
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
    const wrapper = mountWithPermissions(['workflow.todos.read', 'workflow.todos.countersign']);
    try {
      await flushPromises();
      await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click'); await flushPromises();
      if (action === 'countersign') await wrapper.get('[data-testid="workflow-todo-countersign-assignees"]').setValue(['01912345-6789-7abc-8def-0123456789b3']);
      const button = action === 'countersign' ? 'countersign-submit' : 'countersign-cancel';
      await wrapper.get(`[data-testid="workflow-todo-${button}"]`).trigger('click'); await flushPromises();
      expect(execute).toHaveBeenCalledTimes(1);
      expect(listMyWorkflowTodos).toHaveBeenCalledTimes(2);
      expect(success).toHaveBeenCalledTimes(1);
      expect(wrapper.findComponent({ name: 'WorkflowFormRenderer' }).exists()).toBe(false);
      expect(wrapper.get('section').attributes('aria-busy')).toBe('false');
    } finally { wrapper.unmount(); success.mockRestore(); }
  });

  it('无读取权限不发起列表、深链或筛选读取', async () => {
    const wrapper = mountWithPermissions([]); await flushPromises();
    await wrapper.vm.$router.push({ path: '/workflow/todos', query: { resourceId: todo.id } });
    await wrapper.get('form').trigger('submit'); await flushPromises();
    expect(listMyWorkflowTodos).not.toHaveBeenCalled();
    expect(getWorkflowTodo).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('卸载时取消列表读取并忽略迟到失败', async () => {
    const pending = deferred<ReturnType<typeof paged>>();
    vi.mocked(listMyWorkflowTodos).mockReturnValueOnce(pending.promise);
    const wrapper = mountWithPermissions(['workflow.todos.read']); await flushPromises();
    const signal = vi.mocked(listMyWorkflowTodos).mock.calls[0][1];
    wrapper.unmount();
    expect(signal?.aborted).toBe(true);
    pending.reject(new Error('卸载后列表失败')); await flushPromises();
    expect(getWorkflowTodo).not.toHaveBeenCalled();
  });

  it('审批与驳回按钮按独立权限失败关闭', async () => {
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.approve'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="workflow-todo-reject"]').exists()).toBe(false);
  });

  it('退回目标延迟返回后保留已填写的审批字段', async () => {
    let resolveTargets!: (value: Awaited<ReturnType<typeof listWorkflowTodoReturnTargets>>) => void;
    vi.mocked(listWorkflowTodoReturnTargets).mockReturnValue(new Promise(resolve => {
      resolveTargets = resolve;
    }));
    vi.mocked(getWorkflowTodo).mockResolvedValue({
      ...detail,
      formSchema: {
        ...detail.formSchema,
        sections: [{
          sectionKey: 'request',
          fields: [{
            fieldKey: 'decision',
            fieldTypeKey: 'text',
            required: true,
            constraints: {}
          }]
        }]
      },
      fieldPolicies: { decision: 'required' }
    });
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.approve',
      'workflow.todos.return'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    await wrapper.get('[data-field-key="decision"] input').setValue('同意');
    expect(wrapper.get('[data-testid="workflow-todo-approve"]').attributes('disabled')).toBeUndefined();

    resolveTargets([]);
    await flushPromises();
    expect(wrapper.get('[data-testid="workflow-todo-approve"]').attributes('disabled')).toBeUndefined();

    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    await flushPromises();
    expect(approveWorkflowTodo).toHaveBeenCalledWith(
      todo.id,
      todo.revision,
      detail.submissionRevision,
      { decision: '同意' },
      null,
      expect.any(String)
    );
  });

  it('多人审批详情展示权威票数和通过门槛', async () => {
    vi.mocked(getWorkflowTodo).mockResolvedValue({
      ...detail,
      approvalModeKey: 'nOfM',
      requiredApprovalCount: 2,
      approvedCount: 1,
      rejectedCount: 0,
      pendingCount: 2
    });
    const wrapper = mountWithPermissions(['workflow.todos.read']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[data-testid="workflow-approval-progress"]').text())
      .toContain('同意 1 · 驳回 0 · 待处理 2 · 通过门槛 2');
  });

  it('请求进行中阻止重复审批', async () => {
    let resolveApproval!: (value: Awaited<ReturnType<typeof approveWorkflowTodo>>) => void;
    vi.mocked(approveWorkflowTodo).mockReturnValue(new Promise(resolve => {
      resolveApproval = resolve;
    }));
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.approve'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    const button = wrapper.get('[data-testid="workflow-todo-approve"]');
    await button.trigger('click');
    await button.trigger('click');

    expect(approveWorkflowTodo).toHaveBeenCalledTimes(1);
    resolveApproval({
      id: todo.instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: detail.formVersionId,
      businessTitle: null,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'completed',
      revision: 5,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    await flushPromises();
  });

  it.each([403, 422])('服务端返回 %s 时保留详情并展示 ProblemDetails', async status => {
    vi.mocked(approveWorkflowTodo).mockRejectedValue({
      status,
      code: `workflow.test_${status}`,
      title: `error-${status}`,
      traceId: `trace-${status}`
    });
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.approve'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain(`workflow.test_${status}`);
    expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(true);
  });

  it('旧修订冲突后刷新权威待办并关闭已经失效的动作', async () => {
    vi.mocked(approveWorkflowTodo).mockRejectedValue({
      status: 409,
      code: 'workflow.todo_revision_conflict',
      title: '待办已被其他操作处理',
      traceId: 'trace-conflict'
    });
    vi.mocked(listMyWorkflowTodos)
      .mockResolvedValueOnce(paged([todo]))
      .mockResolvedValueOnce(paged([]));
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.approve'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-approve"]').trigger('click');
    await flushPromises();

    expect(listMyWorkflowTodos).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
    expect(wrapper.get('[role="alert"]').text()).toContain('workflow.todo_revision_conflict');
  });

  it('退回入口按独立权限展示并加载服务端合法目标', async () => {
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.return'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-todo-return"]').exists()).toBe(true);
    expect(listWorkflowTodoReturnTargets).toHaveBeenCalledWith(todo.id, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('manager-approval');
  });

  it('选择合法目标并填写原因后提交退回', async () => {
    const wrapper = mountWithPermissions([
      'workflow.todos.read',
      'workflow.todos.return'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    await wrapper.get('[data-testid="workflow-todo-return-target"]').setValue(
      '01912345-6789-7abc-8def-0123456789a3'
    );
    await wrapper.get('[data-testid="workflow-todo-comment"]').setValue('资料不完整');
    await wrapper.get('[data-testid="workflow-todo-return"]').trigger('click');
    await flushPromises();

    expect(returnWorkflowTodo).toHaveBeenCalledWith(
      todo.id,
      '01912345-6789-7abc-8def-0123456789a3',
      todo.revision,
      detail.submissionRevision,
      {},
      '资料不完整',
      expect.any(String)
    );
  });

  it('已办页签加载历史分页并只读展示动作快照', async () => {
    const wrapper = mountWithPermissions(['workflow.todos.read']);
    await flushPromises();

    const tabs = wrapper.findAll('[data-testid="workflow-todo-tabs"] .el-tabs__item');
    await tabs[1].trigger('click');
    await flushPromises();

    expect(listMyWorkflowTodoHistory).toHaveBeenCalled();
    await wrapper.get('[data-testid="workflow-todo-open"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[data-testid="workflow-todo-history-snapshot"]').text()).toContain('同意');
    expect(wrapper.find('[data-testid="workflow-todo-approve"]').exists()).toBe(false);
  });
});
