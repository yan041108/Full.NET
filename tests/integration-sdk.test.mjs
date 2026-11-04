import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { createHmac } from 'node:crypto';
import { createIntegrationClient, IntegrationHttpError, IntegrationProtocolError, webhooksListSubscriptions, webhooksCreateSubscription, verifyWorkflowCompletedWebhook } from '../packages/integration-sdk/dist/index.js';
import { generateIntegrationSdk } from '../scripts/openapi/generate-integration-sdk.mjs';

const tenantId='019bc2b1-2a40-7cc3-8992-a80de51bf299',other='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const row={id:other,tenantId,eventType:'fullnet.workflow.instance.completed',targetUrl:'https://example.com/hooks',isActive:true,version:1};
async function server(t,handler){const instance=http.createServer(handler);await new Promise(resolve=>instance.listen(0,'127.0.0.1',resolve));t.after(()=>new Promise(resolve=>{instance.close(resolve);instance.closeAllConnections();}));return `http://127.0.0.1:${instance.address().port}`;}
const json=(response,status,value,headers={})=>{response.writeHead(status,{'Content-Type':'application/json',...headers});response.end(JSON.stringify(value));};
const options=baseUrl=>({baseUrl,tenantId,allowHttpLocalhost:true,getCredentials:()=> ({accessToken:'fixture-token',tenantId})});

test('生成内容与模块契约一致，独立入口仅提供当前 Webhook 操作',async()=>{const files=await generateIntegrationSdk({check:true});assert.equal(files.length,4);const sdk=await import('../packages/integration-sdk/dist/index.js');assert.equal(Object.keys(sdk).filter(key=>/^webhooks[A-Z]/u.test(key)).length,8);});
test('真实 GET 暂时故障有界重试且重新读取凭据，语言使用规范 BCP47',async t=>{
 let calls=0;const credentials=[];const url=await server(t,(request,response)=>{credentials.push(request.headers.authorization);assert.equal(request.headers['accept-language'],'en-US');calls++;json(response,calls===1?503:200,calls===1?{code:'test.busy'}:[row],{'Retry-After':'0'});});
 let tokens=0;const client=createIntegrationClient({...options(url),locale:'en-us',getCredentials:()=>({accessToken:`token-${++tokens}`,tenantId})});assert.deepEqual(await webhooksListSubscriptions(client,{}),[row]);assert.equal(calls,2);assert.deepEqual(credentials,['Bearer token-1','Bearer token-2']);
});
test('创建 POST 遇到 503 或网络断开不自动重发',async t=>{
 for(const disconnect of [false,true]){let calls=0;const url=await server(t,(_request,response)=>{calls++;if(disconnect)response.destroy();else json(response,503,{code:'test.busy',title:'Busy'});});const client=createIntegrationClient(options(url));await assert.rejects(webhooksCreateSubscription(client,{body:{eventType:row.eventType,targetUrl:row.targetUrl,signingSecret:'independent-fixture-secret'}}),error=>disconnect?error instanceof IntegrationProtocolError:error instanceof IntegrationHttpError&&error.problem.status===503);assert.equal(calls,1);}
});
test('撤销后的 401 只交付 ProblemDetails，不刷新凭据或重放',async t=>{
 let calls=0,tokens=0;const url=await server(t,(_request,response)=>{calls++;json(response,401,{code:'authorization.token_revoked',title:'Revoked',detail:'Use a current credential.'});});const client=createIntegrationClient({...options(url),getCredentials:()=>{tokens++;return {accessToken:'revoked-fixture',tenantId};}});
 await assert.rejects(webhooksListSubscriptions(client,{}),error=>error instanceof IntegrationHttpError&&error.problem.status===401&&error.problem.code==='authorization.token_revoked');assert.equal(calls,1);assert.equal(tokens,1);
});
test('服务端跨企业拒绝与意外跨企业成功 DTO 均不能被接入',async t=>{
 for(const status of [404,200]){let calls=0;const url=await server(t,(_request,response)=>{calls++;json(response,status,status===200?[{...row,tenantId:other}]:{code:'webhooks.delivery.not_found'});});await assert.rejects(webhooksListSubscriptions(createIntegrationClient(options(url)),{}),error=>status===404?error instanceof IntegrationHttpError&&error.problem.status===404:error instanceof IntegrationProtocolError&&error.message==='sdk.response_tenant_mismatch');assert.equal(calls,1);}
});
test('正文预算和畸形 JSON 不触发 GET 重试',async t=>{
 for(const oversized of [false,true]){let calls=0;const url=await server(t,(_request,response)=>{calls++;response.end(oversized?'x'.repeat(100):'{');});const client=createIntegrationClient({...options(url),maxResponseBytes:32});await assert.rejects(webhooksListSubscriptions(client,{}),error=>error instanceof IntegrationProtocolError&&error.message===(oversized?'sdk.response_too_large':'sdk.response_json_invalid'));assert.equal(calls,1);}
});
test('GET 正文读取断流可以有界重试，POST 断流保留未知且不重发',async()=>{
 for(const method of ['GET','POST']){let calls=0;const client=createIntegrationClient({...options('https://example.com'),maxGetRetries:1,fetch:async()=>{calls++;if(calls>1)return Response.json([row]);return new Response(new ReadableStream({start(controller){controller.error(Error('connection lost'));}}));}});
 if(method==='GET'){assert.deepEqual(await client.request('/api/v1/probe',{method}),[row]);assert.equal(calls,2);}else{await assert.rejects(client.request('/api/v1/probe',{method}),/sdk.transport_failure/u);assert.equal(calls,1);}}
});
test('令牌等待和退避受同一期限及调用方取消约束',async t=>{
 let sent=0;const client=createIntegrationClient({...options('http://localhost:1234'),timeoutMilliseconds:20,getCredentials:()=>new Promise(()=>{}),fetch:()=>{sent++;throw Error();}});
 await assert.rejects(client.request('/api/v1/probe'),error=>error.name==='TimeoutError');assert.equal(sent,0);
 let calls=0;const url=await server(t,(_request,response)=>{calls++;json(response,429,{code:'test.busy'},{'Retry-After':'60'});});const abort=new AbortController();const timer=setTimeout(()=>abort.abort(),30);
 try{await assert.rejects(createIntegrationClient({...options(url),timeoutMilliseconds:500}).request('/api/v1/probe',{},abort.signal),error=>error.name==='AbortError');}finally{clearTimeout(timer);}assert.equal(calls,1);
});
test('配置与请求目标不能泄露凭据到其他源或非 API 路径',async()=>{
 for(const baseUrl of ['http://external.example.com','https://user:private@example.com','https://example.com/prefix','invalid'])assert.throws(()=>createIntegrationClient(options(baseUrl)),IntegrationProtocolError);
 const client=createIntegrationClient({...options('https://example.com'),fetch:()=>{throw Error('must not send');}});for(const path of ['https://other.example.com/api/v1/me','//other.example.com/api/v1/me','/api/v1/../../../secret','/login'])await assert.rejects(client.request(path),/sdk.request_path_invalid/u);
});
test('凭据提供器换企业后拒绝发送旧企业 POST，异步凭据也须同一快照',async()=>{
 let sent=0;const client=createIntegrationClient({...options('https://example.com'),getCredentials:async()=>({accessToken:'other-tenant-fixture',tenantId:other}),fetch:()=>{sent++;throw Error();}});
 await assert.rejects(webhooksCreateSubscription(client,{body:{eventType:row.eventType,targetUrl:row.targetUrl,signingSecret:'independent-fixture-secret'}}),/sdk.credential_scope_changed/u);assert.equal(sent,0);
});
test('事件验签消费共用 PascalCase 生产夹具，绑定原始字节和企业/事件头',async()=>{
 const body=await readFile(new URL('../contracts/webhooks/workflow-instance-completed-v1.json',import.meta.url)),event=JSON.parse(body),secret='independent-event-secret';const input={body,secret,signature:createHmac('sha256',secret).update(body).digest('hex'),eventId:event.EventId,eventType:event.EventType,expectedTenantId:tenantId};
 assert.deepEqual(await verifyWorkflowCompletedWebhook(input),event);
 for(const change of [{signature:'0'.repeat(64)},{expectedTenantId:other},{eventId:tenantId},{eventType:'other'},{body:new TextEncoder().encode(JSON.stringify(event))}])await assert.rejects(verifyWorkflowCompletedWebhook({...input,...change}));
 const invalid={...event,OccurredAtUtc:'2026-11-05T08:00:00+08:00'},bytes=Buffer.from(JSON.stringify(invalid));await assert.rejects(verifyWorkflowCompletedWebhook({...input,body:bytes,signature:createHmac('sha256',secret).update(bytes).digest('hex')}));
});
