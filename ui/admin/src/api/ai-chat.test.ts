import { afterEach, describe, expect, it, vi } from 'vitest';
import { askAiKnowledge, streamAiChatMessage } from './ai-chat';
import * as chatApi from './ai-chat';
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

// 公开 Ask 的入口必须能够独立调用，不能误用普通聊天 SSE。
it('公开知识问答客户端入口存在', () => {
  expect(chatApi).toHaveProperty('askAiKnowledge');
});

const citation = { id: '019f0000-0000-7000-8000-000000000001', documentId: '019f0000-0000-7000-8000-000000000002',
  versionId: '019f0000-0000-7000-8000-000000000003', sequence: 0, pageNumber: null, offset: 0, length: 12 };
const answer = { refused: false, message: { id: citation.id, sessionId: citation.documentId, roleKey: 'assistant',
  content: '已核对答案', statusKey: 'completed', promptTokens: null, completionTokens: null, createdAtUtc: '2026-10-07T00:00:00Z',
  knowledgeUnavailableCode: null, citations: [citation] } };
const askBody = { knowledgeBaseIds: [citation.documentId], embeddingModelConfigId: citation.versionId, question: '来源说明' };

describe('公开知识问答调用契约', () => {
  it.each([false, true])('返回完整答案或拒答 refused=%s', async refused => {
    const expected = { refused, message: { ...answer.message, content: refused ? '' : answer.message.content, citations: refused ? [] : [citation] } };
    const fetchMock = vi.fn().mockResolvedValue(Response.json(expected));
    vi.stubGlobal('fetch', fetchMock);
    const signal = new AbortController().signal;
    expect(await askAiKnowledge('session/name', askBody, signal)).toEqual(expected);
    expect(fetchMock.mock.calls[0]?.[0]).toContain('/sessions/session%2Fname/knowledge/ask');
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ method: 'POST', body: JSON.stringify(askBody), signal });
  });
  it.each([
    { ...answer, refused: true },
    { ...answer, message: { ...answer.message, statusKey: 'streaming' } },
    { ...answer, message: { ...answer.message, citations: [] } },
    { ...answer, message: { ...answer.message, citations: [{ ...citation, offset: -1 }] } },
    { ...answer, message: { ...answer.message, citations: [{ ...citation, pageNumber: 0 }] } },
    { ...answer, message: { ...answer.message, citations: [citation, citation] } },
    { ...answer, message: { ...answer.message, knowledgeUnavailableCode: 'ai.knowledge.answer_unavailable' } }
  ])('拒绝未完成或不完整引用的响应 %#', async value => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(value)));
    await expect(askAiKnowledge('session', askBody)).rejects.toThrow('client.invalid_ai_knowledge_answer');
  });
  it.each([403, 409, 422])('Ask HTTP %s 保留 ProblemDetails', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ status, code: 'ai.knowledge.answer_unavailable', title: '来源不可用' },
      { status, headers: { 'content-type': 'application/problem+json' } })));
    await expect(askAiKnowledge('session', askBody)).rejects.toMatchObject({ status, code: 'ai.knowledge.answer_unavailable' });
  });
});
