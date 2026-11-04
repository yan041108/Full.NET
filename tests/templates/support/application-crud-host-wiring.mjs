import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { MODULE_ARTIFACTS, assertHostIntegrationReceipt } from './application-crud-module.mjs';
import { CRUD_ARTIFACTS } from './application-crud-generation.mjs';

// 完整接入已经在模块阶段完成；这里只复核接线、API 编译和重复执行，禁止再次分步修改受管入口。
export function verifyApplicationCrudHostWiring(appRoot, {
  run = spawnSync,
  verifyMissingSdk = false,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-host-wiring'),
} = {}) {
  const moduleDirectory = 'src/Demo.Modules.Catalog';
  const moduleProject = moduleDirectory + '/Demo.Modules.Catalog.csproj';
  const entry = moduleDirectory + '/CatalogModule.cs';
  const compositionProject = 'src/Demo.Composition/Demo.Composition.csproj';
  const catalog = 'src/Demo.Composition/ApplicationModuleCatalog.cs';
  const contributor = moduleDirectory + '/CatalogAuthorizationContributor.cs';
  const target = 'verification/CrudGeneration/module-target.json';
  const trackedPaths = [moduleProject, ...MODULE_ARTIFACTS.map((path) => moduleDirectory + '/' + path),
    entry, compositionProject, catalog, contributor, moduleDirectory + '/Product.manual.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'ui/admin/src/router/index.ts',
    '.fullnet/codegeneration-manifest.json', ...CRUD_ARTIFACTS, 'verification/CrudGeneration/schema.json', target];
  const capture = () => new Map(trackedPaths.map((path) => [path, readFileSync(join(appRoot, path))]));
  const original = capture();
  const entryContent = readFileSync(join(appRoot, entry), 'utf8');
  for (const registration of ['services.AddFullNetGeneratedModuleFeatures();', 'endpoints.MapFullNetGeneratedModuleFeatures();'])
    assert.equal(entryContent.split(registration).length - 1, 1, 'missing or duplicate generated entry registration');
  assert.match(entryContent, /AddSingleton<Full\.NET\.Modules\.Identity\.Contracts\.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>/u);
  const catalogContent = readFileSync(join(appRoot, catalog), 'utf8');
  assert.equal((catalogContent.match(/new CatalogModule\(\)/gu) ?? []).length, 1);
  assert.equal((catalogContent.match(/new Demo\.Modules\.Probe\.ProbeModule\(\)/gu) ?? []).length, 1);
  assert.match(readFileSync(join(appRoot, compositionProject), 'utf8'), /Demo\.Modules\.Catalog[\\/]Demo\.Modules\.Catalog\.csproj/u);
  assert.equal(JSON.parse(readFileSync(join(appRoot, target), 'utf8')).authorizationContributorPath, contributor);
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, integration = false) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args, status: result.status,
      signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    if (integration) assertHostIntegrationReceipt(result.stdout, moduleProject);
    assert.deepEqual(capture(), original, stage + ' changed already integrated application content');
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, 'apply-host-integration', '--schema', join(appRoot, 'verification/CrudGeneration/schema.json'),
    '--repository', appRoot, '--target', join(appRoot, target)];
  if (verifyMissingSdk) {
    const sdkPath = join(appRoot, 'global.json'), originalSdk = readFileSync(sdkPath);
    try {
      // 仅改变本次生成应用的 SDK 选择；失败诊断不能提交任何候选源或改变人工文件。
      const sdk = JSON.parse(originalSdk.toString('utf8'));
      writeFileSync(sdkPath, JSON.stringify({ ...sdk, sdk: { ...sdk.sdk, version: '99.0.100', rollForward: 'disable' } }));
      const options = { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() };
      const build = run('dotnet', ['build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release'], options);
      assert.equal(build.error, undefined); assert.ok(Number.isInteger(build.status) && build.status !== 0 && !build.signal, 'missing SDK must fail a real build');
      const candidate = run('dotnet', args, options);
      assert.equal(candidate.error, undefined); assert.equal(candidate.status, 2, 'failed candidate must be rejected');
      const expected = build.status | 0;
      const diagnostics = String(candidate.stderr ?? '') + '\n' + String(candidate.stdout ?? '');
      assert.ok(diagnostics.includes('构建进程退出码：' + expected), 'candidate must retain the signed SDK exit code');
      assert.deepEqual(capture(), original, 'SDK failure changed managed sources or ownership');
      writeFileSync(join(reportDirectory, 'apply-host-integration-sdk-failure.json'), JSON.stringify({
        buildExitCode: build.status, diagnosticExitCode: expected, cliExitCode: candidate.status,
        diagnostic: '构建进程退出码：' + expected, inputsUnchanged: true,
      }, null, 2));
    } finally { writeFileSync(sdkPath, originalSdk); }
  }
  execute('repeat', args, true);
  execute('build', ['build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet']);
  execute('repeat-after-build', args, true);
  const result = { entryIntegrated: true, compositionIntegrated: true, apiCompiled: true, repeatUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
