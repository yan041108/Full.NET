import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('Native 数据库前置复用已构建 Migrator，CLI 选项不能误传给迁移命令', async () => {
  const source = await readFile(new URL('../Full.NET.IntegrationTests/NativeAot/NativeApiMigratorRunner.cs', import.meta.url), 'utf8');
  const literalArgs = [...source.matchAll(/startInfo\.ArgumentList\.Add\("([^"]+)"\);/g)].map(match => match[1]);
  const separator = literalArgs.indexOf('--');
  assert(separator >= 0);
  for (const option of ['--no-build', '--no-restore', '--no-launch-profile']) {
    const position = literalArgs.indexOf(option);
    assert(position > 0 && position < separator, `${option} 必须约束 dotnet run，不能传给 Migrator`);
    assert.equal(literalArgs.filter(arg => arg === option).length, 1);
  }
  assert.deepEqual(literalArgs.slice(separator + 1), ['migrate', '--seed']);
  assert.equal(literalArgs[0], 'run');
  assert(literalArgs.includes('Release'));
});

test('构建 Integration 时先构建 Migrator，且不新增测试程序集依赖', async () => {
  const source = await readFile(new URL('../Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj', import.meta.url), 'utf8');
  const references = [...source.matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]*Full\.NET\.Host\.Migrator\.csproj)"[^>]*\/>/g)];
  assert.equal(references.length, 1, '原生用例禁用隐式构建之前必须有明确构建前置');
  assert.match(references[0][0], /\bReferenceOutputAssembly="false"/);
});
