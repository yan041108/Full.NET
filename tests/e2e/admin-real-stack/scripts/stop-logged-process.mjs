import { finished } from 'node:stream/promises';

// killed只表示信号已发送；close才保证子进程及其stdio已关闭，不能提前结束日志流。
export async function stopLoggedProcess(child, log, { timeoutMs = 10_000, forceTimeoutMs = 5_000 } = {}) {
  const logFinished = log ? finished(log, { cleanup: true }) : Promise.resolve();
  // 立即接住写盘错误，最终仍向调用方传播，避免等待进程期间出现未处理拒绝。
  void logFinished.catch(() => {});
  let failure;
  try {
    if (child) await waitForClose(child, timeoutMs, forceTimeoutMs);
  } catch (error) {
    failure = error;
  } finally {
    if (log) {
      child?.stdout?.unpipe(log);
      child?.stderr?.unpipe(log);
      if (!log.writableEnded) log.end();
    }
    try {
      await logFinished;
    } catch (error) {
      failure = failure ? new AggregateError([failure, error], 'Process and log shutdown failed') : error;
    }
  }
  if (failure) throw failure;
}

function waitForClose(child, timeoutMs, forceTimeoutMs) {
  const terminated = child.exitCode !== null || child.signalCode !== null;
  const drained = [child.stdout, child.stderr].every(stream => !stream || stream.readableEnded || stream.destroyed);
  if (terminated && drained) return Promise.resolve();
  return new Promise((resolve, reject) => {
    let timer;
    let forceTimer;
    let settled = false;
    const complete = error => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      clearTimeout(forceTimer);
      child.off('close', onClose);
      child.off('error', onError);
      if (error) reject(error);
      else resolve();
    };
    const onClose = () => complete();
    const onError = error => complete(error);
    child.once('close', onClose);
    child.once('error', onError);
    timer = setTimeout(() => {
      forceTimer = setTimeout(() => complete(new Error('Process did not close after shutdown')), forceTimeoutMs);
      try {
        if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
      } catch (error) {
        complete(error);
      }
    }, timeoutMs);
    try {
      if (!terminated && !child.killed) child.kill();
    } catch (error) {
      complete(error);
    }
  });
}
