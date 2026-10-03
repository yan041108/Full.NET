import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';

// 标记端点只证明应用模块进入真实 HTTP 路由，不将匿名响应当作业务授权验收。
export async function verifyApplicationModuleEndpoint(baseUrl, { logPath, request = fetch }) {
  const url = `${baseUrl}/api/v1/application-composition-probe`;
  let evidence;
  try {
    const response = await request(url, { signal: AbortSignal.timeout(15_000), redirect: 'error' });
    evidence = { url, status: response.status, body: await response.text() };
  } catch (error) {
    writeFileSync(logPath, JSON.stringify({ url, error: error instanceof Error ? error.message : String(error) }, null, 2));
    throw error;
  }
  writeFileSync(logPath, JSON.stringify(evidence, null, 2));
  assert.equal(evidence.status, 200, `application module route failed: ${evidence.body}`);
  assert.equal(evidence.body, 'application-module-active', 'application module response marker is incorrect');
}
