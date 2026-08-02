import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import test from 'node:test';

import {
  argumentsForSuite,
  commandsForSuite,
  loadTestMatrix
} from '../../scripts/testing/run-dotnet-test-suite.mjs';
import {
  mainIntegrationPartitionsJson
} from '../../scripts/testing/print-test-matrix.mjs';

test('测试矩阵集中定义三个快速套件和完整 Integration 分片', () => {
  const matrix = loadTestMatrix();
  const recoveryMigrationNumbers = readdirSync(
    new URL('../Full.NET.IntegrationTests/Migrations/', import.meta.url)
  )
    .map(fileName => /^Migration(\d{3}).*RecoveryTests\.cs$/.exec(fileName)?.[1])
    .filter(Boolean)
    .sort();

  assert.deepEqual(Object.keys(matrix.dotnetSuites), [
    'unit',
    'compatibility',
    'architecture'
  ]);
  for (const suite of Object.values(matrix.dotnetSuites)) {
    assert.ok(Number.isInteger(suite.minimum) && suite.minimum > 0);
    assert.match(suite.project, /\.csproj$/);
  }
  assert.ok(matrix.integration.shards.full.minimum > 0);
  assert.ok(matrix.integration.mainPartitions.length > 1);
  assert.deepEqual(
    Object.keys(matrix.integration.migrationSelections),
    ['008', '009', '010', '011', ...recoveryMigrationNumbers]
  );
  for (const selection of Object.values(
    matrix.integration.migrationSelections
  )) {
    assert.match(selection.filter, /MySql/);
    assert.match(selection.filter, /SqlServer/);
    assert.match(selection.filter, /Recovery|PartialRecovery/);
  }
  assert.equal(
    matrix.integration.mainPartitions.reduce(
      (sum, name) => sum + matrix.integration.shards[name].minimum,
      0
    ),
    matrix.integration.shards.full.minimum
  );
});

test('快速套件命令从矩阵生成最低发现数而不是在 CI 复制数字', () => {
  const matrix = loadTestMatrix();
  const args = argumentsForSuite('unit');

  assert.ok(args[0].endsWith('Full.NET.UnitTests.dll'));
  assert.deepEqual(
    args.slice(
      args.indexOf('--minimum-expected-tests'),
      args.indexOf('--minimum-expected-tests') + 2
    ),
    ['--minimum-expected-tests', String(matrix.dotnetSuites.unit.minimum)]
  );
  assert.throws(() => argumentsForSuite('unknown'), /未知测试套件/);
});

test('快速套件默认先构建新鲜程序集，CI 可在统一构建后显式跳过', () => {
  const matrix = loadTestMatrix();
  const commands = commandsForSuite('unit');
  const noBuildCommands = commandsForSuite('unit', { noBuild: true });

  assert.deepEqual(commands[0], {
    command: 'dotnet',
    args: [
      'build',
      matrix.dotnetSuites.unit.project,
      '--configuration',
      'Release',
      '--nologo'
    ]
  });
  assert.equal(commands.length, 2);
  assert.equal(noBuildCommands.length, 1);
  assert.deepEqual(noBuildCommands[0].args, argumentsForSuite('unit'));
});

test('CI 分片 JSON 直接来自测试矩阵', () => {
  const matrix = loadTestMatrix();

  assert.equal(
    mainIntegrationPartitionsJson(),
    JSON.stringify(matrix.integration.mainPartitions)
  );
});
