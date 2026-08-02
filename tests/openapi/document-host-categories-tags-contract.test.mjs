import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/document-host-categories-tags-v1.json'
);
const categoryContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentCategoryContracts.cs'
);
const tagContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentTagContracts.cs'
);
const categoryEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentCategories/Endpoint.cs'
);
const tagEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentTags/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 文档分类与标签 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'document-host-categories-tags-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/document\/host\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^document\.(host_documents\.read|categories\.(read|create|update|delete)|tags\.(read|create|update|delete))$/u
      );
      assert.ok(typeof operation.successStatus === 'number');
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('Host 文档分类与标签 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const categoryContractsSource = await readFile(categoryContractsSourcePath, 'utf8');
  const tagContractsSource = await readFile(tagContractsSourcePath, 'utf8');
  const contractsSource = `${categoryContractsSource}\n${tagContractsSource}`;
  const categoryEndpointSource = await readFile(categoryEndpointSourcePath, 'utf8');
  const tagEndpointSource = await readFile(tagEndpointSourcePath, 'utf8');

  assert.match(categoryContractsSource, /record HostDocumentCategoryResponse/u);
  assert.match(categoryContractsSource, /record CreateHostDocumentCategoryRequest/u);
  assert.match(tagContractsSource, /record HostDocumentTagResponse/u);
  assert.match(tagContractsSource, /record CreateHostDocumentTagRequest/u);
  assert.match(
    categoryEndpointSource,
    /MapGroup\("\/api\/v1\/document\/host\/categories"\)/u
  );
  assert.match(
    tagEndpointSource,
    /MapGroup\("\/api\/v1\/document\/host\/tags"\)/u
  );

  const relativeRoutes = new Map([
    ['/api/v1/document/host/categories', {
      source: categoryEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/document/host/categories/{categoryId}', {
      source: categoryEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{categoryId:guid}",'],
        ['PUT', 'MapPut("/{categoryId:guid}",']
      ])
    }],
    ['/api/v1/document/host/categories/{categoryId}/delete', {
      source: categoryEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{categoryId:guid}/delete",']
      ])
    }],
    ['/api/v1/document/host/tags', {
      source: tagEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/document/host/tags/{tagId}', {
      source: tagEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{tagId:guid}",'],
        ['PUT', 'MapPut("/{tagId:guid}",']
      ])
    }],
    ['/api/v1/document/host/tags/{tagId}/delete', {
      source: tagEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{tagId:guid}/delete",']
      ])
    }]
  ]);

  for (const entry of contract.paths) {
    const route = relativeRoutes.get(entry.path);
    assert.ok(route, `未登记的路由组：${entry.path}`);
    for (const operation of entry.operations) {
      const marker = route.markers.get(operation.method);
      assert.ok(marker, `${entry.path} 缺少 ${operation.method}`);
      assert.match(
        route.source,
        new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u')
      );
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('List')) {
      continue;
    }

    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`${pascal}`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
