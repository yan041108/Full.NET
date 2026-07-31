import assert from 'node:assert/strict';
import test from 'node:test';
import { computeTotpCode, decodeBase32Secret } from './totp-utils.mjs';

const adminPassword = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const adminUsername = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const apiOrigin = 'http://localhost:25173';

test('Production 环境 TOTP 登记后可完成远程超管授予', async t => {
  const skipBootstrap = process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1';
  let stack;

  if (skipBootstrap) {
    const apiUrl = process.env.FULLNET_E2E_API_URL;
    assert.ok(apiUrl, 'FULLNET_E2E_SKIP_BOOTSTRAP=1 时必须提供 FULLNET_E2E_API_URL');
    const { waitForApi } = await import('./wait-for-api.mjs');
    await waitForApi(apiUrl);
    stack = { apiUrl };
  } else {
    process.env.FULLNET_E2E_STACK_PROFILE = 'production-totp';
    const { bootstrapStack, teardownStack } = await import('./bootstrap-stack.mjs');
    stack = await bootstrapStack();
    t.after(async () => {
      await teardownStack();
    });
  }

  const apiUrl = stack.apiUrl;
  process.env.FULLNET_E2E_API_URL = apiUrl;

  const loginResponse = await fetch(`${apiUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: {
      Origin: apiOrigin,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      username: adminUsername,
      password: adminPassword
    })
  });
  assert.equal(loginResponse.status, 200);
  const loginBody = await loginResponse.json();
  assert.ok(loginBody.accessToken);

  const authHeaders = {
    Authorization: `Bearer ${loginBody.accessToken}`,
    Origin: apiOrigin,
    'Content-Type': 'application/json'
  };

  const beginResponse = await fetch(`${apiUrl}/api/v1/identity/me/mfa/totp/begin`, {
    method: 'POST',
    headers: authHeaders
  });
  assert.equal(beginResponse.status, 200);
  const beginBody = await beginResponse.json();
  assert.ok(beginBody.sharedSecretBase32);

  const totpKey = decodeBase32Secret(beginBody.sharedSecretBase32);
  const totpCode = computeTotpCode(totpKey);
  const confirmResponse = await fetch(`${apiUrl}/api/v1/identity/me/mfa/totp/confirm`, {
    method: 'POST',
    headers: authHeaders,
    body: JSON.stringify({ totpCode })
  });
  assert.equal(confirmResponse.status, 200);

  const targetUsername = `totp-target-${Date.now().toString(36)}`;
  const createUserResponse = await fetch(`${apiUrl}/api/v1/identity/users`, {
    method: 'POST',
    headers: authHeaders,
    body: JSON.stringify({
      username: targetUsername,
      displayName: 'TOTP 真实栈目标',
      password: 'FullNet!2026SaTarget'
    })
  });
  assert.equal(createUserResponse.status, 201);

  const missingTotpResponse = await fetch(`${apiUrl}/api/v1/identity/super-administrators/grant`, {
    method: 'POST',
    headers: authHeaders,
    body: JSON.stringify({
      username: targetUsername,
      currentPassword: adminPassword
    })
  });
  assert.equal(missingTotpResponse.status, 401);
  const missingTotpBody = await missingTotpResponse.json();
  assert.equal(missingTotpBody.code, 'identity.mfa.totp_required');

  const grantTotpCode = computeTotpCode(totpKey);
  const grantResponse = await fetch(`${apiUrl}/api/v1/identity/super-administrators/grant`, {
    method: 'POST',
    headers: authHeaders,
    body: JSON.stringify({
      username: targetUsername,
      currentPassword: adminPassword,
      totpCode: grantTotpCode
    })
  });
  assert.equal(grantResponse.status, 200);
  const grantBody = await grantResponse.json();
  assert.equal(grantBody.changed, true);
});
