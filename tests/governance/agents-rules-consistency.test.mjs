import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// 治理一致性测试：保证 AGENTS.md 只作为“单行不变量 + 权威链接”的入口，
// 具体可执行细则集中在 rules/，防止基线段与 rules/ 出现双写漂移或悬空引用。

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

const ruleFiles = [
  'rules/README.md',
  'rules/code-comments.md',
  'rules/development-quality.md',
  'rules/naming-conventions.md',
  'rules/client-frontend.md',
  'rules/rule-evolution.md',
  'rules/skill-evolution.md'
];

// 只应存在于 rules/ 的细节令牌；一旦回流到 AGENTS.md 即视为双写漂移。
const inlineDetailDenylist = [
  'Dommel',
  'FluentMap',
  'Rainbow',
  'Contrib',
  'GridReader',
  'Guid.ToByteArray',
  'UUID_TO_BIN',
  'TimeSwapBinary16',
  'uniqueidentifier',
  'BINARY(16)'
];

const ruleIdPattern = /R-\d{8}-[a-z0-9-]+/g;
const markdownLinkPattern = /\]\(([^)]+)\)/g;

function baselineSection(agentsText) {
  const start = agentsText.indexOf('## Full.NET 不可隐式改变的基线');
  assert.notEqual(start, -1, 'AGENTS.md 必须包含“不可隐式改变的基线”章节');
  const rest = agentsText.slice(start + 1);
  const nextHeading = rest.indexOf('\n## ');
  return nextHeading === -1 ? rest : rest.slice(0, nextHeading);
}

test('AGENTS.md 引用的规则标识必须在 rules/ 中定义', async () => {
  const agentsText = await read('AGENTS.md');
  const referenced = new Set(agentsText.match(ruleIdPattern) ?? []);
  assert.ok(referenced.size > 0, 'AGENTS.md 基线应通过规则标识指向权威细则');

  const defined = new Set();
  for (const file of ruleFiles) {
    const text = await read(file);
    for (const match of text.matchAll(/^#{2,4}\s+(R-\d{8}-[a-z0-9-]+)/gm)) {
      defined.add(match[1]);
    }
  }

  for (const id of referenced) {
    assert.ok(defined.has(id), `AGENTS.md 引用了未在 rules/ 中定义的规则：${id}`);
  }
});

test('基线段每条都必须链接权威源，不得内联细则', async () => {
  const section = baselineSection(await read('AGENTS.md'));
  const bullets = section
    .split('\n')
    .filter(line => line.startsWith('- '));
  assert.ok(bullets.length >= 10, '基线条目数量异常，可能被误删');

  for (const bullet of bullets) {
    const links = [...bullet.matchAll(markdownLinkPattern)].map(match => match[1]);
    const pointsToAuthority = links.some(
      link => link.startsWith('rules/') || link.startsWith('docs/')
    );
    assert.ok(
      pointsToAuthority,
      `基线条目缺少指向 rules/ 或 docs/ 的权威链接：${bullet.slice(0, 40)}…`
    );
  }
});

test('AGENTS.md 不得内联只属于 rules/ 的细节令牌', async () => {
  const agentsText = await read('AGENTS.md');
  for (const token of inlineDetailDenylist) {
    // 以非拉丁字母边界匹配，避免误伤包含相同片段的合法词（如 Contributor 含 Contrib）。
    const escaped = token.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const boundaryPattern = new RegExp(`(?<![A-Za-z])${escaped}(?![A-Za-z])`);
    assert.ok(
      !boundaryPattern.test(agentsText),
      `AGENTS.md 出现应仅存在于 rules/ 的细节令牌“${token}”，请改为链接权威规则`
    );
  }
});

test('AGENTS.md 指向的 rules/ 与 docs/ 链接必须真实存在', async () => {
  const agentsText = await read('AGENTS.md');
  const targets = [...agentsText.matchAll(markdownLinkPattern)]
    .map(match => match[1])
    .filter(link => link.startsWith('rules/') || link.startsWith('docs/'))
    .map(link => link.split('#')[0]);

  for (const target of new Set(targets)) {
    assert.ok(await exists(target), `AGENTS.md 链接指向不存在的文件：${target}`);
  }
});

test('客户端规则文件已建立并被索引引用', async () => {
  assert.ok(await exists('rules/client-frontend.md'), '缺少 rules/client-frontend.md');

  const agentsText = await read('AGENTS.md');
  assert.match(agentsText, /rules\/client-frontend\.md/, 'AGENTS.md 必须引用客户端规则文件');

  const readmeText = await read('rules/README.md');
  assert.match(readmeText, /\(client-frontend\.md\)/, 'rules/README.md 索引必须收录客户端规则文件');
});

test('client-frontend.md 内部链接必须真实存在', async () => {
  const text = await read('rules/client-frontend.md');
  const targets = [...text.matchAll(markdownLinkPattern)]
    .map(match => match[1])
    .filter(link => link.startsWith('../') || link.endsWith('.md'))
    .map(link => link.split('#')[0]);

  for (const target of new Set(targets)) {
    const resolved = path
      .relative(repositoryRoot, path.resolve(repositoryRoot, 'rules', target))
      .split(path.sep)
      .join('/');
    assert.ok(await exists(resolved), `client-frontend.md 链接指向不存在的文件：${target}`);
  }
});

test('测试门槛只有一个机器事实源且 CI 使用稳定命令', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  assert.equal(matrix.schemaVersion, 1);
  for (const suite of Object.values(matrix.dotnetSuites)) {
    assert.ok(Number.isInteger(suite.minimum) && suite.minimum > 0);
  }
  assert.equal(
    matrix.integration.mainPartitions.reduce(
      (sum, name) => sum + matrix.integration.shards[name].minimum,
      0
    ),
    matrix.integration.shards.full.minimum
  );

  const workflow = await read('.github/workflows/ci.yml');
  for (const command of [
    'pnpm test:dotnet:unit',
    'pnpm test:dotnet:compatibility',
    'pnpm test:dotnet:architecture'
  ]) {
    assert.match(workflow, new RegExp(command.replaceAll(':', '\\:')));
  }
  assert.doesNotMatch(
    workflow,
    /--minimum-expected-tests\s+\d+/,
    'CI 不得复制机器清单中的最低发现数'
  );

  for (const file of [
    'README.md',
    'docs/development/getting-started.md',
    'rules/development-quality.md',
    '.agents/skills/fullnet-module-delivery/references/delivery-map.md',
    '.agents/skills/fullnet-performance-hardening/references/performance-map.md'
  ]) {
    const text = await read(file);
    assert.match(
      text,
      /eng\/testing\/test-matrix\.json/,
      `${file} 必须引用测试矩阵唯一事实源`
    );
  }

  const qualityRules = await read('rules/development-quality.md');
  assert.doesNotMatch(
    qualityRules,
    /最新 `docs\/verification\/test-threshold-audit-\*\.md`/,
    '普通测试数量变化不得继续要求人工追加审计长文档'
  );
});

test('治理演进和永久文档使用证据触发而不是每任务扩张', async () => {
  const qualityRules = await read('rules/development-quality.md');
  const ruleEvolution = await read('rules/rule-evolution.md');
  const skillEvolution = await read('rules/skill-evolution.md');

  assert.match(qualityRules, /普通功能.*PR/);
  assert.match(qualityRules, /跨模块或预计超过一个工作日.*计划/);
  assert.match(qualityRules, /性能基准、安全审计、恢复演练或发布.*Verification/);

  assert.match(ruleEvolution, /触发式复盘/);
  assert.match(ruleEvolution, /未命中触发条件.*一行/);
  assert.doesNotMatch(
    ruleEvolution,
    /每项开发、修复、重构、审查或合并任务结束前[\s\S]{0,40}必须执行一次规则复盘/
  );

  assert.match(skillEvolution, /里程碑集中复盘/);
  assert.match(skillEvolution, /冻结新增项目 Skill/);
  assert.doesNotMatch(skillEvolution, /更新命中的候选次数/);
});
