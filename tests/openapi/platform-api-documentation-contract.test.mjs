import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/platform-api-documentation-v1.json'
);
const openApiSourcePath = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Hosting/OpenApi/FullNetOpenApiExtensions.cs'
);
const programSourcePath = path.join(
  repositoryRoot,
  'src/Hosts/Full.NET.Host.Api/Program.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('平台接口文档 OpenAPI 夹具结构完整', async () => {
  const contract = await loadContract();

  assert.deepEqual(Object.keys(contract).sort(), [
    'apiTitle',
    'documentName',
    'openApiJsonPath',
    'scalarUiPath',
    'securitySchemeName',
    'securitySchemeScheme',
    'securitySchemeType'
  ]);
  assert.equal(contract.openApiJsonPath, '/openapi/v1.json');
  assert.equal(contract.scalarUiPath, '/scalar/v1');
  assert.equal(contract.securitySchemeType, 'http');
  assert.equal(contract.securitySchemeScheme, 'bearer');
});

test('平台接口文档 OpenAPI 夹具与宿主常量和映射源码一致', async () => {
  const contract = await loadContract();
  const openApiSource = await readFile(openApiSourcePath, 'utf8');
  const programSource = await readFile(programSourcePath, 'utf8');

  const expectedConstants = new Map([
    ['DocumentName', contract.documentName],
    ['ApiTitle', contract.apiTitle],
    ['OpenApiJsonPath', contract.openApiJsonPath],
    ['ScalarUiPath', contract.scalarUiPath]
  ]);
  for (const [constantName, value] of expectedConstants) {
    assert.ok(
      openApiSource.includes(`const string ${constantName} = "${value}"`),
      `宿主 OpenAPI 常量 ${constantName} 与夹具不一致`
    );
  }

  assert.ok(
    openApiSource.includes(`SecuritySchemes["${contract.securitySchemeName}"]`),
    `宿主缺少安全方案：${contract.securitySchemeName}`
  );
  assert.match(openApiSource, /Type = SecuritySchemeType\.Http/u);
  assert.ok(openApiSource.includes(`Scheme = "${contract.securitySchemeScheme}"`));
  assert.match(programSource, /AddFullNetOpenApi\(\)/u);
  assert.match(programSource, /MapFullNetOpenApi\(\)/u);
  assert.match(programSource, /MapScalarApiReference/u);
});
