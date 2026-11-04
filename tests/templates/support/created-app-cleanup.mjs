import { rmSync } from 'node:fs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

// 清理逐项尝试并保留原始失败，不能由后续文件占用掩盖实际构建或业务错误。
export async function cleanupCreatedApp({ browserRuntime, apiProcess, apiLogStream, workerProcess, workerLogStream, dbContainer, redisContainer, workspace, originalError },
  { removeWorkspace = rmSync } = {}) {
  const errors = [];
  const attempt = async action => { try { await action(); } catch (error) { errors.push(error); } };
  await attempt(() => browserRuntime?.close());
  await attempt(() => stopLoggedProcess(apiProcess, apiLogStream));
  await attempt(() => stopLoggedProcess(workerProcess, workerLogStream));
  await attempt(() => dbContainer?.stop());
  await attempt(() => redisContainer?.stop());
  if ([apiProcess, workerProcess].some(process => process?.pid && process.exitCode === null && process.signalCode === null)) {
    errors.push(new Error('Application process is still running; its workspace is retained.'));
  } else {
    // Windows 的编译文件句柄可能延迟释放；只在本次工作区内做有界重试。
    await attempt(() => removeWorkspace(workspace, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 }));
  }
  if (errors.length) {
    if (originalError) errors.unshift(originalError);
    throw errors.length === 1 ? errors[0] : new AggregateError(errors, 'Created application failed or cleanup did not complete.');
  }
}
