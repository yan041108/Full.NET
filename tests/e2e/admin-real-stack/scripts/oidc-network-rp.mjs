import https from 'node:https';
import http from 'node:http';
import fs from 'node:fs';
import { randomBytes, timingSafeEqual } from 'node:crypto';
import * as oidc from 'openid-client';
import { createRemoteJWKSet, customFetch, jwtVerify } from 'jose';

// 此程序是独立测试 RP；使用成熟客户端校验协议，令牌仅保存在该进程内。
const config = JSON.parse(fs.readFileSync(process.env.FULLNET_INTEROP_CONFIG, 'utf8'));
const tls = { cert: fs.readFileSync(config.certificate), key: fs.readFileSync(config.privateKey) };
const pending = new Map();
const sessions = new Map();
const clients = new Map();
const receipts = new Map();
const cookies = request => Object.fromEntries((request.headers.cookie ?? '').split(';').map(item => item.trim().split('=')));
const opaque = () => randomBytes(32).toString('base64url');
const reply = (response, status, value) => {
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  response.end(JSON.stringify(value));
};
function permitted(request) {
  const supplied = Buffer.from(request.headers['x-fixture-key'] ?? '');
  const expected = Buffer.from(config.controlKey);
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
}
async function body(request) {
  let text = '';
  for await (const chunk of request) { text += chunk; if (text.length > 16_384) throw Error('body_limit'); }
  return text;
}
// 容器使用 Docker 的宿主地址；Windows 端以同一证书和逻辑 URL 走回环，避免依赖机器 hosts。
const loopbackFetch = (input, init) => {
  const target = new URL(input);
  if (target.hostname === 'host.docker.internal') target.hostname = '127.0.0.1';
  return fetch(target, init);
};
const proxy = https.createServer(tls, (request, response) => {
  const target = new URL(request.url, config.backend);
  const upstream = http.request(target, { method: request.method, headers: {
    ...request.headers, host: new URL(config.issuer).host, 'x-forwarded-host': new URL(config.issuer).host,
    'x-forwarded-proto': 'https', 'x-forwarded-for': '127.0.0.1'
  } }, incoming => {
    response.writeHead(incoming.statusCode, incoming.headers); incoming.pipe(response);
  });
  upstream.on('error', () => { if (!response.headersSent) reply(response, 502, { error: 'upstream_unavailable' }); else response.destroy(); });
  request.pipe(upstream);
  response.on('finish', () => {
    if (request.url.includes('.well-known')) fs.appendFileSync(config.protocolLog, `${request.method} ${new URL(request.url, config.issuer).pathname} ${response.statusCode}\n`);
  });
});
await new Promise(resolve => proxy.listen(config.opPort, '0.0.0.0', resolve));
// 官方套件仍是独立容器；这里只终止本轮 TLS 并转发，不生成或替代套件测试结果。
if (config.suiteTlsPort) {
  const suite = https.createServer(tls, (request, response) => {
    const upstream = http.request({ host: '127.0.0.1', port: config.suiteHttpPort, path: request.url, method: request.method,
      headers: { ...request.headers, host: `host.docker.internal:${config.suiteTlsPort}`,
        'x-forwarded-host': `host.docker.internal:${config.suiteTlsPort}`, 'x-forwarded-proto': 'https' } }, incoming => {
      response.writeHead(incoming.statusCode, incoming.headers); incoming.pipe(response);
    });
    upstream.on('error', () => { if (!response.headersSent) reply(response, 502, { error: 'suite_unavailable' }); else response.destroy(); });
    request.pipe(upstream);
  });
  await new Promise(resolve => suite.listen(config.suiteTlsPort, '0.0.0.0', resolve));
}

function findSession(request, client) {
  const handle = cookies(request)[`__Host-interop-${client}`];
  const session = sessions.get(handle);
  return session?.client === client ? session : undefined;
}
async function backchannel(client, request, response) {
  const current = clients.get(client);
  current.stats.received++;
  let raw, claims;
  try {
    raw = new URLSearchParams(await body(request)).get('logout_token');
    ({ payload: claims } = await jwtVerify(raw, current.jwks, { issuer: config.issuer,
      audience: current.id, algorithms: ['RS256'], typ: 'logout+jwt', maxTokenAge: '150s', clockTolerance: 30 }));
    const event = claims.events?.['http://schemas.openid.net/event/backchannel-logout'];
    if (!claims.jti || !Number.isInteger(claims.iat) || 'nonce' in claims || !event || typeof event !== 'object'
      || Array.isArray(event) || (!claims.sid && !claims.sub)) throw Error('logout_claims');
  } catch { current.stats.rejected++; return reply(response, 400, { error: 'invalid_logout_token' }); }
  if (current.fault === 'unavailable') return reply(response, 503, { error: 'receiver_unavailable' });
  const receipt = `${client}:${claims.jti}`;
  if (receipts.has(receipt)) current.stats.duplicates++;
  else {
    receipts.set(receipt, true); current.stats.applied++;
    for (const [handle, session] of sessions) {
      if (session.client === client && (!claims.sid || session.sid === claims.sid) && (!claims.sub || session.sub === claims.sub)) sessions.delete(handle);
    }
  }
  current.lastLogout = raw;
  if (current.fault === 'drop-ack-once') { current.fault = 'ok'; request.socket.destroy(); return; }
  reply(response, 200, {});
}
const rp = https.createServer(tls, async (request, response) => {
  try {
    const url = new URL(request.url, config.rpOrigin);
    if (url.pathname === '/ready') return reply(response, clients.size === 2 ? 200 : 503, { ready: clients.size === 2 });
    const [, client, operation] = url.pathname.split('/');
    const current = clients.get(client);
    if (!current) return reply(response, 404, {});
    if (operation === 'backchannel' && request.method === 'POST') return await backchannel(client, request, response);
    if (operation === 'login') {
      const correlation = opaque(), verifier = oidc.randomPKCECodeVerifier(), state = oidc.randomState(), nonce = oidc.randomNonce();
      pending.set(correlation, { client, verifier, state, nonce });
      const target = oidc.buildAuthorizationUrl(current.protocol, { redirect_uri: `${config.rpOrigin}/${client}/callback`,
        scope: 'openid profile offline_access', state, nonce, code_challenge: await oidc.calculatePKCECodeChallenge(verifier),
        code_challenge_method: 'S256', ...(url.searchParams.has('silent') ? { prompt: 'none' } : {}) });
      response.writeHead(302, { Location: target.href, 'Set-Cookie': `__Host-interop-corr-${client}=${correlation}; Secure; HttpOnly; SameSite=Lax; Path=/`, 'Cache-Control': 'no-store' });
      return response.end();
    }
    if (operation === 'callback') {
      const correlation = cookies(request)[`__Host-interop-corr-${client}`];
      const expected = pending.get(correlation); pending.delete(correlation);
      if (expected?.client !== client) return reply(response, 400, { error: 'correlation' });
      const tokens = await oidc.authorizationCodeGrant(current.protocol, url, {
        pkceCodeVerifier: expected.verifier, expectedState: expected.state, expectedNonce: expected.nonce, idTokenExpected: true
      });
      const claims = tokens.claims();
      if (!claims?.sid || !claims.sub) throw Error('missing_session_claims');
      const handle = opaque(); sessions.set(handle, { client, tokens, sub: claims.sub, sid: claims.sid });
      response.setHeader('Set-Cookie', [`__Host-interop-${client}=${handle}; Secure; HttpOnly; SameSite=Lax; Path=/`,
        `__Host-interop-corr-${client}=; Max-Age=0; Secure; HttpOnly; SameSite=Lax; Path=/`]);
      return reply(response, 200, { authenticated: true });
    }
    if (operation === 'metrics' || operation === 'fault' || operation === 'replay-foreign' || operation === 'refresh-revoked') {
      if (!permitted(request)) return reply(response, 403, {});
      if (operation === 'metrics') return reply(response, 200, current.stats);
      if (operation === 'refresh-revoked') {
        if (!current.lastLoggedOut) return reply(response, 409, {});
        try { await oidc.refreshTokenGrant(current.protocol, current.lastLoggedOut.refresh_token); }
        catch (error) { return reply(response, error.error === 'invalid_grant' ? 200 : 502, { rejected: error.error === 'invalid_grant' }); }
        return reply(response, 500, { error: 'revoked_refresh_accepted' });
      }
      if (operation === 'fault') {
        const value = JSON.parse(await body(request));
        if (!['ok', 'unavailable', 'drop-ack-once'].includes(value.mode)) return reply(response, 400, {});
        current.fault = value.mode; return reply(response, 200, {});
      }
      const other = client === 'a' ? 'b' : 'a';
      const result = await fetch(`${config.rpOrigin}/${other}/backchannel`, { method: 'POST', body: new URLSearchParams({ logout_token: current.lastLogout }) });
      return reply(response, 200, { status: result.status });
    }
    const session = findSession(request, client);
    if (!session) return reply(response, 401, { authenticated: false });
    if (operation === 'session') return reply(response, 200, { authenticated: true });
    if (operation === 'resource') {
      const result = await loopbackFetch(new URL('/api/v1/me', config.issuer), { headers: { Authorization: `Bearer ${session.tokens.access_token}` } });
      await result.arrayBuffer(); return reply(response, result.status, { authorized: result.status === 200 });
    }
    if (!permitted(request)) return reply(response, 403, {});
    if (operation === 'refresh') {
      session.tokens = await oidc.refreshTokenGrant(current.protocol, session.tokens.refresh_token);
      return reply(response, 200, { refreshed: true });
    }
    if (operation === 'userinfo') {
      await oidc.fetchUserInfo(current.protocol, session.tokens.access_token, session.sub);
      return reply(response, 200, { validated: true });
    }
    if (operation === 'logout' || operation === 'logout-other') {
      const result = await loopbackFetch(new URL('/api/v1/identity/oidc/logout/application', config.issuer), {
        method: 'POST', headers: { Authorization: `Bearer ${session.tokens.access_token}`, 'Content-Type': 'application/json', Origin: config.rpOrigin },
        body: JSON.stringify({ clientId: operation === 'logout-other' ? (client === 'a' ? 'interop-b' : 'interop-a') : current.id })
      });
      if (!result.ok) return reply(response, result.status, { error: 'application_logout' });
      current.lastLoggedOut = session.tokens;
      for (const [handle, value] of sessions) if (value === session) sessions.delete(handle);
      return reply(response, 200, {});
    }
    reply(response, 404, {});
  } catch (error) { reply(response, 502, { error: error.code ?? error.name ?? 'protocol_failure' }); }
});
await new Promise(resolve => rp.listen(config.rpPort, '0.0.0.0', resolve));
try {
  for (const name of ['a', 'b']) {
    const id = `interop-${name}`;
    const protocol = await oidc.discovery(new URL(config.issuer), id, { client_secret: config.secrets[name] },
      name === 'a' ? oidc.ClientSecretBasic(config.secrets[name]) : oidc.ClientSecretPost(config.secrets[name]), { [oidc.customFetch]: loopbackFetch });
    oidc.enableNonRepudiationChecks(protocol);
    clients.set(name, { id, protocol, jwks: createRemoteJWKSet(new URL(protocol.serverMetadata().jwks_uri), { [customFetch]: loopbackFetch }),
      fault: 'ok', stats: { received: 0, rejected: 0, applied: 0, duplicates: 0 } });
  }
  fs.writeFileSync(config.readyFile, JSON.stringify({ ready: true, clientVersion: '6.8.8' }));
} catch (error) {
  fs.writeFileSync(config.readyFile, JSON.stringify({ ready: false, error: error.code ?? error.name }));
  proxy.close(); rp.close(); process.exitCode = 1;
}
