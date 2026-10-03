import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CRUD_ARTIFACTS } from './application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './application-crud-module.mjs';

// 只验收已创建应用的源码升级；业务表仍需单独评审并执行成对的增量迁移。
export function verifyApplicationCrudSchemaSourceUpgrade(appRoot, {
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-schema-source-upgrade'),
  run = spawnSync,
} = {}) {
  mkdirSync(reportDirectory, { recursive: true });
  const schemaPath = join(appRoot, 'verification/CrudGeneration/schema.json');
  const moduleRoot = join(appRoot, 'src/Demo.Modules.Catalog');
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const moduleTarget = join(appRoot, 'verification/CrudGeneration/module-target.json');
  const protectedPaths = [
    join(moduleRoot, 'Product.manual.cs'),
    join(moduleRoot, 'CatalogModule.cs'),
    join(appRoot, 'src/Demo.Composition/ApplicationModuleCatalog.cs'),
    join(appRoot, 'src/Demo.Modules.Catalog/CatalogAuthorizationContributor.cs'),
    join(appRoot, 'ui/admin/src/router/index.ts'),
  ];
  const snapshot = () => new Map(protectedPaths.map(path => [path, readFileSync(path)]));
  const execute = (stage, args) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    writeFileSync(join(reportDirectory, `${stage}.json`), JSON.stringify({ status: result.status,
      signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, `${stage} process failed`);
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  const actions = (output, paths, allowUpdate, marker) => {
    const lines = output.split(/\r?\n/u).filter(Boolean);
    if (marker) assert.ok(lines.includes(marker), `missing ${marker}`);
    const fileLines = lines.filter(line => line !== marker);
    assert.equal(fileLines.length, paths.length, 'unexpected generated artifact count');
    const found = new Map();
    for (const line of fileLines) {
      const match = /^(Update|Unchanged) (.+)$/u.exec(line);
      assert.ok(match, `unexpected generation action: ${line}`);
      assert.ok(paths.includes(match[2]), `unexpected generated path: ${match[2]}`);
      assert.equal(found.has(match[2]), false, `duplicate generated path: ${match[2]}`);
      found.set(match[2], match[1]);
    }
    for (const path of paths) {
      assert.ok(found.has(path), `missing generated path: ${path}`);
      if (!allowUpdate) assert.equal(found.get(path), 'Unchanged', `repeat changed ${path}`);
    }
    return found;
  };

  const before = snapshot();
  const schema = JSON.parse(readFileSync(schemaPath, 'utf8'));
  assert.equal(schema.columns.some(column => column.databaseName === 'Description'), false,
    'schema upgrade requires the original product schema');
  schema.columns.push({ databaseName: 'Description', clrPropertyName: 'Description',
    jsonPropertyName: 'description', scalarType: 'String', isNullable: true, maxLength: 500 });
  writeFileSync(schemaPath, JSON.stringify(schema, null, 2) + '\n');

  const sourceArgs = ['exec', cli, '--schema', schemaPath, '--workspace', appRoot, '--apply'];
  const changedSource = actions(execute('source-update', sourceArgs), CRUD_ARTIFACTS, true);
  for (const path of ['backend/ProductContracts.g.cs', 'contracts/openapi/products.generated.openapi.json',
    'templates/migrations/SqlServer/CreateProduct.sql.template',
    'templates/migrations/MySql/CreateProduct.sql.template']) {
    assert.equal(changedSource.get(path), 'Update', `schema change did not update ${path}`);
    assert.match(readFileSync(join(appRoot, path), 'utf8'), /Description|description/u,
      `upgraded artifact does not contain Description: ${path}`);
  }
  actions(execute('source-repeat', sourceArgs), CRUD_ARTIFACTS, false);

  const moduleArgs = ['exec', cli, 'apply-module-integration', '--schema', schemaPath,
    '--repository', appRoot, '--target', moduleTarget];
  const marker = 'Validated ModuleCompilation src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj';
  const changedModule = actions(execute('module-update', moduleArgs), MODULE_ARTIFACTS, true, marker);
  assert.equal(changedModule.get('Generated/Product/ProductContracts.g.cs'), 'Update',
    'schema change did not update module contracts');
  actions(execute('module-repeat', moduleArgs), MODULE_ARTIFACTS, false, marker);
  execute('api-build', ['build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(snapshot(), before, 'schema source upgrade changed application-owned files');

  const result = { nullableColumn: 'Description', sourceUpdated: [...changedSource.values()].filter(value => value === 'Update').length,
    moduleUpdated: [...changedModule.values()].filter(value => value === 'Update').length,
    repeatUnchanged: true, manualPreserved: true, apiCompiled: true, databaseMigrationApplied: false };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
