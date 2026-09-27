import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { MODULE_ARTIFACTS } from './application-crud-module.mjs';

// 只接线并编译隔离应用；授权目录、迁移、Vue 与实际 HTTP 行为仍需独立验收。
export function verifyApplicationCrudHostWiring(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-host-wiring'),
} = {}) {
  const moduleDirectory = 'src/Demo.Modules.Catalog';
  const moduleProject = moduleDirectory + '/Demo.Modules.Catalog.csproj';
  const entry = moduleDirectory + '/CatalogModule.cs';
  const compositionProject = 'src/Demo.Composition/Demo.Composition.csproj';
  const catalog = 'src/Demo.Composition/ApplicationModuleCatalog.cs';
  const protectedPaths = [moduleProject, ...MODULE_ARTIFACTS.map((path) => moduleDirectory + '/' + path),
    moduleDirectory + '/.fullnet/codegeneration-manifest.json', moduleDirectory + '/Product.manual.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'ui/admin/src/router/index.ts',
    '.fullnet/codegeneration-manifest.json', 'backend/ProductSql.g.cs',
    'verification/CrudGeneration/schema.json', 'verification/CrudGeneration/module-target.json'];
  const capture = (paths) => new Map(paths.map((path) => [path, readFileSync(join(appRoot, path))]));
  const protectedSnapshot = capture(protectedPaths);
  const originalProject = readFileSync(join(appRoot, compositionProject), 'utf8');
  const originalReferences = [...originalProject.matchAll(/<ProjectReference\s+Include="([^"]+)"/gu)].map((match) => match[1]);
  assert.match(readFileSync(join(appRoot, catalog), 'utf8'), /new Demo\.Modules\.Probe\.ProbeModule\(\)/u, 'existing application probe is required');
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args,
      status: result.status, signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = (command) => ['exec', cli, command, '--schema', join(appRoot, 'verification/CrudGeneration/schema.json'),
    '--repository', appRoot, '--target', join(appRoot, 'verification/CrudGeneration/module-target.json')];
  const expectLines = (stdout, expected) => assert.deepEqual(stdout.split(/\r?\n/u).filter(Boolean).sort(),
    [...expected].sort(), 'incomplete host wiring result');
  expectLines(execute('entry', args('apply-module-entry-integration')),
    ['Update ' + entry, 'Validated ModuleCompilation ' + moduleProject]);
  const entryContent = readFileSync(join(appRoot, entry), 'utf8');
  assert.match(entryContent, /services\.AddFullNetGeneratedModuleFeatures\(\);/u);
  assert.match(entryContent, /endpoints\.MapFullNetGeneratedModuleFeatures\(\);/u);
  expectLines(execute('composition', args('apply-composition-integration')),
    ['Update ' + compositionProject, 'Update ' + catalog, 'Validated CompositionCompilation ' + compositionProject]);
  const catalogContent = readFileSync(join(appRoot, catalog), 'utf8');
  assert.equal((catalogContent.match(/new CatalogModule\(\)/gu) ?? []).length, 1, 'expected one generated module');
  assert.equal((catalogContent.match(/new Demo\.Modules\.Probe\.ProbeModule\(\)/gu) ?? []).length, 1, 'existing probe was lost or duplicated');
  const projectContent = readFileSync(join(appRoot, compositionProject), 'utf8');
  assert.match(projectContent, /Demo\.Modules\.Catalog[\\/]Demo\.Modules\.Catalog\.csproj/u);
  for (const reference of originalReferences) assert.ok(projectContent.includes('Include="' + reference + '"'), 'existing reference lost');
  assert.deepEqual(capture(protectedPaths), protectedSnapshot, 'wiring changed protected application content');
  const trackedPaths = [...protectedPaths, entry, compositionProject, catalog];
  const integrated = capture(trackedPaths);
  execute('build', ['build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet']);
  assert.deepEqual(capture(trackedPaths), integrated, 'API build changed application sources');
  expectLines(execute('entry-repeat', args('apply-module-entry-integration')), ['Unchanged ' + entry]);
  assert.deepEqual(capture(trackedPaths), integrated, 'repeat entry changed application content');
  expectLines(execute('composition-repeat', args('apply-composition-integration')),
    ['Unchanged ' + compositionProject, 'Unchanged ' + catalog]);
  assert.deepEqual(capture(trackedPaths), integrated, 'repeat composition changed application content');
  const result = { entryIntegrated: true, compositionIntegrated: true, apiCompiled: true, repeatUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
