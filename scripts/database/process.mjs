import { spawn } from 'node:child_process';

/** 保持参数边界并等待句柄关闭，供构建与测试入口传播真实退出状态。 */
export function runProcess(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: 'inherit', shell: false, windowsHide: true, ...options });
    let failure;
    child.on('error', error => { failure = error; });
    child.on('close', (code, signal) => {
      if (failure || code !== 0) reject(failure ?? new Error(`子进程退出：${signal ?? code}`));
      else resolve();
    });
  });
}
