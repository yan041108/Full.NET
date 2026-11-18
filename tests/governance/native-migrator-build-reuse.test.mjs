import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('Native 数据库前置通过共享进程入口执行已构建 Migrator', async () => {
  const source = await readFile(new URL('../Full.NET.IntegrationTests/NativeAot/NativeApiMigratorRunner.cs', import.meta.url), 'utf8');
  assert.match(source, /NativeMigratorProcess\.CreateStartInfo\(/);
  assert.match(source, /SeedProfile\.Development\.ToCanonicalName\(\)/);
  assert.match(source, /NativeMigratorProcess\.RunAsync\(startInfo, cancellationToken\)/);
  const process = await readFile(new URL('../Shared/Testing/NativeMigratorProcess.cs', import.meta.url), 'utf8');
  assert.match(process, /new ProcessStartInfo\("dotnet"\)/);
  assert.match(process, /Path\.GetFullPath\(Path\.Combine\(outputDirectory, \$"\{assemblyName\}\.dll"\)\)/);
  assert.match(process, /"migrate", "--seed", seedProfile/);
  assert.match(process, /new\[\] \{ "dll", "deps\.json", "runtimeconfig\.json" \}/);
  assert.match(process, /if \(!File\.Exists\(artifact\)\)/);
  assert.doesNotMatch(process, /ArgumentList\.Add\("(?:run|build|--no-build|--no-restore|--no-launch-profile)"\)/);
});

test('Integration 只保留一份用于复制 Migrator 产物的别名引用', async () => {
  const source = await readFile(new URL('../Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj', import.meta.url), 'utf8');
  const references = [...source.matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]*Full\.NET\.Host\.Migrator\.csproj)"[^>]*\/>/g)];
  assert.equal(references.length, 1, '原生用例禁用隐式构建之前必须有明确构建前置');
  assert.match(references[0][0], /\bAliases="migratorhost"/);
  assert.doesNotMatch(references[0][0], /\bReferenceOutputAssembly="false"/);
});
