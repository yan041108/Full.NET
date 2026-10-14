import type {
  AiAgentReconciliationResponse as GeneratedResponse,
  ReconcileAiAgentRunRequest as GeneratedRequest
} from './generated/models.generated.js';
import { readAiAgentReconciliationResponse as readGeneratedResponse } from './generated/guards.generated.js';

/** 线协议来自真实 OpenAPI；业务适配层只收紧运行身份及计量一致性。 */
export type AiAgentReconciliationResponse = GeneratedResponse;

/** 对账数字来自已核证材料，省略时继续保留未知预留；不授予重新执行。 */
export type ReconcileAiAgentRunRequest = GeneratedRequest;

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const tokens = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const optionalTokens = (value: unknown): value is number | null => value === null || tokens(value);

/** 对账快照采用运行身份及闭合计量状态校验，避免把未知/畸形数据显示为可退款用量。 */
export function isAiAgentReconciliationResponse(value: unknown): value is AiAgentReconciliationResponse {
  try { readGeneratedResponse(value); } catch { return false; }
  const row = value as Record<string, unknown>;
  return typeof row.runId === 'string' && uuid.test(row.runId) && tokens(row.version) && row.version > 0
    && typeof row.statusKey === 'string' && ['reserved', 'unknown', 'known'].includes(String(row.usageStatus))
    && tokens(row.reservedTokens) && typeof row.quotaMonthKey === 'string' && /^\d{4}-(0[1-9]|1[0-2])$/.test(row.quotaMonthKey)
    && optionalTokens(row.inputTokens) && optionalTokens(row.outputTokens) && optionalTokens(row.cachedInputTokens)
    && (row.usageStatus === 'known'
      ? row.inputTokens !== null && row.outputTokens !== null
      : row.inputTokens === null && row.outputTokens === null && row.cachedInputTokens === null)
    && (row.cachedInputTokens === null || row.inputTokens !== null && (row.cachedInputTokens as number) <= (row.inputTokens as number))
    && typeof row.canReconcile === 'boolean';
}
