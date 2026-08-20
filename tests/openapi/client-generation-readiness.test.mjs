import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import { validateClientGenerationReadiness } from '../../scripts/openapi/validate-client-generation-readiness.mjs';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const fixturesDirectory = path.join(currentDirectory, 'fixtures', 'client-generation');

async function readFixture(name) {
  return JSON.parse(await readFile(path.join(fixturesDirectory, name), 'utf8'));
}

function clone(value) {
  return structuredClone(value);
}

function getOperation(document) {
  return document.paths['/api/v1/identity/users'].get;
}

test('合法 OpenAPI Operation 通过生成就绪门禁', async () => {
  const document = await readFixture('valid.openapi.json');

  assert.deepEqual(validateClientGenerationReadiness(document), []);
});

test('重复 operationId 与多个主 Tag 失败关闭', async () => {
  const document = await readFixture('duplicate-operation-id.openapi.json');

  assert.deepEqual(validateClientGenerationReadiness(document), [
    'POST /api/v1/identity/users: duplicate operationId identityListHostUsers',
    'POST /api/v1/identity/users: expected exactly one primary tag'
  ]);
});

test('缺少运行时 Schema 或显式 Schema 类型失败关闭', async () => {
  const document = await readFixture('missing-runtime-schema.openapi.json');

  assert.deepEqual(validateClientGenerationReadiness(document), [
    'GET /api/v1/settings/config-entries: 200 application/json schema must declare type or $ref'
  ]);
});

test('缺失或不稳定的 Operation 身份失败关闭', async () => {
  const valid = await readFixture('valid.openapi.json');
  const missingOperationId = clone(valid);
  delete getOperation(missingOperationId).operationId;
  const invalidOperationId = clone(valid);
  getOperation(invalidOperationId).operationId = 'Identity_ListHostUsers';
  const missingPrimaryTag = clone(valid);
  getOperation(missingPrimaryTag).tags = ['identity-host-users'];

  assert.deepEqual(validateClientGenerationReadiness(missingOperationId), [
    'GET /api/v1/identity/users: missing operationId'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(invalidOperationId), [
    'GET /api/v1/identity/users: operationId must be lowerCamelCase'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(missingPrimaryTag), [
    'GET /api/v1/identity/users: expected exactly one primary tag'
  ]);
});

test('缺少成功响应、JSON Schema 或安全定义失败关闭', async () => {
  const valid = await readFixture('valid.openapi.json');
  const missingSuccess = clone(valid);
  getOperation(missingSuccess).responses = {
    400: { description: '错误' }
  };
  const missingJsonSchema = clone(valid);
  delete getOperation(missingJsonSchema).responses['200'].content['application/json'].schema;
  const missingSecurity = clone(valid);
  delete getOperation(missingSecurity).security;

  assert.deepEqual(validateClientGenerationReadiness(missingSuccess), [
    'GET /api/v1/identity/users: expected at least one 2xx response'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(missingJsonSchema), [
    'GET /api/v1/identity/users: 200 application/json response must declare schema'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(missingSecurity), [
    'GET /api/v1/identity/users: protected API must declare security'
  ]);
});

test('数组缺少 items、204 含 JSON 与二进制误标 JSON 失败关闭', async () => {
  const valid = await readFixture('valid.openapi.json');
  const arrayWithoutItems = clone(valid);
  getOperation(arrayWithoutItems).responses['200'].content['application/json'].schema = {
    type: 'array'
  };
  const jsonNoContent = clone(valid);
  getOperation(jsonNoContent).responses = {
    204: {
      description: '无内容',
      content: {
        'application/json': {
          schema: { type: 'object' }
        }
      }
    }
  };
  const binaryAsJson = clone(valid);
  getOperation(binaryAsJson).responses['200'].content['application/json'].schema = {
    type: 'string',
    format: 'binary'
  };

  assert.deepEqual(validateClientGenerationReadiness(arrayWithoutItems), [
    'GET /api/v1/identity/users: 200 application/json array schema must declare items'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(jsonNoContent), [
    'GET /api/v1/identity/users: 204 response must not declare JSON content'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(binaryAsJson), [
    'GET /api/v1/identity/users: 200 binary response must not use JSON media type'
  ]);
});

test('未注册的安全方案与显式公开 Operation 分别失败和通过', async () => {
  const valid = await readFixture('valid.openapi.json');
  const unknownSecurityScheme = clone(valid);
  getOperation(unknownSecurityScheme).security = [{ UnknownBearer: [] }];
  const publicOperation = clone(valid);
  getOperation(publicOperation).security = [];

  assert.deepEqual(validateClientGenerationReadiness(unknownSecurityScheme), [
    'GET /api/v1/identity/users: security scheme UnknownBearer is not defined'
  ]);
  assert.deepEqual(validateClientGenerationReadiness(publicOperation, {
    publicOperationIds: ['identityListHostUsers']
  }), []);
});
