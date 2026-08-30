import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getWorkflowTodo } from './workflow-todos';

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
});
