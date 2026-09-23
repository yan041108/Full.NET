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
export async function waitForApi(apiUrl, timeoutMs = 120_000, apiLogPath) {
  const target = `${apiUrl.replace(/\/$/, '')}/health/live`;
  const deadline = Date.now() + timeoutMs;
  let lastError;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(target);
      if (response.ok) {
        return;
      }

      lastError = new Error(`健康检查返回 ${response.status}`);
    } catch (error) {
      lastError = error;
    }

    await sleep(1_000);
  }

  throw new Error(
    `等待 API 就绪超时：${target}（${lastError?.message ?? 'unknown'}）`
    + formatApiLogHint(apiLogPath)
  );
}
