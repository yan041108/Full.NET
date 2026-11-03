import test from 'node:test';import assert from 'node:assert/strict';import http from 'node:http';import fs from 'node:fs';import path from 'node:path';
import {ReplayLedger,replayOwnedRun} from './agui-replay-client.mjs';

const corpus=process.env.FULLNET_A04_AGUI_CORPUS;
if(!corpus)throw Error('Provide the current .NET-generated protocol corpus; missing evidence cannot be skipped.');
async function serve(name,action){
  const ids=JSON.parse(fs.readFileSync(path.join(corpus,name+'.json')));const body=fs.readFileSync(path.join(corpus,name+'.sse'));
  for(const line of body.toString('utf8').split('\n').filter(value=>value.startsWith('data: '))){
    const event=JSON.parse(line.slice(6));
    if(event.type==='RUN_FINISHED'){
      assert.equal('result' in event,false,'不得依赖SDK退役字段兼容转换。');
      if(event.outcome.type==='success')assert.equal('interrupts' in event.outcome,false);
      if(event.outcome.type==='interrupt')assert.ok(event.outcome.interrupts.length>0);
    }
  }
  const requests=[];const server=http.createServer((req,res)=>{requests.push({method:req.method,path:req.url});res.writeHead(200,{'Content-Type':'text/event-stream'});res.end(body);});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin='http://127.0.0.1:'+server.address().port;const url=origin+'/api/v1/ai/agent/runs/'+ids.runId+'/events/stream';
  const identity={origin,runId:ids.runId,actorKey:'unit-owner',scopeKey:'host'};
  try{await action({...identity,...ids,url,ledger:new ReplayLedger(identity)},requests);}
  finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
}

test('official SDK consumes actual mapped full history and one final boundary over TCP',async()=>{
  await serve('history',async(config,requests)=>{
    const result=await replayOwnedRun(config);assert.deepEqual(config.ledger.received,[1,2,3,4]);assert.equal(result.terminalCount,1);
    assert.equal(result.wire[0],'RUN_STARTED');assert.equal(result.wire.at(-1),'RUN_FINISHED');assert.equal(result.status,'completed');
    assert.equal(requests.length,1);assert.equal(requests[0].method,'GET');
  });
});
test('explicit reconnect rewind deduplicates the original source events and terminal',async()=>{
  await serve('history',async(config,requests)=>{
    await replayOwnedRun(config);await replayOwnedRun({...config,afterSequence:0});
    assert.deepEqual(config.ledger.received,[1,2,3,4]);assert.equal(config.ledger.terminals.size,1);assert.equal(requests.length,2);
    assert.ok(requests.every(request=>request.method==='GET'));
  });
});
test('read-only cursor resume is accepted by the standard decoder',async()=>{
  await serve('resume',async(config)=>{
    config.ledger.cursor=2;const result=await replayOwnedRun({...config,afterSequence:2});
    assert.deepEqual(config.ledger.received,[3,4]);assert.equal(result.wire[0],'RUN_STARTED');assert.equal(result.wire.at(-1),'RUN_FINISHED');
  });
});
test('slow subscriber preserves ordered event delivery without a second request',async()=>{
  await serve('history',async(config,requests)=>{
    await replayOwnedRun({...config,delayMilliseconds:15});assert.deepEqual(config.ledger.received,[1,2,3,4]);assert.equal(requests.length,1);
  });
});
test('deduplication state cannot be borrowed by another actor or scope',async()=>{
  await serve('history',async(config,requests)=>{
    await assert.rejects(()=>replayOwnedRun({...config,actorKey:'other'}),/identity_changed/);
    await assert.rejects(()=>replayOwnedRun({...config,scopeKey:'other'}),/identity_changed/);assert.equal(requests.length,0);
  });
});
test('current independent approval is a protocol interruption, not a completed business action',async()=>{
  await serve('awaiting_approval',async(config)=>{
    const result=await replayOwnedRun(config);assert.equal(result.status,'awaiting_approval');assert.equal(result.wire.at(-1),'RUN_FINISHED');
  });
});
test('unknown receiver receipt is a protocol interruption and retains the persisted cursor',async()=>{
  await serve('reconciliation_required',async(config)=>{
    const result=await replayOwnedRun(config);assert.equal(result.status,'reconciliation_required');
    assert.equal(result.cursor,1);assert.equal(result.terminalCount,1);assert.equal(result.wire.at(-1),'RUN_FINISHED');
  });
});
