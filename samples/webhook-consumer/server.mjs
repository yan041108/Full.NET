import http from 'node:http';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { openReceiptStore, isGuid } from './receipt-store.mjs';
import { readSigningSecret, verifySignature } from './signature.mjs';

const eventType = 'fullnet.workflow.instance.completed';
const problem = (status, code) => Object.assign(Error(code), { status });
function header(request, name) { const values = request.headersDistinct[name]; return values?.length === 1 ? values[0] : undefined; }

/** 超过输入上限立即停止接收；响应关闭连接，不继续无限吸收请求正文。 */
function readBody(request, limit) {
  return new Promise((resolve, reject) => {
    const chunks = []; let size = 0;
    const clean = () => { request.off('data', data); request.off('end', end); request.off('error', error); request.off('aborted', aborted); };
    const error = failure => { clean(); reject(failure); };
    const aborted = () => error(problem(400, 'webhook_consumer.request_aborted'));
    const data = chunk => { size += chunk.length; if (size > limit) { request.pause(); error(problem(413, 'webhook_consumer.body_too_large')); } else chunks.push(chunk); };
    const end = () => { clean(); resolve(Buffer.concat(chunks)); };
    request.on('data', data); request.once('end', end); request.once('error', error); request.once('aborted', aborted);
  });
}

/** 接收端只记录当前单一企业的工作流完成事实；生产业务写入应移入自己的事务 Inbox。 */
export async function startReceiver({ secret, tenantId, receiptsFile, host = '127.0.0.1', port = 8085, maxBodyBytes = 262_144, maxReceipts = 10_000, maxLedgerBytes = 8_388_608, requestsPerMinute = 60 }) {
  if (typeof secret !== 'string' || secret.length < 16 || secret.length > 128 || !isGuid(tenantId) || typeof receiptsFile !== 'string'
    || !Number.isInteger(port) || port < 0 || port > 65535 || typeof host !== 'string' || !host
    || ![maxBodyBytes, maxReceipts, maxLedgerBytes, requestsPerMinute].every(n => Number.isSafeInteger(n) && n > 0)
    || maxBodyBytes > 1_048_576 || maxReceipts > 100_000 || maxLedgerBytes > 67_108_864 || requestsPerMinute > 10_000) throw Error('webhook_consumer.config_invalid');
  tenantId = tenantId.toLowerCase();
  const store = await openReceiptStore(receiptsFile, { maxReceipts, maxBytes: maxLedgerBytes });
  let windowStart = Date.now(), requests = 0, active = 0;
  const server = http.createServer({ maxHeaderSize: 16_384 }, async (request, response) => {
    request.on('error', () => {});
    const send = (status, body, type = 'application/json') => { response.writeHead(status, { 'Content-Type': type + '; charset=utf-8', 'Cache-Control': 'no-store', 'Connection': 'close' }); response.end(JSON.stringify(body)); };
    try {
      if (request.url === '/health' && request.method === 'GET') return send(200, { status: 'listening' });
      if (request.url !== '/webhooks') throw problem(404, 'webhook_consumer.not_found');
      if (request.method !== 'POST') { response.setHeader('Allow', 'POST'); throw problem(405, 'webhook_consumer.method_not_allowed'); }
      const now = Date.now(); if (now - windowStart >= 60_000) { windowStart = now; requests = 0; }
      if (++requests > requestsPerMinute || active >= 16) { response.setHeader('Retry-After', '60'); throw problem(429, 'webhook_consumer.rate_limited'); }
      active++;
      try {
        if (!/^application\/json(?:\s*;\s*charset=utf-8)?$/iu.test(header(request, 'content-type') ?? '') || ![undefined, 'identity'].includes(header(request, 'content-encoding'))) throw problem(415, 'webhook_consumer.media_type_invalid');
        if (Number(request.headers['content-length']) > maxBodyBytes) throw problem(413, 'webhook_consumer.body_too_large');
        const body = await readBody(request, maxBodyBytes);
        if (!verifySignature(body, header(request, 'x-fullnet-signature'), secret)) throw problem(401, 'webhook_consumer.signature_invalid');
        let envelope; try { envelope = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(body)); } catch { throw problem(400, 'webhook_consumer.payload_invalid'); }
        const eventId = header(request, 'x-fullnet-event-id'), deliveryId = header(request, 'x-fullnet-delivery-id');
        if (!isGuid(eventId) || !isGuid(deliveryId) || !isGuid(envelope?.eventId) || envelope.eventId.toLowerCase() !== eventId.toLowerCase()
          || !isGuid(envelope.tenantId) || envelope.tenantId.toLowerCase() !== tenantId || envelope.eventType !== eventType || header(request, 'x-fullnet-event-type') !== eventType
          || typeof envelope.occurredAtUtc !== 'string' || !/(?:Z|\+00:00)$/u.test(envelope.occurredAtUtc) || !Number.isFinite(Date.parse(envelope.occurredAtUtc))
          || !isGuid(envelope.data?.instanceId) || !isGuid(envelope.data?.recipientUserId) || typeof envelope.data?.businessType !== 'string' || !envelope.data.businessType
          || typeof envelope.data?.businessId !== 'string' || !envelope.data.businessId) throw problem(400, 'webhook_consumer.payload_invalid');
        const result = await store.accept({ schemaVersion: 1, eventId: eventId.toLowerCase(), tenantId, deliveryId: deliveryId.toLowerCase(), eventType,
          payloadDigest: createHash('sha256').update(body).digest('hex'), receivedAtUtc: new Date().toISOString(), result: { instanceId: envelope.data.instanceId.toLowerCase(), status: 'recorded' } });
        if (result.conflict) throw problem(409, 'webhook_consumer.event_conflict');
        send(200, result);
      } finally { active--; }
    } catch (error) {
      const status = error.status ?? 503, code = error.status ? error.message : 'webhook_consumer.store_unavailable';
      if (!response.headersSent && !response.destroyed) send(status, { type: 'about:blank', title: code, status, code }, 'application/problem+json');
    }
  });
  server.requestTimeout = 10_000; server.headersTimeout = 5_000; server.keepAliveTimeout = 5_000; server.maxConnections = 64;
  try { await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, host, () => { server.off('error', reject); resolve(); }); }); }
  catch (error) { await store.close(); throw error; }
  return { server, address: server.address(), async close() { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); await store.close(); } };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const receiver = await startReceiver({ secret: await readSigningSecret(process.env.FULLNET_WEBHOOK_SIGNING_SECRET_FILE), tenantId: process.env.FULLNET_WEBHOOK_TENANT_ID,
      receiptsFile: process.env.FULLNET_WEBHOOK_RECEIPTS_FILE, host: process.env.FULLNET_WEBHOOK_HOST ?? '127.0.0.1', port: Number(process.env.FULLNET_WEBHOOK_PORT ?? 8085) });
    console.log(`WEBHOOK_CONSUMER_READY port=${receiver.address.port}`);
    for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { receiver.close().catch(() => { process.exitCode = 1; }); });
  } catch { console.error('WEBHOOK_CONSUMER_START_FAILED: check secret file, tenant, ledger ownership and listener configuration'); process.exitCode = 1; }
}
