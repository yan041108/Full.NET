import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import * as flows from '../tests/support/identity-oidc-fixtures.mjs';

// 运行真实 RP 夹具脚本的回调分支，覆盖 state 拒绝逻辑；不模拟浏览器协议交互。
for (const client of ['oidc-rp-a', 'oidc-rp-b']) {
  for (const [name, storedState, returnedState, accepted] of [
    ['匹配 state', 'expected', 'expected', true],
    ['篡改 state', 'expected', 'tampered', false],
    ['缺少回调 state', 'expected', null, false],
    ['没有发起过授权', null, null, false]
  ]) {
    test(`${client} 回调：${name}`, () => {
      const storage = new Map(storedState === null ? [] : [['oidc.pkce.state', storedState]]);
      const status = { dataset: {}, textContent: '' };
      const query = new URLSearchParams({ code: 'fresh-code' });
      if (returnedState !== null) query.set('state', returnedState);
      runInNewContext(readFileSync(new URL(`../fixtures/${client}/app.js`, import.meta.url), 'utf8'), {
        URLSearchParams,
        window: { OIDC_RP_CONFIG: {}, location: { search: `?${query}` } },
        document: { getElementById: id => id === 'status' ? status : null },
        sessionStorage: {
          getItem: key => storage.get(key) ?? null,
          setItem: (key, value) => storage.set(key, value)
        }
      });
      assert.equal(status.dataset.testid, accepted ? 'oidc-authorized' : 'oidc-state-mismatch');
      assert.equal(storage.get('oidc.auth.code'), accepted ? 'fresh-code' : undefined);
    });
  }
}

for (const returnedState of [null, 'tampered-state']) {
  test(`已有中心会话的授权响应拒绝异常 state：${returnedState ?? '缺失'}`, async () => {
    const callback = new URL('http://localhost:5173/?code=fresh-code');
    if (returnedState !== null) callback.searchParams.set('state', returnedState);
    await assert.rejects(() => flows.requestAuthorizationCodeViaRequest({
      async get() {
        return { status: () => 302, headers: () => ({ location: callback.href }) };
      },
      async post() {
        assert.fail('不可信授权响应不得进入换票');
      }
    }, {
      apiBase: 'http://localhost:5149', clientId: 'e2e-client',
      redirectUri: 'http://localhost:5173/'
    }));
  });
}

test('负向 PKCE 探针领取尚未兑换的授权码', async () => {
  let authorizeUrl;
  const request = {
    async get(url) {
      authorizeUrl = new URL(url);
      const callback = new URL('http://localhost:5173/');
      callback.searchParams.set('code', 'fresh-code');
      callback.searchParams.set('state', authorizeUrl.searchParams.get('state'));
      return { status: () => 302, headers: () => ({ location: callback.href }) };
    },
    async post() {
      assert.fail('领取阶段不得提前兑换授权码');
    }
  };
  const pending = await flows.requestAuthorizationCodeViaRequest(request, {
    apiBase: 'http://localhost:5149',
    clientId: 'e2e-client',
    redirectUri: 'http://localhost:5173/'
  });
  assert.equal(pending.code, 'fresh-code');
  assert.equal(pending.token, undefined);
  assert.equal(createHash('sha256').update(pending.verifier).digest('base64url'),
    authorizeUrl.searchParams.get('code_challenge'));
});

test('完整授权流在登录后仅兑换一次并保留机密客户端凭据', async () => {
  const posts = [];
  const request = {
    async get() {
      return { status: () => 200, text: async () =>
        '<input name="__RequestVerificationToken" value="csrf&amp;token">' };
    },
    async post(url, options) {
      const form = new URLSearchParams(options.data);
      posts.push({ url, form });
      if (url.endsWith('/connect/authorize')) {
        assert.equal(form.get('__RequestVerificationToken'), 'csrf&token');
        return { status: () => 302, headers: () => ({
          location: `http://localhost:5173/?code=fresh-code&state=${form.get('state')}`
        }) };
      }
      return { ok: () => true, json: async () => ({ access_token: 'token' }) };
    }
  };
  const flow = await flows.runAuthorizationCodeFlowViaRequest(request, {
    apiBase: 'http://localhost:5149', clientId: 'client',
    redirectUri: 'http://localhost:5173/', clientSecret: 'test-secret',
    username: 'test-user', password: 'test-password'
  });
  assert.equal(posts.length, 2);
  assert.equal(posts[1].url, 'http://localhost:5149/connect/token');
  assert.equal(posts[1].form.get('code'), flow.code);
  assert.equal(posts[1].form.get('code_verifier'), flow.verifier);
  assert.equal(posts[1].form.get('client_secret'), 'test-secret');
  assert.equal(flow.token.access_token, 'token');
});

test('错误 verifier 探针拒绝把服务端故障视为协议拒绝', async () => {
  await assert.rejects(() => flows.expectTokenEndpointRejectsWrongVerifier({
    async post() {
      return { status: () => 500, json: async () => ({ error: 'server_error' }) };
    }
  }, {
    apiBase: 'http://localhost:5149', client: flows.OIDC_CLIENT_A,
    code: 'fresh-code', verifier: flows.createPkcePair().verifier
  }));
});
