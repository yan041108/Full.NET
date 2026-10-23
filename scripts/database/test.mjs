import { fileURLToPath } from 'node:url';
import { inspectDiagnosticsBuild } from './build-state.mjs';
import { runProcess } from './process.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
try {
  if (!(await inspectDiagnosticsBuild(root)).ok)
    await runProcess(process.execPath, ['scripts/database/build.mjs'], { cwd: root });
  await runProcess(process.execPath, ['--test', 'tests/database-diagnostics/cli.test.mjs'], { cwd: root });
} catch (error) {
  process.stderr.write(`诊断工具测试失败：${error.message}\n`);
  process.exitCode = 1;
}
