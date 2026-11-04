import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {importSessionCookies} from './application-import-session-revocation.mjs';

// 只走正式 HTTP，验证拒绝请求无副作用以及 Cookie 会话控制保留原有安全检查。
export async function verifyApplicationIdentityCookieControl(baseUrl,{hostAccessToken,logPath,signal,request=fetch}) {
 const evidence={completed:false,responses:[],scenarios:[]},secrets=[hostAccessToken];
 const jar=importSessionCookies();
 const redact=value=>secrets.reduce((text,key)=>text.replaceAll(key,'[REDACTED]'),String(value));
 const send=async(stage,path,{method='GET',body,status=200,token,headers={},acceptCookies=false,code}={})=>{
  const response=await request(baseUrl+path,{method,headers:{Origin:'http://localhost',...(token?{Authorization:'Bearer '+token}:{}),...(body!==undefined?{'Content-Type':'application/json'}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body),redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
  evidence.responses.push({stage,status:response.status});assert.equal(response.status,status,stage+': HTTP '+response.status);
  const cookies=response.headers.getSetCookie();
  for(const cookie of cookies){const value=cookie.split(';')[0].split('=').slice(1).join('=');if(value)secrets.push(value);}
  if(code){assert.equal(cookies.length,0,stage+': rejection must not mutate cookies');}
  if(acceptCookies)jar.accept(response.headers);
  if(status===204)return {cookies};
  const result=await response.json();
  if(result.accessToken)secrets.push(result.accessToken);
  if(code){assert.equal(result.code,code,stage);assert.equal(result.status,status);assert(!/HostContextRequiredException|SqlException|SELECT |UPDATE /u.test(JSON.stringify(result)),stage+': internal details leaked');}
  return result;
 };
 try{
  // 默认同客户端单会话会撤销同账号旧族；用正式创建的另一账号验真独立族隔离。
  const username='cookie-isolation-'+randomUUID().replaceAll('-','');
  const independentUser=await send('create-independent-user','/api/v1/identity/users',{method:'POST',token:hostAccessToken,status:201,body:{username,displayName:'Cookie isolation',password:'FullNet!2026Secure'}});assert(independentUser.id);
  const independentLogin=await send('independent-login','/api/v1/auth/login',{method:'POST',body:{username,password:'FullNet!2026Secure'}});assert(independentLogin.accessToken);
  const independentBefore=await send('independent-session-before','/api/v1/me',{token:independentLogin.accessToken});assert.equal(independentBefore.id,independentUser.id);assert(independentBefore.sessionId);
  const login=await send('cookie-login','/api/v1/auth/login',{method:'POST',body:{username:'admin',password:'FullNet!2026Secure'},acceptCookies:true});assert(login.accessToken);
  const tenants=await send('available-tenants','/api/v1/tenancy/available',{token:login.accessToken});const tenant=tenants.find(item=>item.identifier==='local');assert(tenant);
  const context=await send('tenant-context','/api/v1/tenancy/context',{method:'PUT',body:{tenantId:tenant.id},token:login.accessToken,acceptCookies:true});assert(context.accessToken);
  const token=context.accessToken;
  const before=await send('session-before','/api/v1/me',{token});assert(before.sessionId);assert.equal(before.tenantId,tenant.id);
  for(const endpoint of ['login','refresh','logout']){
   await send('tenant-bearer-'+endpoint,'/api/v1/auth/'+endpoint,{method:'POST',token,headers:jar.logoutHeaders(),body:endpoint==='login'?{username:'admin',password:'FullNet!2026Secure'}:undefined,status:403,code:'authorization.permission_denied'});
   const after=await send('session-after-'+endpoint,'/api/v1/me',{token});assert.equal(after.sessionId,before.sessionId);assert.equal(after.tenantId,tenant.id);
   evidence.scenarios.push('tenant-bearer-'+endpoint+'-forbidden-without-session-change');
  }
  for(const endpoint of ['refresh','logout']){
   await send('bad-origin-'+endpoint,'/api/v1/auth/'+endpoint,{method:'POST',headers:{...jar.logoutHeaders(),Origin:'https://untrusted.invalid'},status:403,code:'identity.origin_not_allowed'});
   await send('bad-csrf-'+endpoint,'/api/v1/auth/'+endpoint,{method:'POST',headers:{...jar.logoutHeaders(),'X-CSRF-Token':'incorrect'},status:403,code:'identity.csrf_validation_failed'});
   const after=await send('session-after-security-'+endpoint,'/api/v1/me',{token});assert.equal(after.sessionId,before.sessionId);
   evidence.scenarios.push(endpoint+'-origin-and-csrf-preserved');
  }
  const previousCookies=jar.logoutHeaders().Cookie;
  const refresh=await send('cookie-refresh','/api/v1/auth/refresh',{method:'POST',headers:jar.logoutHeaders(),acceptCookies:true});assert(refresh.accessToken);assert.notEqual(refresh.accessToken,token);assert.notEqual(jar.logoutHeaders().Cookie,previousCookies);
  const renewed=await send('renewed-tenant-session','/api/v1/me',{token:refresh.accessToken});assert.equal(renewed.tenantId,tenant.id);assert.notEqual(renewed.sessionId,before.sessionId);
  await send('old-access-token-rejected','/api/v1/me',{token,status:401});evidence.scenarios.push('cookie-refresh-rotates-session-and-preserves-tenant');
  const logoutHeaders=jar.logoutHeaders();
  const logout=await send('cookie-logout','/api/v1/auth/logout',{method:'POST',headers:logoutHeaders,acceptCookies:true,status:204});assert(logout.cookies.some(cookie=>cookie.startsWith('fullnet-csrf=;')));
  await send('revoked-access-token','/api/v1/me',{token:refresh.accessToken,status:401});
  await send('repeated-cookie-logout','/api/v1/auth/logout',{method:'POST',headers:logoutHeaders,status:204});
  const independentAfter=await send('independent-host-session','/api/v1/me',{token:independentLogin.accessToken});assert.equal(independentAfter.id,independentUser.id);assert.equal(independentAfter.sessionId,independentBefore.sessionId);evidence.scenarios.push('cookie-logout-idempotent-and-family-isolated');
  evidence.completed=true;return evidence;
 }catch(error){evidence.error=redact(error.message??error);throw new Error(evidence.error);}
 finally{writeFileSync(logPath,redact(JSON.stringify(evidence,null,2)));}
}
