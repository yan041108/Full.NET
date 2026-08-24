import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import test from 'node:test';

import {
  argumentsForSuite,
  commandsForSuite,
  loadTestMatrix,
  parseSuiteOptions
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
    for (const selection of Object.values(suite.selections ?? {})) {
      assert.ok(Number.isInteger(selection.minimum) && selection.minimum > 0);
      assert.ok(selection.filter.length > 0);
    }
  }
  assert.equal(
    matrix.dotnetSuites.unit.selections['code-generation-realtime'].filter,
    'FullyQualifiedName~CodeGeneration|FullyQualifiedName~Realtime'
  );
  assert.equal(
    matrix.dotnetSuites.architecture.selections['api-native-aot'].filter,
    'FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol'
  );
  assert.deepEqual(matrix.nativeAotPublish.runtimeIdentifier, 'linux-x64');
  assert.ok(matrix.nativeAotIntegration.minimum > 0);
  assert.ok(matrix.nativeAotS3Integration.minimum >= 2);
  assert.ok(matrix.nativeAotKafkaReplayIntegration.minimum >= 2);
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

test('快速套件从测试矩阵解析命名聚焦集', () => {
  const matrix = loadTestMatrix();
  const args = argumentsForSuite('architecture', {
    selection: 'api-native-aot'
  });

  assert.ok(args.includes('--filter'));
  assert.ok(args.includes('FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol'));
  assert.deepEqual(
    args.slice(
      args.indexOf('--minimum-expected-tests'),
      args.indexOf('--minimum-expected-tests') + 2
    ),
    [
      '--minimum-expected-tests',
      String(matrix.dotnetSuites.architecture.selections['api-native-aot'].minimum)
    ]
  );
  assert.deepEqual(parseSuiteOptions([
    '--no-build',
    '--selection',
    'api-native-aot'
  ]), {
    noBuild: true,
    selection: 'api-native-aot',
    filter: null,
    minimumExpectedTests: null
  });
  assert.throws(
    () => argumentsForSuite('architecture', { selection: 'unknown' }),
    /未知聚焦集/
  );
});

test('原始聚焦过滤必须携带正整数最低发现数且不能覆盖命名聚焦集', () => {
  assert.throws(
    () => parseSuiteOptions(['--filter', 'FullyQualifiedName~NativeAot']),
    /必须同时提供 --minimum-expected-tests/
  );
  assert.throws(
    () => parseSuiteOptions([
      '--filter',
      'FullyQualifiedName~NativeAot',
      '--minimum-expected-tests',
      '0'
    ]),
    /正整数/
  );
  assert.throws(
    () => parseSuiteOptions([
      '--selection',
      'api-native-aot',
      '--minimum-expected-tests',
      '1'
    ]),
    /不能与/
  );
});

test('CI 分片 JSON 直接来自测试矩阵', () => {
  const matrix = loadTestMatrix();

  assert.equal(
    mainIntegrationPartitionsJson(),
    JSON.stringify(matrix.integration.mainPartitions)
  );
});
