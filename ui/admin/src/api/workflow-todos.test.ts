import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getWorkflowTodo,
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
  submissionRevision: 4
};

describe('workflow todo 管理 API', () => {
  beforeEach(() => vi.mocked(http.request).mockReset());

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
      activeTodoDueAtUtc: null,
      activeTodoTimeoutStatusKey: null,
      activeTodoReminderCount: 0,
      activeTodoEscalatedAtUtc: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    const targetStepId = '01912345-6789-7abc-8def-0123456789a1';

    await returnWorkflowTodo(
      runtimeDetail.id,
      targetStepId,
      3,
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
          fieldPatch: { summary: '补充材料' },
          comment: '资料不完整',
          idempotencyKey: 'return-001'
        })
      },
      undefined
    );
  });
});
