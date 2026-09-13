export interface CreateAiAgentRunRequest {
  clientRequestId: string;
  definitionKey: string;
  modelConfigId: string;
  prompt: string;
  inputTokenLimit: number;
  outputTokenLimit: number;
}

export interface CreateAiAgentRunResponse {
  runId: string;
}

export interface AiAgentRunResponse {
  id: string;
  statusKey: string;
  definitionKey: string;
  definitionVersion: number;
  deadlineAtUtc: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface AgUiRunProgressState {
  statusKey: string;
  definitionKey: string;
  budget?: {
    inputTokens: number | null;
    outputTokens: number | null;
    usageStatus: string | null;
    outcome: string | null;
  } | null;
  steps: Array<{
    stepKey: string;
    attempt: number;
    statusKey: string;
    inputTokens: number | null;
    outputTokens: number | null;
    errorCode: string | null;
  }>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isCreateAiAgentRunResponse(value: unknown): value is CreateAiAgentRunResponse {
  return isRecord(value) && isGuid(value.runId);
}

export function isAiAgentRunResponse(value: unknown): value is AiAgentRunResponse {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.statusKey === 'string'
    && typeof value.definitionKey === 'string'
    && typeof value.definitionVersion === 'number'
    && typeof value.deadlineAtUtc === 'string'
    && typeof value.createdAtUtc === 'string'
    && typeof value.updatedAtUtc === 'string';
}
