import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const MODULE_ARTIFACTS = [
  'Generated/FullNetGeneratedModuleFeatures.g.cs',
  ...['Contracts', 'Sql', 'Endpoint', 'Feature', 'Record'].map((name) => `Generated/Product/Product${name}.g.cs`),
];

// 仅在已完成 CRUD 生成验收的隔离 Demo 应用接入后端，不修改 API/Composition/Vue。
export function verifyApplicationCrudModule(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-module'),
} = {}) {
  const moduleDirectory = 'src/Demo.Modules.Catalog';
  const moduleProject = moduleDirectory + '/Demo.Modules.Catalog.csproj';
  const moduleRoot = join(appRoot, moduleDirectory);
  const targetPath = join(appRoot, 'verification/CrudGeneration/module-target.json');
  const schemaPath = join(appRoot, 'verification/CrudGeneration/schema.json');
  assert.equal(existsSync(moduleRoot), false, 'module acceptance requires an unused module directory');
  assert.equal(existsSync(targetPath), false, 'module acceptance requires an unused target path');
  assert.equal(existsSync(schemaPath), true, 'CRUD generation must prepare the application schema');
  const hostPaths = ['src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj',
    'src/Demo.Composition/Demo.Composition.csproj', 'src/Demo.Composition/ApplicationModuleCatalog.cs',
    'ui/admin/src/router/index.ts', '.fullnet/codegeneration-manifest.json', 'backend/ProductSql.g.cs'];
  const hostSnapshot = new Map(hostPaths.map((path) => [path, readFileSync(join(appRoot, path))]));
  mkdirSync(moduleRoot);
  const references = ['BuildingBlocks/Full.NET.Abstractions/Full.NET.Abstractions.csproj',
    'BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj',
    'BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj',
    'BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj',
    'Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj'];
  writeFileSync(join(appRoot, moduleProject), `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
${references.map((path) => `    <ProjectReference Include="../../framework/fullnet/src/${path}" />`).join('\n')}
  </ItemGroup>
</Project>
`);
  writeFileSync(join(moduleRoot, 'CatalogModule.cs'), readFileSync(new URL('./fixtures/application-catalog-module.cs.fixture', import.meta.url)));
  writeFileSync(join(moduleRoot, 'Product.manual.cs'), '// 人工模块文件，接入时保留。\n');
  writeFileSync(targetPath, JSON.stringify({ moduleName: 'Catalog', moduleProjectPath: moduleProject,
    moduleEntryPointPath: moduleDirectory + '/CatalogModule.cs',
    compositionProjectPath: 'src/Demo.Composition/Demo.Composition.csproj',
    compositionCatalogPath: 'src/Demo.Composition/ApplicationModuleCatalog.cs',
    vueRouterPath: 'ui/admin/src/router/index.ts',
  }, null, 2));
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, expectedStatus = 0) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args,
      status: result.status, signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, expectedStatus, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, 'apply-module-integration', '--schema', schemaPath, '--repository', appRoot, '--target', targetPath];
  const expectActions = (stdout, kind) => {
    const lines = stdout.split(/\r?\n/u).filter(Boolean).sort();
    assert.deepEqual(lines, [...MODULE_ARTIFACTS.map((path) => kind + ' ' + path),
      'Validated ModuleCompilation ' + moduleProject].sort(), 'incomplete module integration result');
  };
  const files = [...MODULE_ARTIFACTS, '.fullnet/codegeneration-manifest.json',
    'Demo.Modules.Catalog.csproj', 'CatalogModule.cs', 'Product.manual.cs'];
  const capture = () => new Map(files.map((path) => [path, readFileSync(join(moduleRoot, path))]));
  const manualFiles = files.slice(-3).map((path) => [path, readFileSync(join(moduleRoot, path))]);
  expectActions(execute('apply', args), 'Create');
  const applied = capture();
  for (const [path, bytes] of manualFiles) assert.deepEqual(applied.get(path), bytes, 'apply changed ' + path);
  execute('build', ['build', join(appRoot, moduleProject), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(capture(), applied, 'build changed module sources');
  expectActions(execute('repeat', args), 'Unchanged');
  assert.deepEqual(capture(), applied, 'repeat changed module sources or manifest');
  const sqlPath = 'Generated/Product/ProductSql.g.cs';
  writeFileSync(join(moduleRoot, sqlPath), Buffer.concat([applied.get(sqlPath), Buffer.from('\n// 人工修改，禁止自动覆盖。\n')]));
  const customized = capture();
  const conflict = execute('conflict', args, 2);
  assert.ok(conflict.split(/\r?\n/u).includes('Conflict ' + sqlPath), 'missing exact module SQL conflict');
  assert.deepEqual(capture(), customized, 'conflict changed module sources or manifest');
  assert.deepEqual(new Map(hostPaths.map((path) => [path, readFileSync(join(appRoot, path))])), hostSnapshot, 'module integration changed application host content');
  const result = { artifacts: MODULE_ARTIFACTS.length, moduleCompiled: true, conflictRejected: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
