import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {join} from 'node:path';
import {setTimeout as sleep} from 'node:timers/promises';
import {runImportRecoveryCommand} from './run-import-recovery-command.mjs';

// 端口监听不代表初始化后的账号已可登录；只读 SELECT 1 就绪后才启动一次正式迁移。
export async function waitForImportRecoveryDatabase(provider,container,{signal,out,run=runImportRecoveryCommand,timeoutMs=120000}={}){
 assert(['sqlserver','mysql'].includes(provider));
 const command=provider==='sqlserver'
  ? 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 2 -t 2 -h -1 -W -Q "SET NOCOUNT ON; SELECT 1;"; else exec /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -b -l 2 -t 2 -h -1 -W -Q "SET NOCOUNT ON; SELECT 1;"; fi'
  : 'export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; exec mysql --connect-timeout=2 -h127.0.0.1 -uroot -N -B -e "SELECT 1;"';
 const startedAt=new Date().toISOString(),deadline=Date.now()+timeoutMs;let attempts=0;
 while(true){
  signal?.throwIfAborted();assert(Date.now()<deadline,'database actual-login readiness timed out');attempts++;
  const logPath=join(out,'database-ready-'+attempts+'.log');
  try{await run('docker',['exec',container.getId(),'/bin/sh','-c',command],{signal,timeoutMs:Math.min(15000,deadline-Date.now()),logPath});assert.match(readFileSync(logPath,'utf8'),/^\s*1\s*$/mu,'database readiness must return SELECT 1');return{attempts,startedAt,finishedAt:new Date().toISOString(),query:'SELECT 1'};}
  catch(error){if(signal?.aborted||error.cleanupUnconfirmed===true)throw error;assert(Date.now()<deadline,'database actual-login readiness timed out');await sleep(Math.min(500,deadline-Date.now()),undefined,{signal});}
 }
}
