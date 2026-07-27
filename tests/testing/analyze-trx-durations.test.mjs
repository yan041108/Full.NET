import assert from 'node:assert/strict';
import test from 'node:test';

import {
  analyzeTrx,
  formatDuration,
  renderReport
} from '../../scripts/testing/analyze-trx-durations.mjs';
import {
  argumentsFor,
  shards
} from '../../scripts/testing/run-integration-shard.mjs';
import {
  verifyPartitionSets
} from '../../scripts/testing/verify-integration-shards.mjs';

const sampleTrx = `<?xml version="1.0" encoding="utf-8"?>
<TestRun>
  <Results>
    <UnitTestResult testName="Host_users_with_sql_server" outcome="Passed" duration="00:00:03.5000000" />
    <UnitTestResult testName="Host_users_with_mysql" outcome="Passed" duration="00:00:02.2500000" />
    <UnitTestResult testName="NamingContract_MySql_recovers" outcome="Failed" duration="00:00:01.2500000" />
  </Results>
  <TestDefinitions>
    <UnitTest name="Host_users_with_sql_server">
      <TestMethod className="Full.NET.IntegrationTests.Api.IdentityApiSqlServerTests" />
    </UnitTest>
    <UnitTest name="Host_users_with_mysql">
      <TestMethod className="Full.NET.IntegrationTests.Api.IdentityApiMySqlTests" />
    </UnitTest>
    <UnitTest name="NamingContract_MySql_recovers">
      <TestMethod className="Full.NET.IntegrationTests.Migrations.NamingContractMigrationTests" />
    </UnitTest>
  </TestDefinitions>
</TestRun>`;

test('TRX 分析按结果、套件和提供程序聚合', () => {
  const report = analyzeTrx(sampleTrx);

  assert.equal(report.total, 3);
  assert.equal(report.passed, 2);
  assert.equal(report.failed, 1);
  assert.equal(report.durationMs, 7000);
  assert.deepEqual(
    report.byProvider.map(item => [item.name, item.count, item.durationMs]),
    [
      ['MySQL', 2, 3500],
      ['SQL Server', 1, 3500]
    ]
  );
  assert.deepEqual(
    report.bySuite.map(item => [item.name, item.count]),
    [
      ['Api', 2],
      ['Migrations', 1]
    ]
  );
});

test('TRX 报告按耗时降序输出最慢测试', () => {
  const text = renderReport(analyzeTrx(sampleTrx), 2);

  assert.match(text, /总计 3，成功 2，失败 1，其他 0，总测试耗时 7\.000s/);
  assert.ok(text.indexOf('Host_users_with_sql_server') < text.indexOf('Host_users_with_mysql'));
  assert.equal(formatDuration(1250), '1.250s');
  for (const durationAttribute of [
    '',
    'duration="not-a-duration"',
    'duration="00:60:00"'
  ]) {
    const invalidTrx = sampleTrx.replace(
      'duration="00:00:03.5000000"',
      durationAttribute
    );
    assert.throws(
      () => analyzeTrx(invalidTrx),
      /Host_users_with_sql_server.*duration/
    );
  }
});

test('四个 CI 分片数量精确覆盖全量门槛', () => {
  const partition = [
    shards['api-sqlserver'],
    shards['api-mysql'],
    shards.migrations,
    shards.infrastructure
  ];

  assert.equal(
    partition.reduce((sum, shard) => sum + shard.minimum, 0),
    shards.full.minimum
  );
  for (const name of [
    'api-sqlserver',
    'api-mysql',
    'migrations',
    'infrastructure'
  ]) {
    const args = argumentsFor(name);
    assert.ok(args.includes('--filter'));
    assert.ok(args.includes('--minimum-expected-tests'));
    assert.ok(args.includes('--report-trx'));
  }
});

test('分片集合拒绝重复和遗漏', () => {
  const full = [
    { uid: 'one', displayName: 'One' },
    { uid: 'two', displayName: 'Two' }
  ];

  assert.doesNotThrow(() =>
    verifyPartitionSets(full, {
      left: [full[0]],
      right: [full[1]]
    })
  );
  assert.throws(
    () =>
      verifyPartitionSets(
        [
          full[0],
          { uid: full[0].uid, displayName: 'One duplicate' }
        ],
        { left: [full[0]] }
      ),
    /全量测试 UID 重复/
  );
  assert.throws(
    () => verifyPartitionSets(full, { left: [full[0]], right: [full[0]] }),
    /同时落入/
  );
  assert.throws(
    () => verifyPartitionSets(full, { left: [full[0]] }),
    /遗漏 1 项/
  );
});
