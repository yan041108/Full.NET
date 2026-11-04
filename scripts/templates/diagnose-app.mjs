import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function isCompatibleSdkVersion(version) {
  // 与现有 CLI 的 Version/正则边界一致；SDK 选择和预览准入仍由 dotnet 决定。
  const match = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/u.exec(version);
  if (!match || match[0].length !== version.length) return false;
  const parts = match.slice(1, 4).map(Number);
  return parts.every(part => part <= 2_147_483_647)
    && parts[0] === 10 && parts[1] === 0 && parts[2] >= 100;
}

export function diagnoseApplication(args, run = spawnSync, output = console) {
  if (args.length !== 2 || args[0] !== '--profile' || !['development', 'production'].includes(args[1])) {
    output.error('Usage: node .fullnet-tools/diagnose-app.mjs --profile development|production');
    return 64;
  }

  const workspace = process.cwd();
  // CLR 尚未启动时也能给出稳定错误；不回显 SDK 原始输出、路径或任意版本后缀。
  const probe = run('dotnet', ['--version'], {
    cwd: workspace, encoding: 'utf8', windowsHide: true, timeout: 30_000,
    maxBuffer: 1024 * 1024,
  });
  if (probe.error?.code === 'ETIMEDOUT') {
    output.log('code_generation.sdk.probe_timeout error .NET SDK 探测未在等待上限内完成。');
    output.log('  修复：在目标工作区运行 dotnet --version，排查 SDK 启动卡住的问题。');
    return 1;
  }
  const version = probe.stdout?.trim();
  if (probe.error || probe.status !== 0 || !version) {
    output.log('DIAG_SDK_MISSING error .NET SDK 不可用。');
    output.log('  修复：安装 .NET 10 SDK 并确保 dotnet 在 PATH 中，核对目标工作区及父目录的 global.json。');
    return 1;
  }
  if (!isCompatibleSdkVersion(version)) {
    output.log('DIAG_SDK_INCOMPATIBLE error 目标工作区选择的 SDK 不符合当前 .NET 10.0 SDK 基线，或返回的版本格式无效。');
    output.log('  修复：安装 .NET 10 SDK，核对目标工作区及父目录的 global.json；当前基线为 10.0.100 或更高的 10.0 SDK 功能带。');
    return 1;
  }

  // 前置检查不认证应用；继续使用同一工作区的现有 CLI，保留其诊断与退出语义。
  const diagnosis = run('dotnet', [
    'run', '--project', resolve(workspace, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli'),
    '--', 'diagnose', '--workspace', workspace, '--profile', args[1],
  ], { cwd: workspace, stdio: 'inherit', windowsHide: true });
  if (diagnosis.error || diagnosis.status === null) {
    output.error('code_generation.diagnose.start_failed error 诊断 CLI 未能正常启动或结束。');
    return 1;
  }
  return diagnosis.status;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = diagnoseApplication(process.argv.slice(2));
}
