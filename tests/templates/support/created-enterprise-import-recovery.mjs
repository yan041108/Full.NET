import assert from 'node:assert/strict';
import {createHash,randomUUID} from 'node:crypto';
import {createWriteStream,existsSync,mkdirSync,mkdtempSync,readFileSync,readdirSync,writeFileSync} from 'node:fs';
import {createServer} from 'node:net';
import {tmpdir} from 'node:os';
import {join,resolve,parse,relative,isAbsolute,sep} from 'node:path';
import {spawn,spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {setTimeout as sleep} from 'node:timers/promises';
import {buildAppTemplate} from '../../../scripts/templates/build-app-template.mjs';
import {createApp} from '../../../scripts/templates/create-app.mjs';
import {testRunEnvironment} from '../../../scripts/testing/test-run-context.mjs';
import {startDatabaseContainer,startRedisContainer,buildSharedEnv} from './created-app-real-stack.mjs';
import {runImportRecoveryCommand,watchImportRecoveryProcess} from './run-import-recovery-command.mjs';
import {verifyApplicationImportRecovery} from './application-import-recovery.mjs';
import {stopLoggedProcess} from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';
import {waitForApi} from '../../e2e/admin-real-stack/scripts/wait-for-api.mjs';

const repoRoot=resolve(fileURLToPath(new URL('../../../',import.meta.url)));
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
// 源码和所有宿主依赖一起冻结；运行生成的日志、临时目录和 obj 不属于可复用产物。
export function snapshotImportRecoveryApplication(appRoot){
 const files={};
 const visit=directory=>{for(const entry of readdirSync(directory,{withFileTypes:true})){
  if(entry.isDirectory()&&['obj','.tmp','.git','node_modules'].includes(entry.name))continue;
  const file=join(directory,entry.name);if(entry.isDirectory())visit(file);else if(entry.isFile())files[relative(appRoot,file).replaceAll('\\','/')]=hash(readFileSync(file));
 }};
 visit(appRoot);return files;
}
async function freePort(){const server=createServer();await new Promise((ok,no)=>{server.once('error',no);server.listen(0,'127.0.0.1',ok);});const port=server.address().port;await new Promise((ok,no)=>server.close(error=>error?no(error):ok()));return port;}

// 一份独立应用构建同时消费两种正式提供程序；配置用环境覆盖，业务验收不重建。
export async function prepareCreatedImportRecovery({signal}={}){
 const base=process.env.FULLNET_TEST_WORKSPACE_ROOT||(process.platform==='win32'?join(parse(repoRoot).root,'.fullnet-tests'):join(tmpdir(),'fullnet-tests'));
 if(process.platform==='win32'&&process.env.USERPROFILE){const inside=relative(resolve(process.env.USERPROFILE),resolve(base));assert(inside==='..'||inside.startsWith('..'+sep)||isAbsolute(inside),'application workspace must be outside user profile');}
 mkdirSync(base,{recursive:true});const root=mkdtempSync(join(base,'ir-app-'));
 const out=join(repoRoot,'.tmp/template-real-stack/import-recovery',randomUUID());mkdirSync(out,{recursive:true});
 const receipt={prepared:false,root,out,source:null,projects:{},consumerOverlay:{},builds:[]};
 const save=()=>writeFileSync(join(out,'prepare.json'),JSON.stringify(receipt,null,2));save();
 try{
  signal?.throwIfAborted();const {templateRoot}=buildAppTemplate({output:join(root,'package')});
  const appRoot=join(root,'app');createApp({packageRoot:templateRoot,output:appRoot,name:'ImportRecovery',ownerKey:'recovery',database:'sqlserver',preset:'enterprise',httpPort:await freePort()});
  receipt.appRoot=appRoot;receipt.source=JSON.parse(readFileSync(join(appRoot,'framework-manifest.json'))).sourceCommit;
  const worker=join(appRoot,'src/ImportRecovery.Host.Worker');
  const project=join(worker,'ImportRecovery.Host.Worker.csproj');let xml=readFileSync(project,'utf8');
  const old='../../framework/fullnet/src/Hosts/Full.NET.Host.Worker/**/bin/**/*.cs"';assert(xml.includes(old));
  xml=xml.replace(old,'../../framework/fullnet/src/Hosts/Full.NET.Host.Worker/**/bin/**/*.cs;../../framework/fullnet/src/Hosts/Full.NET.Host.Worker/Program.cs"');writeFileSync(project,xml);
  let program=readFileSync(join(appRoot,'framework/fullnet/src/Hosts/Full.NET.Host.Worker/Program.cs'),'utf8');
  const marker='var app = builder.Build();';assert.equal(program.split(marker).length,2);
  program=program.replace(marker,`// 故障屏障只存在于本次验收应用，保留正式执行器工厂及其生命周期。
var probeInner = builder.Services.Last(item => item.ServiceType == typeof(ICommandExecutor));
var probeFactory = probeInner.ImplementationFactory ?? throw new InvalidOperationException("验收要求原写执行器通过工厂注册。");
builder.Services.Remove(probeInner);
builder.Services.Add(new ServiceDescriptor(typeof(ICommandExecutor), provider => new ImportRecoveryCheckpointProbe(
    (ICommandExecutor)probeFactory(provider),
    Environment.GetEnvironmentVariable("FULLNET_PROBE_DIRECTORY")!,
    Environment.GetEnvironmentVariable("FULLNET_PROBE_MODE")!,
    Guid.Parse(Environment.GetEnvironmentVariable("FULLNET_PROBE_TASK")!)), probeInner.Lifetime));
${marker}`);
  writeFileSync(join(worker,'Program.cs'),program);
  writeFileSync(join(worker,'ImportRecoveryCheckpointProbe.cs'),readFileSync(join(repoRoot,'tests/templates/support/import-recovery-checkpoint.cs')));
  for(const file of [project,join(worker,'Program.cs'),join(worker,'ImportRecoveryCheckpointProbe.cs')])receipt.consumerOverlay[relative(appRoot,file).replaceAll('\\','/')]=hash(readFileSync(file));
  for(const host of ['Api','Migrator','Worker']){
   signal?.throwIfAborted();const hostProject=join(appRoot,`src/ImportRecovery.Host.${host}/ImportRecovery.Host.${host}.csproj`);
   const startedAt=new Date().toISOString();await runImportRecoveryCommand('dotnet',['build',hostProject,'-c','Release','--nologo','-m:1','-p:BuildInParallel=false','-clp:ErrorsOnly'],{cwd:appRoot,env:testRunEnvironment(),timeoutMs:600000,logPath:join(out,host.toLowerCase()+'-build.log'),signal});
   receipt.builds.push({host,startedAt,finishedAt:new Date().toISOString(),exitCode:0});receipt.projects[host]=join(appRoot,`src/ImportRecovery.Host.${host}/bin/Release/net10.0/ImportRecovery.Host.${host}.dll`);save();
  }
  receipt.artifacts=Object.fromEntries(Object.values(receipt.projects).map(file=>[file,hash(readFileSync(file))]));receipt.immutableFiles=snapshotImportRecoveryApplication(appRoot);receipt.prepared=true;save();return receipt;
 }catch(error){receipt.error=error.message;receipt.commandFailure={pid:error.pid,cleanupUnconfirmed:error.cleanupUnconfirmed===true};save();throw error;}
}

// 正常与异常路径均等待自有进程的 close；保留独立应用与日志供最终输入/产物核对。
export async function verifyCreatedImportRecovery(provider,prepared,{signal}={}){
 assert(prepared.prepared);assert(['sqlserver','mysql'].includes(provider));
 const out=join(prepared.out,provider);mkdirSync(out,{recursive:true});
 const report={completed:false,provider,source:prepared.source,appRoot:prepared.appRoot,hosts:[]};
 let database,redis,failure;const processes=[];
 const guard=()=>{assert.deepEqual(snapshotImportRecoveryApplication(prepared.appRoot),prepared.immutableFiles);for(const[file,expected]of Object.entries(prepared.artifacts))assert.equal(hash(readFileSync(file)),expected);for(const[file,expected]of Object.entries(prepared.consumerOverlay))assert.equal(hash(readFileSync(join(prepared.appRoot,file))),expected);};
 try{
  guard();signal?.throwIfAborted();const stack=await startDatabaseContainer(provider);database=stack.container;
  signal?.throwIfAborted();const cache=await startRedisContainer();redis=cache.container;signal?.throwIfAborted();
  const env={...testRunEnvironment(),...buildSharedEnv(stack.connectionString,stack.databaseProvider,cache.connectionString),Files__Local__RootPath:join(out,'files'),DataProtection__KeyRingPath:join(out,'data-protection-keys'),FullNet__ImportExport__ExecutionEnabled:'false',FullNet__ImportExport__RunSynchronously:'false',FullNet__ImportExport__BatchSize:'1',FullNet__ImportExport__PollSeconds:'5',FullNet__ImportExport__LeaseSeconds:'60',FullNet__Reporting__Export__ExecutionEnabled:'false'};
  await runImportRecoveryCommand('dotnet',[prepared.projects.Migrator,'--seed','development'],{cwd:prepared.appRoot,env,timeoutMs:600000,logPath:join(out,'migrator.log'),signal});
  const start=async(host,overrides={})=>{
   signal?.throwIfAborted();
   const url='http://127.0.0.1:'+await freePort();const stream=createWriteStream(join(out,host.toLowerCase()+'-'+processes.length+'.log'));
   const child=spawn('dotnet',[prepared.projects[host]],{cwd:prepared.appRoot,env:{...env,...overrides,ASPNETCORE_URLS:url,Kestrel__Endpoints__Http__Url:url},stdio:'pipe',windowsHide:true});
   const hostFailure=watchImportRecoveryProcess(child,stream);const hostSignal=signal?AbortSignal.any([signal,hostFailure]):hostFailure;
   const item={child,stream,pid:child.pid};processes.push(item);child.stdout.pipe(stream,{end:false});child.stderr.pipe(stream,{end:false});
   await waitForApi(url,180000,stream.path,{signal:hostSignal});hostSignal.throwIfAborted();assert.equal(child.exitCode,null);report.hosts.push({host,pid:child.pid});return {...item,url};
  };
  const api=await start('Api');
  const login=await fetch(api.url+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json',Origin:'http://localhost'},body:JSON.stringify({username:'admin',password:'FullNet!2026Secure'}),signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  assert.equal(login.status,200);const session=await login.json();assert(typeof session.accessToken==='string'&&session.accessToken.trim());
  const workbook=(mode,bytes,rows)=>{
   const result=spawnSync(process.platform==='win32'?'python':'python3',['-X','utf8',join(repoRoot,'tests/templates/support/import-recovery-workbook.py')],{input:JSON.stringify({mode,workbook:Buffer.from(bytes).toString('base64'),rows}),maxBuffer:4*1024*1024,timeout:15000,windowsHide:true});
   assert.equal(result.status,0,'real workbook verification failed');return mode==='read'?JSON.parse(result.stdout):result.stdout;
  };
  report.business=await verifyApplicationImportRecovery(api.url,{hostAccessToken:session.accessToken,logPath:join(out,'business.json'),signal,fillWorkbook:(bytes,rows)=>workbook('fill',bytes,rows),inspectReceipt:bytes=>workbook('read',bytes),
   startWorker:(mode,taskId)=>start('Worker',{FullNet__ImportExport__ExecutionEnabled:'true',FULLNET_PROBE_DIRECTORY:out,FULLNET_PROBE_MODE:mode,FULLNET_PROBE_TASK:taskId}),
   killWorker:async item=>{assert.equal(item.child.exitCode,null);assert(item.child.kill('SIGKILL'));await stopLoggedProcess(item.child,item.stream);return{pid:item.pid,forced:true,closed:true,exitCode:item.child.exitCode,signalCode:item.child.signalCode};},
   stopWorker:item=>stopLoggedProcess(item.child,item.stream),
   waitProbe:async(mode,pid,taskId)=>{const file=join(out,mode+'.json'),deadline=Date.now()+150000;while(!existsSync(file)){assert(Date.now()<deadline,'checkpoint probe timeout '+mode);await sleep(100,undefined,{signal});}const probe=JSON.parse(readFileSync(file));assert.equal(probe.pid,pid);assert.equal(probe.taskId,taskId);return probe;}});
  assert.equal(api.child.exitCode,null);guard();report.completed=true;return report;
 }catch(error){failure=error;report.error=error.message;report.commandFailure={pid:error.pid,cleanupUnconfirmed:error.cleanupUnconfirmed===true};throw error;}
 finally{
  const errors=[];for(const item of processes.reverse()){try{await stopLoggedProcess(item.child,item.stream);}catch(error){errors.push(error);}}
  for(const container of [database,redis]){try{await container?.stop();}catch(error){errors.push(error);}}
  report.cleanupSucceeded=errors.length===0&&failure?.cleanupUnconfirmed!==true;report.processesClosed=failure?.cleanupUnconfirmed!==true&&processes.every(x=>x.child.exitCode!==null||x.child.signalCode!==null);
  writeFileSync(join(out,'result.json'),JSON.stringify(report,null,2));console.log('Import recovery evidence: '+out);
  if(errors.length&&!failure)throw new AggregateError(errors,'Import recovery cleanup failed');
 }
}
