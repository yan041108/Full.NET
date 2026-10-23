import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  adminOrigin,
  loginHostAdminAccessToken,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Match 条件编辑保存受控比较与存在规则', async ({ page, request }, testInfo) => {
  // 该真实栈场景包含 API 建档、两次登录、双次保存及窄屏键盘验收，单独给足场景预算。
  test.setTimeout(90_000);
  test.skip(process.env.Notifications__Providers__Smtp__Enabled !== 'true', '仅操作 SMTP 目录的绑定草稿，需显式开启目录；不发送消息。');
  const pageErrors = [];
  let loggedIn = false;
  page.on('response', response => {
    if (response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/v1/auth/login' && response.status() === 200) loggedIn = true;
  });
  page.on('pageerror', error => pageErrors.push(error.message));
  page.on('console', message => {
    // 首次匿名打开时无刷新会话，鉴权层按 CSRF 契约拒绝刷新；登录后的同类错误仍必须失败。
    const anonymousRefresh = !loggedIn && message.location().url.endsWith('/api/v1/auth/refresh')
      && message.text() === 'Failed to load resource: the server responded with a status of 403 (Forbidden)';
    if (message.type() === 'error' && !anonymousRefresh) pageErrors.push(message.text());
  });
  const api = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const token = await loginHostAdminAccessToken(request, testInfo.project.metadata.clientKind);
  const key = `e2e.match.${Date.now()}`;
  const created = await request.post(`${api}/api/v1/notifications/bindings`, {
    headers: { Authorization: `Bearer ${token}`, Origin: adminOrigin(testInfo.project.metadata.clientKind) },
    data: { bindingKey: key, dispatchModeKey: 'match', producerKey: 'tests.e2e', sceneKey: 'match.editor', channelKey: 'email',
      targets: [{ profileKey: 'draft-only', order: 1, condition: { parameterName: 'segment', operatorKey: 'equals', expectedValue: 'standard' } }] }
  });
  expect(created.status()).toBe(201);
  const binding = await created.json();
  await loginAsHostAdmin(page);
  loggedIn = true;
  await clickMainNavLink(page, /场景绑定/, '通知');
  await page.getByRole('button', { name: key, exact: true }).click();
  const parameter = page.getByRole('textbox', { name: '模板参数名', exact: true });
  await expect(parameter).toHaveValue('segment');
  await parameter.focus();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('combobox', { name: '比较方式', exact: true })).toBeFocused();
  await page.getByTestId('notification-condition-value').fill('priority');
  await page.getByTestId('notification-condition-operator').click();
  await page.getByRole('option', { name: '不等于', exact: true }).click();
  const save = () => page.waitForResponse(r => r.request().method() === 'PUT'
    && new URL(r.url()).pathname === `/api/v1/notifications/bindings/${binding.id}`);
  let saved = save();
  await page.getByTestId('notification-bindings-save').click();
  let response = await saved;
  expect(response.status()).toBe(200);
  expect(JSON.parse((await response.json()).draftJson).targets[0].condition)
    .toEqual({ parameterName: 'segment', operatorKey: 'not_equals', expectedValue: 'priority' });
  await expect(page.getByTestId('notification-bindings-save')).toBeEnabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.keyboard.press('Escape');
  const sidebar = page.locator('.art-admin-shell__sidebar');
  await expect.poll(() => sidebar.evaluate(element => element.getBoundingClientRect().right)).toBeLessThanOrEqual(0);
  await parameter.scrollIntoViewIfNeeded();
  const fieldBounds = await parameter.boundingBox();
  expect(fieldBounds.x).toBeGreaterThanOrEqual(0);
  expect(fieldBounds.x + fieldBounds.width).toBeLessThanOrEqual(390);
  await page.screenshot({ path: testInfo.outputPath('match-editor-mobile.png'), fullPage: true });
  await page.getByTestId('notification-condition-operator').click();
  await page.getByRole('option', { name: '存在', exact: true }).click();
  await expect(page.getByTestId('notification-condition-value')).toHaveCount(0);
  saved = save();
  await page.getByTestId('notification-bindings-save').click();
  response = await saved;
  expect(response.status()).toBe(200);
  const condition = JSON.parse((await response.json()).draftJson).targets[0].condition;
  expect(condition.operatorKey).toBe('exists');
  expect(condition.expectedValue ?? null).toBeNull();
  await page.getByRole('button', { name: '打开主导航', exact: true }).click();
  await expect(sidebar).toHaveClass(/is-open/);
  await page.keyboard.press('Escape');
  await expect(sidebar).not.toHaveClass(/is-open/);
  expect(pageErrors).toEqual([]);
});

test('Host 管理员可打开通知控制面；空目录、FanOut 明示且不回显密钥', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '通知平台控制面仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /渠道配置/, '通知');
  const profilesView = page.locator('.notification-profiles-view');
  await expect(profilesView.getByRole('heading', { name: '渠道配置', exact: true })).toBeVisible();
  await expect(page.getByTestId('notification-profiles-empty-catalog')).toBeVisible();
  await expect(page.getByTestId('notification-profiles-create')).toHaveCount(0);
  await expect(profilesView).not.toContainText('vault://');
  await expect(profilesView).not.toContainText('apiToken');

  await clickMainNavLink(page, /通知模板/, '通知');
  const templatesView = page.locator('.notification-templates-view');
  await expect(templatesView.getByRole('heading', { name: '通知模板', exact: true })).toBeVisible();
  await expect(page.getByTestId('notification-templates-create')).toBeVisible();
  await expect(page.getByTestId('notification-templates-channel')).toContainText('inbox');

  const templateKey = `e2e.browser.${Date.now()}`;
  await page.getByTestId('notification-templates-key').fill(templateKey);
  await page.getByTestId('notification-templates-subject').fill('真实栈通知');
  await page.getByTestId('notification-templates-body').fill('你好，{{recipientName}}');
  await page.getByTestId('notification-templates-parameter-name').fill('recipientName');
  await page.getByTestId('notification-templates-parameter-add').click();

  const createResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/v1/notifications/templates'
  );
  await page.getByTestId('notification-templates-create').click();
  const createResponse = await createResponsePromise;
  expect(createResponse.status()).toBe(201);
  await expect(page.getByText('模板草稿已创建')).toBeVisible();

  const templateRow = templatesView.locator('.notification-templates-table .el-table__row')
    .filter({ hasText: templateKey });
  await expect(templateRow).toBeVisible();
  await expect(templateRow.getByTestId('notification-templates-state')).toContainText('草稿');

  const publishResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && /\/api\/v1\/notifications\/templates\/[0-9a-f-]+\/publish$/u.test(
      new URL(response.url()).pathname
    )
  );
  await page.getByTestId('notification-templates-publish').click();
  const publishResponse = await publishResponsePromise;
  expect(publishResponse.status()).toBe(200);
  await expect(page.getByText('模板版本已发布')).toBeVisible();
  await expect(templateRow.getByTestId('notification-templates-state')).toContainText('已发布 v1');

  await clickMainNavLink(page, /场景绑定/, '通知');
  const bindingsView = page.locator('.notification-bindings-view');
  await expect(bindingsView.getByRole('heading', { name: '场景绑定', exact: true })).toBeVisible();
  await page.getByTestId('notification-bindings-mode').click();
  await page.getByRole('option', { name: /扇出/ }).click();
  await expect(page.getByTestId('notification-bindings-fanout')).toContainText('不会隐式多发');
  await page.getByTestId('notification-bindings-create').click();
  await expect(page.getByTestId('notification-bindings-fanout')).toContainText('请确认扇出目标');

  await clickMainNavLink(page, /投递运维/, '通知');
  const deliveriesView = page.locator('.notification-deliveries-view');
  await expect(deliveriesView.getByRole('heading', { name: '投递运维', exact: true })).toBeVisible();
  await expect(deliveriesView.getByRole('heading', { name: '投递状态看板', exact: true })).toBeVisible();
  await expect(deliveriesView).toContainText('可信送达仅来自验签回执');
  await expect(deliveriesView).toContainText('还没有投递记录');
  await expect(page.getByTestId('notification-deliveries-retry')).toHaveCount(0);

  await clickMainNavLink(page, /通知偏好/, '通知');
  await expect(page.locator('[data-channel="inbox"]')).toBeVisible();
  await expect(page.getByTestId('preference-save').first()).toBeVisible();

  await clickMainNavLink(page, /收件端点/, '通知');
  await expect(page.getByTestId('notification-preferences-endpoint-list')).toBeVisible();
  await expect(page.getByTestId('notification-preferences-save')).toHaveCount(0);
  await expect(page.getByTestId('notification-preferences-no-profile')).toContainText(
    '没有可登记的已发布渠道配置，或没有渠道配置读取权限。'
  );

  await clickMainNavLink(page, /通知场景目录/, '通知');
  await expect(page.getByTestId('governance-create')).toBeVisible();
  await expect(page.locator('.notification-governance')).toContainText('场景目录决定分类与合法渠道');

  await clickMainNavLink(page, /发送配额/, '通知');
  await expect(page.getByTestId('governance-create')).toBeVisible();
  await expect(page.locator('.notification-governance')).toContainText('不是当前窗口剩余额度');
});
