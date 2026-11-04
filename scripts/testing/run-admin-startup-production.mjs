import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import path from 'node:path';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';

// 复用最新生产构建，只执行启动 SDK、会话恢复、设置及导航场景；原始失败保留在独立系统 Temp。
const root = fileURLToPath(new URL('../../', import.meta.url));
await withTestRun({ cwd: root, heavy: true }, async () => {
  const env = testRunEnvironment();
  const run = spawnSync(process.execPath, [
    'tests/e2e/admin-parity/node_modules/@playwright/test/cli.js', 'test',
    '--config', 'tests/e2e/admin-parity/playwright.startup-production.config.mjs',
    '--output', path.join(env.TEMP, 'startup-browser')
  ], { cwd: root, env, encoding: 'utf8', windowsHide: true, timeout: 240000, maxBuffer: 15 * 1024 * 1024 });
  const output = (run.stdout ?? '') + (run.stderr ?? '');
  fs.writeFileSync(path.join(env.TEMP, 'startup-browser.log'), output);
  process.stdout.write(output);
  if (run.error) throw run.error;
  if (run.status !== 0) throw new Error(`Startup production browser exit ${run.status}`);
});
