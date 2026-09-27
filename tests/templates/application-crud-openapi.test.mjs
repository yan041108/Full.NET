import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudOpenApi } from './support/application-crud-openapi.mjs';

const base = '/api/v1/catalog/products';
const ref = (name) => ({ $ref: '#/components/schemas/' + name });
const schema = (keys) => ({ type: 'object', properties: Object.fromEntries(keys.map((key) => [key, { type: 'string' }])) });
function document() {
  const operation = (name, status, response, request) => ({ operationId: name, security: [{ Bearer: [] }],
    responses: { [status]: { content: { 'application/json': { schema: ref(response) } } },
      401: { content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { content: { 'application/problem+json': { schema: ref('ProblemDetails') } } } },
    ...(request ? { requestBody: { content: { 'application/json': { schema: ref(request) } } } } : {}),
  });
  const pathParameter = { name: 'id', in: 'path', required: true, schema: { type: 'string', format: 'uuid' } };
  const item = (operations) => Object.fromEntries(Object.entries(operations).map(([method, value]) => [method, { ...value, parameters: [structuredClone(pathParameter)] }]));
  const list = operation('catalogListProducts', '200', 'Page');
  list.parameters = ['page', 'pageSize'].map((name) => ({ name, in: 'query', required: false, schema: { type: 'integer', format: 'int32' } }));
  return { openapi: '3.1.0', components: { securitySchemes: { Bearer: { type: 'http', scheme: 'bearer' } }, schemas: {
    Product: schema(['id', 'tenantId', 'name', 'version', 'createdAtUtc', 'createdById']),
    ProblemDetails: schema(['type', 'title', 'status', 'detail', 'instance']),
    Page: { type: 'object', properties: { items: { type: 'array', items: ref('Product') } } },
    Create: schema(['name']), Update: schema(['name', 'version']), Delete: schema(['version']),
  } }, paths: {
    [base]: { get: list, post: operation('catalogCreateProduct', '201', 'Product', 'Create') },
    [base + '/{id}']: item({ get: operation('catalogGetProduct', '200', 'Product'), put: operation('catalogUpdateProduct', '200', 'Product', 'Update') }),
    [base + '/{id}/delete']: item({ post: operation('catalogDeleteProduct', '200', 'Product', 'Delete') }),
  } };
}
async function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-openapi-'));
  try {
    const expectedPath = join(root, 'generated.json');
    writeFileSync(expectedPath, JSON.stringify(document()));
    await action(expectedPath, join(root, 'result.json'));
  } finally { rmSync(root, { recursive: true, force: true }); }
}
test('served application OpenAPI matches generated operations and field boundaries', async () => fixture(async (expectedPath, logPath) => {
  const original = readFileSync(expectedPath);
  const runtime = document();
  runtime.paths[base + '/'] = runtime.paths[base]; delete runtime.paths[base];
  runtime.components.schemas.RuntimeProduct = runtime.components.schemas.Product;
  runtime.components.schemas.Product = ref('RuntimeProduct');
  let calls = 0;
  const result = await verifyApplicationCrudOpenApi('http://example.test', { expectedPath, logPath, request: async (url, options) => {
    calls++; assert.equal(url, 'http://example.test/openapi/v1.json'); assert.equal(options.redirect, 'error'); assert.ok(options.signal);
    assert.equal(options.headers?.Authorization, undefined);
    return new Response(JSON.stringify(runtime), { headers: { 'content-type': 'application/json' } });
  } });
  assert.deepEqual(result, { operations: 5, parameterShapes: 5, bearerProtected: 5, authenticationProblems: 10, requestShapes: 3, responseShapes: 5, generatedUnchanged: true });
  assert.equal(calls, 1);
  assert.deepEqual(readFileSync(expectedPath), original);
  const evidence = JSON.parse(readFileSync(logPath, 'utf8'));
  assert.equal(evidence.completed, true);
  assert.equal(evidence.comparisons.length, 5);
}));
test('optional query parameters accept nullable scalar schemas and different order', async () => fixture(async (expectedPath, logPath) => {
  const runtime = document();
  runtime.paths[base].get.parameters.reverse();
  for (const parameter of runtime.paths[base].get.parameters) {
    parameter.schema.type = ['null', 'integer'];
    delete parameter.required;
  }
  let calls = 0;
  await verifyApplicationCrudOpenApi('http://example.test', { expectedPath, logPath, request: async () => {
    calls++; return new Response(JSON.stringify(runtime), { headers: { 'content-type': 'application/json' } });
  } });
  assert.equal(calls, 1);
  assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).result.parameterShapes, 5);
}));
const cases = [
  ['missing-path-parameter', (d) => delete d.paths[base + '/{id}'].get.parameters],
  ['wrong-path-format', (d) => d.paths[base + '/{id}'].put.parameters[0].schema.format = 'int64'],
  ['optional-path-parameter', (d) => d.paths[base + '/{id}/delete'].post.parameters[0].required = false],
  ['wrong-parameter-location', (d) => d.paths[base + '/{id}'].get.parameters[0].in = 'query'],
  ['wrong-page-type', (d) => d.paths[base].get.parameters[0].schema.type = 'string'],
  ['wrong-page-format', (d) => d.paths[base].get.parameters[1].schema.format = 'int64'],
  ['required-page', (d) => d.paths[base].get.parameters[0].required = true],
  ['missing-page-size', (d) => d.paths[base].get.parameters.pop()],
  ['unexpected-tenant-parameter', (d) => d.paths[base].post.parameters = [{ name: 'tenantId', in: 'query', schema: { type: 'string' } }]],
  ['duplicate-parameter', (d) => d.paths[base].get.parameters.push(structuredClone(d.paths[base].get.parameters[0]))],
  ['nullable-path-parameter', (d) => d.paths[base + '/{id}'].get.parameters[0].schema.type = ['null', 'string']],
  ['invalid-required-flag', (d) => d.paths[base].get.parameters[0].required = 'false'],
  ['inherited-tenant-parameter', (d) => d.paths[base].parameters = [{ name: 'tenantId', in: 'query', schema: { type: 'string', format: 'uuid' } }]],
  ['missing-401', (d) => delete d.paths[base].get.responses['401']],
  ['missing-403', (d) => delete d.paths[base].post.responses['403']],
  ['wrong-problem-media', (d) => d.paths[base].get.responses['401'].content = { 'application/json': { schema: ref('ProblemDetails') } }],
  ['missing-problem-schema', (d) => delete d.paths[base].get.responses['403'].content['application/problem+json'].schema],
  ['missing-route', (d) => delete d.paths[base + '/{id}'].put],
  ['extra-route', (d) => d.paths[base + '/disable'] = { post: d.paths[base + '/{id}/delete'].post }],
  ['wrong-operation-id', (d) => d.paths[base].post.operationId = 'unexpectedCreate'],
  ['unprotected-operation', (d) => d.paths[base].get.security = []],
  ['anonymous-alternative', (d) => d.paths[base].get.security.push({})],
  ['wrong-security-scheme', (d) => d.components.securitySchemes.Bearer.scheme = 'basic'],
  ['wrong-success-status', (d) => d.paths[base].post.responses = { 200: d.paths[base].post.responses['201'] }],
  ['server-owned-request-field', (d) => d.components.schemas.Create.properties.tenantId = { type: 'string' }],
  ['missing-response-version', (d) => delete d.components.schemas.Product.properties.version],
  ['missing-list-items', (d) => delete d.components.schemas.Page.properties.items],
  ['missing-reference', (d) => d.components.schemas.Product = ref('Absent')],
  ['external-reference', (d) => d.components.schemas.Product = { $ref: 'https://example.test/Product' }],
  ['cyclic-reference', (d) => d.components.schemas.Product = ref('Product')],
];
for (const [name, change] of cases) {
  test(`application OpenAPI rejects ${name} with partial evidence`, async () => fixture(async (expectedPath, logPath) => {
    const original = readFileSync(expectedPath);
    const runtime = document(); change(runtime);
    let calls = 0;
    await assert.rejects(() => verifyApplicationCrudOpenApi('http://example.test', { expectedPath, logPath, request: async () => {
      calls++; return new Response(JSON.stringify(runtime), { headers: { 'content-type': 'application/json' } });
    } }));
    assert.equal(calls, 1);
    assert.deepEqual(readFileSync(expectedPath), original);
    const evidence = JSON.parse(readFileSync(logPath, 'utf8'));
    assert.equal(evidence.completed, false);
    assert.ok(evidence.error);
  }));
}

for (const [name, buildResponse] of [
  ['HTTP-failure', () => new Response('{}', { status: 503, headers: { 'content-type': 'application/json' } })],
  ['wrong-content-type', () => new Response(JSON.stringify(document()), { headers: { 'content-type': 'text/html' } })],
  ['invalid-JSON', () => new Response('invalid JSON', { headers: { 'content-type': 'application/json' } })],
]) {
  test(`application OpenAPI rejects ${name} after requesting the actual document`, async () => fixture(async (expectedPath, logPath) => {
    let calls = 0;
    await assert.rejects(() => verifyApplicationCrudOpenApi('http://example.test', { expectedPath, logPath, request: async () => { calls++; return buildResponse(); } }));
    assert.equal(calls, 1);
    assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).completed, false);
  }));
}
test('application OpenAPI rejects mutation of the generated input during acceptance', async () => fixture(async (expectedPath, logPath) => {
  await assert.rejects(() => verifyApplicationCrudOpenApi('http://example.test', { expectedPath, logPath, request: async () => {
    writeFileSync(expectedPath, '{}');
    return new Response(JSON.stringify(document()), { headers: { 'content-type': 'application/json' } });
  } }), /changed generated contract/u);
  const evidence = JSON.parse(readFileSync(logPath, 'utf8'));
  assert.equal(evidence.completed, false);
  assert.equal(evidence.comparisons.length, 5);
}));
