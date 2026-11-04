import test from 'node:test';
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { mkdtemp, readFile, writeFile, rm, access } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { startReceiver } from '../samples/webhook-consumer/server.mjs';
import { readSigningSecret, verifySignature, computeSignature } from '../samples/webhook-consumer/signature.mjs';
import { openReceiptStore } from '../samples/webhook-consumer/receipt-store.mjs';

const secret = 'independent-consumer-secret', tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf299', eventId = '019bc2b1-2a40-7cc3-8992-a80de51bf298', deliveryId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
const envelope = JSON.parse(await readFile(new URL('../contracts/webhooks/workflow-instance-completed-v1.json', import.meta.url), 'utf8'));
const stamp = (changes={}) => ({version:'1',keyId:tenantId,timestamp:String(Math.floor(Date.now()/1000)),eventId,deliveryId,eventType:envelope.EventType,...changes});
const signature = (body,metadata=stamp()) => computeSignature(body,secret,metadata);
const signedHeaders = (body,metadata=stamp()) => ({'X-FullNet-Signature':signature(body,metadata),'X-FullNet-Signature-Version':metadata.version,'X-FullNet-Key-Id':metadata.keyId,'X-FullNet-Timestamp':metadata.timestamp,'X-FullNet-Event-Id':metadata.eventId,'X-FullNet-Delivery-Id':metadata.deliveryId,'X-FullNet-Event-Type':metadata.eventType});
async function fixture(t, options = {}) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-webhook-consumer-')), receiptsFile = path.join(root, 'receipts.ndjson');
  let receiver = await startReceiver({ secret, keyId:tenantId, tenantId, receiptsFile, port: 0, ...options });
  t.after(async () => { await receiver?.close(); await rm(root, { recursive: true, force: true }); });
  return { root, receiptsFile, get receiver() { return receiver; }, async restart() { await receiver.close(); receiver = undefined; receiver = await startReceiver({ secret, keyId:tenantId, tenantId, receiptsFile, port: 0, ...options }); },
    async send(value = envelope, headers = {}, body = JSON.stringify(value)) {
      const metadata=stamp({eventId:headers['X-FullNet-Event-Id']??value?.EventId??eventId,deliveryId:headers['X-FullNet-Delivery-Id']??deliveryId,eventType:headers['X-FullNet-Event-Type']??envelope.EventType,timestamp:headers['X-FullNet-Timestamp']??String(Math.floor(Date.now()/1000)),keyId:headers['X-FullNet-Key-Id']??tenantId,version:headers['X-FullNet-Signature-Version']??'1'});
      let signed;try{signed=signedHeaders(body,metadata);}catch{signed={'X-FullNet-Signature':'0'.repeat(64)};}
      return fetch(`http://127.0.0.1:${receiver.address.port}/webhooks`, { method:'POST', headers:{'Content-Type':'application/json; charset=utf-8',...signed,...headers},body });
    } };
}

test('真实 HTTP 并发重复只持久化一次，重启后返回原消费结果', async t => {
  const f = await fixture(t);
  const results = await Promise.all(Array.from({ length: 8 }, async () => { const response = await f.send(); assert.equal(response.status, 200); return response.json(); }));
  assert.equal(results.filter(row => row.duplicate === false).length, 1);
  assert.ok(results.every(row => JSON.stringify(row.receipt) === JSON.stringify(results[0].receipt)));
  const contents = await readFile(f.receiptsFile, 'utf8'); assert.equal(contents.trim().split('\n').length, 1); assert.ok(!contents.includes(secret)); assert.ok(!contents.includes('private-business-reference'));
  await f.restart(); const repeated = await f.send(envelope, { 'X-FullNet-Delivery-Id': tenantId }); const result = await repeated.json(); assert.equal(result.duplicate, true); assert.deepEqual(result.receipt, results[0].receipt);
});

test('原始字节签名不能由重新序列化、错误秘密或畸形签名替代', async t => {
  const f = await fixture(t), body = JSON.stringify(envelope, null, 2);
  for (const bad of ['bad', signature(JSON.stringify(envelope)), createHmac('sha256', 'wrong-secret').update(body).digest('hex')]) {
    const response = await f.send(envelope, { 'X-FullNet-Signature': bad }, body); assert.equal(response.status, 401); const problem = await response.json(); assert.equal(problem.code, 'webhook_consumer.signature_invalid'); assert.match(response.headers.get('content-type'), /application\/problem\+json/u);
  }
  assert.equal((await f.send(envelope, {}, body)).status, 200);
  const metadata=stamp(); assert.ok(verifySignature(Buffer.from(body),signature(body,metadata),secret,metadata,{keyId:tenantId}));
});

test('签名通过后仍校验企业、事件、标识和载荷，拒绝跨来源接入', async t => {
  const f = await fixture(t);
  for (const [value, headers] of [[{ ...envelope, TenantId: eventId }, {}], [envelope, { 'X-FullNet-Event-Id': tenantId }], [envelope, { 'X-FullNet-Event-Type': 'other' }], [{ ...envelope, Data: {} }, {}], [{ ...envelope, OccurredAtUtc: '2026-11-04T08:00:00+08:00' }, {}], [null, {}]]) {
    const response = await f.send(value, headers); assert.equal(response.status, 400); assert.equal((await response.json()).code, 'webhook_consumer.payload_invalid');
  }
  assert.equal((await readFile(f.receiptsFile, 'utf8')).length, 0);
});

test('同事件内容冲突不会覆写回执，账本满后仍可确认原重复', async t => {
  const f = await fixture(t, { maxReceipts: 1 }); assert.equal((await f.send()).status, 200);
  const before = await readFile(f.receiptsFile, 'utf8');
  assert.equal((await f.send({ ...envelope, Data: { ...envelope.Data, BusinessId: 'changed' } })).status, 409);
  assert.equal((await f.send({ ...envelope, EventId: tenantId })).status, 503);
  assert.equal((await f.send()).status, 200); assert.equal(await readFile(f.receiptsFile, 'utf8'), before);
});

test('超大固定长度与分块正文受限，不依赖 Content-Length', async t => {
  const f = await fixture(t, { maxBodyBytes: 128 }); assert.equal((await f.send()).status, 413);
  const status = await new Promise((resolve, reject) => {
    const request = http.request({ host: '127.0.0.1', port: f.receiver.address.port, path: '/webhooks', method: 'POST', headers: { 'Content-Type': 'application/json', 'Transfer-Encoding': 'chunked' } }, response => { response.resume(); response.once('end', () => resolve(response.statusCode)); });
    request.on('error', reject); request.write('x'.repeat(100)); request.end('x'.repeat(100));
  }); assert.equal(status, 413); assert.equal((await readFile(f.receiptsFile, 'utf8')).length, 0);
});

test('每分钟有界请求返回 429 和重试提示，错误媒体类型为 415', async t => {
  const f = await fixture(t, { requestsPerMinute: 1 }); assert.equal((await f.send(envelope, { 'Content-Type': 'text/plain' })).status, 415);
  const response = await f.send(); assert.equal(response.status, 429); assert.equal(response.headers.get('retry-after'), '60');
  const health = await fetch(`http://127.0.0.1:${f.receiver.address.port}/health`); assert.equal(health.status, 200); assert.deepEqual(await health.json(), { status: 'listening' });
});

test('独占锁不抢占他人账本，半条记录阻止启动且不截断', async t => {
  const f = await fixture(t);
  await assert.rejects(openReceiptStore(f.receiptsFile), { code: 'EEXIST' }); await access(f.receiptsFile + '.lock');
  await f.restart(); assert.equal((await f.send()).status, 200);
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-webhook-corrupt-')), file = path.join(root, 'receipt.ndjson'); t.after(() => rm(root, { recursive: true, force: true }));
  await writeFile(file, '{"partial":'); await assert.rejects(openReceiptStore(file), /store_corrupt/u); assert.equal(await readFile(file, 'utf8'), '{"partial":'); await assert.rejects(access(file + '.lock'), { code: 'ENOENT' });
});

test('秘密文件只去掉结尾换行，验签 CLI 使用文件引用并不输出秘密', async t => {
  const f = await fixture(t), secretFile = path.join(f.root, 'secret'), payloadFile = path.join(f.root, 'body.json'), body = JSON.stringify(envelope, null, 2);
  await writeFile(secretFile, secret + '\r\n'); await writeFile(payloadFile, body); assert.equal(await readSigningSecret(secretFile), secret);
  const metadata=stamp(),metadataFile=path.join(f.root,'metadata.json');await writeFile(metadataFile,JSON.stringify(metadata));
  const args = ['samples/webhook-consumer/verify-signature.mjs','--secret-file',secretFile,'--key-id',tenantId,'--payload-file',payloadFile,'--metadata-file',metadataFile,'--signature',signature(body,metadata)];
  const ok = spawnSync(process.execPath, args, { encoding: 'utf8', windowsHide: true, timeout: 10_000 }); assert.equal(ok.status, 0); assert.equal(ok.stdout.trim(), 'SIGNATURE_OK'); assert.ok(!(ok.stdout + ok.stderr).includes(secret));
  const bad = spawnSync(process.execPath, [...args.slice(0, -1), '0'.repeat(64)], { encoding: 'utf8', windowsHide: true, timeout: 10_000 }); assert.equal(bad.status, 1); assert.equal(bad.stdout.trim(), 'SIGNATURE_INVALID');
  await writeFile(secretFile, 'short'); await assert.rejects(readSigningSecret(secretFile), /secret_file_invalid/u);
  await writeFile(secretFile, Buffer.from([0xff, ...Buffer.from(secret)])); await assert.rejects(readSigningSecret(secretFile));
});

test('真实 CLI 启动接收并持久化，进程崩溃后保留锁且再次启动拒绝抢占', { timeout: 15_000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-webhook-process-')), secretFile = path.join(root, 'secret'), receiptsFile = path.join(root, 'receipts.ndjson');
  await writeFile(secretFile, secret);
  const args = ['samples/webhook-consumer/server.mjs'], env = { ...process.env, FULLNET_WEBHOOK_SIGNING_SECRET_FILE: secretFile, FULLNET_WEBHOOK_TENANT_ID: tenantId, FULLNET_WEBHOOK_KEY_ID:tenantId, FULLNET_WEBHOOK_RECEIPTS_FILE: receiptsFile, FULLNET_WEBHOOK_HOST: '127.0.0.1', FULLNET_WEBHOOK_PORT: '0' };
  const child = spawn(process.execPath, args, { env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }); let output = '';
  child.stdout.on('data', chunk => { output += chunk; }); child.stderr.on('data', chunk => { output += chunk; });
  t.after(async () => { if (child.exitCode === null && child.signalCode === null) { const ended = once(child, 'close'); child.kill('SIGKILL'); await ended; } await rm(root, { recursive: true, force: true }); });
  const port = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Error('consumer startup timeout')), 5_000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', () => { clearTimeout(timer); reject(Error('consumer exited before readiness')); });
    child.stdout.on('data', () => { const match = output.match(/WEBHOOK_CONSUMER_READY port=(\d+)/u); if (match) { clearTimeout(timer); resolve(Number(match[1])); } });
  });
  const body = JSON.stringify(envelope), response = await fetch(`http://127.0.0.1:${port}/webhooks`, { method: 'POST', headers: { 'Content-Type': 'application/json', ...signedHeaders(body) }, body });
  assert.equal(response.status, 200); assert.equal((await response.json()).duplicate, false);
  const ended = once(child, 'close'); child.kill('SIGKILL'); await ended;
  assert.equal((await readFile(receiptsFile, 'utf8')).trim().split('\n').length, 1); assert.equal(await readFile(receiptsFile + '.lock', 'utf8'), String(child.pid));
  const blocked = spawnSync(process.execPath, args, { env, encoding: 'utf8', windowsHide: true, timeout: 5_000 }); assert.equal(blocked.status, 1); assert.match(blocked.stderr, /WEBHOOK_CONSUMER_START_FAILED/u); assert.ok(!(output + blocked.stderr).includes(secret));
  assert.equal(await readFile(receiptsFile + '.lock', 'utf8'), String(child.pid));
});

test('无效启动配置在监听前拒绝，不产生成功就绪提示', async () => {
  await assert.rejects(startReceiver({ secret, keyId:tenantId, tenantId, receiptsFile: 'relative-file' }), /store_config_invalid/u);
  await assert.rejects(startReceiver({ secret: 'short', tenantId, receiptsFile: 'relative-file' }), /config_invalid/u);
  const result = spawnSync(process.execPath, ['samples/webhook-consumer/server.mjs'], { env: { ...process.env, FULLNET_WEBHOOK_SIGNING_SECRET_FILE: '' }, encoding: 'utf8', windowsHide: true, timeout: 5_000 });
  assert.equal(result.status, 1); assert.ok(!result.stdout.includes('READY')); assert.match(result.stderr, /WEBHOOK_CONSUMER_START_FAILED/u);
});

test('同一跨语言向量绑定完整元数据和原文字节，固定时钟仅用于向量验证',async()=>{
 const vector=JSON.parse(await readFile(new URL('../contracts/webhooks/signature-v1.vector.json',import.meta.url),'utf8')),body=Buffer.from(vector.payload);
 assert.equal(computeSignature(body,vector.secret,vector),vector.signature);
 assert.equal(verifySignature(body,vector.signature,vector.secret,vector,{keyId:vector.keyId,clock:()=>Number(vector.timestamp)*1000}),true);
 for(const change of [{timestamp:String(Number(vector.timestamp)+1)},{eventId:tenantId},{deliveryId:tenantId},{keyId:tenantId},{eventType:'other'},{version:'2'}])assert.equal(verifySignature(body,vector.signature,vector.secret,{...vector,...change},{keyId:vector.keyId,clock:()=>Number(vector.timestamp)*1000}),false);
});
test('签名超窗、未知版本和密钥、非规范时间不能消费，重新签名的延迟事件仍只记录一次',async t=>{
 const f=await fixture(t);assert.equal((await f.send()).status,200);
 for(const headers of [{'X-FullNet-Timestamp':String(Math.floor(Date.now()/1000)-301)},{'X-FullNet-Timestamp':String(Math.floor(Date.now()/1000)+301)},{'X-FullNet-Timestamp':'0'+String(Math.floor(Date.now()/1000))},{'X-FullNet-Key-Id':eventId},{'X-FullNet-Signature-Version':'2'}])assert.equal((await f.send(envelope,headers)).status,401);
 const repeated=await f.send();assert.equal(repeated.status,200);assert.equal((await repeated.json()).duplicate,true);assert.equal((await readFile(f.receiptsFile,'utf8')).trim().split('\n').length,1);
});
