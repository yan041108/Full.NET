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

test('验证策略引用的命令必须真实存在，规则标识保持可追踪', async () => {
  const rules = await read('rules/development-quality.md');
  const { scripts } = JSON.parse(await read('package.json'));
  const commands = new Set([...rules.matchAll(/\bpnpm (test:[a-z0-9:-]+)/g)].map(match => match[1]));
  assert.ok(commands.size > 0, '验证策略必须提供可执行入口');
  for (const command of commands) {
    assert.ok(scripts[command], `验证策略引用了不存在的命令：${command}`);
  }
  // 标识用于历史链接；不固定规则正文的句式或把文字出现当成执行通过。
  for (const id of [
    'R-20260816-local-test-inner-budget',
    'R-20260903-github-actions-first-verification',
    'R-20260905-feature-first-page-acceptance'
  ]) {
    assert.ok(rules.split('\n').some(line => line.startsWith(`### ${id}：`)), `规则标识丢失：${id}`);
  }
});

test('入口与项目 Skill 必须直达同一验证策略，避免复制本地重测步骤', async () => {
  const authority = path.join(repositoryRoot, 'rules/development-quality.md');
  for (const file of [
    'AGENTS.md',
    '.agents/skills/fullnet-module-delivery/SKILL.md',
    '.agents/skills/fullnet-module-delivery/references/delivery-map.md',
    '.agents/skills/fullnet-performance-hardening/SKILL.md',
    '.agents/skills/fullnet-performance-hardening/references/performance-map.md'
  ]) {
    const source = await read(file);
    const links = [...source.matchAll(/\]\(([^)#]+)#([^)]*)\)/g)];
    const policyLinks = links.filter(([, target, anchor]) =>
      path.resolve(repositoryRoot, path.dirname(file), target) === authority &&
      anchor === '11-测试与验证');
    assert.ok(policyLinks.length > 0, `${file} 必须链接权威验证章节`);
    const policy = await readFile(authority, 'utf8');
    assert.ok(policy.includes('## 11. 测试与验证'), '验证章节链接失效');
    // Skill 是策略消费者；复制可执行重测命令会在策略更新后继续误导后续任务。
    assert.doesNotMatch(source, /pnpm test:(?:inner|slice|integration:affected)(?![a-z:-])/, `${file} 不应复制本地 Integration 执行命令`);
  }
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

test('CI 真实栈门禁必须执行含 oidc-center 的 Playwright 全量套件', async () => {
  const workflow = await read('.github/workflows/ci.yml');
  const rootPackage = await read('package.json');
  const realStack = await read(
    'tests/e2e/admin-real-stack/playwright.config.mjs'
  );

  assert.match(workflow, /real-stack-e2e:/);
  assert.match(workflow, /pnpm test:e2e:real/);
  assert.match(rootPackage, /"test:e2e:real:oidc-center"/);
  assert.match(realStack, /name:\s*'vue-admin-oidc-center'/);
  assert.match(realStack, /testMatch:\s*'\*\*\/admin-oidc-center\.spec\.mjs'/);
  assert.match(realStack, /testIgnore:\s*'\*\*\/admin-oidc-center\.spec\.mjs'/);
});
