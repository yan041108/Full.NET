import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getWorkflowTodo,
  getWorkflowTodoCountersignChain,
  countersignWorkflowTodo,
  cancelWorkflowTodoCountersign,
  listWorkflowTodoReturnTargets,
  returnWorkflowTodo
} from './workflow-todos';

vi.mock('./http', () => ({
  http: { request: vi.fn() }
}));

const runtimeDetail = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  instanceId: '01912345-6789-7abc-8def-0123456789ac',
  stepId: '01912345-6789-7abc-8def-0123456789ad',
  assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
  statusKey: 'pending',
  revision: 3,
  approvalModeKey: 'any',
  approvedCount: 0,
  pendingCount: 1,
  rejectedCount: 0,
  requiredApprovalCount: 1,
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
  submissionRevision: 4
};

describe('workflow todo 管理 API', () => {
  beforeEach(() => vi.mocked(http.request).mockReset());

  it('加签链使用生成协议与业务守卫，创建和取消都保留真实回执', async () => {
    const chain = { chainId: runtimeDetail.instanceId, directionKey: 'before', statusKey: 'active', items: [{
      itemId: runtimeDetail.stepId, sequenceNo: 1, assigneeUserId: runtimeDetail.assigneeUserId, statusKey: 'active', todoId: runtimeDetail.id }] };
    const receipt = { id: runtimeDetail.instanceId, definitionVersionId: runtimeDetail.stepId, formVersionId: runtimeDetail.formVersionId,
      businessType: 'counter', businessId: 'one', businessTitle: null, statusKey: 'active', revision: 4, activeTodoId: null, startedAtUtc: '2026-10-26T00:00:00Z' };
    vi.mocked(http.request).mockResolvedValueOnce(chain).mockResolvedValueOnce(receipt).mockResolvedValueOnce(receipt);
    expect(await getWorkflowTodoCountersignChain(runtimeDetail.id)).toEqual(chain);
    expect(await countersignWorkflowTodo(runtimeDetail.id, 'before', [runtimeDetail.assigneeUserId], 3, '咨询', 'counter')).toEqual(receipt);
    expect(await cancelWorkflowTodoCountersign(runtimeDetail.id, 3, '取消', 'cancel')).toEqual(receipt);
    expect(http.request).toHaveBeenNthCalledWith(2, `/api/v1/workflow/todos/${runtimeDetail.id}/countersign`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: expect.any(String) }, undefined);
    expect(http.request).toHaveBeenNthCalledWith(3, `/api/v1/workflow/todos/${runtimeDetail.id}/countersign/cancel`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: expect.any(String) }, undefined);
    expect(JSON.parse(vi.mocked(http.request).mock.calls[1]![1]!.body as string)).toEqual({ assigneeUserIds: [runtimeDetail.assigneeUserId], comment: '咨询', directionKey: 'before', expectedRevision: 3, idempotencyKey: 'counter' });
    expect(JSON.parse(vi.mocked(http.request).mock.calls[2]![1]!.body as string)).toEqual({ comment: '取消', expectedRevision: 3, idempotencyKey: 'cancel' });
  });

  it('读取待办时使用包含表单哈希的运行时端点', async () => {
    vi.mocked(http.request).mockResolvedValue(runtimeDetail);

    await expect(getWorkflowTodo(runtimeDetail.id)).resolves.toEqual(runtimeDetail);

    expect(http.request).toHaveBeenCalledWith(
      `/api/v1/workflow/todos/${runtimeDetail.id}/runtime`,
      { method: 'GET' },
      undefined
    );
  });

  it('读取退回目标时调用当前待办的合法目标端点', async () => {
    vi.mocked(http.request).mockResolvedValue([]);

    await expect(listWorkflowTodoReturnTargets(runtimeDetail.id)).resolves.toEqual([]);

    expect(http.request).toHaveBeenCalledWith(
      `/api/v1/workflow/todos/${runtimeDetail.id}/return-targets?page=1&pageSize=100`,
      { method: 'GET' },
      undefined
    );
  });

  it('多人退回目标允许没有历史代表人，耐久回执允许没有即时待办', async () => {
    const target = { stepId: runtimeDetail.stepId, nodeKey: 'group', assigneeUserId: null, completedAtUtc: '2026-10-25T00:00:00Z' };
    vi.mocked(http.request).mockResolvedValueOnce([target]);
    await expect(listWorkflowTodoReturnTargets(runtimeDetail.id)).resolves.toEqual([target]);
    const receipt = { id: runtimeDetail.instanceId, definitionVersionId: runtimeDetail.stepId, formVersionId: runtimeDetail.formVersionId,
      businessType: 'purchase', businessId: 'PO-001', businessTitle: null, statusKey: 'active', revision: 4, activeTodoId: null,
      dueAtUtc: null, reminderCount: 0, escalatedAtUtc: null, startedAtUtc: '2026-10-25T00:00:00Z' };
    vi.mocked(http.request).mockResolvedValueOnce(receipt);
    await expect(returnWorkflowTodo(runtimeDetail.id, target.stepId, 3, runtimeDetail.submissionRevision, {}, '重新办理', 'durable-return')).resolves.toEqual(receipt);
  });

  it('退回动作携带目标、修订、补丁、原因和幂等键', async () => {
    vi.mocked(http.request).mockResolvedValue({
      id: runtimeDetail.instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789a2',
      formVersionId: runtimeDetail.formVersionId,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789a3',
      businessTitle: null,
      dueAtUtc: null,
      reminderCount: 0,
      escalatedAtUtc: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    const targetStepId = '01912345-6789-7abc-8def-0123456789a1';

    await returnWorkflowTodo(
      runtimeDetail.id,
      targetStepId,
      3,
      runtimeDetail.submissionRevision,
      { summary: '补充材料' },
      '资料不完整',
      'return-001'
    );

    expect(http.request).toHaveBeenCalledWith(
      `/api/v1/workflow/todos/${runtimeDetail.id}/return`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          targetStepId,
          expectedRevision: 3,
          expectedFormRevision: runtimeDetail.submissionRevision,
          fieldPatch: { summary: '补充材料' },
          comment: '资料不完整',
          idempotencyKey: 'return-001'
        })
      },
      undefined
    );
  });
});
