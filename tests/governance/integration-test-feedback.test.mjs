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
    'test:integration:tooling',
    'test:integration:affected:plan',
    'test:integration:affected'
  ];

  for (const script of requiredScripts) {
    assert.ok(packageJson.scripts[script], `package.json 缺少 ${script}`);
  }
});

test('开发规则必须按变更风险分层，并把本地全量留给 main CI', async () => {
  const rules = await read('rules/development-quality.md');
  assert.match(rules, /变更风险分层/);
  assert.match(rules, /共享宿主/);
  assert.match(rules, /SQL Server 与 MySQL/);
  assert.doesNotMatch(
    rules,
    /聚焦运行（`--filter` \/ `-g`）只能作为迭代手段/,
    '规则不得再把所有聚焦运行一律降级为仅迭代证据'
  );
  assert.match(rules, /test:integration:affected:plan/);
  assert.match(rules, /test:integration:affected/);
  assert.match(rules, /任务基线/);
  assert.match(rules, /完整 197 项只保留给 `main` CI/);
  assert.match(rules, /本地任务禁止运行 `test:integration:full`/);
});

test('其它任务窗口必须记录基线并使用受影响测试选择器', async () => {
  const agents = await read('AGENTS.md');
  const performanceSkill = await read(
    '.agents/skills/fullnet-performance-hardening/SKILL.md'
  );
  const moduleSkill = await read(
    '.agents/skills/fullnet-module-delivery/SKILL.md'
  );

  for (const source of [agents, performanceSkill, moduleSkill]) {
    assert.match(source, /git rev-parse HEAD/);
    assert.match(source, /test:integration:affected:plan/);
    assert.match(source, /test:integration:affected/);
    assert.match(source, /完整 197 项只保留给 `main` CI/);
  }
});

test('本地受影响测试选择器不得调用 full', async () => {
  const selector = await read(
    'scripts/testing/run-affected-integration.mjs'
  );

  assert.doesNotMatch(selector, /argumentsFor\(['"]full['"]\)/);
  assert.match(selector, /本地受影响测试选择器禁止执行 full/);
});

test('main Integration 门禁必须使用四分片矩阵并汇总结果', async () => {
  const workflow = await read('.github/workflows/ci.yml');
  assert.match(workflow, /^\s{2}integration-shard:/m);
  assert.match(workflow, /shard:\s*\[api-sqlserver, api-mysql, migrations, infrastructure\]/);
  assert.match(workflow, /^\s{2}integration-gate:/m);
  assert.match(workflow, /needs:\s*\[build-test, integration-shard\]/);
});
