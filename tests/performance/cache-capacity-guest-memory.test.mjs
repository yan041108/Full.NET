import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {withTestRun,testRunEnvironment} from '../../scripts/testing/test-run-context.mjs';
import {readEngineGuestMemory} from '../../eng/load/cache-capacity/guest-memory.mjs';
const imageId='sha256:'+'a'.repeat(64),owner='fullnet-cache-capacity-synthetic',id='b'.repeat(64);
const raw='MemTotal: 20971520 kB\nMemAvailable: 17825792 kB\nCached: 8388608 kB\nSwapFree: 5242880 kB';
function fixture(modify=a=>a){
 const calls=[],events=[];let created,removed=false;
 const docker=async args=>{
  calls.push(args);
  if(args[0]==='create'){
   const labels={};for(let i=0;i<args.length;i++)if(args[i]==='--label'){const [k,v]=args[i+1].split('=');labels[k]=v;}
   created={Id:id,Image:imageId,Config:{Labels:labels},State:{Status:'created',Running:false},HostConfig:{Memory:32*1024**2,MemorySwap:32*1024**2,NanoCpus:1e8,PidsLimit:16,NetworkMode:'none',ReadonlyRootfs:true}};return id;
  }
  if(args[0]==='inspect')return JSON.stringify([modify(created)]);
  if(args[0]==='start'){created.State={Running:false,ExitCode:0,OOMKilled:false};return raw;}
  if(args[0]==='rm'){removed=true;return id;}
  if(args[0]==='ps')return removed||!created?'':id;
  throw Error('Unexpected Docker call '+args[0]);
 };
 return{calls,events,docker,options:{docker,imageId,owner,journal:async e=>events.push(e)}};
}
test('Startup probe reads actual Engine and returns only its own container',async()=>{
 const f=fixture(),m=await readEngineGuestMemory(f.options);assert.equal(m.MemAvailable,17825792*1024);
 assert.ok(f.calls.findIndex(a=>a[0]==='inspect')<f.calls.findIndex(a=>a[0]==='start'));
 assert.deepEqual(f.calls.filter(a=>a[0]==='rm').map(a=>a.at(-1)),[id]);assert.equal(f.events.at(-1).stage,'memory-probe-returned');
});
test('Bad actual quota prevents starting the probe but still cleans it',async()=>{
 const f=fixture(a=>({...a,HostConfig:{...a.HostConfig,Memory:0}}));await assert.rejects(readEngineGuestMemory(f.options),/quota/);
 assert.equal(f.calls.some(a=>a[0]==='start'),false);assert.equal(f.calls.filter(a=>a[0]==='rm').length,1);
});
test('Changed ownership prevents deleting a foreign object and retains the resource boundary',async()=>{
 const f=fixture(a=>({...a,Image:'sha256:'+'c'.repeat(64)}));await assert.rejects(readEngineGuestMemory(f.options),e=>e.retainResources===true);
 assert.equal(f.calls.some(a=>a[0]==='rm'),false);
});
test('Formal measurement reads the existing exact owned SDK without creating a new workload',async()=>{
 const calls=[],docker=async a=>{calls.push(a);return a[0]==='inspect'?JSON.stringify([{Id:id,Image:imageId,Config:{Labels:{'fullnet.cache-capacity.owner':owner}},State:{Running:true},HostConfig:{Memory:8*1024**3,MemorySwap:8*1024**3,NanoCpus:4e9}}]):raw;};
 const m=await readEngineGuestMemory({docker,imageId,owner,container:'owned-sdk',journal:async()=>{}});assert.equal(m.Cached,8388608*1024);assert.deepEqual(calls.map(a=>a[0]),['inspect','exec']);
});
test('A different SDK owner cannot supply capacity memory evidence',async()=>{
 const docker=async()=>JSON.stringify([{Image:imageId,Config:{Labels:{}},State:{Running:true},HostConfig:{}}]);
 await assert.rejects(readEngineGuestMemory({docker,imageId,owner,container:'foreign',journal:async()=>{}}),/owner or limits/);
});
test('An unknown create completion cannot be returned on an empty daemon listing',async()=>{
 const f=fixture(),docker=async a=>{if(a[0]==='create')throw Error('Synthetic create transport timeout');if(a[0]==='ps')return '';return f.docker(a);};
 await assert.rejects(readEngineGuestMemory({...f.options,docker}),e=>e.retainResources===true&&/completion is unknown/.test(e.message));
 assert.equal(f.events.some(x=>x.stage==='memory-probe-returned'),false);
});
test('Actual record I/O failure still exits without unwinding the real test lease',async t=>{
 await withTestRun({cwd:os.tmpdir(),lock:false,build:false},async()=>{
 const directory=fs.mkdtempSync(path.join(testRunEnvironment().TEMP,'fullnet-cache-boundary-test-')),module=new URL('../../eng/load/cache-capacity/guest-memory.mjs',import.meta.url).href;
 const code=`import fs from 'node:fs';import {withTestRun,testRunEnvironment} from 'file:///G:/wwwroot/github_fork/Full.NET/scripts/testing/test-run-context.mjs';import {preserveUnknownBoundary} from ${JSON.stringify(module)};const d=${JSON.stringify(directory)};try{await withTestRun({cwd:d,lockRoot:d+'/locks',build:false,heavy:false},async()=>{fs.writeFileSync(d+'/actor.json',JSON.stringify({pid:process.pid,temp:testRunEnvironment().TEMP}));preserveUnknownBoundary(Error('Unknown create'),()=>{throw Error('Synthetic record IO failure');});});}finally{fs.writeFileSync(d+'/lease-unwound','unexpected');}`;
 const child=spawnSync(process.execPath,['--input-type=module','--eval',code],{env:testRunEnvironment(),encoding:'utf8',windowsHide:true,timeout:10000});
 assert.equal(child.status,2,child.stderr);assert.equal(child.signal,null);assert.equal(fs.existsSync(directory+'/lease-unwound'),false);
 const actor=JSON.parse(fs.readFileSync(directory+'/actor.json','utf8')),locks=fs.readdirSync(directory+'/locks').filter(x=>x.endsWith('.lock'));
 assert.equal(locks.length,1);assert.equal(JSON.parse(fs.readFileSync(directory+'/locks/'+locks[0],'utf8')).pid,child.pid);
 const proof={actualChildExit:child.status,actualChildPid:child.pid,ioFailureDidNotUnwindLease:true,privateLockStayedOwned:true,fixture:directory,runRoot:path.dirname(actor.temp)};
 t.diagnostic(JSON.stringify(proof));
 });
});
