import { describe, expect, it } from 'vitest';
import { HttpProblem } from '../src/api/problem-details';
import { classifyWorkflowTodoActionFailure } from '../src/features/workflow/workflow-todo-action-failure';

describe('workflow todo action failure', () => {
  it('refreshes stale todo state and rotates the idempotency key after a revision conflict', () => {
    expect(classifyWorkflowTodoActionFailure(new HttpProblem({
      status: 409,
      code: 'workflow.revision.conflict'
    }))).toEqual({
      feedbackKey: 'workflow.todo.revisionConflict',
      refreshTodo: true,
      retainIdempotencyKey: false
    });
  });

  it('fails closed and rotates the idempotency key when authority is rejected', () => {
    expect(classifyWorkflowTodoActionFailure(new HttpProblem({
      status: 403,
      code: 'authorization.forbidden'
    }))).toEqual({
      feedbackKey: 'workflow.todo.denied',
      refreshTodo: false,
      retainIdempotencyKey: false
    });
  });

  it('retains the idempotency key only for failures that may have reached the server', () => {
    expect(classifyWorkflowTodoActionFailure(new HttpProblem({
      status: 0,
      code: 'http.network_error'
    }))).toEqual({
      feedbackKey: 'workflow.todo.retryable',
      refreshTodo: false,
      retainIdempotencyKey: true
    });
    expect(classifyWorkflowTodoActionFailure(new HttpProblem({
      status: 503,
      code: 'common.unavailable'
    })).retainIdempotencyKey).toBe(true);
    expect(classifyWorkflowTodoActionFailure(new HttpProblem({
      status: 422,
      code: 'workflow.form.submission.invalid'
    })).retainIdempotencyKey).toBe(false);
  });
});
