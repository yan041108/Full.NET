import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, posix } from 'node:path';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { CRUD_ARTIFACTS, applicationCrudArtifactPath } from './application-crud-generation.mjs';
import { MODULE_ARTIFACTS, assertHostIntegrationReceipt } from './application-crud-module.mjs';

// 源码升级通过同一完整目标原子接入；业务表仍需显式采纳成对迁移。
export function verifyApplicationCrudSchemaSourceUpgrade(appRoot, {
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-schema-source-upgrade'), run = spawnSync,
} = {}) {
  mkdirSync(reportDirectory, { recursive: true });
  const schemaPath = join(appRoot, 'verification/CrudGeneration/schema.json');
  const vueTarget = join(appRoot, 'verification/CrudGeneration/vue-target.json');
  const targetPath = existsSync(vueTarget) ? vueTarget : join(appRoot, 'verification/CrudGeneration/module-target.json');
  const target = JSON.parse(readFileSync(targetPath, 'utf8'));
  const bindings = [target.moduleEntryPointPath, target.compositionProjectPath,
    target.compositionCatalogPath, target.authorizationContributorPath, ...(target.clientRoute ? [target.vueRouterPath] : [])];
  assert.ok(bindings.every(path => typeof path === 'string' && path.length > 0), 'complete target paths are required');
  const adopted = target.clientRoute ? [target.clientRoute.vueComponentPath,
    ...['products.generated.ts', 'products-page.generated.ts'].map(name => posix.join(posix.dirname(target.clientRoute.vueComponentPath), name))] : [];
  const modulePaths = MODULE_ARTIFACTS.map(path => 'src/Demo.Modules.Catalog/' + path);
  const expectedPaths = [...CRUD_ARTIFACTS, ...modulePaths, ...adopted, ...bindings];
  const protectedPaths = [...bindings, target.moduleProjectPath, 'src/Demo.Modules.Catalog/Product.manual.cs'];
  const snapshot = () => new Map(protectedPaths.map(path => [path, readFileSync(join(appRoot, path))]));
  const before = snapshot();
  const schema = JSON.parse(readFileSync(schemaPath, 'utf8'));
  assert.equal(schema.entityCapabilities?.ownershipMode, 'none', 'this sample requires its original ownership mode');
  assert.equal(schema.columns.some(column => column.databaseName === 'Description'), false, 'schema upgrade requires the original product schema');
  schema.columns.push({ databaseName: 'Description', clrPropertyName: 'Description',
    jsonPropertyName: 'description', scalarType: 'String', isNullable: true, maxLength: 500 });
  writeFileSync(schemaPath, JSON.stringify(schema, null, 2) + '\n');

  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, 'apply-host-integration', '--schema', schemaPath, '--repository', appRoot, '--target', targetPath];
  const execute = (stage, command, commandArgs) => {
    const result = run(command, commandArgs, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ status: result.status,
      signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  const actions = (output, allowUpdate) => {
    assertHostIntegrationReceipt(output, target.moduleProjectPath);
    const lines = output.split(/\r?\n/u).filter(line => line && !line.startsWith('Applied Operation ')
      && line !== 'Applied HostIntegration ' + target.moduleProjectPath);
    assert.equal(lines.length, expectedPaths.length, 'unexpected complete integration artifact count');
    const found = new Map();
    for (const line of lines) {
      const match = /^(Update|Unchanged) (.+)$/u.exec(line);
      assert.ok(match, 'unexpected generation action: ' + line);
      assert.ok(expectedPaths.includes(match[2]), 'unexpected generated path: ' + match[2]);
      assert.equal(found.has(match[2]), false, 'duplicate generated path: ' + match[2]);
      found.set(match[2], match[1]);
    }
    for (const path of expectedPaths) {
      assert.ok(found.has(path), 'missing generated path: ' + path);
      if (!allowUpdate || bindings.includes(path)) assert.equal(found.get(path), 'Unchanged', 'unexpected update: ' + path);
    }
    return found;
  };
  const changed = actions(execute('source-update', 'dotnet', args), true);
  for (const path of ['backend/ProductContracts.g.cs', 'contracts/openapi/products.generated.openapi.json',
    'templates/migrations/SqlServer/CreateProduct.sql.template', 'templates/migrations/MySql/CreateProduct.sql.template'].map(applicationCrudArtifactPath)) {
    assert.equal(changed.get(path), 'Update', 'schema change did not update source: ' + path);
    assert.match(readFileSync(join(appRoot, path), 'utf8'), /Description|description/u, 'missing Description: ' + path);
  }
  assert.equal(changed.get('src/Demo.Modules.Catalog/Generated/Product/ProductContracts.g.cs'), 'Update', 'schema change did not update module contracts');
  assert.deepEqual(snapshot(), before, 'source upgrade changed application-owned bindings or manual code');
  actions(execute('source-repeat', 'dotnet', args), false);
  execute('api-build', 'dotnet', ['build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(snapshot(), before, 'source upgrade changed application-owned files');
  const result = { nullableColumn: 'Description', sourceUpdated: CRUD_ARTIFACTS.filter(path => changed.get(path) === 'Update').length,
    moduleUpdated: modulePaths.filter(path => changed.get(path) === 'Update').length,
    repeatUnchanged: true, manualPreserved: true, apiCompiled: true, databaseMigrationApplied: false };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
