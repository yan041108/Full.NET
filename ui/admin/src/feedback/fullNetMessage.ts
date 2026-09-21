import { ElMessage, type MessageOptions } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';

/** 与 Element Plus Message 类型对齐的轻量反馈级别。 */
export type FullNetFeedbackLevel = 'success' | 'warning' | 'error' | 'info';

const DEFAULT_MESSAGE_OPTIONS: Partial<MessageOptions> = {
  placement: 'top',
  showClose: true
};

/** 将 ProblemDetails HTTP 状态映射为 Message 级别。 */
export function resolveProblemFeedbackLevel(
  problem: FullNetProblemDetails
): FullNetFeedbackLevel {
  const status = problem.status;
  if (status >= 500) {
    return 'error';
  }

  if (status === 401 || status === 403 || status === 404) {
    return 'error';
  }

  if (status === 409 || status === 429) {
    return 'warning';
  }

  if (status >= 400) {
    return 'warning';
  }

  return 'error';
}

/** 面向用户的错误文案，不暴露 traceId 等技术字段。 */
export function formatProblemMessage(problem: FullNetProblemDetails): string {
  if (problem.title && problem.title.length > 0) {
    return problem.title;
  }

  if (problem.detail && problem.detail.length > 0) {
    return problem.detail;
  }

  return problem.code;
}

function showMessage(
  level: FullNetFeedbackLevel,
  message: string,
  options?: Partial<MessageOptions>
): void {
  ElMessage({
    ...DEFAULT_MESSAGE_OPTIONS,
    type: level,
    message,
    ...options
  });
}

export function showSuccess(
  message: string,
  options?: Partial<MessageOptions>
): void {
  showMessage('success', message, options);
}

export function showWarning(
  message: string,
  options?: Partial<MessageOptions>
): void {
  showMessage('warning', message, options);
}

export function showError(
  message: string,
  options?: Partial<MessageOptions>
): void {
  showMessage('error', message, options);
}

export function showInfo(
  message: string,
  options?: Partial<MessageOptions>
): void {
  showMessage('info', message, options);
}

/** 展示 API/会话层抛出的 ProblemDetails 或未知错误。 */
export function showProblem(error: unknown, fallbackMessage: string): void {
  if (isFullNetProblemDetails(error)) {
    showMessage(
      resolveProblemFeedbackLevel(error),
      formatProblemMessage(error)
    );
    return;
  }

  showError(fallbackMessage);
}
