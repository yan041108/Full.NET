import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';
import { inspectDiagnosticsBuild } from '../../../scripts/database/build-state.mjs';
import { concentratedAcceptanceTimeout, runConcentratedAcceptance } from '../../../scripts/testing/test-run-context.mjs';

// 复用已有真实栈的 Testcontainers 依赖，不引入另一套数据库驱动或锁文件。
const { GenericContainer, Wait } = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url))('testcontainers');
const root = fileURLToPath(new URL('../../../', import.meta.url));
const exec = promisify(execFile);

test('MySQL 关闭 Performance Schema 时 blocking 和 all 必须明确失败', { timeout: concentratedAcceptanceTimeout }, async t => {
  assert.equal((await inspectDiagnosticsBuild(root)).ok, true, '先运行 pnpm run db:diagnose:build');
  await runConcentratedAcceptance(t, async () => {
    const password = randomBytes(24).toString('hex');
    const container = await new GenericContainer('mysql:8.0')
      .withResourcesQuota({ cpu: 1, memory: 1.5 })
      .withEnvironment({ MYSQL_DATABASE: 'diagnostics', MYSQL_ROOT_PASSWORD: password })
      .withCommand(['--performance-schema=OFF'])
      .withExposedPorts(3306)
      .withWaitStrategy(Wait.forListeningPorts())
      .withStartupTimeout(90000)
      .start();
    try {
      const env = { ...process.env, FULLNET_DIAGNOSTICS_LIVE_CONNECTION:
        `Server=${container.getHost()};Port=${container.getMappedPort(3306)};Database=diagnostics;User ID=root;Password=${password};SslMode=Required` };
      for (const check of ['connection', 'blocking', 'all']) {
        let result;
        try {
          result = { code: 0, ...await exec(process.execPath, ['scripts/database/diagnose.mjs',
            '--provider', 'MySql', '--check', check, '--connection-env', 'FULLNET_DIAGNOSTICS_LIVE_CONNECTION'],
          { cwd: root, env, windowsHide: true, timeout: 60000 }) };
        } catch (error) {
          if (!Number.isInteger(error.code)) throw error;
          result = error;
        }
        assert.equal(result.stderr, '');
        assert.equal(result.stdout.includes(password), false);
        const report = JSON.parse(result.stdout);
        if (check === 'connection') {
          assert.equal(result.code, 0);
          assert.equal(report.results[0].rows[0].PerformanceSchemaEnabled, 0);
        } else {
          assert.equal(result.code, 1);
          assert.equal(report.ok, false);
          const blocking = report.results.find(item => item.check === 'blocking');
          assert.equal(blocking.ok, false);
          assert.equal(blocking.error.category, 'performance_schema_disabled');
          if (check === 'all') assert.equal(report.results.filter(item => item.ok).length, 3);
        }
      }
    } finally { await container.stop(); }
  }, { cwd: root, executionTimeoutMs: 150000 });
});
