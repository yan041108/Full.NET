import type { CodeGenerationRunResponse } from './code-generation-runs.js';

/**
 * 计算从工作区栈顶到目标 Apply 的 LIFO 回滚标识序列。
 */
export function buildCodeGenerationRollbackApplyRunIds(
  runs: readonly CodeGenerationRunResponse[],
  targetApplyRunId: string
): string[] {
  const rolledBackApplyRunIds = new Set(
    runs
      .filter(run =>
        run.operationKind === 'rollback'
        && run.status === 'succeeded'
        && run.sourceApplyRunId)
      .map(run => run.sourceApplyRunId as string)
  );
  const target = runs.find(run => run.id === targetApplyRunId);
  if (!target
    || target.operationKind !== 'apply'
    || target.status !== 'succeeded'
    || !target.moduleKey
    || !target.entityKey) {
    throw new Error('client.invalid_code_generation_rollback_chain');
  }

  const pending = runs
    .filter(run =>
      run.operationKind === 'apply'
      && run.status === 'succeeded'
      && run.moduleKey === target.moduleKey
      && run.entityKey === target.entityKey
      && !rolledBackApplyRunIds.has(run.id))
    .sort(comparePendingApplyRuns);
  const index = pending.findIndex(run => run.id === targetApplyRunId);
  if (index < 0) {
    throw new Error('client.invalid_code_generation_rollback_chain');
  }

  return pending.slice(0, index + 1).map(run => run.id);
}

export function isPendingCodeGenerationRollbackApply(
  runs: readonly CodeGenerationRunResponse[],
  run: CodeGenerationRunResponse
): boolean {
  if (run.operationKind !== 'apply' || run.status !== 'succeeded') {
    return false;
  }

  try {
    buildCodeGenerationRollbackApplyRunIds(runs, run.id);
    return true;
  } catch {
    return false;
  }
}

function comparePendingApplyRuns(
  left: CodeGenerationRunResponse,
  right: CodeGenerationRunResponse
): number {
  const byTime = Date.parse(right.startedAtUtc)
    - Date.parse(left.startedAtUtc);
  if (byTime !== 0) {
    return byTime;
  }

  if (left.id < right.id) {
    return 1;
  }

  if (left.id > right.id) {
    return -1;
  }

  return 0;
}