import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  enterDevelopmentTenant,
  loginAsHostAdmin,
  loginTenantAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueCode(clientKind) {
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `e2e-level-${stamp}-${suffix}`;
}

function positionLevelsView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="org-position-levels"]')
    : page.locator('.org-position-levels-view');
}

async function fillPromptInput(page, clientKind, value) {
  if (clientKind === 'vue') {
    const prompt = page.locator('.el-message-box').last();
    await expect(prompt.locator('input')).toBeVisible();
    await prompt.locator('input').fill(value);
    await prompt.locator('input').press('Enter');
    await expect(prompt).toBeHidden();
    return;
  }

  const layer = page.locator('.layui-layer').last();
  await expect(layer.locator('.layui-layer-input')).toBeVisible();
  await layer.locator('.layui-layer-input').fill(value);
  await layer.locator('.layui-layer-btn0').click({ force: true });
}

async function confirmDisable(page, clientKind) {
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: '禁用', exact: true }).click();
    return;
  }

  await page.locator('.layui-layer-btn0').last().click();
}

async function getPositionLevel(request, clientKind, accessToken, positionLevelId) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/position-levels/${encodeURIComponent(positionLevelId)}`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  return response.json();
}

test('Host 管理员通过双管理端完成真实职级创建更新与禁用', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const code = uniqueCode(clientKind);
  const initialName = `真实栈职级 ${clientKind}`;
  const updatedName = `真实栈职级已更新 ${clientKind}`;

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  await clickMainNavLink(page, /职级管理/);

  const view = positionLevelsView(page, clientKind);
  await expect(view.getByRole('heading', { name: '职级管理', exact: true })).toBeVisible();

  if (clientKind === 'vue') {
    await view.getByTestId('org-position-levels-action-create').click();
    const editor = page.getByTestId('org-position-levels-editor-form');
    await editor.getByLabel('职级编码', { exact: true }).fill(code);
    await editor.getByLabel('显示名称', { exact: true }).fill(initialName);
  } else {
    await view.getByLabel('职级编码', { exact: true }).fill(code);
    await view.getByLabel('显示名称', { exact: true }).fill(initialName);
  }
  const createResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
      && response.url().endsWith('/api/v1/organization/position-levels'));
  if (clientKind === 'vue') {
    await page.getByTestId('org-position-levels-editor-submit').click();
  } else {
    await view.getByRole('button', { name: '创建职级', exact: true }).click();
  }
  const createResponse = await createResponsePromise;
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();

  const levelRow = crudTableRow(view, clientKind, code);
  await expect(levelRow).toBeVisible({ timeout: 15_000 });
  await expect(levelRow.getByText(initialName, { exact: true })).toBeVisible();

  const updateResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'PUT'
      && response.url().endsWith(
        `/api/v1/organization/position-levels/${created.id}`
      ));
  if (clientKind === 'vue') {
    await levelRow.getByTestId('org-position-levels-action-edit').click();
    const editor = page.getByTestId('org-position-levels-editor-form');
    await editor.getByLabel('显示名称', { exact: true }).fill(updatedName);
    await page.getByTestId('org-position-levels-editor-submit').click();
  } else {
    await levelRow.getByRole('button', { name: '编辑', exact: true }).click();
    await fillPromptInput(page, clientKind, updatedName);
  }
  expect((await updateResponsePromise).ok()).toBeTruthy();
  await expect(levelRow.getByText(updatedName, { exact: true })).toBeVisible({
    timeout: 15_000
  });

  const disableResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
      && response.url().endsWith(
        `/api/v1/organization/position-levels/${created.id}/disable`
      ));
  if (clientKind === 'vue') {
    await levelRow.getByTestId('org-position-levels-action-disable').click();
  } else {
    await levelRow.getByRole('button', { name: '禁用', exact: true }).click();
  }
  await confirmDisable(page, clientKind);
  expect((await disableResponsePromise).ok()).toBeTruthy();
  await expect(levelRow.getByText('已禁用', { exact: true })).toBeVisible({
    timeout: 15_000
  });

  const persisted = await getPositionLevel(
    request,
    clientKind,
    accessToken,
    created.id
  );
  expect(persisted.code).toBe(code);
  expect(persisted.name).toBe(updatedName);
  expect(persisted.isActive).toBe(false);
  expect(persisted.version).toBeGreaterThan(created.version);
});
