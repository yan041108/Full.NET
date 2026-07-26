import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

test('Integration 依赖必须按需启动，聚焦测试不得无条件启动三种容器', async () => {
  const fixture = await read(
    'tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs'
  );

  assert.doesNotMatch(
    fixture,
    /Task\.WhenAll\([\s\S]*_sqlServer\.StartAsync\(\)[\s\S]*_mySql\.StartAsync\(\)[\s\S]*_redis\.StartAsync\(\)/,
    '程序集初始化不得无条件并行启动 SQL Server、MySQL 和 Redis'
  );
  assert.match(fixture, /GetOrStartSqlServerAsync/);
  assert.match(fixture, /GetOrStartMySqlAsync/);
  assert.match(fixture, /GetOrStartRedisAsync/);
  assert.match(fixture, /GetRedisConnectionStringAsync/);
});

test('仓库必须提供分层 Integration 命令和耗时分析入口', async () => {
  const packageJson = JSON.parse(await read('package.json'));
  const requiredScripts = [
    'test:integration:smoke',
    'test:integration:api:sqlserver',
    'test:integration:api:mysql',
    'test:integration:migrations',
    'test:integration:infrastructure',
    'test:integration:full',
    'test:integration:durations',
    'test:integration:partitions',
    'test:integration:tooling'
  ];

  for (const script of requiredScripts) {
    assert.ok(packageJson.scripts[script], `package.json 缺少 ${script}`);
  }
});

test('开发规则必须按变更风险分层，并明确全量触发条件', async () => {
  const rules = await read('rules/development-quality.md');
  assert.match(rules, /变更风险分层/);
  assert.match(rules, /全量触发条件/);
  assert.match(rules, /共享宿主/);
  assert.match(rules, /SQL Server 与 MySQL/);
  assert.doesNotMatch(
    rules,
    /聚焦运行（`--filter` \/ `-g`）只能作为迭代手段/,
    '规则不得再把所有聚焦运行一律降级为仅迭代证据'
  );
});

test('main Integration 门禁必须使用四分片矩阵并汇总结果', async () => {
  const workflow = await read('.github/workflows/ci.yml');
  assert.match(workflow, /^\s{2}integration-shard:/m);
  assert.match(workflow, /shard:\s*\[api-sqlserver, api-mysql, migrations, infrastructure\]/);
  assert.match(workflow, /^\s{2}integration-gate:/m);
  assert.match(workflow, /needs:\s*\[build-test, integration-shard\]/);
});
