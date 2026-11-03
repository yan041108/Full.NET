import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
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

test('Workflow Native Kafka重型入口不能进入默认基础或完整分片', () => {
  const matrix = loadTestMatrix();
  for (const key of ['infrastructure', 'full'])
    assert.ok(matrix.integration.shards[key].filter.split('&').includes('TestCategory!=WorkflowNativeKafka'), key);
  const selections = matrix.dotnetSuites.workflowActivityIntegration.selections;
  const methods = new Set();
  for (const key of ['workflow-native-kafka-sql-server', 'workflow-native-kafka-my-sql',
    'workflow-native-kafka-combinations-sql-server', 'workflow-native-kafka-combinations-my-sql',
    'workflow-native-kafka-reconciliation-sql-server', 'workflow-native-kafka-reconciliation-my-sql']) {
    assert.equal(selections[key].minimum, 1);
    assert.match(selections[key].filter, /^FullyQualifiedName=Full\.NET\.IntegrationTests\.Workflow\.WorkflowCollaborationRequestApiTests\.Collaboration_native_(?:physical_kafka|kafka_combinations|kafka_reconciliation)_with_(?:sql_server|mysql)$/);
    methods.add(selections[key].filter);
  }
  assert.equal(methods.size, 6);
});

test('知识发布分批验证完整覆盖聚合集且不降低最低发现数', () => {
  const shards = loadTestMatrix().integration.shards;
  const combined = shards['ai-knowledge-publication'];
  const batches = ['lifecycle', 'sqlserver', 'mysql'].map(key => shards[`ai-knowledge-publication-${key}`]);
  const filters = batches.flatMap(batch => batch.filter.split('|')).sort();
  assert.equal(new Set(filters).size, filters.length);
  assert.deepEqual(filters, combined.filter.split('|').sort());
  assert.equal(batches.reduce((sum, batch) => sum + batch.minimum, 0), combined.minimum);
  assert.ok(batches.every(batch => batch.timeout === '10m'));
  assert.deepEqual(shards['ai-knowledge-publication-native'], {
    filter: 'FullyQualifiedName~NativeKnowledgePublicationE2ETests', minimum: 2, timeout: '10m'
  });
});

test('快速套件支持校验式构建复用但不能与手动 no-build 混用', () => {
  assert.equal(parseSuiteOptions(['--reuse-build']).reuseBuild, true);
  assert.throws(() => parseSuiteOptions(['--reuse-build', '--no-build']), /不能/);
});

test('测试矩阵包含默认快速套件并允许登记聚焦套件和 Integration 分片', () => {
  const matrix = loadTestMatrix();
  const recoveryMigrationNumbers = readdirSync(
    new URL('../Full.NET.IntegrationTests/Migrations/', import.meta.url)
  )
    .map(fileName => /^Migration(\d{3}).*RecoveryTests\.cs$/.exec(fileName)?.[1])
    .filter(Boolean)
    .sort();

  for (const requiredSuite of [
    'unit',
    'compatibility',
    'architecture'
  ]) assert.ok(matrix.dotnetSuites[requiredSuite], `缺少默认套件 ${requiredSuite}`);
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
  const registered = Object.keys(matrix.integration.migrationSelections);
  for (const number of ['008', '009', '010', '011', ...recoveryMigrationNumbers]) {
    assert.ok(
      registered.includes(number),
      `migrationSelections 必须登记 ${number}`
    );
  }
  for (const selection of Object.values(
    matrix.integration.migrationSelections
  )) {
    if (selection.filter.includes('MySql') && selection.filter.includes('SqlServer')) continue;
    // DataRow 共用同一完整方法名，不能要求提供程序名出现在过滤器；必须核对实际测试属性。
    const target = /^FullyQualifiedName~(\w+)(?:\.(\w+))?$/.exec(selection.filter);
    assert.ok(target, `无法验证双库过滤器 ${selection.filter}`);
    const root = new URL('../Full.NET.IntegrationTests/', import.meta.url);
    const files = readdirSync(root, { recursive: true }).filter(file =>
      file.replaceAll('\\', '/').split('/').at(-1) === `${target[1]}.cs`);
    assert.equal(files.length, 1);
    const source = readFileSync(new URL(files[0].replaceAll('\\', '/'), root), 'utf8');
    const methods = [...source.matchAll(/((?:\[[^\]]+\]\s*)+)public\s+(?:async\s+)?Task(?:<[^>]+>)?\s+(\w+)\s*\(/g)]
      .filter(match => match[1].includes('[TestMethod]') && (!target[2] || match[2] === target[2]));
    assert.ok(methods.length > 0, `未找到测试方法 ${selection.filter}`);
    assert.ok(selection.minimum >= methods.length * 2);
    for (const method of methods) {
      if (method[1].includes('[DataRow(true)]') && method[1].includes('[DataRow(false)]')) {
        // 布尔用例必须实际分支创建双库，不能把任意 true/false 当成双提供程序证据。
        const body = source.slice(method.index + method[0].length).split('[TestMethod]')[0];
        const parameter = /^bool\s+(\w+)\s*\)/.exec(body)?.[1];
        assert.ok(parameter, `无法识别双库布尔参数 ${selection.filter}.${method[2]}`);
        assert.match(body, new RegExp(`\\b${parameter}\\s*\\?\\s*await\\s+SharedDatabaseFixture\\.CreateSqlServerDatabaseAsync\\(\\)\\s*:\\s*await\\s+SharedDatabaseFixture\\.CreateMySqlDatabaseAsync\\(\\)`));
        continue;
      }
      assert.match(method[1], /\[DataRow\(DatabaseProvider\.SqlServer\)\]/);
      assert.match(method[1], /\[DataRow\(DatabaseProvider\.MySql\)\]/);
    }
  }
  for (const migrationNumber of ['114', '115', '116', '117', '118']) {
    const filter = matrix.integration.migrationSelections[migrationNumber].filter;
    assert.match(
      filter,
      new RegExp(`Migration${migrationNumber}.*RecoveryTests\\.MySql_`)
    );
    assert.match(
      filter,
      new RegExp(`Migration${migrationNumber}.*RecoveryTests\\.SqlServer_`)
    );
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
    reuseBuild: false,
    selection: 'api-native-aot',
    filter: null,
    minimumExpectedTests: null
  });
  assert.throws(
    () => argumentsForSuite('architecture', { selection: 'unknown' }),
    /未知聚焦集/
  );
  assert.deepEqual(parseSuiteOptions([
    '--',
    '--selection',
    'api-native-aot'
  ]), {
    noBuild: false,
    reuseBuild: false,
    selection: 'api-native-aot',
    filter: null,
    minimumExpectedTests: null
  });
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
