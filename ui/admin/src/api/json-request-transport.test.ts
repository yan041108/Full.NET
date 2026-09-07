import { afterEach, describe, expect, it, vi } from 'vitest';
import { createAiChatSession } from './ai-chat';
import { createNotificationIntent } from './notification-intents';

afterEach(() => vi.unstubAllGlobals());

describe('手写 API 的 JSON 传输协议', () => {
  it.each([
    {
      path: '/api/v1/ai/chat/sessions',
      body: { modelConfigId: '01912345-6789-7abc-8def-0123456789ab' },
      send: (signal: AbortSignal) => createAiChatSession({ modelConfigId: '01912345-6789-7abc-8def-0123456789ab' }, signal),
      responseError: 'client.invalid_ai_chat_session'
    },
    {
      path: '/api/v1/notifications/intents',
      body: { producerKey: 'test', sceneKey: 'test', templateKey: 'test', recipients: [], parameters: {}, idempotencyKey: 'test-1' },
      send: (signal: AbortSignal) => createNotificationIntent({
        producerKey: 'test', sceneKey: 'test', templateKey: 'test', recipients: [], parameters: {}, idempotencyKey: 'test-1'
      }, signal),
      responseError: 'client.invalid_notification_intent_response'
    }
  ])('$path 发送 JSON、声明媒体类型并透传取消信号', async ({ path, body, send, responseError }) => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('null', {
      status: 200, headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    // 故意返回非法响应，同时验证请求已按 Fetch 协议发出且响应守卫仍失败关闭。
    await expect(send(controller.signal)).rejects.toThrow(responseError);

    const [actualPath, init] = fetchMock.mock.calls[0]!;
    expect(actualPath).toBe(path);
    expect(init?.body).toBe(JSON.stringify(body));
    expect(new Headers(init?.headers).get('content-type')).toBe('application/json');
    expect(init?.signal).toBe(controller.signal);
  });
});
