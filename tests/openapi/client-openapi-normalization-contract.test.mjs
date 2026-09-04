import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  normalizeClientOpenApi,
  serializeClientOpenApi
} from '../../scripts/openapi/normalize-client-openapi.mjs';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '..',
  '..'
);

function createDocument({ reversed = false } = {}) {
  const hostUserSchema = {
    type: 'object',
    properties: reversed
      ? {
          manager: { $ref: '#/components/schemas/HostUser' },
          id: { type: 'string', format: 'uuid' }
        }
      : {
          id: { type: 'string', format: 'uuid' },
          manager: { $ref: '#/components/schemas/HostUser' }
        }
  };
  const listOperation = {
    operationId: 'identityListHostUsers',
    tags: ['IdentityHostUsers'],
    security: [{ Bearer: [] }],
    parameters: [
      { name: 'page', in: 'query', schema: { type: 'integer' } },
      { name: 'pageSize', in: 'query', schema: { type: 'integer' } }
    ],
    responses: {
      200: {
        description: '成功',
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/HostUserPage' }
          }
        }
      }
    }
  };
  const unselectedOperation = {
    operationId: 'identityListHostRoles',
    tags: ['IdentityHostRoles'],
    security: [{ Bearer: [] }],
    responses: {
      200: {
        description: '成功',
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/HostRolePage' }
          }
        }
      }
    }
  };

  return {
    openapi: '3.1.0',
    servers: [{ url: 'http://localhost:5149' }],
    'x-generated-at': '2026-08-21T00:00:00Z',
    info: reversed
      ? { version: 'v1', title: 'Full.NET API' }
      : { title: 'Full.NET API', version: 'v1' },
    paths: reversed
      ? {
          '/api/v1/identity/roles': { get: unselectedOperation },
          '/api/v1/identity/users': { get: listOperation }
        }
      : {
          '/api/v1/identity/users': { get: listOperation },
          '/api/v1/identity/roles': { get: unselectedOperation }
        },
    components: {
      securitySchemes: {
        Unused: { type: 'apiKey', name: 'X-Unused', in: 'header' },
        Bearer: { type: 'http', scheme: 'bearer' }
      },
      schemas: reversed
        ? {
            HostRolePage: { type: 'object' },
            HostUser: hostUserSchema,
            HostUserPage: {
              type: 'object',
              properties: {
                items: {
                  type: 'array',
                  items: { $ref: '#/components/schemas/HostUser' }
                }
              }
            }
          }
        : {
            HostUserPage: {
              type: 'object',
              properties: {
                items: {
                  type: 'array',
                  items: { $ref: '#/components/schemas/HostUser' }
                }
              }
            },
            HostUser: hostUserSchema,
            HostRolePage: { type: 'object' }
          }
    }
  };
}

test('对象与 Path/Schema 输入顺序不同仍产生逐字节相同快照', () => {
  const operationIds = ['identityListHostUsers'];
  const left = serializeClientOpenApi(normalizeClientOpenApi(
    createDocument(),
    operationIds
  ));
  const right = serializeClientOpenApi(normalizeClientOpenApi(
    createDocument({ reversed: true }),
    operationIds
  ));

  assert.equal(left, right);
  assert.match(left, /\n$/u);
  assert.equal(left.includes('\r\n'), false);
});

test('只保留 manifest 精确 Operation、使用中的安全方案与传递循环引用', () => {
  const normalized = normalizeClientOpenApi(
    createDocument(),
    ['identityListHostUsers']
  );

  assert.deepEqual(Object.keys(normalized.paths), ['/api/v1/identity/users']);
  assert.equal(
    normalized.paths['/api/v1/identity/users'].get.operationId,
    'identityListHostUsers'
  );
  assert.deepEqual(Object.keys(normalized.components.securitySchemes), ['Bearer']);
  assert.deepEqual(Object.keys(normalized.components.schemas), [
    'HostUser',
    'HostUserPage'
  ]);
  assert.equal(
    normalized.components.schemas.HostUser.properties.manager.$ref,
    '#/components/schemas/HostUser'
  );
});

test('移除 servers、生成时间与开发机 URL，但保留有序协议数组', () => {
  const normalized = normalizeClientOpenApi(
    createDocument(),
    ['identityListHostUsers']
  );
  const operation = normalized.paths['/api/v1/identity/users'].get;

  assert.equal('servers' in normalized, false);
  assert.equal('x-generated-at' in normalized, false);
  assert.equal(serializeClientOpenApi(normalized).includes('localhost:5149'), false);
  assert.deepEqual(operation.parameters.map(parameter => parameter.name), [
    'page',
    'pageSize'
  ]);
});

test('manifest 引用缺失 Operation 时失败关闭', () => {
  assert.throws(
    () => normalizeClientOpenApi(createDocument(), [
      'identityListHostUsers',
      'filesUploadHostFile'
    ]),
    /manifest operationId not found: filesUploadHostFile/u
  );
});

test('manifest 与规范快照精确登记生成操作且 CI 只执行离线 check', async () => {
  const manifest = JSON.parse(await readFile(path.join(
    repositoryRoot,
    'contracts',
    'openapi',
    'client-generation-manifest-v1.json'
  ), 'utf8'));
  const snapshot = JSON.parse(await readFile(path.join(
    repositoryRoot,
    'contracts',
    'openapi',
    'fullnet-client-v1.openapi.json'
  ), 'utf8'));
  const packageJson = JSON.parse(await readFile(path.join(
    repositoryRoot,
    'package.json'
  ), 'utf8'));
  const workflow = await readFile(path.join(
    repositoryRoot,
    '.github',
    'workflows',
    'ci.yml'
  ), 'utf8');

  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.entries.length, 289);
  assert.equal(new Set(manifest.entries.map(entry => entry.operationId)).size, 289);
  assert.deepEqual(
    manifest.entries
      .filter(entry => entry.generatedGroup === 'workflow-forms')
      .map(entry => entry.operationId),
    [
      'workflowListForms',
      'workflowGetForm',
      'workflowCreateForm',
      'workflowUpdateFormDraft',
      'workflowPublishForm',
      'workflowGetFormComponentCatalog',
      'workflowGetFormVersion'
    ]
  );
  assert.deepEqual(
    [...new Set(manifest.entries.map(entry => entry.generatedGroup))].sort(),
    [
      'auditing-host-access-logs',
      'auditing-host-exception-logs',
      'auditing-host-operation-logs',
      'auditing-host-outbound-call-logs',
      'code-generation-catalog',
      'code-generation-previews',
      'code-generation-runs',
      'code-generation-templates',
      'document-host-categories',
      'document-host-items',
      'document-host-permissions',
      'document-host-recycle-bin',
      'document-host-shares',
      'document-host-statistics',
      'document-host-tags',
      'files-host-files',
      'identity-auth-session',
      'identity-host-api-keys',
      'identity-host-menus',
      'identity-host-modules',
      'identity-host-online-sessions',
      'identity-host-roles',
      'identity-host-users',
      'identity-me',
      'identity-super-administrators',
      'identity-totp-enrollment',
      'jobs-host-job-health',
      'jobs-host-job-schedules',
      'jobs-host-jobs',
      'notifications-bindings',
      'notifications-deliveries',
      'notifications-host-announcements',
      'notifications-inbox-messages',
      'notifications-provider-profiles',
      'notifications-recipient-endpoints',
      'notifications-templates',
      'observability-log-files',
      'organization-host-user-management',
      'organization-tenant-position-levels',
      'organization-tenant-positions',
      'organization-tenant-units',
      'organization-tenant-user-positions',
      'organization-tenant-user-units',
      'platform-host-dashboard',
      'serial-numbers-rules',
      'settings-host-config-entries',
      'settings-host-diagnostic-policy',
      'settings-host-dict-types',
      'settings-host-enum-catalogs',
      'settings-tenant-dict-types',
      'tenancy-host-tenant-packages',
      'tenancy-host-tenants',
      'workflow-cc',
      'workflow-definitions',
      'workflow-forms',
      'workflow-instances',
      'workflow-todos'
    ]
  );
  assert.equal(
    manifest.entries.every(entry => entry.status === 'generated'),
    true
  );
  assert.equal(
    Object.values(snapshot.paths).flatMap(pathItem => Object.values(pathItem)).length,
    289
  );
  assert.equal(
    snapshot.paths['/api/v1/workflow/forms/component-catalog'].get.operationId,
    'workflowGetFormComponentCatalog'
  );
  assert.equal(
    packageJson.scripts['openapi:client:snapshot'],
    'node scripts/openapi/snapshot-client-openapi.mjs'
  );
  assert.match(
    workflow,
    /name: Verify canonical client OpenAPI snapshot\s+run: pnpm openapi:client:snapshot --check --offline/u
  );
});
