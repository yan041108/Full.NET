import assert from 'node:assert/strict';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

// 发布积压包含尚未到期的租约与重试；不能仅凭短暂观察无新增副作用判定排空。
export function isOutboxDrained(backlog) {
  return backlog != null && ['pendingCount', 'dueRetryCount', 'activeLeaseCount', 'deadLetterCount']
    .every(key => backlog[key] === 0 || backlog[key] === '0');
}

// 仅接受调用方保存的自有子进程句柄；强制退出不执行宿主的优雅停机路径。
export async function crashLoggedWorker(child, stream) {
  assert.ok(child && Number.isInteger(child.pid) && child.pid > 0 && child.pid !== process.pid,
    'Worker must be an owned child process');
  assert.equal(child.exitCode, null, 'Worker already exited before crash injection');
  assert.equal(child.signalCode, null, 'Worker already terminated before crash injection');
  assert.equal(child.kill('SIGKILL'), true, 'Worker crash signal was not accepted');
  await stopLoggedProcess(child, stream);
  assert.ok(child.exitCode !== null || child.signalCode !== null, 'Worker did not exit');
  return { pid: child.pid, signal: 'SIGKILL', exitCode: child.exitCode, signalCode: child.signalCode };
}
