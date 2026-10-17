import { readFileSync } from 'node:fs';
import { setTimeout as sleep } from 'node:timers/promises';

function formatApiLogHint(apiLogPath) {
  if (!apiLogPath) {
    return '';
  }

  try {
    const text = readFileSync(apiLogPath, 'utf8').trim();
    if (!text) {
      return `\n(API 日志 ${apiLogPath} 为空；进程可能未写入或立即退出。)`;
    }

    const lines = text.split(/\r?\n/);
    const tail = lines.slice(-40).join('\n');
    return `\n(API 日志末尾 ${apiLogPath}:\n${tail})`;
  } catch {
    return '';
  }
}

/** 轮询 API 存活探针，避免在 Host 尚未监听时开始浏览器测试。 */
export async function waitForApi(apiUrl, timeoutMs = 120_000, apiLogPath, { signal } = {}) {
  const target = `${apiUrl.replace(/\/$/, '')}/health/live`;
  const deadline = Date.now() + timeoutMs;
  let lastError;

  while (Date.now() < deadline) {
    signal?.throwIfAborted();
    try {
      // 总 deadline 与单次探针都必须能中断已连接但不返回的请求，保证 finally 可以清理宿主。
      const attempt = AbortSignal.timeout(Math.max(1, Math.min(5_000, deadline - Date.now())));
      const response = await fetch(target, { signal: signal ? AbortSignal.any([signal, attempt]) : attempt });
      void response.body?.cancel().catch(() => {});
      if (response.ok) {
        return;
      }

      lastError = new Error(`健康检查返回 ${response.status}`);
    } catch (error) {
      signal?.throwIfAborted();
      lastError = error;
    }

    const remaining = deadline - Date.now();
    if (remaining <= 0) break;
    await sleep(Math.min(1_000, remaining), undefined, { signal });
  }

  throw new Error(
    `等待 API 就绪超时：${target}（${lastError?.message ?? 'unknown'}）`
    + formatApiLogHint(apiLogPath)
  );
}
