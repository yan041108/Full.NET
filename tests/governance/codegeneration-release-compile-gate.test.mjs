import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

test('CodeGeneration Release 编译门禁集成测试存在且纳入受影响集', async () => {
  const compilationTest = await readFile(
    path.join(
      repositoryRoot,
      'tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationCompilationTests.cs'
    ),
    'utf8'
  );
  assert.match(compilationTest, /ModuleIntegrationCompilationTests/u);

  const affectedRunner = await readFile(
    path.join(repositoryRoot, 'scripts/testing/run-affected-integration.mjs'),
    'utf8'
  );
  assert.match(affectedRunner, /Full\.NET\.IntegrationTests\.CodeGeneration/u);
});
