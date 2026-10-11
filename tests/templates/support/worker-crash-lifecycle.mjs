import assert from 'node:assert/strict';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

// 就绪探针不覆盖后台扫描；每次启动的 Worker 都必须保留并检查相应日志。
export function assertNoWorkerTimeoutScanFailures(logs) {
  assert.ok(logs.length > 0, 'Worker logs required');
  for (const log of logs) assert.equal(log.includes('Workflow todo timeout worker iteration failed.'), false,
    'Worker timeout scan failed');
}

// 发布积压包含尚未到期的租约与重试；不能仅凭短暂观察无新增副作用判定排空。
export function isOutboxDrained(backlog) {
  return backlog != null && ['pendingCount', 'dueRetryCount', 'activeLeaseCount', 'deadLetterCount']
    .every(key => backlog[key] === 0 || backlog[key] === '0');
}

// 先排空再冻结全量站内信基线；迟到的正常补投不能被误判为重启重复副作用。
export async function verifyDrainedWorkerRestart({ drain, readInbox, restart, validateInbox = () => {} }) {
  const beforeBacklog = await drain('before');
  assert.ok(isOutboxDrained(beforeBacklog), 'Outbox before restart is not drained');
  const beforeInbox = await readInbox();
  await validateInbox(beforeInbox, 'before');
  const messageIds = beforeInbox.items.map(item => item.id).sort();
  await restart();
  const afterBacklog = await drain('after');
  assert.ok(isOutboxDrained(afterBacklog), 'Outbox after restart is not drained');
  const afterInbox = await readInbox();
  await validateInbox(afterInbox, 'after');
  assert.deepEqual(afterInbox.items.map(item => item.id).sort(), messageIds,
    'restart duplicated notifications');
  return { beforeBacklog, afterBacklog, messageIds };
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
