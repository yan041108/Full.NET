import { afterEach, describe, expect, it, vi } from 'vitest';
import { streamAiChatMessage } from './ai-chat';
import { configureAuthentication } from './http';

afterEach(() => { vi.unstubAllGlobals(); configureAuthentication(); });

describe('聊天 HTTP 与流错误契约', () => {
  it.each([403, 404, 409, 422])('HTTP %s 保留 ProblemDetails，不交给 SSE 解析器', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status, code: 'ai.chat.rejected', title: '请求被拒绝', traceId: 'chat-trace'
    }), { status, headers: { 'content-type': 'application/problem+json' } })));
    const handlers = { onDelta: vi.fn(), onDone: vi.fn(), onError: vi.fn() };
    await expect(streamAiChatMessage('session', { content: 'hello' }, handlers))
      .rejects.toMatchObject({ status, code: 'ai.chat.rejected', title: '请求被拒绝' });
    expect(handlers.onDelta).not.toHaveBeenCalled();
    expect(handlers.onDone).not.toHaveBeenCalled();
    expect(handlers.onError).not.toHaveBeenCalled();
  });

  it('已启动流中的错误交给错误回调，不能发出成功通知', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      'event: delta\ndata: {"delta":"部分文本"}\n\nevent: error\ndata: {"message":"生成失败"}\n\n',
      { headers: { 'content-type': 'text/event-stream' } }
    )));
    const handlers = { onDelta: vi.fn(), onDone: vi.fn(), onError: vi.fn() };
    await streamAiChatMessage('session', { content: 'hello' }, handlers);
    expect(handlers.onDelta).toHaveBeenCalledWith('部分文本');
    expect(handlers.onError).toHaveBeenCalledWith('生成失败');
    expect(handlers.onDone).not.toHaveBeenCalled();
  });
});
