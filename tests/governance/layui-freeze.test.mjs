import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const frozenCommit = 'ec6eae92';
const frozenDirectory = 'ui/admin-layui';

test('Layui 存量目录必须保持 2026-08-02 冻结基线及已授权安全补丁', async () => {
  const result = spawnSync(
    'git',
    ['diff', '--exit-code', '--name-only', frozenCommit, '--', frozenDirectory, `:(exclude)${frozenDirectory}/package.json`],
    {
      cwd: process.cwd(),
      encoding: 'utf8'
    }
  );

  // 本次所有者授权清零依赖漏洞，只接纳精确 Vitest 补丁；其余清单仍逐字段冻结。
  const frozenPackage = spawnSync('git', ['show', `${frozenCommit}:${frozenDirectory}/package.json`], {
    cwd: process.cwd(), encoding: 'utf8'
  });
  assert.equal(frozenPackage.status, 0, frozenPackage.stderr.trim());
  const expectedPackage = JSON.parse(frozenPackage.stdout);
  expectedPackage.devDependencies.vitest = '4.1.11';
  const actualPackage = JSON.parse(await readFile(`${frozenDirectory}/package.json`, 'utf8'));
  assert.deepEqual(actualPackage, expectedPackage, '仅授权 Vitest 4.1.11 安全补丁，其他冻结依赖与配置不可变化。');

  assert.equal(
    result.status,
    0,
    [
      `检测到 ${frozenDirectory} 在冻结决策后发生未授权修改。`,
      '新后台能力只允许交付 Vue；Layui 例外任务必须先由项目所有者明确授权并独立调整冻结基线。',
      result.stdout.trim(),
      result.stderr.trim()
    ].filter(Boolean).join('\n')
  );

  const untracked = spawnSync(
    'git',
    ['ls-files', '--others', '--exclude-standard', '--', frozenDirectory],
    {
      cwd: process.cwd(),
      encoding: 'utf8'
    }
  );

  assert.equal(untracked.status, 0, untracked.stderr.trim());
  assert.equal(
    untracked.stdout.trim(),
    '',
    [
      `检测到 ${frozenDirectory} 下存在未跟踪的新文件。`,
      'Layui 冻结同时禁止新增页面、脚本、测试和生成产物。',
      untracked.stdout.trim()
    ].filter(Boolean).join('\n')
  );
});

test('默认客户端门禁不得选择 Layui 活动包', async () => {
  const [rootPackage, clientCi, bundleBudgets] = await Promise.all([
    readFile('package.json', 'utf8'),
    readFile('.github/workflows/ci.yml', 'utf8'),
    readFile('tests/performance/frontend-bundle-budgets.json', 'utf8')
  ]);

  const packageDefinition = JSON.parse(rootPackage);
  assert.match(
    packageDefinition.scripts['test:clients'],
    /--filter=!@fullnet\/admin-layui/
  );
  assert.match(
    packageDefinition.scripts['build:clients'],
    /--filter=!@fullnet\/admin-layui/
  );
  assert.doesNotMatch(
    packageDefinition.scripts['test:clients'],
    /--filter=@fullnet\/admin-layui/
  );
  assert.doesNotMatch(
    packageDefinition.scripts['build:clients'],
    /--filter=@fullnet\/admin-layui/
  );
  assert.match(packageDefinition.scripts['test:e2e:admin'], /admin-parity-e2e/);
  assert.match(
    packageDefinition.scripts['test:e2e:layui-frozen'],
    /admin-parity-e2e/
  );
  assert.equal(packageDefinition.scripts['test:e2e'], 'pnpm test:e2e:admin');

  assert.doesNotMatch(clientCi, /@fullnet\/admin-layui/);
  assert.match(clientCi, /pnpm test:e2e:admin/);
  assert.doesNotMatch(clientCi, /admin-layui exec vite/);

  const budgets = JSON.parse(bundleBudgets);
  assert.ok(
    budgets.targets.every(target => !String(target.dist).includes('admin-layui')),
    '活动包体预算不得再包含 Layui dist'
  );
});
