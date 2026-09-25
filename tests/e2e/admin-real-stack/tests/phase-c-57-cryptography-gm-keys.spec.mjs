import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const seedKeyKey = 'host-integration-signing';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：国密密钥目录与 SM2 受控签验（无通用解密）（清单 57）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const statusResponse = await request.get(`${apiBaseUrl}/api/v1/cryptography/status`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(statusResponse.ok()).toBeTruthy();
  const status = await statusResponse.json();
  expect(status.algorithm).toBe('SM2');
  expect(status.signingPurpose.length).toBeGreaterThan(0);

  const keysResponse = await request.get(`${apiBaseUrl}/api/v1/cryptography/keys`, { headers });
  expect(keysResponse.ok()).toBeTruthy();
  const keys = await keysResponse.json();
  const seedKey = keys.find((item) => item.keyKey === seedKeyKey);
  expect(seedKey).toBeTruthy();
  expect(seedKey.publicKeyHex.length).toBeGreaterThan(0);
  expect(JSON.stringify(keys).toLowerCase()).not.toMatch(/privatekeyhex/);

  const invalidVerify = await request.post(`${apiBaseUrl}/api/v1/cryptography/sm2/verify`, {
    headers,
    data: {
      keyKey: seedKeyKey,
      message: 'fullnet-integration-payload',
      signatureHex: '00'.repeat(64)
    }
  });
  expect(invalidVerify.status()).toBe(422);
  const invalidProblem = await invalidVerify.json();
  expect(invalidProblem.code).toBe('cryptography.sm2.signature_invalid');

  const signResponse = await request.post(`${apiBaseUrl}/api/v1/cryptography/sm2/sign`, {
    headers,
    data: { keyKey: seedKeyKey, message: 'fullnet-integration-payload' }
  });
  if (signResponse.ok()) {
    const signed = await signResponse.json();
    const verifyOk = await request.post(`${apiBaseUrl}/api/v1/cryptography/sm2/verify`, {
      headers,
      data: {
        keyKey: seedKeyKey,
        message: 'fullnet-integration-payload',
        signatureHex: signed.signatureHex
      }
    });
    expect(verifyOk.ok()).toBeTruthy();
    expect((await verifyOk.json()).isValid).toBe(true);
  } else {
    expect(signResponse.status()).toBe(409);
    const signProblem = await signResponse.json();
    expect(signProblem.code).toBe('cryptography.key.private_key_not_configured');
  }
});

test('UI：国密控制面页（清单 57，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '国密页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /国密密钥/);

  await expect(page.getByText('国密控制面', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('不提供匿名通用加解密', { exact: false })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
