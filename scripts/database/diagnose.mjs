import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { diagnosticsBuildPaths } from './build-paths.mjs';
import { inspectDiagnosticsBuild } from './build-state.mjs';

// 诊断不隐式构建或启动服务；构建单独走仓库资源锁，stdout 保持可解析 JSON。
const root = fileURLToPath(new URL('../../', import.meta.url));
const { dll } = diagnosticsBuildPaths(root);
let verified = false;
try { verified = (await inspectDiagnosticsBuild(root)).ok; } catch { /* 校验失败时关闭运行入口，不输出环境或异常细节。 */ }
if (!verified) {
  process.stderr.write('诊断工具未构建或输入/产物已改变，请运行 pnpm run db:diagnose:build。\n');
  process.exitCode = 2;
} else {
  const child = spawn('dotnet', [dll, ...process.argv.slice(2)], {
    cwd: root, env: process.env, stdio: 'inherit', windowsHide: true, shell: false
  });
  child.on('error', () => { process.stderr.write('无法启动 .NET 诊断工具。\n'); process.exitCode = 2; });
  child.on('close', code => { process.exitCode = code ?? 1; });
}
