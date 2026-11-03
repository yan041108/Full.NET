import { HttpAgent } from '@ag-ui/client';

const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** 测试消费账本只去重已授权流的事件，不以其身份参数替代服务器授权。 */
export class ReplayLedger {
  constructor({origin,actorKey,scopeKey,runId}) {
    if(!guid.test(runId)||!actorKey||!scopeKey)throw Error('client.agui.invalid_identity');
    this.key=[origin,actorKey,scopeKey,runId.toLowerCase()].join('|');this.runId=runId.toLowerCase();
    this.cursor=0;this.received=[];this.terminals=new Set();this.status=undefined;
  }
  observe(event) {
    if(event.type==='RUN_STARTED'&&event.runId?.toLowerCase()!==this.runId)throw Error('client.agui.run_mismatch');
    if(event.type==='STATE_SNAPSHOT'){this.status=event.snapshot.statusKey;return true;}
    if(event.type==='CUSTOM'){
      const value=event.value;
      if(value?.runId?.toLowerCase()!==this.runId||!Number.isSafeInteger(value.sequence)||value.sequence<1)throw Error('client.agui.invalid_sequence');
      if(value.sequence<=this.cursor)return false;
      if(value.sequence!==this.cursor+1)throw Error('client.agui.sequence_gap');
      this.cursor=value.sequence;this.received.push(value.sequence);return true;
    }
    if(event.type==='RUN_FINISHED'||event.type==='RUN_ERROR'){
      const key=[event.type,this.cursor,this.status,event.code??event.outcome?.type??''].join('|');
      if(this.terminals.has(key))return false;this.terminals.add(key);
    }
    return true;
  }
}

/** 官方SDK负责帧解析、协议验证和订阅；此公开扩展只把传输固定为既有运行的只读GET。 */
class OwnedReplayAgent extends HttpAgent {
  requestInit(){return {method:'GET',headers:{...this.headers,Accept:'text/event-stream'},redirect:'error',signal:this.abortController.signal};}
}

export async function replayOwnedRun({url,actorKey,scopeKey,runId,threadId,token,ledger,afterSequence=0,abortAt,delayMilliseconds=0,fetchImpl,onEvent,expectedErrorCode}) {
  const endpoint=new URL(url);
  const path=endpoint.pathname.match(/^\/api\/v1\/ai\/agent\/runs\/([^/]+)\/events\/stream$/);
  if(!['http:','https:'].includes(endpoint.protocol)||!['localhost','127.0.0.1','[::1]'].includes(endpoint.hostname)
    ||endpoint.username||endpoint.password||endpoint.hash||!path||path[1].toLowerCase()!==runId.toLowerCase()||!guid.test(runId)
    ||!Number.isSafeInteger(afterSequence)||afterSequence<0)throw Error('client.agui.invalid_destination');
  const identity=[endpoint.origin,actorKey,scopeKey,runId.toLowerCase()].join('|');
  if(ledger.key!==identity)throw Error('client.agui.identity_changed');
  endpoint.search='';endpoint.searchParams.set('afterSequence',String(afterSequence));
  const agent=new OwnedReplayAgent({url:endpoint.href,threadId,headers:token?{Authorization:'Bearer '+token}:{},fetch:fetchImpl});
  const wire=[];let stopped=false;let protocolError;
  try{
    await agent.runAgent({runId},{onEvent:async({event})=>{
      if(stopped)return {stopPropagation:true};
      wire.push(event.type);const accepted=ledger.observe(event);
      if(event.type==='RUN_ERROR')protocolError=event.code;
      if(onEvent)await onEvent(event);
      if(abortAt&&ledger.cursor>=abortAt){stopped=true;agent.abortRun();}
      if(delayMilliseconds)await new Promise(resolve=>setTimeout(resolve,delayMilliseconds));
      if(!accepted)return {stopPropagation:true};
    }});
  }catch(error){if(!stopped&&!(expectedErrorCode&&protocolError===expectedErrorCode&&wire.at(-1)==='RUN_ERROR'))throw error;}
  if(expectedErrorCode&&protocolError!==expectedErrorCode)throw Error('client.agui.expected_error_missing');
  return {wire,aborted:stopped,cursor:ledger.cursor,status:ledger.status,terminalCount:ledger.terminals.size,protocolError};
}
