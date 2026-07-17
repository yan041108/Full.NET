import { describe, expect, it } from 'vitest';
import {
  isFullNetProblemDetails,
  readProblemDetails
} from '../src/problem-details.js';

describe('Full.NET ProblemDetails', () => {
  it('接受包含稳定错误码的 Full.NET 错误契约', () => {
    expect(isFullNetProblemDetails({
      type: 'https://full.net/errors/validation.failed',
      status: 400,
      code: 'validation.failed',
      traceId: 'trace-1',
      errors: { name: ['名称不能为空'] }
    })).toBe(true);
  });

  it('拒绝缺少稳定错误码的普通 ProblemDetails', () => {
    expect(isFullNetProblemDetails({
      status: 400,
      title: 'Bad Request'
    })).toBe(false);
  });

  it('代理返回非 JSON 内容时构造安全的统一错误', async () => {
    const response = new Response('<html>bad gateway</html>', {
      status: 502,
      statusText: 'Bad Gateway',
      headers: { 'content-type': 'text/html' }
    });

    await expect(readProblemDetails(response)).resolves.toEqual({
      status: 502,
      code: 'http.unexpected_response',
      title: 'Bad Gateway'
    });
  });

  it('响应声明为 JSON 但内容损坏时不泄露解析异常', async () => {
    const response = new Response('{broken', {
      status: 500,
      statusText: 'Internal Server Error',
      headers: { 'content-type': 'application/problem+json' }
    });

    await expect(readProblemDetails(response)).resolves.toEqual({
      status: 500,
      code: 'http.unexpected_response',
      title: 'Internal Server Error'
    });
  });
});
