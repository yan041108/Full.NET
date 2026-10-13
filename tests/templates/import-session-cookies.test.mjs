import test from 'node:test';
import assert from 'node:assert/strict';
import {importSessionCookies} from './support/application-import-session-revocation.mjs';

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
