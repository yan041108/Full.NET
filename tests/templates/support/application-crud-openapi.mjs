import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';

const methods = ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'];
const productBase = '/api/v1/catalog/products';
const normalize = (path) => path.replace(/\/+$/u, '');
function operations(document) {
  const result = new Map();
  for (const [path, item] of Object.entries(document.paths ?? {})) {
    const normalized = normalize(path);
    if (normalized !== productBase && !normalized.startsWith(productBase + '/')) continue;
    // 当前生成器使用操作内联参数；拒绝路径继承，避免额外租户参数被忽略后误报通过。
    assert.ok(item.parameters === undefined || (Array.isArray(item.parameters) && item.parameters.length === 0), 'path-level parameters unsupported');
    for (const method of methods) {
      if (!Object.hasOwn(item, method)) continue;
      const key = method + ' ' + normalized;
      assert.equal(result.has(key), false, 'duplicate normalized operation: ' + key);
      result.set(key, item[method]);
    }
  }
  return result;
}
function resolveSchema(document, value) {
  const seen = new Set();
  while (value?.$ref) {
    const reference = value.$ref;
    assert.ok(typeof reference === 'string' && reference.startsWith('#/components/schemas/'), 'schema reference must be local');
    assert.equal(seen.has(reference), false, 'cyclic schema reference');
    seen.add(reference);
    const name = reference.slice('#/components/schemas/'.length).replaceAll('~1', '/').replaceAll('~0', '~');
    assert.ok(Object.hasOwn(document.components?.schemas ?? {}, name), 'missing schema reference');
    value = document.components.schemas[name];
  }
  assert.ok(value && typeof value === 'object', 'missing schema');
  return value;
}
function properties(document, value, list = false) {
  let resolved = resolveSchema(document, value);
  if (list) {
    const items = resolveSchema(document, resolved.properties?.items);
    assert.equal(items.type, 'array', 'list items must be an array');
    resolved = resolveSchema(document, items.items);
  }
  assert.ok(resolved.properties && typeof resolved.properties === 'object', 'schema properties missing');
  return Object.keys(resolved.properties).sort();
}

function parameters(document, operation) {
  const values = operation.parameters ?? [];
  assert.ok(Array.isArray(values), 'operation parameters must be an array');
  const seen = new Set();
  return values.map((parameter) => {
    assert.ok(parameter && !parameter.$ref, 'parameter must be inline');
    assert.ok(typeof parameter.name === 'string' && parameter.name.length > 0, 'parameter name missing');
    assert.ok(['query', 'path'].includes(parameter.in), 'unexpected parameter location');
    const key = parameter.in + ':' + parameter.name;
    assert.equal(seen.has(key), false, 'duplicate parameter: ' + key);
    seen.add(key);
    assert.ok(parameter.required === undefined || typeof parameter.required === 'boolean', 'invalid parameter required flag');
    const required = parameter.required === true;
    const schema = resolveSchema(document, parameter.schema);
    let types = Array.isArray(schema.type) ? [...schema.type] : [schema.type];
    // 可选查询参数的 CLR 可空表示与生成契约等价；路径参数不能因此变成可空。
    if (parameter.in === 'query' && !required) types = types.filter((type) => type !== 'null');
    assert.equal(types.length, 1, 'parameter must have one scalar type');
    assert.ok(['string', 'integer', 'number', 'boolean'].includes(types[0]), 'parameter scalar type missing');
    if (parameter.in === 'path') assert.equal(schema.nullable === true, false, 'nullable path parameter');
    return { name: parameter.name, in: parameter.in, required, type: types[0], format: schema.format ?? null };
  }).sort((left, right) => (left.in + ':' + left.name).localeCompare(right.in + ':' + right.name));
}

// 对照只读生成契约与应用实际服务的文档，证明接入子集，不替代完整错误或类型契约。
export async function verifyApplicationCrudOpenApi(baseUrl, { expectedPath, logPath, request = fetch }) {
  const original = readFileSync(expectedPath);
  const evidence = { completed: false, comparisons: [] };
  try {
    const expected = JSON.parse(original.toString('utf8'));
    const response = await request(baseUrl + '/openapi/v1.json', { redirect: 'error', signal: AbortSignal.timeout(30_000) });
    evidence.status = response.status;
    evidence.contentType = response.headers.get('content-type');
    assert.equal(response.status, 200, 'served OpenAPI request failed');
    assert.match(evidence.contentType ?? '', /^application\/json(?:;|$)/iu);
    let actual;
    try { actual = JSON.parse(await response.text()); } catch { throw new Error('invalid served OpenAPI JSON'); }
    assert.match(actual.openapi ?? '', /^3\.(?:0|1)\./u, 'unsupported OpenAPI version');
    evidence.documentVersion = actual.openapi;
    const expectedOperations = operations(expected);
    const actualOperations = operations(actual);
    assert.equal(expectedOperations.size, 5, 'generated contract must contain five product operations');
    assert.deepEqual([...actualOperations.keys()].sort(), [...expectedOperations.keys()].sort(), 'served product operations differ');
    assert.equal(actual.components?.securitySchemes?.Bearer?.type, 'http');
    assert.equal(actual.components.securitySchemes.Bearer.scheme?.toLowerCase(), 'bearer');
    let requestShapes = 0;
    for (const [key, expectedOperation] of expectedOperations) {
      const actualOperation = actualOperations.get(key);
      const comparison = { operation: key, operationId: actualOperation.operationId };
      evidence.comparisons.push(comparison);
      assert.equal(actualOperation.operationId, expectedOperation.operationId, key + ': operationId');
      comparison.parameters = parameters(actual, actualOperation);
      assert.deepEqual(comparison.parameters, parameters(expected, expectedOperation), key + ': parameters');
      const security = actualOperation.security;
      assert.ok(Array.isArray(security) && security.length > 0, key + ': missing security');
      assert.ok(security.every((requirement) => requirement && Object.keys(requirement).length > 0), key + ': anonymous security alternative');
      assert.ok(security.some((requirement) => Array.isArray(requirement.Bearer)), key + ': missing Bearer');
      comparison.bearerProtected = true;
      const successes = Object.keys(expectedOperation.responses ?? {}).filter((status) => /^2\d\d$/u.test(status)).sort();
      assert.deepEqual(Object.keys(actualOperation.responses ?? {}).filter((status) => /^2\d\d$/u.test(status)).sort(), successes, key + ': success statuses');
      assert.equal(successes.length, 1, 'expected one generated success response');
      comparison.successStatus = successes[0];
      comparison.authenticationProblems = [];
      for (const status of ['401', '403']) {
        assert.ok(expectedOperation.responses[status]?.content?.['application/problem+json'], 'generated authentication problem missing');
        const problem = actualOperation.responses?.[status]?.content?.['application/problem+json']?.schema;
        const problemSchema = resolveSchema(actual, problem);
        assert.ok(Object.hasOwn(problemSchema.properties ?? {}, 'status'), key + ': missing ProblemDetails status');
        comparison.authenticationProblems.push(status);
      }
      const responseSchema = (operation) => operation.responses[successes[0]]?.content?.['application/json']?.schema;
      const isList = key === 'get ' + productBase;
      comparison.responseProperties = properties(actual, responseSchema(actualOperation), isList);
      assert.deepEqual(comparison.responseProperties, properties(expected, responseSchema(expectedOperation), isList), key + ': response fields');
      if (expectedOperation.requestBody) {
        const requestSchema = (operation) => operation.requestBody?.content?.['application/json']?.schema;
        comparison.requestProperties = properties(actual, requestSchema(actualOperation));
        assert.deepEqual(comparison.requestProperties, properties(expected, requestSchema(expectedOperation)), key + ': request fields');
        for (const field of ['id', 'tenantId', 'createdAtUtc', 'createdById']) assert.equal(comparison.requestProperties.includes(field), false, 'server-owned request field');
        requestShapes++;
      } else assert.equal(actualOperation.requestBody, undefined, key + ': unexpected request body');
    }
    assert.equal(requestShapes, 3);
    assert.deepEqual(readFileSync(expectedPath), original, 'acceptance changed generated contract');
    evidence.completed = true;
    evidence.result = { operations: 5, parameterShapes: 5, bearerProtected: 5, authenticationProblems: 10, requestShapes, responseShapes: 5, generatedUnchanged: true };
    return evidence.result;
  } catch (error) {
    evidence.error = error instanceof Error ? error.message : String(error);
    throw new Error(evidence.error);
  } finally {
    writeFileSync(logPath, JSON.stringify(evidence, null, 2));
  }
}
