import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { ReplayLedger, replayOwnedRun } from './agui-replay-client.mjs';

// 此驱动消费正在运行的正式API；ready只传递本批临时登录凭据，结果文件不保存令牌或原正文。
const root=process.env.FULLNET_A04_AGUI_BRIDGE;
if(!root)throw Error('Provide a fresh A04 bridge.');
const origin='http://127.0.0.1:64215';
const results=[];
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function exists(file){try{await fs.access(file);return true;}catch{return false;}}
async function read(file){return JSON.parse(await fs.readFile(file,'utf8'));}
async function write(directory,name,value={passed:true}){
  const file=path.join(directory,name+'.json');await fs.writeFile(file+'.writing',JSON.stringify(value));await fs.rename(file+'.writing',file);
}
async function wait(file){
  const deadline=Date.now()+15*60*1000;
  while(!await exists(file)){
    if(await exists(path.join(root,'failed.json')))throw Error('A04 peer failed.');
    if(Date.now()>deadline)throw Error('A04 bridge timed out.');await sleep(150);
  }
  return read(file);
}
let requests=[];
async function actualFetch(url,init={}){
  const headers=new Headers(init.headers);headers.set('Host','localhost');
  requests.push({method:init.method??'GET',path:new URL(url).pathname});
  return fetch(url,{...init,headers,signal:AbortSignal.any([...(init.signal?[init.signal]:[]),AbortSignal.timeout(30000)])});
}
function config(ready,active=false){
  const runId=active?ready.activeRun:ready.terminalRun;
  const identity={origin,runId,actorKey:ready.actorKey,scopeKey:ready.scopeKey};
  return {...identity,url:origin+`/api/v1/ai/agent/runs/${runId}/events/stream`,threadId:active?ready.activeThread:ready.terminalThread,
    token:ready.token,ledger:new ReplayLedger(identity),fetchImpl:actualFetch};
}
async function problem(url,token,status,code){
  const response=await actualFetch(url,{headers:{Authorization:'Bearer '+token}});
  assert.equal(response.status,status);assert.match(response.headers.get('content-type'),/application\/problem\+json/);
  const value=await response.json();assert.equal(value.code??value.extensions?.code,code);
}
async function runProvider(provider){
  const directory=path.join(root,provider);
  const seen=new Set();
  for(let index=0;index<2;index++){
    let scopeDirectory;const deadline=Date.now()+15*60*1000;
    while(!scopeDirectory){
      if(await exists(path.join(root,'failed.json')))throw Error('A04 peer failed.');
      for(const entry of await fs.readdir(directory,{withFileTypes:true}).catch(()=>[]))
        if(entry.isDirectory()&&!seen.has(entry.name)&&await exists(path.join(directory,entry.name,'ready.json'))){scopeDirectory=path.join(directory,entry.name);seen.add(entry.name);break;}
      if(Date.now()>deadline)throw Error('A04 scope not ready.');if(!scopeDirectory)await sleep(150);
    }
    const ready=await read(path.join(scopeDirectory,'ready.json'));requests=[];
    const terminal=config(ready);
    // 官方客户端实际中断连接后，以最后已交付序号续传；重放整个窗口仍只消费每个源事件一次。
    const interrupted=await replayOwnedRun({...terminal,abortAt:20});assert.equal(interrupted.aborted,true);
    const cursor=terminal.ledger.cursor;assert.ok(cursor>=20&&cursor<ready.maximum);
    const resumed=await replayOwnedRun({...terminal,afterSequence:cursor,expectedErrorCode:'ai.agent_run.terminal'});
    assert.equal(resumed.status,'cancelled');assert.equal(terminal.ledger.cursor,ready.maximum);
    await replayOwnedRun({...terminal,expectedErrorCode:'ai.agent_run.terminal'});
    assert.deepEqual(terminal.ledger.received,Array.from({length:ready.maximum},(_,i)=>i+1));assert.equal(terminal.ledger.terminals.size,1);
    const slow=config(ready);await replayOwnedRun({...slow,delayMilliseconds:3,expectedErrorCode:'ai.agent_run.terminal'});
    assert.equal(slow.ledger.cursor,ready.maximum);
    await problem(terminal.url+'?afterSequence='+(ready.maximum+1),ready.token,409,'ai.agent_run.events_cursor_expired');
    await problem(terminal.url+'?afterSequence=-1',ready.token,409,'ai.agent_run.events_cursor_expired');
    await problem(terminal.url,ready.otherToken,404,'ai.agent_run.not_found');
    if(ready.scopeKey!=='host')await problem(origin+`/api/v1/ai/agent/runs/${ready.hostRun}/events/stream`,ready.token,404,'ai.agent_run.not_found');
    // 先等当前快照实际到达，再让C#夹具正式重登撤销原会话；这不是连接前的单次认证测试。
    let requested=false;
    const lost=await replayOwnedRun({...config(ready,true),expectedErrorCode:'authorization.permission_denied',onEvent:async event=>{
      if(event.type==='STATE_SNAPSHOT'&&!requested){requested=true;await write(scopeDirectory,'revoke');}
    }});
    assert.equal(lost.wire.at(-1),'RUN_ERROR');assert.equal(lost.protocolError,'authorization.permission_denied');
    await write(scopeDirectory,'read-complete');
    const gap=await wait(path.join(scopeDirectory,'gap.json'));
    await problem(terminal.url,gap.token,409,'ai.agent_run.events_cursor_expired');
    assert.ok(requests.every(request=>request.method==='GET'),'标准客户端不得创建或重派运行。');
    const result={provider,scope:ready.scopeKey==='host'?'host':'tenant',native:ready.native,passed:true,
      cursor:ready.maximum,uniqueEvents:terminal.ledger.received.length,terminalCount:terminal.ledger.terminals.size,
      disconnectedAt:cursor,requestCount:requests.length,currentSessionRejected:true,slowSubscriberMilliseconds:3};
    results.push(result);await write(scopeDirectory,'complete',result);await write(root,'network-results',{passed:true,scopes:results});
  }
}
try{await Promise.all(['SqlServer','MySql'].map(runProvider));assert.equal(results.length,4);console.log(JSON.stringify({passed:true,scopes:results}));}
catch(error){await fs.mkdir(root,{recursive:true});await write(root,'failed',{passed:false,errorType:error?.constructor?.name??'Error'});throw error;}
