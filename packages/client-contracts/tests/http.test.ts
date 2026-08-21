import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('headless HTTP 客户端', () => {
  it('失败响应抛出稳定 ProblemDetails 错误码', async () => {
    const http = createHttpClient();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-shared'
    }), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' }
    })));

    await expect(http.request('/api/v1/me')).rejects.toMatchObject({
      code: 'authorization.denied',
      traceId: 'trace-shared'
    });
  });

  it('损坏错误响应降级为统一错误而不泄露解析异常', async () => {
    const http = createHttpClient();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{broken', {
      status: 502,
      statusText: 'Bad Gateway',
      headers: { 'content-type': 'application/problem+json' }
    })));

    await expect(http.request('/api/v1/me')).rejects.toEqual({
      status: 502,
      code: 'http.unexpected_response',
      title: 'Bad Gateway'
    });
  });

  it('并发 401 只刷新一次并各自重放一次', async () => {
    const http = createHttpClient();
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockImplementation(() => Promise.resolve(new Response(
        JSON.stringify({ ok: true }),
        {
          status: 200,
          headers: { 'content-type': 'application/json' }
        })));
    const refresh = vi.fn().mockResolvedValue(true);
    vi.stubGlobal('fetch', fetchMock);
    http.configureAuthentication({
      getAccessToken: () => 'access-token',
      refresh
    });

    await Promise.all([http.request('/first'), http.request('/second')]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it('RequestOptions.headers 只补充缺失头且不覆盖 init', async () => {
    const http = createHttpClient();
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await http.request('/api/v1/auth/refresh', {
      method: 'POST',
      headers: { 'content-type': 'application/json' }
    }, undefined, {
      retryUnauthorized: false,
      headers: {
        'X-CSRF-Token': 'csrf-token',
        'content-type': 'text/plain'
      }
    });

    const [, init] = fetchMock.mock.calls[0];
    const headers = new Headers(init.headers);
    expect(headers.get('content-type')).toBe('application/json');
    expect(headers.get('X-CSRF-Token')).toBe('csrf-token');
  });
});
