import { describe, expect, it } from 'vitest';
import { createHttpClient } from '../src/api/http';

describe('受保护文件的二进制传输', () => {
  it('保持原始字节，二进制401仍按单次刷新机制重试并使用当前凭据', async () => {
    const calls: UniNamespace.RequestOptions[] = []; let token = 'old'; let refreshes = 0;
    const bytes = new Uint8Array([137, 80, 78, 71]).buffer;
    const denied = new TextEncoder().encode(JSON.stringify({ type: 'about:blank', title: 'Expired', status: 401, code: 'identity.session_expired' })).buffer;
    const request = ((options: UniNamespace.RequestOptions) => {
      calls.push(options); options.success?.({ statusCode: calls.length === 1 ? 401 : 200,
        data: calls.length === 1 ? denied : bytes, header: {}, cookies: [] });
      return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['request'];
    const client = createHttpClient({ request, getLocale: () => 'en-US' });
    client.configureAuthentication({ getAccessToken: () => token, refresh: async () => { refreshes++; token = 'current'; return true; } });
    expect(await client.request({ path: '/api/v1/workflow/todos/one/form-attachments/file/content', responseType: 'arraybuffer' })).toBe(bytes);
    expect(refreshes).toBe(1); expect(calls.map(call => call.responseType)).toEqual(['arraybuffer', 'arraybuffer']);
    expect(calls.map(call => call.dataType)).toEqual(['text', 'text']);
    expect(calls.map(call => call.header?.Authorization)).toEqual(['Bearer old', 'Bearer current']);
    expect(calls[1]?.header?.['Accept-Language']).toBe('en-US');
  });

  it('403二进制ProblemDetails不能被当成文件内容', async () => {
    const request = ((options: UniNamespace.RequestOptions) => {
      options.success?.({ statusCode: 403, data: new TextEncoder().encode(JSON.stringify({ type: 'about:blank', title: 'Forbidden', code: 'workflow.todo.forbidden' })).buffer,
        header: {}, cookies: [] }); return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['request'];
    const client = createHttpClient({ request, getLocale: () => 'zh-CN' });
    await expect(client.request({ path: '/api/v1/file', responseType: 'arraybuffer' })).rejects.toMatchObject({ status: 403, code: 'workflow.todo.forbidden' });
  });
});
