import assert from 'node:assert/strict';
import http from 'node:http';
import { Readable } from 'node:stream';
import { readFile, writeFile } from 'node:fs/promises';
import { createIntegrationClient, IntegrationHttpError, webhooksCreateSubscription, webhooksListSubscriptions, webhooksListSubscriptionDeliveries, webhooksSetSubscriptionStatus, verifyWorkflowCompletedWebhook } from '@fullnet/integration-sdk';
import { webhooksPrepareSigningKeyRotation, webhooksGetPendingSigningKeyRotation, webhooksGetSigningKeyRotation, webhooksActivateSigningKeyRotation, webhooksCancelSigningKeyRotation } from '@fullnet/integration-sdk';

const env = process.env, phase = process.argv[2];
// 本地动态端口没有企业 DNS；仅由测试适配器设置 HTTP Host，响应和授权仍来自真实 Kestrel。
const adapter = host => (url,init) => new Promise((resolve,reject) => {
  const headers = new Headers(init.headers); headers.set('Host',host);
  const request = http.request(url,{method:init.method,headers:Object.fromEntries(headers),signal:init.signal},response => {
    resolve(new Response([204,205,304].includes(response.statusCode)?null:Readable.toWeb(response),{status:response.statusCode,headers:Object.fromEntries(Object.entries(response.headers).filter(([,value])=>value!==undefined).map(([key,value])=>[key,Array.isArray(value)?value.join(', '):value]))}));
  }); request.on('error',reject); request.end(init.body);
});
const client = createIntegrationClient({baseUrl:env.SDK_API_URL,tenantId:env.SDK_TENANT_A,getCredentials:()=>({accessToken:env.SDK_TOKEN_A,tenantId:env.SDK_TENANT_A}),allowHttpLocalhost:true,fetch:adapter('acme.localhost'),maxGetRetries:0});
if (phase === 'bootstrap') {
  const created = await webhooksCreateSubscription(client,{body:{eventType:'fullnet.workflow.instance.completed',targetUrl:'https://93.184.216.34/sdk',signingSecret:env.SDK_SIGNING_SECRET}});
  assert.equal(created.tenantId.toLowerCase(),env.SDK_TENANT_A.toLowerCase());
  const subscriptions = await webhooksListSubscriptions(client,{}); assert.ok(subscriptions.some(row=>row.id===created.id));
  const foreign = createIntegrationClient({baseUrl:env.SDK_API_URL,tenantId:env.SDK_TENANT_B,getCredentials:()=>({accessToken:env.SDK_TOKEN_B,tenantId:env.SDK_TENANT_B}),allowHttpLocalhost:true,fetch:adapter(env.SDK_HOST_B),maxGetRetries:0});
  await assert.rejects(webhooksSetSubscriptionStatus(foreign,{subscriptionId:created.id,body:{isActive:false,version:created.version}}),error=>error instanceof IntegrationHttpError&&error.problem.status===404);
  assert.equal((await webhooksListSubscriptions(client,{})).find(row=>row.id===created.id).isActive,true);
  const operationId=crypto.randomUUID(),prepared=await webhooksPrepareSigningKeyRotation(client,{subscriptionId:created.id,body:{operationId,version:created.version,signingSecret:env.SDK_ROTATED_SIGNING_SECRET}});
  assert.equal(prepared.stateKey,'prepared');assert.notEqual(prepared.signingKeyId,created.signingKeyId);
  assert.deepEqual(await webhooksGetPendingSigningKeyRotation(client,{subscriptionId:created.id}),prepared);assert.deepEqual(await webhooksGetSigningKeyRotation(client,{subscriptionId:created.id,operationId}),prepared);
  assert.equal((await webhooksListSubscriptions(client,{})).find(row=>row.id===created.id).signingKeyId,created.signingKeyId);
  const activated=await webhooksActivateSigningKeyRotation(client,{subscriptionId:created.id,operationId,body:{version:prepared.preparedVersion,signingKeyId:prepared.signingKeyId}});assert.equal(activated.stateKey,'activated');
  const cancelledPreparation=await webhooksPrepareSigningKeyRotation(client,{subscriptionId:created.id,body:{operationId:crypto.randomUUID(),version:activated.completedVersion,signingSecret:'sdk-cancelled-prepared-secret'}});
  const cancelled=await webhooksCancelSigningKeyRotation(client,{subscriptionId:created.id,operationId:cancelledPreparation.operationId,body:{version:cancelledPreparation.preparedVersion,signingKeyId:cancelledPreparation.signingKeyId}});assert.equal(cancelled.stateKey,'cancelled');
  assert.equal((await webhooksListSubscriptions(client,{})).find(row=>row.id===created.id).signingKeyId,activated.signingKeyId);
  await writeFile('bootstrap.json',JSON.stringify({subscriptionId:created.id,tenantId:created.tenantId,signingKeyId:activated.signingKeyId})); console.log('SDK_BOOTSTRAP_OK crossTenant=404 rotation=activated cancellation=confirmed');
} else if (phase === 'event') {
  const input=JSON.parse(await readFile('event-input.json','utf8')),body=new TextEncoder().encode(input.body);
  const verify=()=>verifyWorkflowCompletedWebhook({body,signature:input.signature,eventId:input.eventId,deliveryId:input.deliveryId,eventType:input.eventType,version:input.version,keyId:input.keyId,timestamp:input.timestamp,
    expectedTenantId:env.SDK_TENANT_A,resolveSigningSecret:keyId=>keyId===env.SDK_KEY_ID?env.SDK_SIGNING_SECRET:null});
  const event=await verify(); assert.equal(event.EventId,input.eventId); assert.deepEqual(await verify(),event);
  const history=await webhooksListSubscriptionDeliveries(client,{subscriptionId:input.subscriptionId,page:1,pageSize:20});assert.ok(history.items.some(row=>row.eventId===event.EventId));
  await writeFile('event-consumed.json',JSON.stringify({eventId:event.EventId,instanceId:event.Data.InstanceId}));console.log('SDK_EVENT_OK');
} else if (phase === 'revoked') {
  await assert.rejects(webhooksListSubscriptions(client,{}),error=>error instanceof IntegrationHttpError&&error.problem.status===401);console.log('SDK_REVOKED_OK');
} else throw Error('Unknown SDK fixture phase.');
