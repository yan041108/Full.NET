import { describe, expect, it, vi } from 'vitest';
import workflowFixture from '../../../packages/client-contracts/src/fixtures/workflow-form-schema-v1.json';
import type { HttpClient } from '../src/api/http';
import { createWorkflowTodoClient } from '../src/features/workflow/workflow-todo-client';
import type { WorkflowSchemaCache } from '../src/features/workflow/workflow-schema-cache';

const todoId = '01912345-6789-7abc-8def-0123456789ab';
const formVersionId = '01912345-6789-7abc-8def-0123456789af';

function todoDetail() {
  return {
    id: todoId,
    instanceId: '01912345-6789-7abc-8def-0123456789ac',
    stepId: '01912345-6789-7abc-8def-0123456789ad',
    assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
    statusKey: 'active',
    revision: 3,
    formVersionId,
    formSchemaHash: workflowFixture.contentHash,
    formSchema: workflowFixture.formSchema,
    submission: workflowFixture.submission,
    fieldPolicies: workflowFixture.fieldPolicies,
    submissionRevision: 4
  };
}

describe('workflow todo client', () => {
  it('guards todo detail and caches only the visible immutable schema', async () => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue(todoDetail())
    };
    const cache: WorkflowSchemaCache = {
      read: vi.fn(),
      write: vi.fn()
    };
    const client = createWorkflowTodoClient(http, cache);

    const detail = await client.get(todoId);

    expect(detail.formSchemaHash).toBe(workflowFixture.contentHash);
    expect(http.request).toHaveBeenCalledWith({
      path: `/api/v1/workflow/todos/${todoId}/runtime`
    });
    expect(cache.write).toHaveBeenCalledWith(
      formVersionId,
      workflowFixture.contentHash,
      detail.formSchema
    );
  });

  it('fails closed when the API returns an invalid schema hash', async () => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue({
        ...todoDetail(),
        formSchemaHash: 'invalid'
      })
    };
    const cache: WorkflowSchemaCache = {
      read: vi.fn(),
      write: vi.fn()
    };

    await expect(createWorkflowTodoClient(http, cache).get(todoId))
      .rejects.toThrow('workflow.todo.invalid-detail');
    expect(cache.write).not.toHaveBeenCalled();
  });

  it('sends the minimal action contract without adding tenant or authority fields', async () => {
    const http: HttpClient = {
      request: vi.fn().mockResolvedValue({
        id: '01912345-6789-7abc-8def-0123456789b0',
        definitionVersionId: '01912345-6789-7abc-8def-0123456789b1',
        formVersionId,
        businessType: 'leave',
        businessId: 'leave-1',
        statusKey: 'completed',
        revision: 4,
        activeTodoId: null,
        startedAtUtc: '2026-08-30T00:00:00Z'
      })
    };
    const cache: WorkflowSchemaCache = { read: vi.fn(), write: vi.fn() };

    await createWorkflowTodoClient(http, cache).approve(todoId, {
      expectedRevision: 3,
      fieldPatch: { decision: 'approved' },
      comment: 'ok',
      idempotencyKey: 'mobile-approve-1'
    });

    expect(http.request).toHaveBeenCalledWith({
      path: `/api/v1/workflow/todos/${todoId}/approve`,
      method: 'POST',
      data: {
        expectedRevision: 3,
        fieldPatch: { decision: 'approved' },
        comment: 'ok',
        idempotencyKey: 'mobile-approve-1'
      }
    });
  });
});
