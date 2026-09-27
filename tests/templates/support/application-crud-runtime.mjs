import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// 复用应用当前启动装配，但不启动监听、后台服务或数据访问；只验收构建、作用域和路由元数据。
export function verifyApplicationCrudRuntime(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-runtime'),
} = {}) {
  const probeRoot = join(appRoot, 'verification/CrudRuntimeProbe');
  assert.equal(existsSync(probeRoot), false, 'runtime acceptance requires an unused probe directory');
  const source = readFileSync(join(appRoot, 'src/Demo.Host.Api/Program.cs'), 'utf8');
  const builderAnchor = 'var builder = WebApplication.CreateBuilder(args);';
  const runAnchor = 'app.Run();';
  for (const anchor of [builderAnchor, runAnchor]) {
    assert.equal(source.split(anchor).length - 1, 1, 'runtime probe requires one standard anchor: ' + anchor);
  }
  const capture = () => {
    const result = new Map();
    const visit = (relative) => {
      const path = join(appRoot, relative);
      if (!existsSync(path)) return;
      for (const entry of readdirSync(path, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
        if (entry.name === 'bin' || entry.name === 'obj') continue;
        const child = relative + '/' + entry.name;
        if (entry.isDirectory()) visit(child);
        else if (entry.isFile()) result.set(child, readFileSync(join(appRoot, child)));
      }
    };
    for (const directory of ['src', 'backend', 'clients', 'contracts', 'reports', 'templates', '.fullnet', 'verification/CrudGeneration']) visit(directory);
    for (const path of ['fullnet-app.json', 'framework-manifest.json', 'appsettings.json', 'ui/admin/src/router/index.ts']) {
      result.set(path, readFileSync(join(appRoot, path)));
    }
    return result;
  };
  const original = capture();
  mkdirSync(probeRoot, { recursive: true });
  writeFileSync(join(probeRoot, 'CrudRuntimeProbe.csproj'), `<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><FullNetPublishMode>Jit</FullNetPublishMode></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/Demo.Host.Api/Demo.Host.Api.csproj" /></ItemGroup>
</Project>
`);
  const setup = readFileSync(new URL('./fixtures/application-crud-runtime-setup.cs.fixture', import.meta.url), 'utf8');
  const checks = readFileSync(new URL('./fixtures/application-crud-runtime-checks.cs.fixture', import.meta.url), 'utf8');
  writeFileSync(join(probeRoot, 'Program.cs'), 'using Demo.Modules.Catalog.Generated;\nusing Microsoft.AspNetCore.Authorization;\nusing Microsoft.AspNetCore.Routing;\nusing Microsoft.Extensions.Options;\nusing System.Text.Json;\n'
    + source.replace(builderAnchor, builderAnchor + '\n' + setup).replace(runAnchor, checks));
  mkdirSync(reportDirectory, { recursive: true });
  const execute = (stage, args, timeout) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout, windowsHide: true });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args,
      status: result.status, signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    assert.deepEqual(capture(), original, stage + ' changed application sources');
    return result.stdout ?? '';
  };
  execute('build', ['build', join(probeRoot, 'CrudRuntimeProbe.csproj'), '-c', 'Release', '-v', 'quiet'], 300_000);
  const stdout = execute('run', ['exec', join(probeRoot, 'bin/Release/net10.0/CrudRuntimeProbe.dll'), '--environment', 'Development'], 60_000);
  const marker = 'FULLNET_APPLICATION_CRUD_RUNTIME ';
  const lines = stdout.split(/\r?\n/u).filter((line) => line.startsWith(marker));
  assert.equal(lines.length, 1, 'runtime acceptance requires one complete report');
  const result = JSON.parse(lines[0].slice(marker.length));
  assert.deepEqual(result, { moduleRegistered: true, scopedServices: 2, protectedRoutes: 5, jsonRoundTrip: true }, 'incomplete runtime result');
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
