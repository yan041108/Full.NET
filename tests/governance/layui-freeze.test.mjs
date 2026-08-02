import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const frozenCommit = 'ec6eae92';
const frozenDirectory = 'ui/admin-layui';

test('Layui 存量目录必须保持 2026-08-02 冻结基线', () => {
  const result = spawnSync(
    'git',
    ['diff', '--exit-code', '--name-only', frozenCommit, '--', frozenDirectory],
    {
      cwd: process.cwd(),
      encoding: 'utf8'
    }
  );

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
