import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';

// 只验证真实请求的授权拒绝，不用这些结果证明允许租户 CRUD 或数据隔离。
export async function verifyApplicationCrudHttpDenial(baseUrl, { hostAccessToken, logPath, request = fetch }) {
  assert.ok(typeof hostAccessToken === 'string' && hostAccessToken.trim(), 'authenticated Host token is required');
  const redact = (value) => String(value).replaceAll(hostAccessToken, '[REDACTED]');
  const id = '01900000-0000-7000-8000-000000000001';
  const routes = [
    { method: 'GET', path: '/?page=1&pageSize=5' },
    { method: 'GET', path: `/${id}` },
    { method: 'POST', path: '/', body: { name: 'Denied application product' } },
    { method: 'PUT', path: `/${id}`, body: { name: 'Denied update', version: '1' } },
    { method: 'POST', path: `/${id}/delete`, body: { version: '1' } },
  ];
  const evidence = { completed: false, responses: [] };
  try {
    for (const subject of ['anonymous', 'host-admin']) {
      const status = subject === 'anonymous' ? 401 : 403;
      const code = subject === 'anonymous' ? 'identity.session_not_active' : 'authorization.permission_denied';
      for (const route of routes) {
        const url = `${baseUrl}/api/v1/catalog/products${route.path}`;
        const entry = { subject, method: route.method, url };
        evidence.responses.push(entry);
        let response;
        try {
          // 日志只保存主体标签与响应，不序列化带有 Bearer 的请求配置。
          response = await request(url, {
            method: route.method,
            headers: { 'Content-Type': 'application/json', Origin: 'http://localhost',
              ...(subject === 'host-admin' ? { Authorization: `Bearer ${hostAccessToken}` } : {}) },
            body: route.body === undefined ? undefined : JSON.stringify(route.body),
            redirect: 'error', signal: AbortSignal.timeout(15_000),
          });
          entry.status = response.status;
          entry.contentType = response.headers.get('content-type');
          entry.body = redact(await response.text());
        } catch (error) {
          entry.error = redact(error instanceof Error ? error.message : error);
          throw new Error(entry.error);
        }
        assert.equal(entry.status, status, `${subject} ${route.method} ${route.path}: ${entry.body}`);
        assert.match(entry.contentType ?? '', /^application\/problem\+json(?:;|$)/iu, 'authorization must return ProblemDetails');
        const problem = JSON.parse(entry.body);
        assert.equal(problem.status, status, 'ProblemDetails status mismatch');
        assert.equal(problem.code, code, 'unexpected authorization machine code');
      }
    }
    evidence.completed = true;
    return { requests: 10, anonymousDenied: 5, hostDenied: 5 };
  } finally {
    // 失败也保留到失败请求为止的证据，不把后续未执行请求计为成功。
    writeFileSync(logPath, JSON.stringify(evidence, null, 2));
  }
}
