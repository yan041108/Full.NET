import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真栈 API 加载流水号规则列表', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '流水号规则动作权限切片仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /流水号规则/, '流水号');

  const view = page.locator('.serial-number-rules-view');
  await expect(view.getByRole('heading', { name: /流水号规则/, level: 1 })).toBeVisible();
  await expect(view.getByTestId('serial-rule-filter-name')).toBeVisible();
  await expect(view.getByTestId('serial-rule-pattern-hint')).toBeVisible();
});

test('Host 管理员可创建、更新并预览流水号规则', async ({ page, request }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind !== 'vue',
    '流水号规则写路径与预览仅验收 Vue'
  );
  test.setTimeout(90_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const ruleKey = `e2e.serial.${stamp}`;

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /流水号规则/, '流水号');

  const view = page.locator('.serial-number-rules-view');
  await expect(view.getByRole('heading', { name: /流水号规则/, level: 1 })).toBeVisible();
  await expect(view.getByTestId('serial-rule-create')).toBeVisible();

  await view.getByTestId('serial-rule-key').fill(ruleKey);
  await view.getByTestId('serial-rule-display-name').fill(`E2E 流水号 ${stamp}`);
  await view.getByTestId('serial-rule-pattern').fill(
    'E2E-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:4}'
  );
  await view.getByTestId('serial-rule-minimum').fill('1');
  await view.getByTestId('serial-rule-maximum').fill('9999');
  await view.getByTestId('serial-rule-create').click();
  await expect(view.getByText('规则已创建')).toBeVisible({ timeout: 15_000 });

  await view.getByTestId('serial-rule-display-name').fill(`E2E 流水号更新 ${stamp}`);
  await view.getByTestId('serial-rule-save').click();
  await expect(view.getByText('规则已更新')).toBeVisible({ timeout: 15_000 });

  await view.getByTestId('serial-rule-preview-tenant').fill('acme');
  await view.getByTestId('serial-rule-preview-sequence').fill('7');
  await view.getByTestId('serial-rule-preview-at').fill('2026-08-20T00:00:00Z');
  await view.getByTestId('serial-rule-preview').click();
  await expect(view.getByTestId('serial-rule-preview-value')).toHaveText(
    'E2E-20260820-acme-0007',
    { timeout: 15_000 }
  );

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/serial-numbers/rules?page=1&pageSize=20&key=${encodeURIComponent(ruleKey)}&sortBy=ruleKey&sortDirection=asc`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(listResponse.status()).toBe(200);
  const pageBody = await listResponse.json();
  expect(pageBody.items.some(item => item.ruleKey === ruleKey)).toBeTruthy();
});

test('受限 Host 账号访问流水号规则 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '流水号规则动作权限切片仅验证 Vue 管理端'
  );

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/serial-numbers/rules?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /流水号规则/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'serial-numbers/rules'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

test('仅有 read 时写操作按钮不可见且 create/update/preview API 返回 403', async ({
  page,
  request
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind !== 'vue',
    '精确按钮权限仅验收 Vue'
  );

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'serial_numbers.rules.read'
    ]
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );

  const createResponse = await request.post(`${apiBaseUrl}/api/v1/serial-numbers/rules`, {
    data: {
      ruleKey: `e2e.forbidden.${Date.now().toString(36)}`,
      displayName: 'forbidden',
      description: null,
      scope: 1,
      resetInterval: 1,
      pattern: 'X-{sequence:3}',
      minimumValue: 1,
      maximumValue: 999,
      displayOrder: 1,
      isEnabled: true
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(createResponse.status()).toBe(403);
  expect((await createResponse.json()).code).toBe('authorization.permission_denied');

  const previewResponse = await request.post(
    `${apiBaseUrl}/api/v1/serial-numbers/rules/preview`,
    {
      data: {
        scope: 1,
        pattern: 'X-{tenant}-{sequence:3}',
        tenantIdentifier: 'acme',
        sequenceValue: 1,
        atUtc: '2026-08-20T00:00:00Z'
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(previewResponse.status()).toBe(403);
  expect((await previewResponse.json()).code).toBe('authorization.permission_denied');

  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  const seedKey = `e2e.seed.${Date.now().toString(36)}`;
  const seeded = await request.post(`${apiBaseUrl}/api/v1/serial-numbers/rules`, {
    data: {
      ruleKey: seedKey,
      displayName: 'seed for update 403',
      description: null,
      scope: 1,
      resetInterval: 1,
      pattern: 'S-{tenant}-{sequence:3}',
      minimumValue: 1,
      maximumValue: 999,
      displayOrder: 1,
      isEnabled: true
    },
    headers: {
      Authorization: `Bearer ${adminToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(seeded.status()).toBe(201);
  const seededRule = await seeded.json();

  const updateResponse = await request.put(
    `${apiBaseUrl}/api/v1/serial-numbers/rules/${seededRule.id}`,
    {
      data: {
        displayName: 'should fail',
        description: null,
        scope: 1,
        resetInterval: 1,
        pattern: 'S-{tenant}-{sequence:3}',
        minimumValue: 1,
        maximumValue: 999,
        displayOrder: 1,
        isEnabled: true,
        version: seededRule.version
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(updateResponse.status()).toBe(403);
  expect((await updateResponse.json()).code).toBe('authorization.permission_denied');

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /流水号规则/, '流水号');
  const view = page.locator('.serial-number-rules-view');
  await expect(view.getByRole('heading', { name: /流水号规则/, level: 1 })).toBeVisible();
  await expect(view.getByTestId('serial-rule-create')).toHaveCount(0);
  await expect(view.getByTestId('serial-rule-save')).toHaveCount(0);
  await expect(view.getByTestId('serial-rule-preview')).toHaveCount(0);
  await expect(view.getByTestId('serial-rule-enable')).toHaveCount(0);
  await expect(view.getByTestId('serial-rule-disable')).toHaveCount(0);
});
