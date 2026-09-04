import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const scriptPath = path.join(
  repositoryRoot,
  'scripts/openapi/check-openapi-breaking-changes.mjs'
);

const baselineContract = {
  id: 'sample-v1',
  version: 1,
  description: '基线说明',
  paths: [
    {
      path: '/api/v1/samples',
      operations: [
        {
          method: 'GET',
          permission: 'samples.read',
          successStatus: 200,
          responseSchema: 'SampleResponsePage'
        },
        {
          method: 'POST',
          permission: 'samples.write',
          successStatus: 201,
          requestSchema: 'CreateSampleRequest',
          responseSchema: 'SampleResponse'
        }
      ]
    },
    {
      path: '/api/v1/samples/{sampleId}',
      operations: [
        {
          method: 'GET',
          permission: 'samples.read',
          successStatus: 200,
          responseSchema: 'SampleResponse'
        }
      ]
    }
  ],
  schemas: {
    SampleResponse: {
      properties: ['id', 'name']
    },
    SampleResponsePage: {
      properties: ['items', 'page', 'pageSize', 'total'],
      itemSchema: 'SampleResponse'
    },
    CreateSampleRequest: {
      properties: ['name']
    }
  }
};

const platformContract = {
  apiTitle: 'Full.NET API',
  documentName: 'v1',
  openApiJsonPath: '/openapi/v1.json',
  scalarUiPath: '/scalar/v1',
  securitySchemeName: 'Bearer',
  securitySchemeType: 'http',
  securitySchemeScheme: 'bearer'
};

function clone(value) {
  return structuredClone(value);
}

async function writeContractSet(directoryPath, contracts) {
  await mkdir(directoryPath, { recursive: true });

  await Promise.all(
    Object.entries(contracts).map(([fileName, contract]) =>
      writeFile(
        path.join(directoryPath, fileName),
        `${JSON.stringify(contract, null, 2)}\n`,
        'utf8'
      )
    )
  );
}

async function compareDirectories(baseline, current) {
  const temporaryRoot = await mkdtemp(
    path.join(os.tmpdir(), 'fullnet-openapi-compatibility-')
  );
  const baselineDirectory = path.join(temporaryRoot, 'baseline');
  const currentDirectory = path.join(temporaryRoot, 'current');

  try {
    await writeContractSet(baselineDirectory, baseline);
    await writeContractSet(currentDirectory, current);

    return spawnSync(
      process.execPath,
      [
        scriptPath,
        '--baseline-directory',
        baselineDirectory,
        '--current-directory',
        currentDirectory
      ],
      {
        cwd: repositoryRoot,
        encoding: 'utf8'
      }
    );
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
}

test('新增规范版本契约保持兼容，身份、路由键或 schema 结构冲突会失败', async () => {
  const currentContract = clone(baselineContract);
  currentContract.description = '新的说明不影响机器契约';
  currentContract.paths.reverse();
  currentContract.paths
    .find(({ path: contractPath }) => contractPath === '/api/v1/samples')
    .operations.reverse();
  currentContract.schemas.SampleResponse.properties = ['displayName', 'name', 'id'];
  currentContract.schemas.NewResponse = { properties: ['value'] };
  currentContract.paths.push({
    path: '/api/v1/samples/search',
    operations: [
      {
        method: 'GET',
        permission: 'samples.read',
        successStatus: 200,
        responseSchema: 'SampleResponsePage'
      }
    ]
  });

  const currentContracts = {
    'sample-v1.json': currentContract,
    'sample-v2.json': {
      id: 'sample-v2',
      version: 2,
      paths: [],
      schemas: {}
    }
  };
  const result = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    currentContracts
  );

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /OpenAPI compatibility check passed/);

  const duplicateResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    {
      ...currentContracts,
      'sample-v1-copy.json': clone(currentContract)
    }
  );

  assert.equal(duplicateResult.status, 1);
  assert.match(
    duplicateResult.stderr,
    /duplicate contract identity: sample-v1-copy\.json and sample-v1\.json use id=sample-v1, version=1/
  );

  const invalidIdentityContract = clone(currentContract);
  invalidIdentityContract.version = '1';
  const invalidIdentityResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    {
      ...currentContracts,
      'sample-invalid-version.json': invalidIdentityContract
    }
  );

  assert.equal(invalidIdentityResult.status, 1);
  assert.match(
    invalidIdentityResult.stderr,
    /invalid contract identity: sample-invalid-version\.json requires a non-empty string id and positive integer version/
  );

  const misnamedContract = clone(currentContract);
  misnamedContract.id = 'sample-v3';
  misnamedContract.version = 3;
  const misnamedResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    {
      ...currentContracts,
      'sample-misnamed.json': misnamedContract
    }
  );

  assert.equal(misnamedResult.status, 1);
  assert.match(
    misnamedResult.stderr,
    /contract identity mismatch: sample-misnamed\.json must be named sample-v3\.json/
  );

  const mismatchedVersionContract = clone(currentContract);
  mismatchedVersionContract.id = 'sample-v4';
  mismatchedVersionContract.version = 3;
  const mismatchedVersionResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    {
      ...currentContracts,
      'sample-v4.json': mismatchedVersionContract
    }
  );

  assert.equal(mismatchedVersionResult.status, 1);
  assert.match(
    mismatchedVersionResult.stderr,
    /contract version mismatch: sample-v4\.json id suffix v4 does not match version=3/
  );

  const duplicatePathContract = clone(baselineContract);
  duplicatePathContract.paths.push(clone(duplicatePathContract.paths[0]));
  const duplicatePathResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': duplicatePathContract }
  );

  assert.equal(duplicatePathResult.status, 1);
  assert.match(
    duplicatePathResult.stderr,
    /duplicate contract path: sample-v1\.json \/api\/v1\/samples/
  );

  const duplicateOperationContract = clone(baselineContract);
  duplicateOperationContract.paths[0].operations.push(
    clone(duplicateOperationContract.paths[0].operations[0])
  );
  const duplicateOperationResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': duplicateOperationContract }
  );

  assert.equal(duplicateOperationResult.status, 1);
  assert.match(
    duplicateOperationResult.stderr,
    /duplicate contract operation: sample-v1\.json GET \/api\/v1\/samples/
  );

  const duplicateSchemaPropertyContract = clone(baselineContract);
  duplicateSchemaPropertyContract.schemas.SampleResponse.properties.push('id');
  const duplicateSchemaPropertyResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': duplicateSchemaPropertyContract }
  );

  assert.equal(duplicateSchemaPropertyResult.status, 1);
  assert.match(
    duplicateSchemaPropertyResult.stderr,
    /duplicate schema property: sample-v1\.json SampleResponse\.id/
  );

  const danglingSchemaReferenceContract = clone(baselineContract);
  danglingSchemaReferenceContract.paths.push({
    path: '/api/v1/samples/import',
    operations: [
      {
        method: 'POST',
        permission: 'samples.write',
        successStatus: 202,
        requestSchema: 'MissingImportRequest',
        responseSchema: 'MissingImportResponse'
      }
    ]
  });
  danglingSchemaReferenceContract.schemas.NewResponsePage = {
    properties: ['items'],
    itemSchema: 'MissingResponse'
  };
  const danglingSchemaReferenceResult = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': danglingSchemaReferenceContract }
  );

  assert.equal(danglingSchemaReferenceResult.status, 1);
  assert.match(
    danglingSchemaReferenceResult.stderr,
    /unknown schema reference: sample-v1\.json POST \/api\/v1\/samples\/import requestSchema=MissingImportRequest/
  );
  assert.match(
    danglingSchemaReferenceResult.stderr,
    /unknown schema reference: sample-v1\.json POST \/api\/v1\/samples\/import responseSchema=MissingImportResponse/
  );
  assert.match(
    danglingSchemaReferenceResult.stderr,
    /unknown schema reference: sample-v1\.json NewResponsePage itemSchema=MissingResponse/
  );
});

test('删除版本化契约文件会失败', async () => {
  const result = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    {}
  );

  assert.equal(result.status, 1);
  assert.match(result.stderr, /contract removed: sample-v1\.json/);
});

test('删除路径或操作以及改变稳定操作字段会失败', async () => {
  const currentContract = clone(baselineContract);
  currentContract.paths = currentContract.paths.filter(
    ({ path: contractPath }) =>
      contractPath !== '/api/v1/samples/{sampleId}'
  );
  const collectionPath = currentContract.paths[0];
  collectionPath.operations = collectionPath.operations.filter(
    ({ method }) => method !== 'GET'
  );
  const createOperation = collectionPath.operations[0];
  createOperation.permission = 'samples.admin';
  createOperation.successStatus = 200;
  createOperation.requestSchema = 'ReplaceSampleRequest';
  createOperation.responseSchema = 'NewResponse';

  const result = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': currentContract }
  );

  assert.equal(result.status, 1);
  assert.match(
    result.stderr,
    /path removed: sample-v1\.json \/api\/v1\/samples\/\{sampleId\}/
  );
  assert.match(
    result.stderr,
    /operation removed: sample-v1\.json GET \/api\/v1\/samples/
  );
  assert.match(
    result.stderr,
    /operation changed: sample-v1\.json POST \/api\/v1\/samples permission/
  );
  assert.match(
    result.stderr,
    /operation changed: sample-v1\.json POST \/api\/v1\/samples successStatus/
  );
  assert.match(
    result.stderr,
    /operation changed: sample-v1\.json POST \/api\/v1\/samples requestSchema/
  );
  assert.match(
    result.stderr,
    /operation changed: sample-v1\.json POST \/api\/v1\/samples responseSchema/
  );
});

test('删除 schema、删除属性或改变 itemSchema 会失败', async () => {
  const currentContract = clone(baselineContract);
  delete currentContract.schemas.CreateSampleRequest;
  currentContract.schemas.SampleResponse.properties = ['name'];
  currentContract.schemas.SampleResponsePage.itemSchema = 'NewResponse';

  const result = await compareDirectories(
    { 'sample-v1.json': baselineContract },
    { 'sample-v1.json': currentContract }
  );

  assert.equal(result.status, 1);
  assert.match(
    result.stderr,
    /schema removed: sample-v1\.json CreateSampleRequest/
  );
  assert.match(
    result.stderr,
    /schema property removed: sample-v1\.json SampleResponse\.id/
  );
  assert.match(
    result.stderr,
    /schema itemSchema changed: sample-v1\.json SampleResponsePage/
  );
});

test('改变平台 OpenAPI 稳定配置会失败', async () => {
  const currentContract = clone(platformContract);
  currentContract.description = '说明字段允许变化';
  currentContract.securitySchemeScheme = 'basic';

  const result = await compareDirectories(
    { 'platform-api-documentation-v1.json': platformContract },
    { 'platform-api-documentation-v1.json': currentContract }
  );

  assert.equal(result.status, 1);
  assert.match(
    result.stderr,
    /stable setting changed: platform-api-documentation-v1\.json securitySchemeScheme/
  );
});

test('修复历史契约的版本后缀元数据不被误报为破坏变化', async () => {
  const malformedBaseline = clone(baselineContract);
  malformedBaseline.version = 2;

  const result = await compareDirectories(
    { 'sample-v1.json': malformedBaseline },
    { 'sample-v1.json': baselineContract }
  );

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /OpenAPI compatibility check passed/u);
});

test('Vue 覆盖清单允许追加 API 绑定但拒绝改写既有绑定', async () => {
  const baseline = {
    schemaVersion: 1,
    entries: [{ apiModule: 'ui/admin/src/api/samples.ts' }],
    consumerModules: [{
      apiModule: 'ui/admin/src/api/samples.ts',
      consumers: ['ui/admin/src/views/SamplesView.vue']
    }]
  };
  const additive = clone(baseline);
  additive.entries.push({ apiModule: 'ui/admin/src/api/new-samples.ts' });
  additive.consumerModules.push({
    apiModule: 'ui/admin/src/api/new-samples.ts',
    consumers: ['ui/admin/src/views/NewSamplesView.vue']
  });

  const additiveResult = await compareDirectories(
    { 'vue-client-coverage-v1.json': baseline },
    { 'vue-client-coverage-v1.json': additive }
  );
  assert.equal(additiveResult.status, 0, additiveResult.stderr);

  const rewritten = clone(additive);
  rewritten.consumerModules[0].consumers = ['ui/admin/src/views/OtherView.vue'];
  const rewrittenResult = await compareDirectories(
    { 'vue-client-coverage-v1.json': baseline },
    { 'vue-client-coverage-v1.json': rewritten }
  );
  assert.equal(rewrittenResult.status, 1);
  assert.match(
    rewrittenResult.stderr,
    /stable setting changed: vue-client-coverage-v1\.json consumerModules/u
  );
});

test('客户端生成清单允许 pilot 晋升为 generated，禁止降级或改写绑定', async () => {
  const baseline = {
    schemaVersion: 1,
    entries: [
      {
        operationId: 'identityListHostUsers',
        apiModule: 'ui/admin/src/api/users.ts',
        generatedGroup: 'identity-host-users',
        status: 'pilot'
      }
    ]
  };

  const promoted = clone(baseline);
  promoted.entries[0].status = 'generated';
  promoted.entries.push({
    operationId: 'filesListHostFiles',
    apiModule: 'ui/admin/src/api/host-files.ts',
    generatedGroup: 'files-host-files',
    status: 'pilot'
  });

  const promotedResult = await compareDirectories(
    { 'client-generation-manifest-v1.json': baseline },
    { 'client-generation-manifest-v1.json': promoted }
  );
  assert.equal(promotedResult.status, 0, promotedResult.stderr);

  const demoted = clone(promoted);
  demoted.entries[0].status = 'pilot';
  const demotedResult = await compareDirectories(
    { 'client-generation-manifest-v1.json': promoted },
    { 'client-generation-manifest-v1.json': demoted }
  );
  assert.equal(demotedResult.status, 1);
  assert.match(
    demotedResult.stderr,
    /stable setting changed: client-generation-manifest-v1\.json entries/u
  );

  const rewritten = clone(promoted);
  rewritten.entries[0].apiModule = 'ui/admin/src/api/other.ts';
  const rewrittenResult = await compareDirectories(
    { 'client-generation-manifest-v1.json': promoted },
    { 'client-generation-manifest-v1.json': rewritten }
  );
  assert.equal(rewrittenResult.status, 1);
  assert.match(
    rewrittenResult.stderr,
    /stable setting changed: client-generation-manifest-v1\.json entries/u
  );
});

test('标准客户端 OpenAPI 快照允许追加 path/schema/tag，禁止改写既有 Operation', async () => {
  const baseline = {
    openapi: '3.1.0',
    info: { title: 'Full.NET client', version: '1.0.0' },
    tags: [{ name: 'IdentityHostUsers' }],
    paths: {
      '/api/v1/identity/users': {
        get: {
          operationId: 'identityListHostUsers',
          tags: ['IdentityHostUsers'],
          responses: { '200': { description: 'OK' } }
        }
      }
    },
    components: {
      schemas: {
        HostUserResponse: { type: 'object', properties: { id: { type: 'string' } } }
      },
      securitySchemes: {
        Bearer: { type: 'http', scheme: 'bearer' }
      }
    }
  };

  const additive = clone(baseline);
  additive.tags.push({ name: 'IdentityHostRoles' });
  additive.paths['/api/v1/identity/roles'] = {
    get: {
      operationId: 'identityListHostRoles',
      tags: ['IdentityHostRoles'],
      responses: { '200': { description: 'OK' } }
    }
  };
  additive.components.schemas.HostRoleResponse = {
    type: 'object',
    properties: { id: { type: 'string' } }
  };
  additive.paths['/api/v1/identity/users'].get.responses['400'] = {
    description: 'Bad Request'
  };

  const additiveResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': additive }
  );
  assert.equal(additiveResult.status, 0, additiveResult.stderr);

  const rewritten = clone(additive);
  rewritten.paths['/api/v1/identity/users'].get.operationId = 'identityListHostUsersChanged';
  const rewrittenResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': additive },
    { 'fullnet-client-v1.openapi.json': rewritten }
  );
  assert.equal(rewrittenResult.status, 1);
  assert.match(
    rewrittenResult.stderr,
    /stable setting changed: fullnet-client-v1\.openapi\.json paths/u
  );
});

test('标准客户端 OpenAPI 允许纠正既有 Workflow 严格草稿元数据但拒绝借机改写 schema', async () => {
  const baseline = {
    openapi: '3.1.0',
    info: { title: 'Full.NET client', version: '1.0.0' },
    tags: [],
    paths: {},
    components: {
      schemas: {
        WorkflowDefinitionDraft: {
          type: 'object',
          properties: { schemaVersion: { type: 'integer' } }
        }
      },
      securitySchemes: {}
    }
  };

  const repaired = clone(baseline);
  repaired.components.schemas.WorkflowDefinitionDraft.additionalProperties = false;

  const repairedResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': repaired }
  );
  assert.equal(repairedResult.status, 0, repairedResult.stderr);

  const rewritten = clone(repaired);
  rewritten.components.schemas.WorkflowDefinitionDraft.properties.schemaVersion = {
    type: 'string'
  };
  const rewrittenResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': rewritten }
  );
  assert.equal(rewrittenResult.status, 1);
  assert.match(
    rewrittenResult.stderr,
    /stable setting changed: fullnet-client-v1\.openapi\.json components/u
  );
});

test('标准客户端 OpenAPI 允许纠正 JSON 省略字段必填性但拒绝扩大豁免', async () => {
  const baseline = {
    openapi: '3.1.0',
    info: { title: 'Full.NET client', version: '1.0.0' },
    tags: [],
    paths: {},
    components: {
      schemas: {
        CodeGenerationPreviewRequest: {
          type: 'object',
          properties: {
            hasVersion: { type: ['null', 'boolean'] },
            columns: { type: 'array', items: { type: 'string' } }
          },
          required: ['hasVersion', 'columns']
        }
      },
      securitySchemes: {}
    }
  };

  const repaired = clone(baseline);
  repaired.components.schemas.CodeGenerationPreviewRequest.required = ['columns'];
  const repairedResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': repaired }
  );
  assert.equal(repairedResult.status, 0, repairedResult.stderr);

  const widened = clone(repaired);
  widened.components.schemas.CodeGenerationPreviewRequest.required = [];
  const widenedResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': widened }
  );
  assert.equal(widenedResult.status, 1);
  assert.match(
    widenedResult.stderr,
    /stable setting changed: fullnet-client-v1\.openapi\.json components/u
  );
});

test('标准客户端 OpenAPI 只允许已审查的可选请求与响应字段扩展', async () => {
  const baseline = {
    openapi: '3.1.0',
    info: { title: 'Full.NET client', version: '1.0.0' },
    tags: [],
    paths: {
      '/api/v1/notifications/host-announcements': {
        get: {
          operationId: 'notificationsListHostAnnouncements',
          parameters: [{ in: 'query', name: 'page', schema: { type: 'integer' } }],
          responses: { '200': { description: 'OK' } }
        }
      }
    },
    components: {
      schemas: {
        CreateHostAnnouncementRequest: {
          type: 'object',
          properties: { title: { type: 'string' } },
          required: ['title']
        },
        HostAnnouncementResponse: {
          type: 'object',
          properties: { id: { type: 'string' } },
          required: ['id']
        }
      },
      securitySchemes: {}
    }
  };

  const additive = clone(baseline);
  additive.paths['/api/v1/notifications/host-announcements'].get.parameters.unshift({
    in: 'query',
    name: 'title',
    schema: { type: 'string' }
  });
  additive.components.schemas.CreateHostAnnouncementRequest.properties.kind = {
    type: ['null', 'string']
  };
  additive.components.schemas.HostAnnouncementResponse.properties.kind = { type: 'string' };
  additive.components.schemas.HostAnnouncementResponse.required.splice(1, 0, 'kind');

  const additiveResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': additive }
  );
  assert.equal(additiveResult.status, 0, additiveResult.stderr);

  const rewritten = clone(additive);
  rewritten.components.schemas.CreateHostAnnouncementRequest.required.push('kind');
  const rewrittenResult = await compareDirectories(
    { 'fullnet-client-v1.openapi.json': baseline },
    { 'fullnet-client-v1.openapi.json': rewritten }
  );
  assert.equal(rewrittenResult.status, 1);
  assert.match(
    rewrittenResult.stderr,
    /stable setting changed: fullnet-client-v1\.openapi\.json components/u
  );
});

test('Git ref 模式可确认当前 contracts 相对 HEAD 无破坏变化', () => {
  const result = spawnSync(
    process.execPath,
    [scriptPath, '--base-ref', 'HEAD', '--repository-root', repositoryRoot],
    {
      cwd: repositoryRoot,
      encoding: 'utf8'
    }
  );

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /OpenAPI compatibility check passed/);
});

test('无效 Git ref 返回使用错误而不是兼容结果', () => {
  const result = spawnSync(
    process.execPath,
    [
      scriptPath,
      '--base-ref',
      'refs/heads/does-not-exist',
      '--repository-root',
      repositoryRoot
    ],
    {
      cwd: repositoryRoot,
      encoding: 'utf8'
    }
  );

  assert.equal(result.status, 2);
  assert.match(result.stderr, /Unable to load OpenAPI baseline from Git ref/);
});
