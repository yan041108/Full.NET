import {
  isAiChatSession,
  isAiChatSessionPage,
  type AiChatSession,
  type AiChatSessionListQuery,
  type AiChatSessionPage,
  type CreateAiChatSessionRequest,
  type StreamAiChatMessageRequest,
  type UpdateAiChatSessionRequest,
  type AiChatStreamDeltaEvent,
  type AiChatStreamDoneEvent,
  type AiChatStreamErrorEvent
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, readProblemDetails } from '@fullnet/client-contracts';
import { request, requestResponse } from './http';

export interface AiChatStreamHandlers {
  onDelta: (delta: string) => void;
  onDone: (event: AiChatStreamDoneEvent) => void;
  onError: (message: string) => void;
}

function buildListQuery(query: AiChatSessionListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 50));
  return params.toString();
}

export async function listAiChatSessions(
  query: AiChatSessionListQuery = {},
  signal?: AbortSignal
): Promise<AiChatSessionPage> {
  const value = await request<unknown>(
    `/api/v1/ai/chat/sessions?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiChatSessionPage(value)) {
    throw new Error('client.invalid_ai_chat_session_page');
  }
  return value;
}

export async function getAiChatSession(
  id: string,
  signal?: AbortSignal
): Promise<AiChatSession> {
  const value = await request<unknown>(
    `/api/v1/ai/chat/sessions/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiChatSession(value)) {
    throw new Error('client.invalid_ai_chat_session');
  }
  return value;
}

export async function createAiChatSession(
  body: CreateAiChatSessionRequest,
  signal?: AbortSignal
): Promise<AiChatSession> {
  const value = await request<unknown>(
    '/api/v1/ai/chat/sessions',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiChatSession(value)) {
    throw new Error('client.invalid_ai_chat_session');
  }
  return value;
}

export async function updateAiChatSession(
  id: string,
  body: UpdateAiChatSessionRequest,
  signal?: AbortSignal
): Promise<AiChatSession> {
  const value = await request<unknown>(
    `/api/v1/ai/chat/sessions/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiChatSession(value)) {
    throw new Error('client.invalid_ai_chat_session');
  }
  return value;
}

export async function deleteAiChatSession(
  id: string,
  signal?: AbortSignal
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/ai/chat/sessions/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
  return value === true;
}

export async function cancelAiChatGeneration(
  sessionId: string,
  signal?: AbortSignal
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/ai/chat/sessions/${encodeURIComponent(sessionId)}/cancel`,
    { method: 'POST' },
    signal
  );
  return value === true;
}

export async function streamAiChatMessage(
  sessionId: string,
  body: StreamAiChatMessageRequest,
  handlers: AiChatStreamHandlers,
  signal?: AbortSignal
): Promise<void> {
  const response = await requestResponse(
    `/api/v1/ai/chat/sessions/${encodeURIComponent(sessionId)}/messages/stream`,
    {
      method: 'POST',
      headers: {
        accept: 'text/event-stream',
        'content-type': 'application/json'
      },
      body: JSON.stringify(body)
    },
    signal
  );

  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (!response.body) {
    throw new Error('client.invalid_ai_chat_stream');
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
        const payload = line.slice('data:'.length).trim();
        if (payload.length > 0) {
          dispatchStreamEvent(eventName, payload, handlers);
        }
      }
      lineBreakIndex = buffer.indexOf('\n');
    }
  }
}

function dispatchStreamEvent(
  eventName: string,
  payload: string,
  handlers: AiChatStreamHandlers
): void {
  try {
    const data = JSON.parse(payload) as unknown;
    if (eventName === 'delta') {
      const delta = data as AiChatStreamDeltaEvent;
      handlers.onDelta(delta.delta);
      return;
    }
    if (eventName === 'done') {
      handlers.onDone(data as AiChatStreamDoneEvent);
      return;
    }
    if (eventName === 'error') {
      const error = data as AiChatStreamErrorEvent;
      handlers.onError(error.message);
    }
  } catch (error: unknown) {
    const message = isFullNetProblemDetails(error)
      ? error.title ?? error.code
      : 'client.invalid_ai_chat_stream_event';
    handlers.onError(message);
  }
}
