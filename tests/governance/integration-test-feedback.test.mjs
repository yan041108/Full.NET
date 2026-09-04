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
    'test:integration:messaging-heavy',
    'test:integration:full',
    'test:integration:durations',
    'test:integration:partitions',
    'test:integration:tooling',
    'test:task:start',
    'test:inner',
    'test:slice',
    'test:integration:affected:plan',
    'test:integration:affected'
  ];

  for (const script of requiredScripts) {
    assert.ok(packageJson.scripts[script], `package.json 缺少 ${script}`);
  }

  assert.equal(
    packageJson.scripts['test:inner'],
    'node scripts/testing/run-affected-integration.mjs --phase inner'
  );
  assert.equal(
    packageJson.scripts['test:slice'],
    'node scripts/testing/run-affected-integration.mjs --phase slice'
  );
});

test('开发规则必须按开发阶段分层，并把完整集合留给 main CI', async () => {
  const rules = await read('rules/development-quality.md');
  assert.match(rules, /变更风险分层/);
  assert.match(rules, /inner/);
  assert.match(rules, /slice/);
  assert.match(rules, /merge/);
  assert.match(rules, /任务快照/);
  assert.match(rules, /共享宿主/);
  assert.match(rules, /SQL Server 与 MySQL/);
  assert.match(rules, /未登记迁移.*migrations/);
  assert.doesNotMatch(
    rules,
    /聚焦运行（`--filter` \/ `-g`）只能作为迭代手段/,
    '规则不得再把所有聚焦运行一律降级为仅迭代证据'
  );
  assert.match(rules, /test:integration:affected:plan/);
  assert.match(rules, /test:integration:affected/);
  assert.match(rules, /messaging-heavy/);
  assert.match(rules, /11\.2 新增 Integration 测试门禁/);
  assert.match(rules, /任务基线/);
  assert.match(rules, /完整集合只保留给 `main` CI/);
  assert.match(rules, /本地任务禁止运行 `test:integration:full`/);
  assert.match(rules, /R-20260816-local-test-inner-budget/);
  assert.match(rules, /R-20260903-github-actions-first-verification/);
  assert.match(rules, /R-20260905-feature-first-page-acceptance/);
  assert.match(rules, /功能建设阶段不得以页面级真实栈 E2E 全绿作为每个增量切片的强制退出条件/);
  assert.match(rules, /逐页人工验收/);
  assert.match(rules, /环境重型验证必须优先交给 GitHub Actions/);
  assert.match(rules, /按精确 commit SHA 核对所有必需工作流/);
  assert.match(rules, /GitHub Actions 不可用时的受影响测试补偿/);
  assert.match(rules, /pnpm test:inner/);
  assert.match(rules, /禁止在 inner 运行 `pnpm test:e2e:real`/);
  assert.match(rules, /只读、已迁移的 schema 模板/);
  assert.match(rules, /附加 `FullyQualifiedName~MySql`/);
  assert.match(rules, /禁止用 `~Identity`/);
});

test('其它任务窗口使用快照和受影响测试选择器，不复制测试总数', async () => {
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
    assert.match(source, /完整集合只保留给 `main` CI/);
  }
  assert.match(agents, /test:task:start/);
  assert.match(agents, /test:integration:affected:plan/);
  assert.match(agents, /R-20260816-local-test-inner-budget/);
  assert.match(agents, /R-20260903-github-actions-first-verification/);
  assert.match(agents, /R-20260905-feature-first-page-acceptance/);
  assert.match(agents, /功能纵向切片优先/);
  assert.match(agents, /默认交给 GitHub Actions/);
  assert.match(agents, /核对目标提交的必需工作流/);
});

test('统一构建后的快速套件必须显式跳过重复构建', async () => {
  const sources = await Promise.all([
    read('README.md'),
    read('docs/development/getting-started.md'),
    read(
      '.agents/skills/fullnet-performance-hardening/references/performance-map.md'
    )
  ]);

  for (const source of sources) {
    assert.match(source, /dotnet build Full\.NET\.slnx/);
    assert.match(source, /pnpm test:dotnet:unit -- --no-build/);
    assert.match(source, /pnpm test:dotnet:compatibility -- --no-build/);
    assert.match(source, /pnpm test:dotnet:architecture -- --no-build/);
  }
});

test('本地受影响测试选择器不得调用 full', async () => {
  const selector = await read(
    'scripts/testing/run-affected-integration.mjs'
  );

  assert.doesNotMatch(selector, /argumentsFor\(['"]full['"]\)/);
  assert.doesNotMatch(selector, /完整 199|199 项/);
  assert.match(selector, /本地受影响测试选择器禁止执行 full/);
});

test('main Integration 门禁必须从测试矩阵读取分片并汇总结果', async () => {
  const workflow = await read('.github/workflows/ci.yml');
  assert.match(workflow, /^\s{2}integration-matrix:/m);
  assert.match(workflow, /print-test-matrix\.mjs/);
  assert.match(workflow, /^\s{2}integration-shard:/m);
  assert.match(workflow, /fromJSON\(needs\.integration-matrix\.outputs\.shards\)/);
  assert.doesNotMatch(
    workflow,
    /shard:\s*\[api-sqlserver, api-mysql, migrations, infrastructure\]/
  );
  assert.match(workflow, /^\s{2}integration-gate:/m);
  assert.match(workflow, /needs:\s*\[build-test, integration-shard\]/);
});

test('本地 API 工厂必须支持只读模板克隆，容器默认复用', async () => {
  const fixture = await read(
    'tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs'
  );
  const factory = await read(
    'tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs'
  );
  const template = await read(
    'tests/Full.NET.IntegrationTests/ApiSchemaTemplate.cs'
  );

  assert.match(fixture, /WithReuse\(true\)/);
  assert.match(fixture, /TESTCONTAINERS_REUSE_ENABLE/);
  assert.match(fixture, /FULLNET_TESTCONTAINERS_REUSE/);
  assert.match(factory, /TryHydrateEmptyDatabaseAsync/);
  assert.match(factory, /RunDbUpMigrationsAsync/);
  assert.match(template, /只读 schema 模板/);
  assert.match(template, /FULLNET_API_SCHEMA_TEMPLATE/);
  assert.match(template, /RESTORE DATABASE/);
  assert.match(template, /CREATE TABLE \{quotedTarget\}\.\{quotedTable\} LIKE/);
  assert.match(template, /ClearAllPools/);
  assert.match(template, /ContainsBootstrapDataAsync/);
});

test('Playwright 本地必须压低 Vite 日志并允许复用已有 dev server', async () => {
  const parity = await read('tests/e2e/admin-parity/playwright.config.mjs');
  const realStack = await read(
    'tests/e2e/admin-real-stack/playwright.config.mjs'
  );

  for (const source of [parity, realStack]) {
    assert.match(source, /--logLevel error/);
    assert.match(source, /PLAYWRIGHT_WEBSERVER_LOGS/);
    assert.match(source, /reporter: process\.env\.GITHUB_ACTIONS \? 'github' : 'line'/);
  }

  assert.match(realStack, /reuseExistingServer: !process\.env\.CI/);
});
