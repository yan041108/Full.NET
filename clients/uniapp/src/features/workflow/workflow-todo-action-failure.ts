import { HttpProblem } from '../../api/problem-details';

export type WorkflowTodoActionFeedbackKey =
  | 'workflow.todo.denied'
  | 'workflow.todo.failed'
  | 'workflow.todo.retryable'
  | 'workflow.todo.revisionConflict';

export interface WorkflowTodoActionFailure {
  readonly feedbackKey: WorkflowTodoActionFeedbackKey;
  readonly refreshTodo: boolean;
  readonly retainIdempotencyKey: boolean;
}

/**
 * 将审批失败转换为页面恢复策略；只有服务端可能已接收的瞬态失败才复用原幂等键。
 */
export function classifyWorkflowTodoActionFailure(error: unknown): WorkflowTodoActionFailure {
  if (!(error instanceof HttpProblem)) {
    return failure('workflow.todo.failed', false);
  }
  if (error.status === 409 && error.code === 'workflow.revision.conflict') {
    return {
      feedbackKey: 'workflow.todo.revisionConflict',
      refreshTodo: true,
      retainIdempotencyKey: false
    };
  }
  if (error.status === 403) {
    return failure('workflow.todo.denied', false);
  }
  if (error.status === 0 || error.status >= 500) {
    return failure('workflow.todo.retryable', true);
  }
  return failure('workflow.todo.failed', false);
}

function failure(
  feedbackKey: WorkflowTodoActionFeedbackKey,
  retainIdempotencyKey: boolean
): WorkflowTodoActionFailure {
  return {
    feedbackKey,
    refreshTodo: false,
    retainIdempotencyKey
  };
}
