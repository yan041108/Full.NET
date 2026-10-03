import http from 'node:http';
import { realpathSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { performance } from 'node:perf_hooks';

// 测试端只保留交付证明，拒绝其他任务和无效状态，不记录原始标签、注解或完整告警正文。
export function parseNotification(body, namespace) {
  if (body?.version !== '4' || body.receiver !== 'local-test' || !['firing', 'resolved'].includes(body.status)
      || !Array.isArray(body.alerts) || body.alerts.length !== 1 || body.truncatedAlerts > 0) throw new Error('Invalid notification');
  const alert = body.alerts[0];
  if (alert?.labels?.namespace !== namespace || alert.labels.pod !== 'consumer-restart'
      || alert.labels.alertname !== 'FullNetLogConsumerRestarting' || alert.status !== body.status
      || typeof alert.fingerprint !== 'string' || !/^[a-zA-Z0-9_-]{1,128}$/.test(alert.fingerprint)
      || !Number.isFinite(Date.parse(alert.startsAt)) || !Number.isFinite(Date.parse(alert.endsAt))) throw new Error('Unrelated or invalid alert');
  return { status: alert.status, fingerprint: alert.fingerprint, namespace,
    pod: alert.labels.pod, alertname: alert.labels.alertname };
}

export function createReceiver({ namespace, nonce, failFirst = 0, holdMilliseconds = 0, now = () => performance.now() }) {
  if (!Number.isInteger(failFirst) || failFirst < 0 || failFirst > 8) throw new RangeError('Invalid bounded fault budget');
  if (!Number.isInteger(holdMilliseconds) || holdMilliseconds < 0 || holdMilliseconds > 300000
      || (holdMilliseconds && failFirst)) throw new RangeError('Invalid bounded outage duration');
  const events = [];
  const attempts = [];
  let outageStart;
  return http.createServer((request, response) => {
    if (request.method === 'GET' && request.url === `/events/${nonce}`) {
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify(events));
      return;
    }
    if (request.method === 'GET' && request.url === `/attempts/${nonce}`) {
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify(attempts));
      return;
    }
    if (request.method !== 'POST' || request.url !== `/sink/${nonce}`) {
      response.writeHead(404).end(); return;
    }
    let size = 0;
    const chunks = [];
    request.on('data', chunk => {
      size += chunk.length;
      // 两项边界避免重试洪峰或大正文把本地测试接收端变成无界队列。
      if (size > 65536 && !response.writableEnded) { response.writeHead(413).end(() => request.destroy()); }
      else if (size <= 65536) chunks.push(chunk);
    });
    request.on('error', () => {});
    request.on('end', () => {
      if (response.writableEnded) return;
      try {
        const event = parseNotification(JSON.parse(Buffer.concat(chunks).toString('utf8')), namespace);
        if (attempts.length >= 100) { response.writeHead(503).end(); return; }
        // 只有经过身份验证的有效请求消耗故障预算；503 尝试不进入已接收事件集合。
        // 以首次有效通知和单调时钟计时；部署等待及无效请求不能提前耗尽故障窗口。
        const timestamp = holdMilliseconds ? now() : 0;
        if (holdMilliseconds) outageStart ??= timestamp;
        const elapsedMs = holdMilliseconds ? Math.floor(timestamp - outageStart) : 0;
        const code = attempts.length < failFirst || (holdMilliseconds && elapsedMs < holdMilliseconds) ? 503 : 200;
        attempts.push({ ...event, code, ...(holdMilliseconds ? { elapsedMs } : {}) });
        if (code === 503) { response.writeHead(503).end(); return; }
        events.push(event);
        response.writeHead(200).end();
      } catch { response.writeHead(400).end(); }
    });
  });
}

// ConfigMap 投影路径包含符号链接，入口须比较真实路径，否则直接执行会被误判为模块导入。
if (process.argv[1] && realpathSync(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const namespace = process.env.TEST_NAMESPACE;
  const nonce = process.env.TEST_NONCE;
  if (!namespace?.startsWith('fullnet-log-restart-') || !/^[a-f0-9]{32}$/.test(nonce ?? '')) throw new Error('Invalid isolated receiver configuration');
  const server = createReceiver({ namespace, nonce, failFirst: Number(process.env.TEST_FAIL_FIRST ?? 0),
    holdMilliseconds: Number(process.env.TEST_HOLD_MILLISECONDS ?? 0) });
  server.listen(8080, '0.0.0.0');
  process.on('SIGTERM', () => { server.closeAllConnections(); server.close(); });
}
