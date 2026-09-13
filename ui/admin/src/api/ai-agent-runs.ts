import {
  isAiAgentRunResponse,
  isCreateAiAgentRunResponse,
  type AgUiRunProgressState,
  type AiAgentRunResponse,
  type CreateAiAgentRunRequest,
  type CreateAiAgentRunResponse
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, readProblemDetails } from '@fullnet/client-contracts';
import { request, requestResponse } from './http';

export type AgUiStreamEvent = {
  eventType: string;
  payload: unknown;
};

export interface AgUiAgentRunStreamOptions {
  afterSequence?: number;
  onEvent: (event: AgUiStreamEvent) => void;
  onError: (message: string) => void;
}

export async function createAiAgentRun(
  body: CreateAiAgentRunRequest,
  signal?: AbortSignal
): Promise<CreateAiAgentRunResponse> {
  const value = await request<unknown>(
    '/api/v1/ai/agent/runs',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isCreateAiAgentRunResponse(value)) {
    throw new Error('client.invalid_ai_agent_run_create_response');
  }
  return value;
}

export async function getAiAgentRun(
  runId: string,
  signal?: AbortSignal
): Promise<AiAgentRunResponse> {
  const value = await request<unknown>(
    `/api/v1/ai/agent/runs/${encodeURIComponent(runId)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiAgentRunResponse(value)) {
    throw new Error('client.invalid_ai_agent_run_response');
  }
  return value;
}

export async function cancelAiAgentRun(
  runId: string,
  signal?: AbortSignal
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/ai/agent/runs/${encodeURIComponent(runId)}/cancel`,
    { method: 'POST' },
    signal
  );
  return value === true;
}

export async function resumeAiAgentRun(
  runId: string,
  signal?: AbortSignal
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/ai/agent/runs/${encodeURIComponent(runId)}/resume`,
    { method: 'POST' },
    signal
  );
  return value === true;
}

export async function streamAiAgentRunEvents(
  runId: string,
  handlers: AgUiAgentRunStreamOptions,
  signal?: AbortSignal
): Promise<void> {
  const params = new URLSearchParams();
  if (handlers.afterSequence !== undefined) {
    params.set('afterSequence', String(handlers.afterSequence));
  }
  const query = params.toString();
  const response = await requestResponse(
    `/api/v1/ai/agent/runs/${encodeURIComponent(runId)}/events/stream${query ? `?${query}` : ''}`,
    {
      method: 'GET',
      headers: { accept: 'text/event-stream' }
    },
    signal
  );

  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (!response.body) {
    throw new Error('client.invalid_ai_agent_run_stream');
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let eventName = 'message';

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    let lineBreakIndex = buffer.indexOf('\n');
    while (lineBreakIndex >= 0) {
      const line = buffer.slice(0, lineBreakIndex).trimEnd();
      buffer = buffer.slice(lineBreakIndex + 1);
      if (line.startsWith('event:')) {
        eventName = line.slice('event:'.length).trim();
      } else if (line.startsWith('data:')) {
        const payloadText = line.slice('data:'.length).trim();
        if (payloadText.length > 0) {
          dispatchAgUiEvent(eventName, payloadText, handlers);
        }
      }
      lineBreakIndex = buffer.indexOf('\n');
    }
  }
}

function dispatchAgUiEvent(
  eventName: string,
  payloadText: string,
  handlers: AgUiAgentRunStreamOptions
): void {
  try {
    const payload = JSON.parse(payloadText) as unknown;
    handlers.onEvent({ eventType: eventName, payload });
  } catch (error: unknown) {
    const message = isFullNetProblemDetails(error)
      ? error.title ?? error.code
      : 'client.invalid_ai_agent_run_stream_event';
    handlers.onError(message);
  }
}

export function parseAgUiStateSnapshot(payload: unknown): AgUiRunProgressState | undefined {
  if (typeof payload !== 'object' || payload === null) {
    return undefined;
  }
  const record = payload as Record<string, unknown>;
  const snapshot = record.snapshot;
  if (typeof snapshot !== 'object' || snapshot === null) {
    return undefined;
  }
  const state = snapshot as Record<string, unknown>;
  if (typeof state.statusKey !== 'string' || typeof state.definitionKey !== 'string') {
    return undefined;
  }
  const steps = Array.isArray(state.steps) ? state.steps : [];
  return {
    statusKey: state.statusKey,
    definitionKey: state.definitionKey,
    budget: typeof state.budget === 'object' && state.budget !== null
      ? {
          inputTokens: readNullableNumber((state.budget as Record<string, unknown>).inputTokens),
          outputTokens: readNullableNumber((state.budget as Record<string, unknown>).outputTokens),
          usageStatus: readNullableString((state.budget as Record<string, unknown>).usageStatus),
          outcome: readNullableString((state.budget as Record<string, unknown>).outcome)
        }
      : null,
    steps: steps.map(step => {
      const item = step as Record<string, unknown>;
      return {
        stepKey: String(item.stepKey ?? ''),
        attempt: Number(item.attempt ?? 0),
        statusKey: String(item.statusKey ?? ''),
        inputTokens: readNullableNumber(item.inputTokens),
        outputTokens: readNullableNumber(item.outputTokens),
        errorCode: readNullableString(item.errorCode)
      };
    })
  };
}

function readNullableNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null;
}

function readNullableString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}
