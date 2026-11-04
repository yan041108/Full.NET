import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CRUD_ARTIFACTS, applicationCrudArtifactPath } from './application-crud-generation.mjs';

export const MODULE_ARTIFACTS = [
  'Generated/FullNetGeneratedModuleFeatures.g.cs',
  ...['Contracts', 'Sql', 'Endpoint', 'Feature', 'Record'].map((name) => `Generated/Product/Product${name}.g.cs`),
];

export function assertHostIntegrationReceipt(stdout, moduleProject) {
  const lines = (stdout ?? '').split(/\r?\n/u).filter(Boolean);
  assert.equal(lines.filter((line) => /^Applied Operation [0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(line)).length, 1,
    'missing durable operation receipt');
  assert.ok(lines.includes('Applied HostIntegration ' + moduleProject), 'missing complete integration result');
}

// 在独立应用一次接入模块、Composition 与授权；后续阶段验证运行时和页面，不再分步修改受管入口。
export function verifyApplicationCrudModule(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-module'),
  removeTestSqlComment = false,
} = {}) {
  const moduleDirectory = 'src/Demo.Modules.Catalog';
  const moduleProject = moduleDirectory + '/Demo.Modules.Catalog.csproj';
  const moduleRoot = join(appRoot, moduleDirectory);
  const entry = moduleDirectory + '/CatalogModule.cs';
  const contributor = moduleDirectory + '/CatalogAuthorizationContributor.cs';
  const compositionProject = 'src/Demo.Composition/Demo.Composition.csproj';
  const catalog = 'src/Demo.Composition/ApplicationModuleCatalog.cs';
  const targetPath = join(appRoot, 'verification/CrudGeneration/module-target.json');
  const schemaPath = join(appRoot, 'verification/CrudGeneration/schema.json');
  const manifestPath = '.fullnet/codegeneration-manifest.json';
  assert.equal(existsSync(moduleRoot), false, 'module acceptance requires an unused module directory');
  assert.equal(existsSync(targetPath), false, 'module acceptance requires an unused target path');
  assert.equal(existsSync(schemaPath), true, 'CRUD generation must prepare the application schema');
  const protectedPaths = ['src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj',
    'ui/admin/src/router/index.ts', ...CRUD_ARTIFACTS, 'backend/Product.manual.cs'];
  const capture = (paths) => new Map(paths.map((path) => [path, readFileSync(join(appRoot, path))]));
  const protectedSnapshot = capture(protectedPaths);
  const originalReferences = [...readFileSync(join(appRoot, compositionProject), 'utf8').matchAll(/<ProjectReference\s+Include="([^"]+)"/gu)]
    .map((match) => match[1]);
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
    <ProjectReference Condition="'$(FullNetAotAnalysis)' == 'true' or '$(FullNetPublishMode)' == 'NativeAot' or '$(PublishAot)' == 'true'" Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Data.Dapper/Full.NET.Data.Dapper.csproj" />
  </ItemGroup>
</Project>
`);
  writeFileSync(join(appRoot, entry), readFileSync(new URL('./fixtures/application-catalog-module.cs.fixture', import.meta.url)));
  writeFileSync(join(appRoot, contributor), readFileSync(new URL('./fixtures/application-catalog-authorization-contributor.cs.fixture', import.meta.url)));
  writeFileSync(join(moduleRoot, 'Product.manual.cs'), '// 人工模块文件，接入时保留。\n');
  writeFileSync(targetPath, JSON.stringify({ moduleName: 'Catalog', moduleProjectPath: moduleProject,
    moduleEntryPointPath: entry, compositionProjectPath: compositionProject, compositionCatalogPath: catalog,
    vueRouterPath: 'ui/admin/src/router/index.ts', authorizationContributorPath: contributor,
  }, null, 2));
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, expectedStatus = 0) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args, status: result.status,
      signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, expectedStatus, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result;
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, 'apply-host-integration', '--schema', schemaPath, '--repository', appRoot, '--target', targetPath];
  const receipt = (result) => {
    assertHostIntegrationReceipt(result.stdout, moduleProject);
  };
  const trackedPaths = [...protectedPaths, manifestPath, moduleProject, entry, contributor, compositionProject, catalog,
    moduleDirectory + '/Product.manual.cs', ...MODULE_ARTIFACTS.map((path) => moduleDirectory + '/' + path)];
  const originalModuleProject = readFileSync(join(appRoot, moduleProject));
  receipt(execute('apply', args));
  const manifest = JSON.parse(readFileSync(join(appRoot, manifestPath), 'utf8'));
  for (const relative of MODULE_ARTIFACTS) {
    const path = moduleDirectory + '/' + relative;
    const owned = manifest.artifacts.find((artifact) => artifact.relativePath === path);
    assert.ok(owned, 'complete root manifest does not own ' + path);
    assert.equal(owned.sha256, createHash('sha256').update(readFileSync(join(appRoot, path))).digest('hex'));
    if (relative !== MODULE_ARTIFACTS[0]) assert.equal(owned.owner, 'acme/catalog/product');
  }
  assert.match(readFileSync(join(appRoot, entry), 'utf8'), /services\.AddFullNetGeneratedModuleFeatures\(\);/u);
  assert.match(readFileSync(join(appRoot, entry), 'utf8'), /endpoints\.MapFullNetGeneratedModuleFeatures\(\);/u);
  const catalogSource = readFileSync(join(appRoot, catalog), 'utf8');
  assert.equal((catalogSource.match(/new CatalogModule\(\)/gu) ?? []).length, 1, 'module missing or duplicated');
  assert.equal((catalogSource.match(/new Demo\.Modules\.Probe\.ProbeModule\(\)/gu) ?? []).length, 1, 'existing application probe lost');
  const projectSource = readFileSync(join(appRoot, compositionProject), 'utf8');
  for (const reference of originalReferences) assert.ok(projectSource.includes('Include="' + reference + '"'), 'existing reference lost');
  assert.deepEqual(readFileSync(join(appRoot, moduleProject)), originalModuleProject, 'module project changed');
  assert.deepEqual(capture(protectedPaths), protectedSnapshot, 'complete integration changed protected files');
  const applied = capture(trackedPaths);
  execute('build', ['build', join(appRoot, moduleProject), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(capture(trackedPaths), applied, 'build changed application sources');
  receipt(execute('repeat', args));
  assert.deepEqual(capture(trackedPaths), applied, 'repeat changed application sources or ownership');
  const sqlPath = 'Generated/Product/ProductSql.g.cs';
  const conflictArtifacts = [...MODULE_ARTIFACTS.filter((path) => path !== sqlPath), sqlPath];
  for (const [index, relative] of conflictArtifacts.entries()) {
    const path = moduleDirectory + '/' + relative;
    writeFileSync(join(appRoot, path), Buffer.concat([applied.get(path), Buffer.from('\n// 人工修改，禁止自动覆盖。\n')]));
    const customized = capture(trackedPaths);
    const conflict = execute(index === 0 ? 'conflict' : 'conflict-' + (index + 1), args, 2);
    assert.ok((conflict.stderr ?? '').includes(path) || (conflict.stdout ?? '').includes('Conflict ' + path), 'missing exact conflict path');
    assert.deepEqual(capture(trackedPaths), customized, 'conflict changed application files or manifest');
    if (relative !== sqlPath || removeTestSqlComment) {
      // 只撤销本验收追加的注释，不能借后续阶段接入自动重新认领人工漂移。
      writeFileSync(join(appRoot, path), applied.get(path));
      assert.deepEqual(capture(trackedPaths), applied, 'test comment cleanup changed unrelated content');
    }
  }
  assert.equal(readFileSync(join(appRoot, applicationCrudArtifactPath('backend/ProductSql.g.cs')), 'utf8'), protectedSnapshot.get(applicationCrudArtifactPath('backend/ProductSql.g.cs')).toString('utf8'));
  const manualPath = moduleDirectory + '/Product.manual.cs';
  const manualBefore = readFileSync(join(appRoot, manualPath), 'utf8');
  writeFileSync(join(appRoot, manualPath), manualBefore + '\nnamespace Demo.Modules.Catalog;\ninternal static class ProductBusinessPolicy { public static bool CanPublish(string name) => !string.IsNullOrWhiteSpace(name); }\n');
  const manualSnapshot = capture(trackedPaths);
  receipt(execute('manual-repeat', args));
  assert.deepEqual(capture(trackedPaths), manualSnapshot, 'repeat integration changed developer-owned business code');
  execute('manual-build', ['build', join(appRoot, moduleProject), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(capture(trackedPaths), manualSnapshot, 'module build changed developer-owned business code');
  const result = { artifacts: MODULE_ARTIFACTS.length, moduleCompiled: true, conflictRejected: true, conflictArtifacts, manualExtensionPreserved: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
