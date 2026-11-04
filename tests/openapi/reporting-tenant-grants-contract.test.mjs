import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

test('真实授权分页 OpenAPI 保留精确版本、鉴权、错误响应和 UUID 标识集合', () => {
 const snapshot = JSON.parse(readFileSync(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
 const operation = snapshot.paths['/api/v1/reporting/definitions/{definitionId}/versions/{versionNumber}/tenant-grants'].get;
 assert.equal(operation.operationId, 'reportingListTenantVersionGrants');
 assert.ok(operation.security.length);
 for (const status of ['200', '400', '401', '403', '404']) assert.ok(operation.responses[status]);
 assert.deepEqual(operation.parameters.filter(p => p.in === 'path').map(p => p.name).sort(), ['definitionId', 'versionNumber']);
 assert.deepEqual(operation.parameters.filter(p => p.in === 'query').map(p => p.name).sort(), ['page', 'pageSize']);
 const response = operation.responses['200'].content['application/json'].schema;
 const schema = snapshot.components.schemas[response.$ref.split('/').at(-1)];
 assert.equal(schema.properties.items.type, 'array');
 assert.equal(schema.properties.items.items.format, 'uuid');
 for (const field of ['page', 'pageSize', 'total']) assert.ok(schema.properties[field]);
});
