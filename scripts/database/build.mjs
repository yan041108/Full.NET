import { fileURLToPath } from 'node:url';
import { prepareIsolatedBuild } from '../testing/isolated-build-output.mjs';
import { testRunEnvironment, withTestRun } from '../testing/test-run-context.mjs';
import { diagnosticsBuildPaths } from './build-paths.mjs';
import { diagnosticsInputs, invalidateDiagnosticsBuild, recordDiagnosticsBuild } from './build-state.mjs';
import { runProcess } from './process.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
try {
  const paths = diagnosticsBuildPaths(root);
  const isolated = await prepareIsolatedBuild({ cwd: root, output: paths.output, command: 'dotnet', args: [
    'build', 'src/Tools/Full.NET.DatabaseDiagnostics.Cli/Full.NET.DatabaseDiagnostics.Cli.csproj', '--configuration', 'Release'
  ] });
  // 记录与编译持有同一把输出锁，其他构建不能在产物登记前插入。
  await withTestRun({ cwd: isolated.directory, buildBudget: { cpu: 1, memoryGiB: 1 } }, async () => {
    await invalidateDiagnosticsBuild(paths);
    const before = await diagnosticsInputs(root);
    await runProcess('dotnet', isolated.args, { cwd: root, env: testRunEnvironment() });
    await recordDiagnosticsBuild(root, before);
    process.stdout.write('诊断工具已构建，输入与运行依赖摘要已验证并登记。\n');
  });
} catch (error) {
  process.stderr.write(`诊断工具构建失败：${error.message}\n`);
  process.exitCode = 1;
}
