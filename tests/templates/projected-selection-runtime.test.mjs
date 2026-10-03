import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { projectCompositionSource } from '../../scripts/templates/project-preset-composition.mjs';
import { resolvePresetModules, VALID_PRESETS } from '../../scripts/templates/preset-modules.mjs';

const composition = resolve('src/Composition/Full.NET.Composition');
const modularity = resolve('src/BuildingBlocks/Full.NET.Modularity/Modules');
const sources = {
  catalog: readFileSync(join(composition, 'FullNetModuleCatalog.cs'), 'utf8'),
  selection: readFileSync(join(composition, 'FullNetModuleSelection.cs'), 'utf8'),
  project: readFileSync(join(composition, 'Full.NET.Composition.csproj'), 'utf8'),
};

for (const preset of VALID_PRESETS) {
  test(`projected ${preset} resolves optional contracts and rejects unavailable implementations`, () => {
    const workspace = mkdtempSync(join(tmpdir(), 'fullnet-selection-runtime-'));
    try {
      const modules = resolvePresetModules(preset);
      writeFileSync(join(workspace, 'Selection.cs'), projectCompositionSource(sources, modules).selection);
      for (const file of ['IFullNetModule.cs', 'ModulePipelineStage.cs', 'ModuleSelectionAnalysis.cs']) {
        writeFileSync(join(workspace, file), readFileSync(join(modularity, file)));
      }
      writeFileSync(join(workspace, 'Options.cs'), readFileSync(join(composition, 'FullNetModuleSelectionOptions.cs')));
      writeFileSync(join(workspace, 'Probe.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
</Project>`);
      writeFileSync(join(workspace, 'Program.cs'), `
using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;

string[] names = [${modules.map(name => JSON.stringify(name)).join(',')}];
var config = new ConfigurationBuilder().AddInMemoryCollection(names.Select((name, index) =>
    new KeyValuePair<string, string?>($"FullNet:Modules:Enabled:{index}", name))).Build();
IFullNetModule[] modules = names.Select(name => (IFullNetModule)new ProbeModule(name)).ToArray();
if (FullNetModuleSelection.ResolveEnabledModules(config, modules).Count != names.Length)
    throw new Exception("Selected implementation was omitted");
if (!FullNetModuleSelection.AnalyzeConfiguration(config, modules).IsValid)
    throw new Exception("Selected configuration failed analysis");

// 裁剪掉的官方模块仍可作为可选契约来源，但不能被运行时配置重新启用。
var unavailable = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["FullNet:Modules:Enabled:0"] = "Identity", ["FullNet:Modules:Enabled:1"] = "Ai" }).Build();
ExpectRejected(() => FullNetModuleSelection.ResolveEnabledModules(unavailable, modules));
modules[0] = new ProbeModule("Identity", ["UnknownProducer"]);
ExpectRejected(() => FullNetModuleSelection.ResolveEnabledModules(config, modules));
if (FullNetModuleSelection.AnalyzeConfiguration(config, modules).IsValid)
    throw new Exception("Unknown contract producer accepted by analysis");
Console.WriteLine("PASS projected runtime");

void ExpectRejected(Action action) {
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception("Invalid selection was accepted");
}
sealed class ProbeModule(string name, string[]? optional = null) : IFullNetModule {
    public string Name => name;
    public IReadOnlyCollection<string> Dependencies => [];
    public IReadOnlyCollection<string> OptionalContractDependencies => optional ?? (name == "Identity" ? ["Files", "Notifications"] : []);
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
}
`);
      const result = spawnSync('dotnet', ['run', '--project', join(workspace, 'Probe.csproj'), '-c', 'Release', '-v', 'quiet'], {
        cwd: workspace, encoding: 'utf8', timeout: 120_000, windowsHide: true,
      });
      assert.equal(result.status, 0, result.stderr || result.stdout || result.error?.message);
      assert.match(result.stdout, /PASS projected runtime/);
    } finally {
      rmSync(workspace, { recursive: true, force: true });
    }
  });
}
