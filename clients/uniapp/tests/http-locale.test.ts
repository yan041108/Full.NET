import { describe, expect, it } from 'vitest';

import { createHttpClient } from '../src/api/http';
import { HttpProblem } from '../src/api/problem-details';

type RequestCall = {
  readonly url: string;
  readonly method?: string;
  readonly data?: unknown;
  readonly header?: Record<string, string>;
};

type PlannedResponse =
  | { readonly statusCode: number; readonly data: unknown }
  | { readonly failure: unknown };

function createRequest(responses: readonly PlannedResponse[]): {
  readonly request: Uni['request'];
  readonly calls: RequestCall[];
} {
  const calls: RequestCall[] = [];
  let index = 0;
  const request = ((options: UniNamespace.RequestOptions) => {
    calls.push({
      url: options.url,
      method: options.method,
      data: options.data,
      header: options.header as Record<string, string> | undefined
    });
    const response = responses[index++];
    if ('failure' in response) {
      options.fail?.(response.failure as UniNamespace.GeneralCallbackResult);
    } else {
      options.success?.({
        statusCode: response.statusCode,
        data: response.data as UniNamespace.RequestSuccessCallbackResult['data'],
        header: {},
        cookies: []
      });
    }
    return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
  }) as Uni['request'];

  return { request, calls };
}

describe('HTTP client locale and ProblemDetails contract', () => {
  it('reads the latest locale and token on every request while preserving safe caller headers', async () => {
    const transport = createRequest([
      { statusCode: 200, data: { value: 1 } },
      { statusCode: 201, data: { value: 2 } }
    ]);
    let locale: 'zh-CN' | 'en-US' = 'zh-CN';
    let token: string | undefined = undefined;
    const http = createHttpClient({
      request: transport.request,
      getLocale: () => locale,
      getAccessToken: () => token
    });

    await expect(http.request<{ value: number }>({
      path: '/api/v1/one',
      headers: { 'X-Request-Id': 'first', 'accept-language': 'spoofed', authorization: 'Basic ignored' }
    })).resolves.toEqual({ value: 1 });

    locale = 'en-US';
    token = 'fresh-access-token';
    await expect(http.request<{ value: number }>({ path: '/api/v1/two', method: 'POST' }))
      .resolves.toEqual({ value: 2 });

    expect(transport.calls).toEqual([
      {
        url: '/api/v1/one',
        method: 'GET',
        data: undefined,
        header: { 'X-Request-Id': 'first', 'Accept-Language': 'zh-CN' }
      },
      {
        url: '/api/v1/two',
        method: 'POST',
        data: undefined,
        header: { 'Accept-Language': 'en-US', Authorization: 'Bearer fresh-access-token' }
      }
    ]);
  });

  it('returns data for every 2xx response', async () => {
    const transport = createRequest([{ statusCode: 204, data: { removed: true } }]);
    const http = createHttpClient({ request: transport.request, getLocale: () => 'zh-CN' });

    await expect(http.request({ path: '/api/v1/resource', method: 'DELETE' }))
      .resolves.toEqual({ removed: true });
  });

  it('preserves standard ProblemDetails fields for non-2xx JSON responses', async () => {
    const transport = createRequest([{
      statusCode: 409,
      data: {
        title: 'Profile changed.',
        code: 'identity.profile_version_conflict',
        traceId: 'trace-conflict',
        violations: [{ field: 'profileVersion', code: 'identity.profile_version_conflict', arguments: {} }]
      }
    }]);
    const http = createHttpClient({ request: transport.request, getLocale: () => 'en-US' });

    await expect(http.request({ path: '/api/v1/me/locale', method: 'PUT' })).rejects.toMatchObject({
      status: 409,
      code: 'identity.profile_version_conflict',
      traceId: 'trace-conflict',
      violations: [{ field: 'profileVersion', code: 'identity.profile_version_conflict', arguments: {} }]
    } satisfies Partial<HttpProblem>);
  });

  it('uses stable safe failures for non-JSON responses and transport failures', async () => {
    const transport = createRequest([
      { statusCode: 502, data: '<html>gateway failure</html>' },
      { failure: { errMsg: 'request:fail timeout https://internal.example/token=secret' } }
    ]);
    const http = createHttpClient({ request: transport.request, getLocale: () => 'zh-CN' });

    await expect(http.request({ path: '/api/v1/one' })).rejects.toMatchObject({
      status: 502,
      code: 'http.unexpected_response',
      title: 'Request failed.',
      detail: undefined
    } satisfies Partial<HttpProblem>);
    await expect(http.request({ path: '/api/v1/two' })).rejects.toMatchObject({
      status: 0,
      code: 'http.network_error',
      title: 'Network request failed.',
      detail: undefined
    } satisfies Partial<HttpProblem>);
  });

  it.each([null, undefined, '200', Number.NaN, 200.5])(
    'rejects a malformed success status code of %s without leaving the request pending',
    async statusCode => {
      const request = ((options: UniNamespace.RequestOptions) => {
        queueMicrotask(() => options.success?.({
          statusCode,
          data: { value: 'ignored' },
          header: {},
          cookies: []
        } as unknown as UniNamespace.RequestSuccessCallbackResult));
        return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
      }) as Uni['request'];
      const http = createHttpClient({ request, getLocale: () => 'zh-CN' });

      await expect(http.request({ path: '/api/v1/malformed' })).rejects.toMatchObject({
        status: 0,
        code: 'http.unexpected_response',
        title: 'Request failed.'
      } satisfies Partial<HttpProblem>);
    }
  );

  it.each([
    ['a null callback response', null],
    ['a response without statusCode', { data: { value: 'ignored' }, header: {}, cookies: [] }]
  ])('rejects %s from an asynchronous success callback', async (_description, response) => {
    const request = ((options: UniNamespace.RequestOptions) => {
      queueMicrotask(() => options.success?.(response as unknown as UniNamespace.RequestSuccessCallbackResult));
      return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['request'];
    const http = createHttpClient({ request, getLocale: () => 'zh-CN' });

    await expect(http.request({ path: '/api/v1/malformed' })).rejects.toMatchObject({
      status: 0,
      code: 'http.unexpected_response',
      title: 'Request failed.'
    } satisfies Partial<HttpProblem>);
  });

  it('settles only once when success is followed by fail', async () => {
    const request = ((options: UniNamespace.RequestOptions) => {
      options.success?.({ statusCode: 200, data: { value: 'first' }, header: {}, cookies: [] });
      options.fail?.({ errMsg: 'request:fail after success' });
      return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['request'];
    const http = createHttpClient({ request, getLocale: () => 'zh-CN' });

    await expect(http.request({ path: '/api/v1/once' })).resolves.toEqual({ value: 'first' });
  });

  it('settles only once when fail is followed by success', async () => {
    const request = ((options: UniNamespace.RequestOptions) => {
      options.fail?.({ errMsg: 'request:fail first' });
      options.success?.({ statusCode: 200, data: { value: 'ignored' }, header: {}, cookies: [] });
      return { abort() {}, onHeadersReceived() {}, offHeadersReceived() {} };
    }) as Uni['request'];
    const http = createHttpClient({ request, getLocale: () => 'zh-CN' });

    await expect(http.request({ path: '/api/v1/once' })).rejects.toMatchObject({
      status: 0,
      code: 'http.network_error'
    } satisfies Partial<HttpProblem>);
  });

  it('omits blank tokens and trims a non-blank token before writing Authorization', async () => {
    const transport = createRequest([
      { statusCode: 200, data: {} },
      { statusCode: 200, data: {} },
      { statusCode: 200, data: {} }
    ]);
    let token: string | undefined;
    const http = createHttpClient({
      request: transport.request,
      getLocale: () => 'zh-CN',
      getAccessToken: () => token
    });

    await http.request({ path: '/api/v1/no-token' });
    token = '   ';
    await http.request({ path: '/api/v1/blank-token' });
    token = '  fresh-access-token  ';
    await http.request({ path: '/api/v1/trimmed-token' });

    expect(transport.calls.map(call => call.header)).toEqual([
      { 'Accept-Language': 'zh-CN' },
      { 'Accept-Language': 'zh-CN' },
      { 'Accept-Language': 'zh-CN', Authorization: 'Bearer fresh-access-token' }
    ]);
  });
});
