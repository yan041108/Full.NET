import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {waitForImportRecoveryDatabase} from './support/import-recovery-database-ready.mjs';

for(const provider of ['sqlserver','mysql']){
 test(provider+' 端口已开但登录暂未就绪时，成功 SELECT 1 后才放行',async()=>{
  const out=mkdtempSync(join(tmpdir(),'import-ready-'));let calls=0;
  try{const result=await waitForImportRecoveryDatabase(provider,{getId:()=> 'owned-database'},{out,run:async(_command,_args,{logPath})=>{calls++;writeFileSync(logPath,calls===1?'Login failed':'1\n');if(calls===1)throw new Error('not initialized');}});
   assert.equal(calls,2);assert.equal(result.attempts,2);assert.equal(result.query,'SELECT 1');
  }finally{rmSync(out,{recursive:true,force:true});}
 });
}

test('已取消的就绪等待不得启动外部进程',async()=>{
 let calls=0;await assert.rejects(waitForImportRecoveryDatabase('sqlserver',{getId:()=> 'owned'},{signal:AbortSignal.abort(),run:async()=>{calls++;}}));assert.equal(calls,0);
});

test('外部进程清理未确认时立即失败，不循环启动新探针',async()=>{
 let calls=0;const error=Object.assign(new Error('unconfirmed'),{cleanupUnconfirmed:true});
 await assert.rejects(waitForImportRecoveryDatabase('mysql',{getId:()=> 'owned'},{out:tmpdir(),run:async()=>{calls++;throw error;}}),error);assert.equal(calls,1);
});
