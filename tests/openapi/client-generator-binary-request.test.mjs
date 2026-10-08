import assert from 'node:assert/strict';
import test from 'node:test';
import { stripTypeScriptTypes } from 'node:module';
import { renderGeneratedFiles } from '../../scripts/openapi/generate-fullnet-client.mjs';

function document(required = true, mediaTypes = ['application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'text/markdown', 'text/plain']) {
  return { openapi: '3.1.0', paths: {
    '/versions/{documentId}': { post: {
      operationId: 'uploadVersion',
      parameters: [
        { in: 'path', name: 'documentId', required: true, schema: { type: 'string' } },
        { in: 'query', name: 'fileName', required: true, schema: { type: 'string' } }
      ],
      requestBody: { required, content: Object.fromEntries(mediaTypes.map(type => [type, {
        schema: { type: 'string', format: 'binary' }
      }])) },
      responses: { 202: { content: { 'application/json': { schema: { $ref: '#/components/schemas/Import' } } } } }
    } }
  }, components: { schemas: { Import: {
    type: 'object', required: ['status'], properties: { status: { type: 'string' } }
  } } } };
}

async function load(files) {
  const uri = code => `data:text/javascript;base64,${Buffer.from(code).toString('base64')}`;
  const guards = uri(stripTypeScriptTypes(files['guards.generated.ts']));
  const operations = stripTypeScriptTypes(files['operations.generated.ts'])
    .replace("'./guards.generated.js'", JSON.stringify(guards));
  return import(uri(operations));
}

test('原始二进制上传保留 Blob 字节、显式媒体类型和查询编码，JSON 响应仍经守卫', async () => {
  const files = renderGeneratedFiles(document());
  assert.match(files['operations.generated.ts'], /readonly body: Blob;/u);
  assert.match(files['operations.generated.ts'], /readonly contentType:.*application\/pdf.*text\/markdown.*text\/plain/u);
  assert.doesNotMatch(files['operations.generated.ts'], /JSON\.stringify\(parameters\.body\)|new FormData/u);
  const { uploadVersion } = await load(files);
  const body = new Blob([new Uint8Array([0, 255, 42])]);
  const signal = new AbortController().signal;
  const options = { retryUnauthorized: false };
  let call;
  const http = { request: async (...args) => { call = args; return { status: 'pending_confirmation' }; } };
  assert.deepEqual(await uploadVersion(http, {
    documentId: 'a/b', fileName: '中文 +.pdf', body, contentType: 'application/pdf'
  }, signal, options), { status: 'pending_confirmation' });
  assert.equal(call[0], '/versions/a%2Fb?fileName=%E4%B8%AD%E6%96%87+%2B.pdf');
  assert.equal(call[1].body, body);
  assert.deepEqual([...new Uint8Array(await call[1].body.arrayBuffer())], [0, 255, 42]);
  assert.equal(new Headers(call[1].headers).get('content-type'), 'application/pdf');
  assert.equal(call[2], signal);
  assert.equal(call[3], options);
  const docxMime = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
  await uploadVersion(http, { documentId: 'a', fileName: '原文.docx', body, contentType: docxMime }, signal, options);
  assert.equal(call[1].body, body);
  assert.equal(new Headers(call[1].headers).get('content-type'), docxMime);
  await assert.rejects(uploadVersion({ request: async () => ({ status: 42 }) }, {
    documentId: 'a', fileName: 'a.pdf', body, contentType: 'application/pdf'
  }), /invalid_import/u);
});

test('单一媒体类型无需调用方重复声明；可选正文缺省时不发送 Content-Type', async () => {
  const files = renderGeneratedFiles(document(false, ['application/octet-stream']));
  assert.match(files['operations.generated.ts'], /readonly body\?: Blob;/u);
  assert.doesNotMatch(files['operations.generated.ts'], /readonly contentType/u);
  const { uploadVersion } = await load(files);
  let init;
  await uploadVersion({ request: async (_path, value) => { init = value; return { status: 'ok' }; } }, {
    documentId: 'a', fileName: 'a.txt'
  });
  assert.equal(init.body, undefined);
  assert.equal(new Headers(init.headers).has('content-type'), false);
});

test('非二进制原始正文不能被静默生成为 Blob', () => {
  const invalid = document();
  invalid.paths['/versions/{documentId}'].post.requestBody.content['text/plain'].schema = { type: 'object' };
  assert.throws(() => renderGeneratedFiles(invalid), /不支持的 requestBody/u);
});

test('原始二进制请求支持运行时 Stream Schema 引用', () => {
  const referenced = document(true, ['application/pdf']);
  referenced.components.schemas.Stream = { type: 'string', format: 'binary' };
  referenced.paths['/versions/{documentId}'].post.requestBody.content['application/pdf'].schema = {
    $ref: '#/components/schemas/Stream'
  };
  const files = renderGeneratedFiles(referenced);
  assert.match(files['operations.generated.ts'], /readonly body: Blob;/u);
  assert.match(files['operations.generated.ts'], /'content-type': "application\/pdf"/u);
});
