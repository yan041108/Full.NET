import { describe, expect, it } from 'vitest';
import { createHttpClient } from '../src/api/http';

describe('authenticated file upload', () => {
  it('401 后只刷新一次，重发读取最新 Bearer、语言且不覆盖 multipart 类型', async () => {
    const calls: Array<{ url: string; header: Record<string, string> }> = [];
    let token: string | undefined = 'old'; let refreshed = 0;
    let locale: 'zh-CN' | 'en-US' = 'zh-CN';
    const uploadFile = ((options: UniNamespace.UploadFileOption) => {
      calls.push({ url: options.url, header: options.header as Record<string, string> });
      options.success?.({ statusCode: calls.length === 1 ? 401 : 200, data: calls.length === 1 ? '{"code":"auth.expired"}' : '{"fileId":"owned"}' });
      return { abort() {}, onProgressUpdate() {}, offProgressUpdate() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['uploadFile'];
    const client = createHttpClient({ request: (() => {}) as unknown as Uni['request'], uploadFile,
      getLocale: () => locale, apiBaseUrl: 'https://api.example.test' });
    client.configureAuthentication({ getAccessToken: () => token, refresh: async () => { refreshed++; token = 'current'; locale = 'en-US'; return true; } });
    await expect(client.upload({ path: '/api/v1/workflow/todos/owned/signature-uploads?expectedRevision=1', filePath: '/tmp/signature.png' })).resolves.toEqual({ fileId: 'owned' });
    expect(refreshed).toBe(1); expect(calls).toHaveLength(2);
    expect(calls[0].header).toEqual({ 'Accept-Language': 'zh-CN', Authorization: 'Bearer old' });
    expect(calls[1].header).toEqual({ 'Accept-Language': 'en-US', Authorization: 'Bearer current' });
    expect(calls[1].url).toBe('https://api.example.test/api/v1/workflow/todos/owned/signature-uploads?expectedRevision=1');
  });

  it('绝对目的地址不接收凭据，已配置匿名认证桥也不回退旧令牌', async () => {
    const headers: unknown[] = [];
    const uploadFile = ((options: UniNamespace.UploadFileOption) => {
      headers.push(options.header); options.success?.({ statusCode: 200, data: '{}' });
      return { abort() {}, onProgressUpdate() {}, offProgressUpdate() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['uploadFile'];
    const client = createHttpClient({ request: (() => {}) as unknown as Uni['request'], uploadFile,
      getLocale: () => 'zh-CN', getAccessToken: () => 'stale' });
    client.configureAuthentication({ getAccessToken: () => undefined, refresh: async () => false });
    await expect(client.upload({ path: 'https://other.example.test/upload', filePath: '/tmp/signature.png' })).rejects.toMatchObject({ code: 'http.upload_unavailable' });
    expect(headers).toHaveLength(0);
    await client.upload({ path: '/api/v1/owned', filePath: '/tmp/signature.png' });
    expect(headers).toEqual([{ 'Accept-Language': 'zh-CN' }]);
  });
});
