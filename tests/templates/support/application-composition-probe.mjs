import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// 仅在已创建的隔离 Demo 应用内接入验收模块；不修改受管框架或正式模板。
export function prepareApplicationCompositionProbe(appRoot) {
  const compositionRoot = join(appRoot, 'src/Demo.Composition');
  const projectPath = join(compositionRoot, 'Demo.Composition.csproj');
  const catalogPath = join(compositionRoot, 'ApplicationModuleCatalog.cs');
  const project = readFileSync(projectPath, 'utf8');
  const catalog = readFileSync(catalogPath, 'utf8');
  const emptyCatalog = /private static IReadOnlyList<IFullNetModule> CreateModules\(\) =>\s*\[\s*\];/gu;
  assert.equal([...catalog.matchAll(emptyCatalog)].length, 1, 'probe requires one empty application catalog');
  assert.equal((project.match(/<\/Project>/gu) ?? []).length, 1, 'probe requires one application project');
  const moduleRoot = join(appRoot, 'src/Demo.Modules.Probe');
  const probeRoot = join(appRoot, 'verification/CompositionProbe');
  mkdirSync(moduleRoot, { recursive: true });
  mkdirSync(probeRoot, { recursive: true });
  writeFileSync(join(moduleRoot, 'Demo.Modules.Probe.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
  </ItemGroup>
</Project>
`);
  writeFileSync(join(moduleRoot, 'ProbeModule.cs'), readFileSync(new URL('./fixtures/application-probe-module.cs.fixture', import.meta.url)));
  writeFileSync(join(probeRoot, 'CompositionProbe.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/Demo.Composition/Demo.Composition.csproj" /></ItemGroup>
</Project>
`);
  writeFileSync(join(probeRoot, 'Program.cs'), readFileSync(new URL('./fixtures/application-composition-program.cs.fixture', import.meta.url)));
  writeFileSync(projectPath, project.replace('</Project>', '  <ItemGroup><ProjectReference Include="../Demo.Modules.Probe/Demo.Modules.Probe.csproj" /></ItemGroup>\n</Project>'));
  writeFileSync(catalogPath, catalog.replace(emptyCatalog, 'private static IReadOnlyList<IFullNetModule> CreateModules() =>\n    [\n        new Demo.Modules.Probe.ProbeModule(),\n    ];'));
  return probeRoot;
}

export function verifyApplicationComposition(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-composition'),
} = {}) {
  const probeRoot = prepareApplicationCompositionProbe(appRoot);
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, timeout) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout });
    writeFileSync(join(reportDirectory, `${stage}.json`), JSON.stringify({
      args, status: result.status, signal: result.signal, error: result.error?.message,
      stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.status, 0, `${stage} failed: ${result.error?.message ?? ''}\n${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    return result.stdout ?? '';
  };
  execute('build', ['build', join(probeRoot, 'CompositionProbe.csproj'), '-c', 'Release', '-v', 'quiet'], 300_000);
  const stdout = execute('run', ['exec', join(probeRoot, 'bin/Release/net10.0/CompositionProbe.dll')], 60_000);
  const resultLines = stdout.split(/\r?\n/u).filter((line) => line.startsWith('FULLNET_APPLICATION_COMPOSITION '));
  assert.equal(resultLines.length, 1, 'incomplete runtime result: expected one report');
  const result = JSON.parse(resultLines[0].slice('FULLNET_APPLICATION_COMPOSITION '.length));
  assert.deepEqual(result, { roles: 3, reservedNames: 6, invalidGraphs: 9 }, 'incomplete runtime result');
  return result;
}
