import {withTestRun} from './test-run-context.mjs';

/** 编译只持有工作区锁；正式验收取得重型资源后必须严格复核，禁止隐式补编译。 */
export async function withPreparedTestRun({prepare, ...options}, action) {
  const budget = options.timeoutMs ?? 30 * 60_000;
  if (!Number.isFinite(budget) || budget <= 0) throw new Error('测试排队预算必须为有限正数。');
  const queuedAt = performance.now();
  let preparationStartedAt;
  const prepared = await withTestRun({...options, heavy: false, timeoutMs: budget}, async () => {
    preparationStartedAt = performance.now();
    return prepare('reuse');
  });
  // 两阶段共用排队预算，编译耗时独立；不能叠加两轮完整等待期限。
  const remaining = budget - (preparationStartedAt - queuedAt);
  if (remaining <= 0) throw new Error('等待测试资源超时：准备阶段已耗尽排队预算。');
  return withTestRun({...options, timeoutMs: remaining}, async () => {
    await prepare('verify');
    return action(prepared);
  });
}
