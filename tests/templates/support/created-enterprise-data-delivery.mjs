import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createWriteStream, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildAppTemplate } from '../../../scripts/templates/build-app-template.mjs';
import { createApp } from '../../../scripts/templates/create-app.mjs';
import { startDatabaseContainer, startRedisContainer, buildSharedEnv, runDotnet } from './created-app-real-stack.mjs';
import { verifyEnterpriseDataDeliveryHttp } from './application-enterprise-data-delivery.mjs';
import { verifyReportingGrantManagementHttp } from './application-reporting-grants.mjs';
import { verifyEnterpriseDataOutputHttp } from './application-enterprise-data-output.mjs';
import { verifyApplicationPrintingBrowser } from './application-printing-browser.mjs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';
import { waitForApi } from '../../e2e/admin-real-stack/scripts/wait-for-api.mjs';

const repoRoot = resolve(fileURLToPath(new URL('../../../', import.meta.url)));

async function freePort() {
 const server = createServer();
 await new Promise((resolve, reject) => { server.once('error',reject); server.listen(0,'127.0.0.1',resolve); });
 const port = server.address().port;
 await new Promise((resolve,reject) => server.close(error => error ? reject(error) : resolve()));
 return port;
}

// 使用应用拥有的 DLL、配置和绝对文件根；直接启动 DLL，避免 dotnet run 的孙进程残留。
export async function verifyCreatedEnterpriseDataDelivery(provider, { signal } = {}) {
 assert.ok(['sqlserver','mysql'].includes(provider));
 const reportParent = join(repoRoot,'.tmp/template-real-stack/enterprise-delivery',provider);
 mkdirSync(reportParent,{recursive:true});
 const root = mkdtempSync(join(reportParent,'run-'));
 const processes = [];
 let database;
 let externalDatabase;
 let redis;
 let failure;
 const report = { completed:false, provider, hosts:[] };
 try {
  signal?.throwIfAborted();
  const { templateRoot } = buildAppTemplate({ output:join(root,'package') });
  const appRoot = join(root,'app');
  createApp({packageRoot:templateRoot,output:appRoot,name:'EnterpriseDelivery',ownerKey:'delivery',database:provider,preset:'enterprise',httpPort:await freePort()});
  const manifest = JSON.parse(readFileSync(join(appRoot,'framework-manifest.json'),'utf8'));
  assert.equal(manifest.projectedPreset,'enterprise');
  for (const name of ['244_DemoEnterpriseRequestImportReceipt.sql','245_ReportingTenantGrant.sql','246_PrintingTenantGrant.sql']) {
   const script = manifest.migrationInventory.scripts.find(item => item.name === name); assert.ok(script,name);
   assert.deepEqual(Object.keys(script.providers).sort(),['MySql','SqlServer']);
  }
  report.sourceCommit = manifest.sourceCommit;
  report.applicationRoot = appRoot;
  const databaseStack = await startDatabaseContainer(provider); database = databaseStack.container;
  // 两种主库都使用测试自有的 SQL Server 外部只读驱动；MySQL TLS 未验收，不能在此降级。
  const externalStack = provider === 'sqlserver' ? databaseStack : await startDatabaseContainer('sqlserver');
  if (provider !== 'sqlserver') externalDatabase = externalStack.container;
  const externalSource = {providerKey:'sql_server',serverHost:externalStack.container.getHost(),port:externalStack.container.getMappedPort(1433),
   databaseName:'master',username:externalStack.connectionString.match(/(?:^|;)User Id=([^;]+)/iu)?.[1],
   password:externalStack.connectionString.match(/(?:^|;)Password=([^;]+)/iu)?.[1],trustServerCertificate:true};
  assert.ok(externalSource.username && externalSource.password,'owned external credentials missing');
  const redisStack = await startRedisContainer(); redis = redisStack.container;
  const browserPort = await freePort();
  const env = { ...buildSharedEnv(databaseStack.connectionString,databaseStack.databaseProvider,redisStack.connectionString),
   Identity__AllowedOrigins__3:'http://localhost:'+browserPort,
   Files__Local__RootPath:join(root,'files'), FullNet__ImportExport__RunSynchronously:'false', FullNet__ImportExport__ExecutionEnabled:'false',
   FullNet__ImportExport__PollSeconds:'5', FullNet__ImportExport__BatchSize:'1',
   // 保留真实导入 Worker，但禁用报表恢复循环，避免与请求内导出争抢并误归因执行宿主。
   FullNet__Reporting__Export__ExecutionEnabled:'false',
   FullNet__ExternalDatabaseAccess__AllowedDestinations__0__Provider:'SqlServer',
   FullNet__ExternalDatabaseAccess__AllowedDestinations__0__Host:externalSource.serverHost,
   FullNet__ExternalDatabaseAccess__AllowedDestinations__0__Port:String(externalSource.port),
   // 此豁免仅针对本次随机容器的自签名证书，不改变生成应用的生产默认值。
   FullNet__ExternalDatabaseAccess__AllowedDestinations__0__AllowUntrustedCertificate:'true' };
  const profile = JSON.parse(readFileSync(join(appRoot,'fullnet-app.json'),'utf8'));
  const apiUrl = `http://127.0.0.1:${profile.httpPort}`;
  const workerUrl = `http://127.0.0.1:${await freePort()}`;
  const projects = {};
  for (const host of ['Api','Worker','Migrator']) {
   signal?.throwIfAborted();
   const project = join(appRoot,`src/EnterpriseDelivery.Host.${host}/EnterpriseDelivery.Host.${host}.csproj`);
   runDotnet(['build',project,'-c','Release','--nologo','-v','quiet'],appRoot,env,300_000,join(root,host.toLowerCase()+'-build.log'));
   projects[host] = join(appRoot,`src/EnterpriseDelivery.Host.${host}/bin/Release/net10.0/EnterpriseDelivery.Host.${host}.dll`);
  }
  signal?.throwIfAborted();
  runDotnet([projects.Migrator,'--seed','development'],appRoot,env,600_000,join(root,'migrator.log'));
  signal?.throwIfAborted();
  runDotnet([projects.Migrator],appRoot,env,600_000,join(root,'migrator-repeat.log'));
  report.hostsBuilt = 3; report.migratorRepeated = true;
  const start = async (host,url,overrides) => {
   const logPath = join(root,host.toLowerCase()+'.log'); const stream = createWriteStream(logPath);
   const child = spawn('dotnet',[projects[host]],{cwd:appRoot,env:{...process.env,...env,...overrides,ASPNETCORE_URLS:url,Kestrel__Endpoints__Http__Url:url},stdio:'pipe',windowsHide:true});
   processes.push({child,stream}); child.stdout.pipe(stream,{end:false}); child.stderr.pipe(stream,{end:false});
   await waitForApi(url,180_000,logPath,{signal});
   const ready = await fetch(url+'/health/ready',{signal:signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000)}); assert.equal(ready.status,200,host+' readiness');
   assert.equal(child.exitCode,null,host+' exited');
   assert.ok(Number.isInteger(child.pid));
   assert.ok(!report.hosts.some(item => item.pid === child.pid),'hosts must be independent processes');
   report.hosts.push({host,pid:child.pid,ready:true});
   return child;
  };
  await start('Api',apiUrl,{});
  const login = await fetch(apiUrl+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json',Origin:'http://localhost'},
   body:JSON.stringify({username:'admin',password:'FullNet!2026Secure'}),redirect:'error',signal:signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000)});
  assert.equal(login.status,200,'login HTTP '+login.status);
  let credentials;
  try { credentials = await login.json(); } catch { throw new Error('Login JSON invalid'); }
  assert.ok(typeof credentials.accessToken === 'string' && credentials.accessToken.trim(),'Host session missing');
  const fillWorkbook = (template,values) => {
   const python = process.platform === 'win32' ? 'python' : 'python3';
   const result = spawnSync(python,['-X','utf8',join(repoRoot,'tests/templates/support/fill-delivery-workbook.py')],{
    cwd:root,input:JSON.stringify({template:Buffer.from(template).toString('base64'),values}),maxBuffer:4*1024*1024,timeout:15_000,windowsHide:true});
   assert.equal(result.status,0,'official template fill failed'); return result.stdout;
  };
  const business = await verifyEnterpriseDataDeliveryHttp(apiUrl,{hostAccessToken:credentials.accessToken,fillWorkbook,
   logPath:join(root,'business.json'),signal,startWorker:() => start('Worker',workerUrl,{FullNet__ImportExport__ExecutionEnabled:'true'})});
  report.business = business;
  // 前序业务已切到 Tenant，重新取得 Host 会话，不能复用失效的初始 scope 令牌。
  const hostLogin = await fetch(apiUrl+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json',Origin:'http://localhost'},
   body:JSON.stringify({username:'admin',password:'FullNet!2026Secure'}),redirect:'error',signal:signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000)});
  assert.equal(hostLogin.status,200,'reporting Host login HTTP '+hostLogin.status);
  const hostSession = await hostLogin.json();assert.ok(typeof hostSession.accessToken === 'string' && hostSession.accessToken.trim());
  report.reportingGrants = await verifyReportingGrantManagementHttp(apiUrl,{hostAccessToken:hostSession.accessToken,tenantId:business.tenantId,signal});
  const loginHost = async (credentials={username:'admin',password:'FullNet!2026Secure'}) => {
   const response = await fetch(apiUrl+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json',Origin:'http://localhost'},
    body:JSON.stringify(credentials),redirect:'error',signal:signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000)});
   assert.equal(response.status,200,'output Host login HTTP '+response.status);
   let session; try { session = await response.json(); } catch { throw new Error('Output Host login JSON invalid'); }
   assert.ok(typeof session.accessToken === 'string' && session.accessToken.trim(),'output Host session missing');
   const csrfCookie=response.headers.getSetCookie().find(cookie=>cookie.startsWith('fullnet-csrf='));
   const csrfToken=csrfCookie?.split(';',1)[0].slice('fullnet-csrf='.length);
   assert.ok(typeof csrfToken==='string' && /^[A-Za-z0-9_-]{1,256}$/u.test(csrfToken),'output Host CSRF cookie missing');
   return {accessToken:session.accessToken,csrfToken};
  };
  const verifyWorkbook = (bytes,expectedValue) => {
   const python = process.platform === 'win32' ? 'python' : 'python3';
   const verified = spawnSync(python,['-X','utf8',join(repoRoot,'tests/templates/support/verify-reporting-workbook.py')],{
    cwd:root,input:JSON.stringify({workbook:Buffer.from(bytes).toString('base64'),expectedValue}),encoding:'utf8',maxBuffer:4*1024*1024,timeout:15_000,windowsHide:true});
   assert.equal(verified.status,0,'reporting workbook verification failed');
   return JSON.parse(verified.stdout);
  };
  report.dataOutput = await verifyEnterpriseDataOutputHttp(apiUrl,{hostAccessToken:(await loginHost()).accessToken,tenantId:business.tenantId,
   externalDataSource:externalSource,loginHost,verifyWorkbook,logPath:join(root,'data-output.json'),signal,businessRecordId:business.requestId,
   verifyPrintingBrowser:fixture=>verifyApplicationPrintingBrowser(appRoot,apiUrl,root,{port:browserPort,fixture,signal})});
  for (const {child} of processes) { assert.equal(child.exitCode,null,'host exited during acceptance'); assert.equal(child.signalCode,null); }
  report.completed = true;
  return report;
 } catch (error) {
  // HTTP 验收器不传播签发响应或错误正文，凭据只留在进程内存。
  failure = error; report.error = error instanceof Error ? error.message : String(error); throw error;
 } finally {
  const errors = [];
  for (const {child,stream} of processes.reverse()) { try { await stopLoggedProcess(child,stream); } catch(error) { errors.push(error); } }
  for (const container of [externalDatabase,database,redis]) { try { await container?.stop(); } catch(error) { errors.push(error); } }
  report.cleanupSucceeded = errors.length === 0;
  writeFileSync(join(root,'result.json'),JSON.stringify(report,null,2));
  // 保留本次独立应用与失败证据，既不删除其他工作区，也不触碰共享命名容器。
  console.log('Enterprise delivery evidence: '+root);
  if (errors.length && !failure) throw new AggregateError(errors,'Enterprise acceptance resource cleanup failed');
 }
}
