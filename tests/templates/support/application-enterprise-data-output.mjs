import { randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';

class AcceptanceError extends Error {}
const ensure = (condition, message) => { if (!condition) throw new AcceptanceError(message); };

// 从应用自己的正式入口验证输出；凭据和查询值只留在内存，报告不保存响应正文。
export async function verifyEnterpriseDataOutputHttp(baseUrl, {hostAccessToken,tenantId,externalDataSource,loginHost,verifyWorkbook,logPath,signal,request=fetch}) {
 const evidence={completed:false,responses:[],reporting:{completed:false},printing:{completed:false}};
 let stage='configuration'; let token=hostAccessToken;
 const send=async(name,path,method='GET',body,status=200,accessToken=token,binary=false)=>{
  stage=name;
  const headers={Origin:'http://localhost'};
  if(accessToken) headers.Authorization='Bearer '+accessToken;
  if(body!==undefined) headers['Content-Type']='application/json';
  const response=await request(baseUrl+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),redirect:'error',
   signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15_000)]):AbortSignal.timeout(15_000)});
  evidence.responses.push({stage:name,status:response.status});
  ensure(response.status===status,name+': unexpected HTTP '+response.status);
  if(binary) {
   ensure(response.headers.get('Content-Type')?.split(';')[0]==='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',name+': invalid workbook content type');
   const bytes=new Uint8Array(await response.arrayBuffer());
   ensure(bytes.length>0 && bytes.length<=4*1024*1024,name+': invalid workbook size');
   return bytes;
  }
  try {return await response.json();} catch {throw new AcceptanceError(name+': invalid JSON response');}
 };
 try {
  ensure(typeof token==='string' && token.trim(),'Host session is required');
  ensure(typeof loginHost==='function' && typeof verifyWorkbook==='function','output verification callbacks are required');
  ensure(externalDataSource?.providerKey==='sql_server','reviewed SQL Server external source is required');
  const group=await send('group','/api/v1/reporting/groups','POST',{parentId:null,name:'Output acceptance',sortOrder:10,isEnabled:true},201);
  const source=await send('source','/api/v1/reporting/data-sources','POST',{...externalDataSource,tenantId:null,name:'Owned output fixture',isEnabled:true},201);
  const definition=await send('definition','/api/v1/reporting/definitions','POST',{groupId:group.id,dataSourceId:source.id,
   definitionKey:'output_'+randomUUID().replaceAll('-',''),name:'Output acceptance',description:null,
   queryPortKey:'reporting.database_engine_version',parameterSchema:[],layoutConfigJson:'{}',isEnabled:true},201);
  const root='/api/v1/reporting/definitions/'+definition.id;
  const first=await send('publish-first',root+'/publish','POST',{changeNote:'Frozen output',version:definition.version});
  ensure(first.definitionId===definition.id && first.versionNumber===1,'published version mismatch');
  const draft=await send('draft',root);
  const second=await send('publish-second',root+'/publish','POST',{changeNote:'Unassigned version',version:draft.version});
  ensure(second.versionNumber===2,'second published version mismatch');
  const grant=root+'/versions/1/tenant-grants/'+tenantId;
  ensure(await send('grant',grant,'PUT')===true,'version grant failed');
  const switched=await send('tenant-context','/api/v1/tenancy/context','PUT',{tenantId});
  ensure(switched.context?.tenantId===tenantId && typeof switched.accessToken==='string' && switched.accessToken.trim(),'tenant context mismatch');
  // 切换后不能复用旧 Host scope；另一个 Host 会话稍后撤销授权，不使当前 Tenant 会话失效。
  token=switched.accessToken;
  const catalog=await send('catalog','/api/v1/reporting/published-definitions');
  const visible=catalog.filter(item=>item.definitionId===definition.id);
  ensure(visible.length===1 && visible[0].versionNumber===1,'catalog version mismatch');
  ensure(!/"(?:passwordProtected|password|username|serverHost|connectionString)"/iu.test(JSON.stringify(catalog)),'catalog leaked connection configuration');
  await send('draft-denied','/api/v1/reporting/definitions','GET',undefined,403);
  const execution=await send('execute',root+'/execute','POST',{versionNumber:null,parameters:[]});
  ensure(execution.definitionId===definition.id && execution.versionNumber===1,'execution version mismatch');
  ensure(execution.columns?.length===1 && execution.columns[0].columnKey==='EngineVersion','execution columns mismatch');
  ensure(execution.rows?.length===1,'external query row missing');
  const engine=execution.rows[0].values?.EngineVersion;
  ensure(typeof engine==='string' && /^\d+\.\d+\.\d+(?:\.\d+)?$/u.test(engine),'external query row invalid');
  await send('ungranted-version-denied',root+'/execute','POST',{versionNumber:2,parameters:[]},403);
  await send('anonymous-execute-denied',root+'/execute','POST',{versionNumber:1,parameters:[]},401,null);
  const exported=await send('export','/api/v1/reporting/export-tasks','POST',{definitionId:definition.id,formatKey:'excel',versionNumber:null,parameters:[]},201);
  ensure(exported.definitionId===definition.id && exported.tenantId===tenantId && exported.versionNumber===1,'export tenant or version mismatch');
  // 当前正式创建入口优先同步执行；此证据不升级为 Worker 恢复或崩溃接管验收。
  ensure(exported.statusKey==='succeeded' && exported.rowCount===1,'export row or terminal state mismatch');
  const download='/api/v1/reporting/export-tasks/'+exported.id+'/download';
  const bytes=await send('download',download,'GET',undefined,200,token,true);
  stage='workbook';
  let workbook;
  try {workbook=await verifyWorkbook(bytes,engine);} catch {throw new AcceptanceError('workbook verification failed');}
  ensure(workbook?.worksheets===1 && workbook.dataRows===1,'workbook verification incomplete');
  evidence.reporting={completed:true,definitionId:definition.id,taskId:exported.id,versionNumber:1,rowCount:1,downloadBytes:bytes.length,downloadVerified:true};

  const available=await send('available','/api/v1/tenancy/available');
  const current=available.find(item=>item.id===tenantId);
  ensure(current && typeof current.name==='string' && typeof current.identifier==='string','trusted tenant profile missing');
  const template=await send('printing-create','/api/v1/printing/templates','POST',{templateKey:'output_'+randomUUID().replaceAll('-',''),name:'Tenant output card',
   formSchemaKey:'printing.tenant_profile_card',layoutHtml:'<article>{{tenantName}} / {{tenantCode}}</article><script>window.outputUnsafe=true</script>',isEnabled:true},201);
  const printRoot='/api/v1/printing/templates/'+template.id;
  const published=await send('printing-publish',printRoot+'/publish','POST',{changeNote:'Frozen tenant card',version:template.version});
  ensure(published.templateId===template.id && published.versionNumber===1,'printing published version mismatch');
  const preview=await send('printing-preview',printRoot+'/preview','POST',{versionNumber:1});
  ensure(preview.templateId===template.id && preview.versionNumber===1 && preview.formSchemaKey==='printing.tenant_profile_card','printing version mismatch');
  ensure(preview.boundFields?.tenantName===current.name && preview.boundFields.tenantCode===current.identifier,'printing tenant binding mismatch');
  const encode=value=>value.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#39;');
  ensure(typeof preview.html==='string' && preview.html.includes(encode(current.name)) && preview.html.includes(encode(current.identifier)), 'printing tenant HTML binding missing');
  ensure(!/<script\b|outputUnsafe|\{\{/iu.test(preview.html),'printing HTML rendering invalid');
  await send('anonymous-printing-denied',printRoot+'/preview','POST',{versionNumber:1},401,null);
  evidence.printing={completed:true,templateId:template.id,versionNumber:1,bindingVerified:true,scriptsRemoved:true};

  stage='revoker-login'; const revoker=await loginHost();
  ensure(typeof revoker==='string' && revoker.trim(),'fresh revoker Host session missing');
  ensure(await send('revoke',grant,'DELETE',undefined,200,revoker)===true,'version revoke failed');
  await send('revoked-download-denied',download,'GET',undefined,403);
  await send('revoked-execute-denied',root+'/execute','POST',{versionNumber:1,parameters:[]},403);
  await send('revoked-export-denied','/api/v1/reporting/export-tasks','POST',{definitionId:definition.id,formatKey:'excel',versionNumber:1,parameters:[]},403);
  const revokedCatalog=await send('revoked-catalog','/api/v1/reporting/published-definitions');
  ensure(!revokedCatalog.some(item=>item.definitionId===definition.id),'revoked definition remains in catalog');
  evidence.reporting.revokedAccessDenied=true;
  evidence.completed=true;return evidence;
 } catch(error) {
  // 网络、JSON 和验证器异常可能带任意响应值；只传播本工具拥有的固定诊断。
  evidence.error=error instanceof AcceptanceError?error.message:stage+': acceptance failed';
  throw new Error(evidence.error);
 } finally {writeFileSync(logPath,JSON.stringify(evidence,null,2));}
}
