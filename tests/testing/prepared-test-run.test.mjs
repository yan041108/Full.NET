import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {randomUUID,createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import {prepareTestBuild} from '../../scripts/testing/test-build-reuse.mjs';
import {withPreparedTestRun} from '../../scripts/testing/prepared-test-run.mjs';
const parent=path.resolve('.tmp/prepared-test-run-fixtures');fs.mkdirSync(parent,{recursive:true});
function fixture(){
 const cwd=fs.mkdtempSync(path.join(parent,'case-')),token=randomUUID();
 fs.writeFileSync(path.join(cwd,'.fixture-owner'),token);
 fs.writeFileSync(path.join(cwd,'.gitignore'),'.tmp/\nbin/\nlocks/\n');
 fs.writeFileSync(path.join(cwd,'sample.csproj'),'<Project />');fs.writeFileSync(path.join(cwd,'Program.cs'),'original');
 execFileSync('git',['init','--quiet'],{cwd,windowsHide:true});
 const lockRoot=path.join(cwd,'locks');fs.mkdirSync(lockRoot);
 let builds=0;const modes=[];let afterPreparation=()=>{},preparedResolve;
 const prepared=new Promise(resolve=>{preparedResolve=resolve;});
 const prepare=async mode=>{modes.push(mode);const result=await prepareTestBuild({cwd,project:'sample.csproj',assembly:'bin/sample.dll',args:['build','sample.csproj'],mode,sdkVersion:'10.0.401',build:async()=>{builds++;fs.mkdirSync(path.join(cwd,'bin'));fs.writeFileSync(path.join(cwd,'bin/sample.dll'),'compiled');}});if(mode==='reuse'){afterPreparation();preparedResolve(true);}return result;};
 const heavy=path.join(lockRoot,'heavy.lock');
 const hold=()=>fs.writeFileSync(heavy,JSON.stringify({pid:process.pid,token}));
 const release=()=>{if(fs.existsSync(heavy)){assert.equal(JSON.parse(fs.readFileSync(heavy)).token,token);fs.unlinkSync(heavy);}};
 const clean=()=>{assert.equal(fs.realpathSync(path.dirname(cwd)),fs.realpathSync(parent));assert.ok(path.basename(cwd).startsWith('case-'));assert.equal(fs.readFileSync(path.join(cwd,'.fixture-owner'),'utf8'),token);fs.rmSync(cwd,{recursive:true});};
 return {cwd,token,lockRoot,prepare,prepared,modes,heavy,hold,release,clean,get builds(){return builds;},set afterPreparation(value){afterPreparation=value;}};
}
test('编译准备不等待重型锁，运行必须等待重型锁',async()=>{
 const f=fixture();f.hold();let executed=false;const pending=withPreparedTestRun({...f,heavy:true,timeoutMs:8000},async()=>{executed=true;});
 try{assert.equal(await Promise.race([f.prepared,delay(3000,false)]),true);assert.equal(f.builds,1);assert.equal(executed,false);f.release();await pending;assert.equal(executed,true);assert.deepEqual(f.modes,['reuse','verify']);}
 finally{f.release();await pending.catch(()=>{});f.clean();}
});
test('正式运行前只校验产物，不隐式补编译',async()=>{
 const f=fixture();let executed=false;
 try{await withPreparedTestRun({...f,heavy:true},async()=>{executed=true;});assert.equal(f.builds,1);assert.deepEqual(f.modes,['reuse','verify']);assert.equal(executed,true);}
 finally{f.clean();}
});
for(const kind of ['源码','产物'])test(kind+'在准备后改变必须阻止运行，不能补编译',async()=>{
 const f=fixture();let executed=false;
 f.afterPreparation=()=>queueMicrotask(()=>fs.writeFileSync(path.join(f.cwd,kind==='源码'?'Program.cs':'bin/sample.dll'),'tampered'));
 try{await assert.rejects(withPreparedTestRun({...f,heavy:true},async()=>{executed=true;}),/构建输入或产物已改变/);assert.equal(f.builds,1);assert.equal(executed,false);}
 finally{f.clean();}
});
test('等待重型锁期间取消，保留他方锁并释放本任务资源',async()=>{
 const f=fixture();f.hold();const abort=new AbortController();let executed=false;
 const pending=withPreparedTestRun({...f,heavy:true,signal:abort.signal,timeoutMs:8000},async()=>{executed=true;});pending.catch(()=>{});
 try{assert.equal(await Promise.race([f.prepared,delay(3000,false)]),true);assert.equal(f.builds,1);abort.abort();await assert.rejects(pending);assert.equal(executed,false);assert.equal(JSON.parse(fs.readFileSync(f.heavy)).token,f.token);}
 finally{abort.abort();await pending.catch(()=>{});f.release();f.clean();}
});
test('运行和异步收尾完成前一直持有重型锁',async()=>{
 const f=fixture();let cleanupFinished=false;
 try{await withPreparedTestRun({...f,heavy:true},async()=>{assert.ok(fs.existsSync(f.heavy));await delay(30);assert.ok(fs.existsSync(f.heavy));cleanupFinished=true;return 7;});assert.equal(cleanupFinished,true);assert.equal(fs.existsSync(f.heavy),false);}
 finally{f.clean();}
});
test('两阶段共用排队预算，不能取得两份完整等待预算',async()=>{
 const f=fixture();f.hold();let executed=false;
 const canonical=fs.realpathSync(f.cwd),key=process.platform==='win32'?canonical.toLowerCase():canonical;
 const workspace=path.join(f.lockRoot,createHash('sha256').update(key).digest('hex')+'.lock');
 fs.writeFileSync(workspace,JSON.stringify({pid:process.pid,token:f.token}));
 const releaseWorkspace=setTimeout(()=>{assert.equal(JSON.parse(fs.readFileSync(workspace)).token,f.token);fs.unlinkSync(workspace);},600);
 let releaseHeavy;
 const pending=withPreparedTestRun({...f,heavy:true,timeoutMs:1200},async()=>{executed=true;});pending.catch(()=>{});
 f.prepared.then(()=>{releaseHeavy=setTimeout(()=>f.release(),950);});
 try{await assert.rejects(pending,/等待测试资源超时/);assert.equal(f.builds,1);assert.equal(executed,false);}
 finally{clearTimeout(releaseWorkspace);clearTimeout(releaseHeavy);await pending.catch(()=>{});f.release();if(fs.existsSync(workspace)){assert.equal(JSON.parse(fs.readFileSync(workspace)).token,f.token);fs.unlinkSync(workspace);}f.clean();}
});
test('准备失败立即返回，不申请重型锁或执行验收',async()=>{
 const f=fixture();f.hold();let executed=false;
 try{await assert.rejects(withPreparedTestRun({...f,heavy:true,timeoutMs:500,prepare:async()=>{throw new Error('受控准备失败');}},async()=>{executed=true;}),/受控准备失败/);assert.equal(executed,false);assert.equal(JSON.parse(fs.readFileSync(f.heavy)).token,f.token);}
 finally{f.release();f.clean();}
});
test('编译准备耗时不侵占排队预算',async()=>{
 const f=fixture();let executed=false;
 const prepare=async mode=>{const result=await f.prepare(mode);if(mode==='reuse')await delay(700);return result;};
 try{await withPreparedTestRun({...f,heavy:true,timeoutMs:500,prepare},async()=>{executed=true;});assert.equal(executed,true);assert.equal(f.builds,1);assert.deepEqual(f.modes,['reuse','verify']);}
 finally{f.clean();}
});
