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

test('测试门槛在 canonical 来源与最新审计记录中保持一致', async () => {
  const canonicalFiles = [
    'README.md',
    'docs/development/getting-started.md',
    '.github/workflows/ci.yml',
    '.agents/skills/fullnet-module-delivery/references/delivery-map.md'
  ];
  const suites = [
    'Full.NET.UnitTests',
    'Full.NET.CompatibilityTests',
    'Full.NET.ArchitectureTests',
    'Full.NET.IntegrationTests'
  ];
  const canonicalThresholds = [];
  for (const file of canonicalFiles) {
    const text = await read(file);
    const thresholds = suites.map(suite => {
      const pattern = new RegExp(
        `${suite.replaceAll('.', '\\.')}[\\s\\S]{0,400}?--minimum-expected-tests\\s+(\\d+)`,
        'g'
      );
      const matches = [...text.matchAll(pattern)];
      assert.ok(matches.length > 0, `${file} 缺少 ${suite} 的测试门槛`);
      return Math.max(...matches.map(match => Number(match[1])));
    });
    canonicalThresholds.push(thresholds);
  }

  for (const thresholds of canonicalThresholds.slice(1)) {
    assert.deepEqual(
      thresholds,
      canonicalThresholds[0],
      'README、getting-started、CI 与 Skill delivery-map 的门槛必须一致'
    );
  }

  const auditText = await read('docs/verification/test-threshold-audit-2026-07-19.md');
  const auditMatches = [...auditText.matchAll(
    /四处 canonical 门槛[^\n]*\*\*(\d+)\/(\d+)\/(\d+)\/(\d+)\*\*/g
  )];
  assert.ok(auditMatches.length > 0, '测试门槛审计缺少 canonical 门槛记录');
  const latestAuditThresholds = auditMatches.at(-1).slice(1).map(Number);
  assert.deepEqual(
    latestAuditThresholds,
    canonicalThresholds[0],
    '最新测试门槛审计必须与四个 canonical 来源一致'
  );

  const qualityRules = await read('rules/development-quality.md');
  assert.match(
    qualityRules,
    /test-threshold-audit/,
    '开发质量规则必须要求同步最新测试门槛审计记录'
  );
});
