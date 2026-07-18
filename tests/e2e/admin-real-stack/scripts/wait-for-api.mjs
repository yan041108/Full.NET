import { setTimeout as sleep } from 'node:timers/promises';

/** 轮询 API 存活探针，避免在 Host 尚未监听时开始浏览器测试。 */
export async function waitForApi(apiUrl, timeoutMs = 120_000) {
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
  );
}
