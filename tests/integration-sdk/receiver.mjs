import { startReceiver } from '../webhook-receiver/server.mjs';
import { readSigningSecret } from '../webhook-receiver/signature.mjs';
import { writeFile, rename } from 'node:fs/promises';
import { createInterface } from 'node:readline';
import path from 'node:path';

// 只在独立验收项目捕获已成功接收的有限原文/签名，生产样例仍只保存不含正文的消费回执。
const receiver=await startReceiver({secret:await readSigningSecret(process.env.FULLNET_WEBHOOK_SIGNING_SECRET_FILE),keyId:process.env.FULLNET_WEBHOOK_KEY_ID,
  tenantId:process.env.FULLNET_WEBHOOK_TENANT_ID,receiptsFile:path.resolve('receipts.ndjson'),port:0});
let capture=Promise.resolve();
receiver.server.on('request',(request,response)=>{
 if(request.method!=='POST'||request.url!=='/webhooks')return;
 const chunks=[];let size=0;request.on('data',chunk=>{size+=chunk.length;if(size<=262144)chunks.push(chunk);});
 response.once('finish',()=>{if(response.statusCode!==200||size>262144)return;
  const frame={body:Buffer.concat(chunks).toString('utf8'),signature:request.headers['x-fullnet-signature'],version:request.headers['x-fullnet-signature-version'],keyId:request.headers['x-fullnet-key-id'],
    timestamp:request.headers['x-fullnet-timestamp'],eventId:request.headers['x-fullnet-event-id'],deliveryId:request.headers['x-fullnet-delivery-id'],eventType:request.headers['x-fullnet-event-type']};
  capture=capture.then(async()=>{await writeFile('event-input.json.tmp',JSON.stringify(frame));await rename('event-input.json.tmp','event-input.json');}).catch(()=>{console.error('RECEIVER_CAPTURE_FAILED');process.exitCode=1;});
 });
});
console.log(`RECEIVER_READY port=${receiver.address.port}`);
const input=createInterface({input:process.stdin});
for await(const line of input){if(line==='close')break;}
input.close();await receiver.close();await capture;
