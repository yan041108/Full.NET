import { afterEach, describe, expect, it, vi } from 'vitest';
import { configureAuthentication, request } from '../js/core/http.js';

afterEach(() => {
  vi.unstubAllGlobals();
  configureAuthentication();
});

describe('Layui HTTP 适配器', () => {
  it('失败响应保留与 Vue 一致的稳定错误契约', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-layui'
    }), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' }
    })));

    await expect(request('/api/v1/me')).rejects.toMatchObject({
      code: 'authorization.denied',
      traceId: 'trace-layui'
    });
  });

  it('成功响应返回 JSON 数据', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'user-1',
      displayName: '系统管理员'
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    })));

    await expect(request('/api/v1/me'))
      .resolves.toEqual({ id: 'user-1', displayName: '系统管理员' });
  });

  it('无内容响应不解析 JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    await expect(request('/api/v1/session', { method: 'DELETE' }))
      .resolves.toBeUndefined();
  });

  it('携带 Cookie、取消信号和默认 Accept 请求头', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const abortController = new AbortController();
    vi.stubGlobal('fetch', fetchMock);

    await request('/api/v1/me', {
      headers: { 'x-client': 'layui-admin' }
    }, abortController.signal);

    const [, requestInit] = fetchMock.mock.calls[0];
    const headers = new Headers(requestInit.headers);
    expect(requestInit.credentials).toBe('include');
    expect(requestInit.signal).toBe(abortController.signal);
    expect(headers.get('accept')).toBe('application/json');
    expect(headers.get('x-client')).toBe('layui-admin');
  });

  it('未传独立参数时保留 RequestInit 中的取消信号', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const abortController = new AbortController();
    vi.stubGlobal('fetch', fetchMock);

    await request('/api/v1/me', { signal: abortController.signal });

    const [, requestInit] = fetchMock.mock.calls[0];
    expect(requestInit.signal).toBe(abortController.signal);
  });

  it('损坏错误响应降级为统一错误而不泄露解析异常', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{broken', {
      status: 502,
      statusText: 'Bad Gateway',
      headers: { 'content-type': 'application/problem+json' }
    })));

    await expect(request('/api/v1/me')).rejects.toEqual({
      status: 502,
      code: 'http.unexpected_response',
      title: 'Bad Gateway'
    });
  });

  it('并发 401 只刷新一次并携带内存令牌重放请求', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockImplementation(() => Promise.resolve(new Response(
        JSON.stringify({ ok: true }),
        { status: 200, headers: { 'content-type': 'application/json' } }
      )));
    const refresh = vi.fn().mockResolvedValue(true);
    vi.stubGlobal('fetch', fetchMock);
    configureAuthentication({
      getAccessToken: () => 'access-token',
      refresh
    });

    await Promise.all([request('/first'), request('/second')]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
    const [, retryInit] = fetchMock.mock.calls[2];
    expect(new Headers(retryInit.headers).get('authorization'))
      .toBe('Bearer access-token');
  });
});
