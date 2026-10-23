import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test, { before } from 'node:test';
import { fileURLToPath } from 'node:url';
import { diagnosticsBuildPaths } from '../../scripts/database/build-paths.mjs';
import { inspectDiagnosticsBuild } from '../../scripts/database/build-state.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const { dll } = diagnosticsBuildPaths(root);
before(async () => assert.equal((await inspectDiagnosticsBuild(root)).ok, true,
  '测试要求当前源码的有效构建；使用 pnpm run test:database-diagnostics 自动准备。'));
function invoke(args, connection) {
  const env = { ...process.env };
  delete env.FULLNET_DIAGNOSTICS_TEST_CONNECTION;
  if (connection !== undefined) env.FULLNET_DIAGNOSTICS_TEST_CONNECTION = connection;
  const result = spawnSync('dotnet', [dll, ...args, '--connection-env', 'FULLNET_DIAGNOSTICS_TEST_CONNECTION'], {
    cwd: root, env, encoding: 'utf8', timeout: 20000, windowsHide: true
  });
  assert.ifError(result.error);
  assert.equal(result.stderr, '');
  return { code: result.status, report: JSON.parse(result.stdout), output: result.stdout };
}

test('拒绝未实现 Provider、任意 SQL、重复和缺值参数', () => {
  for (const args of [
    ['--provider', 'PostgreSQL'], ['--provider', 'Oracle'],
    ['--provider', 'SqlServer', '--sql', 'DELETE FROM users'],
    ['--provider', 'SqlServer', '--check', 'connection', '--check', 'all'],
    ['--provider', 'SqlServer', '--table']
  ]) {
    const result = invoke(args);
    assert.equal(result.code, 2);
    assert.equal(result.report.error, 'invalid_arguments');
  }
});

test('表过滤不能携带 SQL、跨 schema 或超长标识符', () => {
  for (const table of ['x;DROP TABLE x', 'dbo.users', 'x'.repeat(65)]) {
    assert.equal(invoke(['--provider', 'MySql', '--table', table]).code, 2);
  }
});

test('显式环境变量缺失时不借用个人保存的诊断账户', () => {
  const result = invoke(['--provider', 'SqlServer']);
  assert.equal(result.code, 2);
  assert.equal(result.report.error, 'connection_not_configured');
});

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} 配置错误脱敏，数据库未指定时拒绝连接`, () => {
    const marker = 'NEVER_ECHO_this_password_935';
    for (const connection of [`invalid-key-${marker}=x`, `Server=127.0.0.1;Password=${marker}`]) {
      const result = invoke(['--provider', provider], connection);
      assert.equal(result.code, 1);
      assert.equal(result.report.error.category, 'invalid_configuration');
      assert.equal(result.output.includes(marker), false);
    }
  });
}
