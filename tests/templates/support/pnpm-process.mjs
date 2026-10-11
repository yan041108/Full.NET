import { spawnSync } from 'node:child_process';

export function runPnpm(args, options = {}, run = spawnSync) {
  const processOptions = { windowsHide: true, ...options };
  if (process.platform === 'win32') {
    // 这里只接收验收脚本的固定参数；先拒绝空白、展开及控制语法，再构造无参数数组的命令。
    if (args.some(argument => typeof argument !== 'string' || !/^[A-Za-z0-9_@./:=+-]+$/u.test(argument))) {
      throw new TypeError('Expected a fixed pnpm argument without shell syntax.');
    }
    return run('pnpm ' + args.join(' '), { ...processOptions, shell: true });
  }

  // Unix 直接传递参数数组，保持参数边界且不经过 shell。
  return run('pnpm', args, { ...processOptions, shell: false });
}

// 生成应用安装可能遇到 Windows 短暂目录占用；只对明确的 EBUSY 重试一次并保留两次结果。
export function runPnpmInstall(options = {}, run = runPnpm) {
  const attempts = [];
  let result = run(['install', '--frozen-lockfile'], options); attempts.push(result);
  if (result.status > 0 && /\bERR_PNPM_EBUSY\b/u.test((result.stdout ?? '') + (result.stderr ?? ''))) {
    result = run(['install', '--frozen-lockfile'], options); attempts.push(result);
  }
  return { result, attempts };
}
