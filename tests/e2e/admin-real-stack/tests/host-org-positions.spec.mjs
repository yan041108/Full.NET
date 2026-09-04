import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  enterDevelopmentTenant,
  enterTenantAccessToken,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginTenantAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueCode(clientKind, prefix) {
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `${prefix}-${stamp}-${suffix}`;
}

async function createTenantResource(
  request,
  clientKind,
  accessToken,
  path,
  data
) {
  const response = await request.post(`${apiBaseUrl}${path}`, {
    data,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind),
      'Content-Type': 'application/json'
    }
  });
  expect(response.status()).toBe(201);
  return response.json();
}

async function getPosition(request, clientKind, accessToken, positionId) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/positions/${encodeURIComponent(positionId)}`,
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

test('Host 管理员通过双管理端把真实职位绑定到机构与职级', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const unitCode = uniqueCode(clientKind, 'e2e-unit');
  const positionLevelCode = uniqueCode(clientKind, 'e2e-level');
  const positionCode = uniqueCode(clientKind, 'e2e-position');
  const unit = await createTenantResource(
    request,
    clientKind,
    accessToken,
    '/api/v1/organization/units',
    {
      code: unitCode,
      name: `真实栈机构 ${clientKind}`,
      parentId: null,
      displayOrder: 31
    }
  );
  const positionLevel = await createTenantResource(
    request,
    clientKind,
    accessToken,
    '/api/v1/organization/position-levels',
    {
      code: positionLevelCode,
      name: `真实栈职级 ${clientKind}`,
      displayOrder: 32
    }
  );
  const position = await createTenantResource(
    request,
    clientKind,
    accessToken,
    '/api/v1/organization/positions',
    {
      code: positionCode,
      name: `真实栈职位 ${clientKind}`,
      displayOrder: 33
    }
  );

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  await clickMainNavLink(page, /职位管理/);

  await expect(page.getByRole('heading', { name: '职位管理', exact: true })).toBeVisible();
  const positionsView = clientKind === 'layui'
    ? page.locator('[data-route-view="org-positions"]')
    : page.locator('.org-positions-view');
  const positionRow = crudTableRow(positionsView, clientKind, positionCode);
  const identity = clientKind === 'layui'
    ? positionRow.locator('div').first()
    : positionRow.locator('.art-data-row__main');
  await expect(positionRow).toBeVisible();

  const unitBindingResponse = page.waitForResponse(response =>
    response.request().method() === 'PUT'
      && response.url().endsWith(
        `/api/v1/organization/positions/${position.id}/unit`
      ));
  if (clientKind === 'vue') {
    await positionRow.locator('.el-select').first().click();
    await page.getByRole('option', { name: new RegExp(unitCode) }).click();
  } else {
    await positionRow.getByLabel('所属机构', { exact: true }).selectOption(unit.id);
  }
  expect((await unitBindingResponse).ok()).toBeTruthy();
  if (clientKind === 'vue') {
    await expect(positionRow.locator('.el-select').first()).toContainText(unit.name);
  } else {
    await expect(identity.getByText(unit.name, { exact: true })).toBeVisible();
  }

  const positionLevelBindingResponse = page.waitForResponse(response =>
    response.request().method() === 'PUT'
      && response.url().endsWith(
        `/api/v1/organization/positions/${position.id}/position-level`
      ));
  if (clientKind === 'vue') {
    await positionRow.locator('.el-select').nth(1).click();
    await page.getByRole('option', { name: new RegExp(positionLevelCode) }).click();
  } else {
    await positionRow
      .getByLabel('所属职级', { exact: true })
      .selectOption(positionLevel.id);
  }
  expect((await positionLevelBindingResponse).ok()).toBeTruthy();
  if (clientKind === 'vue') {
    await expect(positionRow.locator('.el-select').nth(1)).toContainText(positionLevel.name);
  } else {
    await expect(identity.getByText(positionLevel.name, { exact: true })).toBeVisible();
  }

  const persisted = await getPosition(
    request,
    clientKind,
    accessToken,
    position.id
  );
  expect(persisted.unitId).toBe(unit.id);
  expect(persisted.unitCode).toBe(unit.code);
  expect(persisted.positionLevelId).toBe(positionLevel.id);
  expect(persisted.positionLevelCode).toBe(positionLevel.code);
});

test('受限 Host 账号在租户上下文中访问职位 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const hostToken = await loginAccessToken(request, clientKind);
  const tenantToken = await enterTenantAccessToken(request, clientKind, hostToken);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/positions?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${tenantToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  await enterDevelopmentTenant(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /职位管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'organization/positions'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
