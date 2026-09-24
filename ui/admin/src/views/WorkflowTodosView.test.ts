import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowTodoDetail, WorkflowTodoListItemResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import {
  approveWorkflowTodo,
  getWorkflowTodo,
  listMyWorkflowTodoHistory,
  listWorkflowTodoReturnTargets,
  listMyWorkflowTodos,
  rejectWorkflowTodo,
  returnWorkflowTodo
} from '../api/workflow-todos';
import WorkflowTodosView from './WorkflowTodosView.vue';

vi.mock('../api/workflow-todos', () => ({
  approveWorkflowTodo: vi.fn(),
  getWorkflowTodo: vi.fn(),
  listMyWorkflowTodoHistory: vi.fn(),
  listWorkflowTodoReturnTargets: vi.fn(),
  listMyWorkflowTodos: vi.fn(),
  rejectWorkflowTodo: vi.fn(),
  returnWorkflowTodo: vi.fn()
}));

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
  statusKey: 'pending',
  revision: 3,
  formVersionId: '01912345-6789-7abc-8def-0123456789af',
  formSchemaHash: 'a'.repeat(64),
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
  return mount(WorkflowTodosView, { global: { plugins: [pinia] } });
}

describe('WorkflowTodosView', () => {
  beforeEach(() => {
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
    expect(listWorkflowTodoReturnTargets).toHaveBeenCalledWith(todo.id);
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
