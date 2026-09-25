import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：im.wechat_miniprogram 订阅消息 Provider 与绑定列表（清单 61）', async ({
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

  const typesResponse = await request.get(`${apiBaseUrl}/api/v1/notifications/provider-types`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(typesResponse.ok()).toBeTruthy();
  const types = await typesResponse.json();
  const miniProgramType = types.find((item) => item.providerTypeKey === 'im.wechat_miniprogram');

  const bindingsResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/wechat-miniprogram/bindings?page=1&pageSize=5`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );

  if (miniProgramType) {
    expect(miniProgramType.receiptModeKey).toBe('none');
    expect(miniProgramType.supportedChannelKeys).toContain('wechat_miniprogram');
    const fieldNames = (miniProgramType.nonSecretFields ?? []).map((field) => field.name);
    expect(fieldNames).toEqual(expect.arrayContaining(['appId', 'defaultTemplateId']));

    expect(bindingsResponse.ok()).toBeTruthy();
    const pageResult = await bindingsResponse.json();
    expect(Array.isArray(pageResult.items)).toBe(true);
    expect(typeof pageResult.total).toBe('number');

    const exchangeResponse = await request.post(
      `${apiBaseUrl}/api/v1/notifications/wechat-miniprogram/bindings/exchange`,
      {
        headers,
        data: {
          providerProfileVersionId: '00000000-0000-0000-0000-000000000001',
          jsCode: 'e2e-invalid-js-code'
        }
      }
    );
    expect(exchangeResponse.status()).toBeGreaterThanOrEqual(400);
    expect(exchangeResponse.status()).toBeLessThan(500);
  } else {
    expect(bindingsResponse.status()).toBe(404);
    test.info().annotations.push({
      type: 'note',
      description:
        'real-stack 未启用 Notifications:Providers:WeChatMiniProgram:Enabled；闭环以 WeChatMiniProgram* 单元/OpenAPI 为准'
    });
  }
});

test('UI：微信小程序绑定与订阅授权页冒烟（清单 61，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '微信小程序绑定页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /微信小程序绑定/, '通知');

  await expect(page.getByRole('heading', { name: '微信小程序绑定', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(
    page.getByText('绑定与订阅登记由小程序端受保护 API 完成', { exact: false })
  ).toBeVisible();
  const emptyState = page.getByTestId('wechat-miniprogram-bindings-empty');
  const bindingItem = page.getByTestId('wechat-miniprogram-bindings-item');
  await expect(emptyState.or(bindingItem.first())).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);

  await clickMainNavLink(page, /渠道配置/, '通知');
  const emptyCatalog = page.getByTestId('notification-profiles-empty-catalog');
  if (!(await emptyCatalog.isVisible())) {
    const createButton = page.getByTestId('notification-profiles-create');
    if (await createButton.isVisible()) {
      await createButton.click();
      await page.getByTestId('notification-profiles-type').click();
      const option = page.getByRole('option', { name: 'im.wechat_miniprogram' });
      if (await option.isVisible()) {
        await option.click();
        await expect(page.getByTestId('notification-profiles-receipt-mode')).toContainText('无回执');
      }
    }
  }
});
