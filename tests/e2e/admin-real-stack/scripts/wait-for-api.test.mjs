import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { waitForApi } from './wait-for-api.mjs';

async function hangingHost() {
 let fallbackTriggered = false;
 const timers = new Set();
 const server = createServer((request,response) => {
  // 旧探针只有等待这个迟到响应才能退出；新探针必须先按 deadline/取消结束。
  const timer = setTimeout(() => { fallbackTriggered = true; response.writeHead(503).end(); },1500);
  timers.add(timer);
 });
 await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
 return { url:'http://127.0.0.1:'+server.address().port, fallback:() => fallbackTriggered,
  close:async () => { timers.forEach(clearTimeout); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); } };
}

test('API startup deadline interrupts a connected but nonresponding health request', async () => {
 const host = await hangingHost();
 try { await assert.rejects(waitForApi(host.url,150),/超时/u); assert.equal(host.fallback(),false,'startup deadline did not interrupt fetch'); }
 finally { await host.close(); }
});

test('API startup cancellation interrupts an in-flight health probe', async () => {
 const host = await hangingHost(); const controller = new AbortController();
 const timer = setTimeout(() => controller.abort(new Error('test cancelled')),100);
 try { await assert.rejects(waitForApi(host.url,300,undefined,{signal:controller.signal}),/test cancelled/u); assert.equal(host.fallback(),false); }
 finally { clearTimeout(timer); await host.close(); }
});

test('API startup accepts the live endpoint without consuming a streaming body', async () => {
 const server = createServer((request,response) => { assert.equal(request.url,'/health/live'); response.writeHead(200).end('ok'); });
 await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
 try { await waitForApi('http://127.0.0.1:'+server.address().port,1000); }
 finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
});
