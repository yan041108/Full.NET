import test from 'node:test';
import assert from 'node:assert/strict';
import {importSessionCookies} from './support/application-import-session-revocation.mjs';
import {verifyApplicationImportSessionRevocation} from './support/application-import-session-revocation.mjs';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

test('退出必须使用切租户后轮换的刷新 Cookie 和 CSRF，不能只发送 Bearer',()=>{
 const jar=importSessionCookies();
 assert.throws(()=>jar.logoutHeaders(),/session cookies/);
 jar.accept(new Headers({'set-cookie':'fullnet-refresh=old; Path=/'}));
 assert.throws(()=>jar.logoutHeaders(),/session cookies/);
 const headers=new Headers();headers.append('set-cookie','fullnet-refresh=new; HttpOnly; Path=/');headers.append('set-cookie','fullnet-csrf=csrf; Path=/');
 jar.accept(headers);
 assert.deepEqual(jar.logoutHeaders(),{Cookie:'fullnet-refresh=new; fullnet-csrf=csrf','X-CSRF-Token':'csrf'});
 assert(!jar.logoutHeaders().Cookie.includes('old'));
});

test('删除后的 Cookie 不得继续用于退出或写入证据',()=>{
 const jar=importSessionCookies();const headers=new Headers();headers.append('set-cookie','fullnet-refresh=secret; Path=/');headers.append('set-cookie','fullnet-csrf=csrf; Path=/');jar.accept(headers);
 jar.accept(new Headers({'set-cookie':'fullnet-refresh=; expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/'}));
 assert.throws(()=>jar.logoutHeaders(),/session cookies/);
 assert.equal(JSON.stringify(jar),'{}');
});

test('默认安全 Cookie 配置必须发送实际 __Host 刷新会话',()=>{
 const jar=importSessionCookies();const headers=new Headers();headers.append('set-cookie','__Host-fullnet-refresh=secure; Secure; HttpOnly; Path=/');headers.append('set-cookie','fullnet-csrf=csrf; Secure; Path=/');jar.accept(headers);
 assert.deepEqual(jar.logoutHeaders(),{Cookie:'__Host-fullnet-refresh=secure; fullnet-csrf=csrf','X-CSRF-Token':'csrf'});
});

test('实际消费者登出只发轮换后的 Cookie/CSRF，旧令牌探针仍带 Bearer',async()=>{
 const out=mkdtempSync(join(tmpdir(),'import-session-protocol-'));let logins=0,checked=false;
 const tenant='01980000-0000-7000-8000-000000000010',id='01980000-0000-7000-8000-000000000011';
 const json=body=>new Response(JSON.stringify(body),{status:200,headers:{'Content-Type':'application/json'}});
 const session=(accessToken,cookie)=>{const headers=new Headers({'Content-Type':'application/json'});headers.append('set-cookie','__Host-fullnet-refresh='+cookie+'; Path=/');headers.append('set-cookie','fullnet-csrf='+cookie+'-csrf; Path=/');return new Response(JSON.stringify({accessToken}),{headers});};
 const request=async(url,options)=>{
  const path=new URL(url).pathname;
  if(path==='/api/v1/auth/login'){if(++logins===3)throw new Error('consumer-protocol-checked');return session('test-host-access','initial');}
  if(path==='/api/v1/tenancy/available')return json([{identifier:'local',id:tenant}]);
  if(path==='/api/v1/tenancy/context')return session('test-tenant-access','rotated');
  if(path.endsWith('/template'))return new Response(new Uint8Array([80,75]));
  if(path==='/api/v1/import-export/tasks')return new Response(JSON.stringify({id,tenantId:tenant,validRowCount:3,invalidRowCount:0}),{status:201});
  if(path.endsWith('/execute'))return json({statusKey:'queued',nextLineNumber:0,processedRowCount:0,succeededRowCount:0,executionFailedRowCount:0,hasErrorReceipt:false});
  if(path==='/api/v1/auth/logout'){
   assert.equal(options.headers.Authorization,undefined,'anonymous cookie logout must not inherit tenant Bearer');
   assert.match(options.headers.Cookie,/__Host-fullnet-refresh=rotated/u);assert.equal(options.headers['X-CSRF-Token'],'rotated-csrf');return new Response(null,{status:204});
  }
  if(path==='/api/v1/me'){assert.equal(options.headers.Authorization,'Bearer test-tenant-access');checked=true;return new Response('{}',{status:401});}
  throw new Error('Unexpected protocol request '+path);
 };
 try{await assert.rejects(verifyApplicationImportSessionRevocation('http://localhost',{request,fillWorkbook:()=>new Uint8Array([80,75]),logPath:join(out,'business.json')}),/consumer-protocol-checked/u);assert(checked);}
 finally{rmSync(out,{recursive:true,force:true});}
});
