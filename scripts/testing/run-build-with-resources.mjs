import { spawn } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { testRunEnvironment, withTestRun } from './test-run-context.mjs';
import { prepareIsolatedBuild } from './isolated-build-output.mjs';

/** 只包装前台构建，不包装已持有运行上下文的测试入口，避免嵌套申请同一把锁。 */
export function parseBuildArguments(args) {
  const options = { cwd: process.cwd(), buildBudget: {} };
  let isolatedOutput;
  let index = 0;
  for (; index < args.length && args[index] !== '--'; index += 2) {
    const [name, value] = args.slice(index, index + 2);
    if (!value || value === '--') throw new Error(`${name} 缺少参数。`);
    if (name === '--cwd') options.cwd = path.resolve(value);
    else if (name === '--isolated-output') isolatedOutput = path.resolve(value);
    else if (name === '--cpu') options.buildBudget.cpu = Number(value);
    else if (name === '--memory-gib') options.buildBudget.memoryGiB = Number(value);
    else throw new Error(`未知构建选项：${name}`);
  }
  if (args[index] !== '--' || !args[index + 1]) throw new Error('用法：node scripts/testing/run-build-with-resources.mjs [--cwd 目录] [--cpu 4] [--memory-gib 6] -- dotnet build ...');
  return { options, isolatedOutput, command: args[index + 1], args: args.slice(index + 2) };
}

export async function runBuild(args, { lockRoot, resourceProbe, onWait } = {}) {
  const parsed = parseBuildArguments(args);
  const sourceCwd = parsed.options.cwd;
  if (parsed.isolatedOutput) {
    const isolated = await prepareIsolatedBuild({ cwd: sourceCwd, output: parsed.isolatedOutput,
      command: parsed.command, args: parsed.args });
    // 子进程仍读取原源码；锁保护真实且覆盖传递依赖的独立输出，避免同源码目录一律串行。
    parsed.options.cwd = isolated.directory;
    parsed.args = isolated.args;
  }
  process.stdout.write(`构建准备：源码=${sourceCwd}；输出=${parsed.isolatedOutput ?? '工作区共享输出（互斥）'}\n`);
  return withTestRun({ ...parsed.options, lockRoot, resourceProbe, ...(onWait ? { onWait } : {}) }, () => new Promise((resolve, reject) => {
    process.stdout.write(`构建已准入：CPU并行度=${testRunEnvironment().DOTNET_PROCESSOR_COUNT}；内存预算=${parsed.options.buildBudget.memoryGiB ?? 6} GiB\n`);
    const child = spawn(parsed.command, parsed.args, { cwd: sourceCwd,
      env: testRunEnvironment(), stdio: 'inherit', shell: false, windowsHide: true });
    let failure;
    child.on('error', error => { failure = error; });
    // 等待句柄关闭再释放预算；启动失败也走 close，不能只在 exit/error 时释放。
    child.on('close', (code, signal) => {
      if (failure || code !== 0) reject(failure ?? new Error(`构建退出：${signal ?? code}`));
      else resolve();
    });
  }));
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  runBuild(process.argv.slice(2)).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
