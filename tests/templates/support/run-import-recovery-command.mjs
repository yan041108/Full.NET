import {spawn} from 'node:child_process';
import {createWriteStream} from 'node:fs';
import {finished} from 'node:stream/promises';

// 立即接住宿主和日志错误，用同一取消信号中断就绪轮询；监听保留到资源清理完成。
export function watchImportRecoveryProcess(child,log){
 const controller=new AbortController();
 const fail=error=>controller.abort(error);
 child.on('error',fail);log.on('error',fail);
 return controller.signal;
}

// 应用消费者命令与日志只由创建它的测试持有，预算包含异常路径和日志排空。
export async function runImportRecoveryCommand(command,args,{cwd,env,logPath,signal,timeoutMs,cleanupTimeoutMs=5000,spawnProcess=spawn}) {
 signal?.throwIfAborted();
 if(!Number.isFinite(timeoutMs)||timeoutMs<=0||!Number.isFinite(cleanupTimeoutMs)||cleanupTimeoutMs<=0)throw new Error('command timeout must be positive');
 const child=spawnProcess(command,args,{cwd,env,windowsHide:true,detached:process.platform!=='win32',stdio:['ignore','pipe','pipe']});
 const log=createWriteStream(logPath);
 const flushed=finished(log,{cleanup:true});void flushed.catch(()=>{});
 let failure,termination=Promise.resolve(),cleanupTimer,closed=false,resolveClose;
 const closeResult=new Promise(resolve=>{resolveClose=resolve;child.once('close',code=>{closed=true;resolve(code);});});
 const terminate=()=>{
  if(cleanupTimer)return;
  // 清理也有独立预算；未确认关闭时拒绝验收，并显式标记清理未确认，不能伪装成功。
  cleanupTimer=setTimeout(()=>{
   if(closed)return;
   failure??=new Error('owned process cleanup timeout');failure.cleanupUnconfirmed=true;
   child.stdout.destroy();child.stderr.destroy();resolveClose(null);
  },cleanupTimeoutMs);
  if(!child.pid)return;
  termination=(async()=>{
   if(process.platform==='win32'){
    if(child.exitCode!==null||child.signalCode!==null)return;
    const killer=spawn('taskkill',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});
    await new Promise((resolve,reject)=>{killer.once('error',reject);killer.once('close',code=>code===0?resolve():reject(new Error('owned process tree did not terminate')));});
   }else{
    // 父进程已退出也必须终止自有会话中的后代，不能以 exitCode 代替整个管道关闭。
    try{process.kill(-child.pid,'SIGKILL');}catch(error){if(error.code!=='ESRCH')throw error;}
   }
  })();void termination.catch(error=>{failure??=error;});
 };
 const fail=error=>{failure??=error;terminate();};
 child.on('error',fail);log.on('error',fail);
 child.stdout.pipe(log,{end:false});child.stderr.pipe(log,{end:false});
 const abort=()=>fail(signal.reason instanceof Error?signal.reason:new Error('command cancelled'));
 signal?.addEventListener('abort',abort,{once:true});if(signal?.aborted)abort();
 const timer=setTimeout(()=>fail(new Error('import recovery command timeout')),timeoutMs);
 const code=await closeResult;
 clearTimeout(timer);clearTimeout(cleanupTimer);signal?.removeEventListener('abort',abort);
 if(!failure?.cleanupUnconfirmed){try{await termination;}catch(error){failure??=error;}}
 child.stdout.unpipe(log);child.stderr.unpipe(log);log.end();
 try{await flushed;}catch(error){failure??=error;}
 if(failure){failure.pid=child.pid;throw failure;}
 if(code!==0){const error=new Error('import recovery command failed: '+code);error.pid=child.pid;throw error;}
 return {pid:child.pid,exitCode:code};
}
