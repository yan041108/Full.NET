import { afterEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue HTTP 适配器', () => {
  it('失败响应抛出稳定 ProblemDetails 错误码', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-vue'
    }), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' }
    })));

    await expect(request('/api/v1/me')).rejects.toMatchObject({
      code: 'authorization.denied',
      traceId: 'trace-vue'
    });
  });

  it('成功响应返回强类型 JSON 数据', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'user-1',
      displayName: '系统管理员'
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    })));

    await expect(request<{ id: string; displayName: string }>('/api/v1/me'))
      .resolves.toEqual({ id: 'user-1', displayName: '系统管理员' });
  });

  it('无内容响应不尝试解析 JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, {
      status: 204
    })));

    await expect(request<void>('/api/v1/session', { method: 'DELETE' }))
      .resolves.toBeUndefined();
  });

  it('携带 Cookie、取消信号和默认 Accept 请求头', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const abortController = new AbortController();
    vi.stubGlobal('fetch', fetchMock);

    await request<void>('/api/v1/me', {
      headers: { 'x-client': 'vue-admin' }
    }, abortController.signal);

    const [, requestInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(requestInit.headers);
    expect(requestInit.credentials).toBe('include');
    expect(requestInit.signal).toBe(abortController.signal);
    expect(headers.get('accept')).toBe('application/json');
    expect(headers.get('x-client')).toBe('vue-admin');
  });

  it('未传独立参数时保留 RequestInit 中的取消信号', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const abortController = new AbortController();
    vi.stubGlobal('fetch', fetchMock);

    await request<void>('/api/v1/me', { signal: abortController.signal });

    const [, requestInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(requestInit.signal).toBe(abortController.signal);
  });
});
