import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {runImportRecoveryCommand} from './support/run-import-recovery-command.mjs';

async function fixture(fn){const directory=mkdtempSync(join(tmpdir(),'import-command-'));try{await fn(directory);}finally{rmSync(directory,{recursive:true,force:true});}}
test('正常子进程退出并完成日志',()=>fixture(async dir=>{
 const logPath=join(dir,'run.log');const result=await runImportRecoveryCommand(process.execPath,['-e','console.log("complete")'],{logPath,timeoutMs:5000});
 assert.equal(result.exitCode,0);assert.match(readFileSync(logPath,'utf8'),/complete/);
}));
test('已取消时不启动命令',()=>fixture(async dir=>{
 const controller=new AbortController();controller.abort(new Error('cancelled before start'));
 await assert.rejects(runImportRecoveryCommand(process.execPath,['-e','console.log("should not start")'],{logPath:join(dir,'run.log'),signal:controller.signal,timeoutMs:5000}),/cancelled before start/);
}));
test('命令超过预算必须失败且子进程已经关闭',()=>fixture(async dir=>{
 let failure;try{await runImportRecoveryCommand(process.execPath,['-e','setTimeout(()=>process.exit(0),500)'],{logPath:join(dir,'run.log'),timeoutMs:50});}catch(error){failure=error;}
 assert(failure,'timeout must reject');assert.match(failure.message,/timeout/);assert(Number.isInteger(failure.pid));
 assert.throws(()=>process.kill(failure.pid,0),error=>error.code==='ESRCH');
}));
test('运行中取消会关闭自有进程并完成日志',()=>fixture(async dir=>{
 const controller=new AbortController();const timer=setTimeout(()=>controller.abort(new Error('cancelled during run')),50);
 let failure;try{await runImportRecoveryCommand(process.execPath,['-e','setTimeout(()=>process.exit(0),500)'],{logPath:join(dir,'run.log'),signal:controller.signal,timeoutMs:5000});}catch(error){failure=error;}finally{clearTimeout(timer);}
 assert(failure);assert.match(failure.message,/cancelled during run/);assert(Number.isInteger(failure.pid));
 assert.throws(()=>process.kill(failure.pid,0),error=>error.code==='ESRCH');
}));
import {spawn} from 'node:child_process';
import {writeFileSync} from 'node:fs';

test('父进程退出后继承管道不能绕过总预算',{skip:process.platform==='win32'?'Windows Node 管道不保留孙进程句柄；用确定性 close 屏障覆盖预算':false},()=>fixture(async dir=>{
 const pidFile=join(dir,'descendant.pid');let descendant;
 const script=`const {spawn}=require('node:child_process');const {writeFileSync}=require('node:fs');const c=spawn(process.execPath,['-e','setTimeout(()=>{},4000)'],{stdio:['ignore',process.stdout,process.stderr]});writeFileSync(${JSON.stringify(pidFile)},String(c.pid));setTimeout(()=>process.exit(0),100);`;
 const started=Date.now();let failure;
 try{await runImportRecoveryCommand(process.execPath,['-e',script],{logPath:join(dir,'run.log'),timeoutMs:500});}catch(error){failure=error;}
 finally{try{descendant=Number(readFileSync(pidFile,'utf8'));process.kill(descendant,'SIGKILL');}catch(error){if(error.code!=='ESRCH'&&error.code!=='ENOENT')throw error;}}
 assert(failure,'inherited pipes must fail at deadline');assert.match(failure.message,/timeout/);assert(Date.now()-started<1800,'deadline must not wait for descendant natural exit');
}));
import {EventEmitter} from 'node:events';
import {PassThrough} from 'node:stream';

test('已退出父进程的未关闭管道必须有界失败',()=>fixture(async dir=>{
 const child=new EventEmitter();child.stdout=new PassThrough();child.stderr=new PassThrough();child.exitCode=0;child.signalCode=null;
 const started=Date.now();const natural=setTimeout(()=>child.emit('close',0),3000);
 try{await assert.rejects(runImportRecoveryCommand('owned-test',[],{logPath:join(dir,'run.log'),timeoutMs:50,cleanupTimeoutMs:100,spawnProcess:()=>child}),/timeout/);assert(Date.now()-started<1000);}finally{clearTimeout(natural);child.stdout.destroy();child.stderr.destroy();}
}));

import {watchImportRecoveryProcess} from './support/run-import-recovery-command.mjs';

test('宿主启动和日志错误立即取消就绪等待',()=>{
 for(const origin of ['child','log']){
  const child=new EventEmitter(),log=new EventEmitter();const signal=watchImportRecoveryProcess(child,log);const failure=new Error(origin+' failed');
  assert.doesNotThrow(()=>(origin==='child'?child:log).emit('error',failure));assert.equal(signal.aborted,true);assert.equal(signal.reason,failure);
 }
});
test('日志目录不存在时关闭命令并传播写盘错误',()=>fixture(async dir=>{
 let failure;try{await runImportRecoveryCommand(process.execPath,['-e','setTimeout(()=>{},5000)'],{logPath:join(dir,'absent','run.log'),timeoutMs:1000});}catch(error){failure=error;}
 assert(failure);assert.equal(failure.code,'ENOENT');assert.throws(()=>process.kill(failure.pid,0),error=>error.code==='ESRCH');
}));
