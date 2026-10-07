import { randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';

class AcceptanceError extends Error {}
const ensure = (condition, message) => { if (!condition) throw new AcceptanceError(message); };
const identifier = value => ensure(typeof value==='string' && /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(value),'response identifier invalid');

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
  identifier(tenantId);
  ensure(typeof loginHost==='function' && typeof verifyWorkbook==='function','output verification callbacks are required');
  ensure(externalDataSource?.providerKey==='sql_server','reviewed SQL Server external source is required');
  const group=await send('group','/api/v1/reporting/groups','POST',{parentId:null,name:'Output acceptance',sortOrder:10,isEnabled:true},201);
  identifier(group.id);
  const source=await send('source','/api/v1/reporting/data-sources','POST',{...externalDataSource,tenantId:null,name:'Owned output fixture',isEnabled:true},201);
  identifier(source.id);
  const definition=await send('definition','/api/v1/reporting/definitions','POST',{groupId:group.id,dataSourceId:source.id,
   definitionKey:'output_'+randomUUID().replaceAll('-',''),name:'Output acceptance',description:null,
   queryPortKey:'reporting.database_engine_version',parameterSchema:[],layoutConfigJson:'{}',isEnabled:true},201);
  identifier(definition.id);
  const root='/api/v1/reporting/definitions/'+definition.id;
  const first=await send('publish-first',root+'/publish','POST',{changeNote:'Frozen output',version:definition.version});
  ensure(first.definitionId===definition.id && first.versionNumber===1,'published version mismatch');
  const draft=await send('draft',root);
  const second=await send('publish-second',root+'/publish','POST',{changeNote:'Unassigned version',version:draft.version});
  ensure(second.versionNumber===2,'second published version mismatch');
  const grant=root+'/versions/1/tenant-grants/'+tenantId;
  ensure(await send('grant',grant,'PUT')===true,'version grant failed');

  // 模板管理和预览当前均为 HostOnly；先核对 Host 发布，再记录 Tenant 拒绝，不伪造租户绑定成功。
  const template=await send('printing-create','/api/v1/printing/templates','POST',{templateKey:'output_'+randomUUID().replaceAll('-',''),name:'Tenant output card',
   formSchemaKey:'printing.tenant_profile_card',layoutHtml:'<article>{{tenantName}} / {{tenantCode}}</article><script>window.outputUnsafe=true</script>',isEnabled:true},201);
  identifier(template.id);
  const printRoot='/api/v1/printing/templates/'+template.id;
  const published=await send('printing-publish',printRoot+'/publish','POST',{changeNote:'Frozen tenant card',version:template.version});
  ensure(published.templateId===template.id && published.versionNumber===1,'printing published version mismatch');
  ensure(typeof published.layoutHtml==='string' && !/<script\b|outputUnsafe/iu.test(published.layoutHtml),'printing published HTML invalid');
  evidence.printing={status:'tenant-preview-not-supported',templateId:template.id,versionNumber:1,hostPublished:true,scriptsRemovedAtPublish:true};
  // 第一方 ClientId 由服务端固定，默认每客户端单会话；另建最小权限用户，避免重新登录同一 admin 撤销租户会话。
  const revokerCredentials={username:'revoke_'+randomUUID().replaceAll('-',''),password:'Init!'+randomUUID()+'A9'};
  const revokerRole=await send('revoker-role','/api/v1/identity/roles','POST',{code:'revoke_'+randomUUID().replaceAll('-',''),name:'Owned output revoker'},201);
  identifier(revokerRole.id);
  const permissions=['reporting.definitions.grant_tenants'];
  const assigned=await send('revoker-permissions','/api/v1/identity/roles/'+revokerRole.id+'/permissions','PUT',{permissionCodes:permissions,version:revokerRole.version});
  ensure(assigned.permissionCodes?.length===1 && assigned.permissionCodes[0]===permissions[0],'revoker permissions mismatch');
  const revokerUser=await send('revoker-user','/api/v1/identity/users','POST',{...revokerCredentials,displayName:'Owned output revoker'},201);
  identifier(revokerUser.id);
  const rolePath='/api/v1/identity/users/'+revokerUser.id+'/roles';
  const roleSnapshot=await send('revoker-role-snapshot',rolePath);
  const roles=await send('revoker-assign-role',rolePath,'PUT',{roleIds:[revokerRole.id],version:roleSnapshot.version});
  ensure(roles.userId===revokerUser.id && roles.roleIds?.length===1 && roles.roleIds[0]===revokerRole.id,'revoker role binding mismatch');
  stage='revoker-login'; let revoker=await loginHost(revokerCredentials);
  ensure(typeof revoker==='string' && revoker.trim(),'fresh revoker Host session missing');
  // 管理员创建的账号必须走正式自助改密，再使用轮换后的 Host access token。
  const changed=await send('revoker-password','/api/v1/me/password','POST',{currentPassword:revokerCredentials.password,newPassword:'Changed!'+randomUUID()+'A9'},200,revoker);
  ensure(typeof changed.accessToken==='string' && changed.accessToken.trim(),'revoker password rotation session missing');
  revoker=changed.accessToken;
  const switched=await send('tenant-context','/api/v1/tenancy/context','PUT',{tenantId});
  ensure(switched.context?.tenantId===tenantId && typeof switched.accessToken==='string' && switched.accessToken.trim(),'tenant context mismatch');
  // 切换后不能复用旧 Host scope；不同用户的 Host 会话稍后撤销授权，不使当前 Tenant 会话失效。
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
  identifier(exported.id);
  ensure(exported.definitionId===definition.id && exported.versionNumber===1,'export definition or version mismatch');
  // 当前正式创建入口优先同步执行；此证据不升级为 Worker 恢复或崩溃接管验收。
  ensure(exported.statusKey==='succeeded' && exported.rowCount===1,'export row or terminal state mismatch');
  // 导出 DTO 不公开 TenantId；在已验证的可信 Tenant scope 读回任务，不扩展公共响应字段。
  const detail=await send('export-read','/api/v1/reporting/export-tasks/'+exported.id);
  ensure(detail.id===exported.id && detail.definitionId===definition.id && detail.versionNumber===1,'export task identity mismatch');
  ensure(detail.statusKey==='succeeded' && detail.rowCount===1,'export task state mismatch');
  const download='/api/v1/reporting/export-tasks/'+exported.id+'/download';
  const bytes=await send('download',download,'GET',undefined,200,token,true);
  stage='workbook';
  let workbook;
  try {workbook=await verifyWorkbook(bytes,engine);} catch {throw new AcceptanceError('workbook verification failed');}
  ensure(workbook?.worksheets===1 && workbook.dataRows===1,'workbook verification incomplete');
  evidence.reporting={completed:true,definitionId:definition.id,taskId:exported.id,versionNumber:1,rowCount:1,downloadBytes:bytes.length,downloadVerified:true};

  await send('tenant-printing-denied',printRoot+'/preview','POST',{versionNumber:1},403);
  await send('anonymous-printing-denied',printRoot+'/preview','POST',{versionNumber:1},401,null);
  evidence.printing.tenantPreviewDenied=true;

  ensure(await send('revoke',grant,'DELETE',undefined,200,revoker)===true,'version revoke failed');
  await send('revoked-download-denied',download,'GET',undefined,403);
  await send('revoked-execute-denied',root+'/execute','POST',{versionNumber:1,parameters:[]},403);
  await send('revoked-export-denied','/api/v1/reporting/export-tasks','POST',{definitionId:definition.id,formatKey:'excel',versionNumber:1,parameters:[]},403);
  const revokedCatalog=await send('revoked-catalog','/api/v1/reporting/published-definitions');
  ensure(!revokedCatalog.some(item=>item.definitionId===definition.id),'revoked definition remains in catalog');
  evidence.reporting.revokedAccessDenied=true;
  evidence.reporting.revokerDifferentUser=true;
  evidence.completed=true;return evidence;
 } catch(error) {
  // 网络、JSON 和验证器异常可能带任意响应值；只传播本工具拥有的固定诊断。
  evidence.error=error instanceof AcceptanceError?error.message:stage+': acceptance failed';
  throw new Error(evidence.error);
 } finally {writeFileSync(logPath,JSON.stringify(evidence,null,2));}
}
