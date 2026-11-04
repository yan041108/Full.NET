import assert from 'node:assert/strict';
import test from 'node:test';
import { stripTypeScriptTypes } from 'node:module';
import { renderGeneratedFiles } from '../../scripts/openapi/generate-fullnet-client.mjs';

const ref = name => ({ $ref: '#/components/schemas/' + name });
function fixture() {
  return renderGeneratedFiles({ openapi: '3.1.0', paths: {
    '/session': { get: { operationId: 'identityGetCurrentUser', responses: { 200: { content: { 'application/json': { schema: ref('Page') } } } } } },
    '/logout': { post: { operationId: 'identityLogout', responses: { 204: { description: 'No Content' } } } },
    '/business': { get: { operationId: 'businessList', responses: { 200: { content: { 'application/json': { schema: ref('Unrelated') } } } } } }
  }, components: { schemas: {
    Row: { type: 'object', required: ['version'], properties: { version: { type: ['integer', 'string'], pattern: '^-?(?:0|[1-9]\\d*)$' } } },
    Page: { type: 'object', required: ['items'], properties: { items: { type: 'array', items: ref('Row') } } },
    Unrelated: { type: 'object', required: ['secret'], properties: { secret: { type: 'string' } } }
  } } });
}

test('启动入口保持接收者、可选请求选项、取消与嵌套响应验证', async () => {
  const files = fixture();
  const guards = stripTypeScriptTypes(files['startup-guards.generated.ts']);
  const operations = stripTypeScriptTypes(files['startup-operations.generated.ts']).replace(/^import[\s\S]*?;\s*/gmu, '');
  const generated = await import('data:text/javascript;base64,' + Buffer.from(guards + '\n' + operations).toString('base64'));
  let response = { items: [{ version: '42' }] }; const calls = [];
  const http = { request(...args) { assert.equal(this, http); calls.push(args); return Promise.resolve(response); } };
  const signal = new AbortController().signal, options = { retryUnauthorized: false, skipAuthentication: true };
  assert.deepEqual(await generated.identityGetCurrentUser(http, {}, signal, options), { items: [{ version: 42 }] });
  assert.equal(response.items[0].version, '42'); assert.equal(calls[0][0], '/session');
  assert.equal(calls[0][2], signal); assert.equal(calls[0][3], options);
  response = { items: [{ version: '9007199254740993' }] };
  await assert.rejects(generated.identityGetCurrentUser(http, {}), /invalid_page/u);
  assert.equal(await generated.identityLogout(http, {}, signal), undefined); assert.equal(calls.at(-1).length, 3);
  assert.equal(calls.at(-1)[1].method, 'POST');
  const failure = new Error('transport failure'); http.request = () => Promise.reject(failure);
  await assert.rejects(generated.identityGetCurrentUser(http, {}), error => error === failure);
});

test('启动入口仅携带所需模型闭包，完整入口仍保留业务操作', () => {
  const files = fixture();
  assert.match(files['startup-guards.generated.ts'], /readRow/u);
  assert.doesNotMatch(files['startup-guards.generated.ts'], /Unrelated/u);
  assert.doesNotMatch(files['startup-operations.generated.ts'], /businessList/u);
  assert.match(files['operations.generated.ts'], /businessList/u);
  assert.match(files['operations.generated.ts'], /identityGetCurrentUser/u);
});

test('没有启动操作的独立应用继续只生成原四种产物', () => {
  const files = renderGeneratedFiles({ openapi: '3.1.0', paths: {} });
  assert.deepEqual(Object.keys(files).sort(), ['guards.generated.ts', 'index.generated.ts', 'models.generated.ts', 'operations.generated.ts']);
});
