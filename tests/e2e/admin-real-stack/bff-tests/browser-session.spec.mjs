import { expect, test } from '@playwright/test';
import { createHash, createPublicKey, randomBytes, verify } from 'node:crypto';
import path from 'node:path';

test('HTTPS 登录、恢复、租户切换、密钥详情和注销保持令牌在服务端', async ({ page, context, isMobile }) => {
  const apiRequests = [];
  const pageErrors = [];
  const serverErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  page.on('response', response => {
    if (new URL(response.url()).pathname.startsWith('/api/') && response.status() >= 500) serverErrors.push(response.status());
  });
  page.on('request', request => {
    if (new URL(request.url()).pathname.startsWith('/api/')) apiRequests.push(request);
  });
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.goto('/');
  const challenged = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/identity/browser/login');
  await page.getByTestId('login-oidc-center').click();
  const challenge = await challenged;
  expect(challenge.status(), challenge.status() === 302 ? '' : (await challenge.text()).slice(0, 300)).toBe(302);
  await page.locator('input[name="username"]').fill('admin');
  await page.locator('input[name="password"]').fill(process.env.FULLNET_BFF_UI_PASSWORD);
  await page.locator('button[type="submit"]').first().click();
  await expect(page.locator('#main-content')).toBeVisible({ timeout: 60_000 });
  if (!isMobile) await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  const session = await page.evaluate(async () => (await fetch('/api/v1/identity/browser/session')).json());
  expect(Object.keys(session).sort()).toEqual(['csrfToken', 'expiresAtUtc']);
  const cookies = await context.cookies();
  expect(cookies.find(cookie => cookie.name === '__Host-fullnet-browser')).toMatchObject({ httpOnly: true, secure: true, sameSite: 'Lax', path: '/' });
  const storage = await page.evaluate(() => ({ ...localStorage, ...sessionStorage }));
  expect(Object.keys(storage).filter(key => /access|refresh|pkce|verifier|credential/i.test(key))).toEqual([]);
  // 第二个机密 RP 的凭据只在 Playwright Node 进程中；真实浏览器复用中心 Cookie 完成无提示 SSO。
  const verifier = randomBytes(32).toString('base64url');
  const state = randomBytes(24).toString('base64url'); const nonce = randomBytes(24).toString('base64url');
  const origin = process.env.FULLNET_BFF_UI_ORIGIN;
  const redirect = process.env.FULLNET_BFF_SECOND_RP_CALLBACK;
  const rp = await context.newPage();
  await rp.goto('/connect/authorize?' + new URLSearchParams({ client_id: 'browser-second-rp', response_type: 'code',
    redirect_uri: redirect, scope: 'openid profile', prompt: 'none', state, nonce,
    code_challenge: createHash('sha256').update(verifier).digest('base64url'), code_challenge_method: 'S256' }));
  await rp.waitForURL(url => url.origin === new URL(redirect).origin && url.pathname === '/server-callback.html');
  const callback = new URL(rp.url()); expect(callback.searchParams.get('state')).toBe(state);
  expect((await context.cookies(redirect)).some(cookie => cookie.name === '__Host-fullnet-browser')).toBe(false);
  expect(callback.searchParams.has('error')).toBe(false);
  const exchange = await context.request.post('/connect/token', { form: { grant_type: 'authorization_code',
    client_id: 'browser-second-rp', client_secret: 'browser-second-rp-fixture-secret', redirect_uri: redirect,
    code: callback.searchParams.get('code'), code_verifier: verifier } });
  expect(exchange.status()).toBe(200);
  const tokens = await exchange.json();
  const [header, payload, signature] = tokens.id_token.split('.');
  const claims = JSON.parse(Buffer.from(payload, 'base64url')); const metadata = JSON.parse(Buffer.from(header, 'base64url'));
  const discovery = await (await context.request.get('/.well-known/openid-configuration')).json();
  const jwks = await (await context.request.get(discovery.jwks_uri)).json();
  expect(metadata.alg).toBe('RS256');
  expect(verify('RSA-SHA256', Buffer.from(header + '.' + payload), createPublicKey({ key: jwks.keys.find(key => key.kid === metadata.kid), format: 'jwk' }), Buffer.from(signature, 'base64url'))).toBe(true);
  expect(claims.iss).toBe(origin + '/identity');
  expect(claims.aud).toBe('browser-second-rp'); expect(claims.nonce).toBe(nonce);
  expect(claims.exp).toBeGreaterThan(Date.now() / 1000);
  await rp.close();
  await page.reload();
  await expect(page.locator('#main-content')).toBeVisible();
  const denied = await page.evaluate(async () => (await fetch('/api/v1/tenancy/context', {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ tenantId: null })
  })).status);
  expect(denied).toBe(403);
  const changed = await page.evaluate(async () => {
    const state = await (await fetch('/api/v1/identity/browser/session')).json();
    const response = await fetch('/api/v1/tenancy/context', { method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': state.csrfToken }, body: JSON.stringify({ tenantId: null }) });
    return { status: response.status, body: await response.text() };
  });
  expect(changed).toEqual({ status: 204, body: '' });
  await page.goto('/#/identity/oidc-signing-keys');
  await expect(page.locator('.oidc-signing-keys-view')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'browser-test', exact: true })).toBeVisible();
  const nextKey = page.getByRole('row').filter({ hasText: 'browser-next' });
  const lifecycle = await page.evaluate(async () => (await (await fetch('/api/v1/identity/oidc-signing-keys')).json())
    .keys.find(key => key.keyId === 'browser-next').lifecycleState);
  const operations = lifecycle === 'unregistered' ? ['publish', 'activate'] : lifecycle === 'active' ? [] : ['activate'];
  for (const operation of operations) {
    const updated = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/api/v1/identity/oidc-signing-keys/browser-next/${operation}`);
    await nextKey.getByTestId(`oidc-signing-keys-action-${operation}`).click();
    await page.getByRole('button', { name: '确定', exact: true }).click();
    expect((await updated).status()).toBe(200);
  }
  await expect(nextKey.getByText('活跃', { exact: true })).toBeVisible();
  const previousKey = page.getByRole('row').filter({ hasText: 'browser-test' });
  await expect(previousKey.getByTestId('oidc-signing-keys-action-retire')).toHaveCount(0);
  await previousKey.getByTestId('oidc-signing-keys-action-detail').click();
  await expect(page.locator('.oidc-signing-keys-hash')).toHaveText(/^[A-F0-9]{64}$/);
  await expect(page.getByRole('dialog', { name: '签名密钥详情' })).toBeVisible();
  await expect(page.locator('.el-message')).toHaveCount(0);
  await expect(page.getByRole('dialog', { name: '签名密钥详情' })).toBeInViewport({ ratio: 0.9 });
  const output = process.env.FULLNET_BFF_UI_OUTPUT;
  await page.screenshot({ path: path.resolve(output, '../..', `${path.basename(path.dirname(output))}-${test.info().project.name}-key-details.png`), fullPage: true, animations: 'disabled' });
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  const deliveries = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/identity/oidc-logout-deliveries');
  await page.goto('/#/identity/oidc-clients');
  expect((await deliveries).status()).toBe(200);
  const reloadedDeliveries = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/identity/oidc-logout-deliveries');
  await page.getByRole('button', { name: '刷新投递状态', exact: true }).click();
  expect((await reloadedDeliveries).status()).toBe(200);
  await page.getByRole('row').filter({ hasText: 'browser-second-rp' }).getByTestId('oidc-clients-action-edit').click();
  const editor = page.locator('.art-form-dialog');
  const mfaField = editor.locator('.el-form-item').filter({ hasText: '要求多因素认证' });
  const mfa = mfaField.getByRole('switch');
  const mfaControl = mfaField.locator('.el-switch');
  await mfaControl.scrollIntoViewIfNeeded();
  await expect(mfa).toHaveAttribute('aria-checked', 'false');
  await mfaControl.click(); await expect(mfa).toHaveAttribute('aria-checked', 'true');
  const backchannel = editor.locator('.el-form-item').filter({ hasText: '退出通知接收地址' }).getByRole('textbox');
  await backchannel.fill('https://rp.example/backchannel');
  await expect(backchannel).toHaveValue('https://rp.example/backchannel');
  await page.screenshot({ path: path.resolve(output, '../..', `${path.basename(path.dirname(output))}-${test.info().project.name}-client-editor.png`), fullPage: true, animations: 'disabled' });
  await editor.getByRole('button', { name: '取消', exact: true }).click();
  const ownGrants = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/me/oidc-authorizations');
  await page.goto('/#/account/security');
  expect((await ownGrants).status()).toBe(200);
  await expect(page.locator('.security-settings-view')).toBeVisible();
  await expect(page.getByText('修改密码后，所有设备的登录会话失效，请重新登录。', { exact: true })).toBeVisible();
  await expect(page.locator('.security-settings-view .el-table')).toHaveCount(1);
  await page.screenshot({ path: path.resolve(output, '../..', `${path.basename(path.dirname(output))}-${test.info().project.name}-account-security.png`), fullPage: true, animations: 'disabled' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  for (const request of apiRequests) expect((await request.allHeaders()).authorization).toBeUndefined();
  expect(pageErrors).toEqual([]); expect(serverErrors).toEqual([]);
  await page.evaluate(async () => {
    const state = await (await fetch('/api/v1/identity/browser/session')).json();
    const response = await fetch('/api/v1/identity/browser/logout', { method: 'POST', headers: { 'X-CSRF-Token': state.csrfToken } });
    if (response.status !== 204) throw new Error(`logout: ${response.status}`);
  });
  expect(await page.evaluate(async () => (await fetch('/api/v1/me')).status)).toBe(401);
  expect((await context.request.get('/api/v1/me', { headers: { Authorization: 'Bearer ' + tokens.access_token } })).status()).toBe(200);
  await page.goto('/connect/logout');
  await expect(page.locator('form')).toBeVisible();
  await page.locator('form button').first().click();
  expect((await context.request.get('/api/v1/me', { headers: { Authorization: 'Bearer ' + tokens.access_token } })).status()).toBe(401);
  await page.goto('/');
  await expect(page.getByTestId('login-oidc-center')).toBeVisible();
});
