import { expect, it } from 'vitest';
import { isAiAgentReconciliationResponse } from '../src/ai-agent-reconciliation';

const row = { runId: '00000000-0000-0000-0000-000000000001', version: 8, statusKey: 'reconciliation_required', usageStatus: 'unknown',
  reservedTokens: 200, quotaMonthKey: '2026-09', inputTokens: null, outputTokens: null, cachedInputTokens: null, canReconcile: true };
it('未知用量按 null 保留，不伪造零计量', () => expect(isAiAgentReconciliationResponse(row)).toBe(true));
it.each([{ version: -1 }, { reservedTokens: NaN }, { inputTokens: -1 }, { usageStatus: 'complete' }, { quotaMonthKey: '2026-13' },
  { inputTokens: 1, cachedInputTokens: 2 }, { canReconcile: 'true' }, { runId: 'fake' }])('拒绝畸形账本字段 %j', patch => {
  expect(isAiAgentReconciliationResponse({ ...row, ...patch })).toBe(false);
});
