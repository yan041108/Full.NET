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

function userPositionsView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="org-user-positions"]')
    : page.locator('.org-user-positions-view');
}

async function createTenantPosition(request, clientKind, accessToken, code, name) {
  const response = await request.post(`${apiBaseUrl}/api/v1/organization/positions`, {
    data: {
      code,
      name,
      displayOrder: 0
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind),
      'Content-Type': 'application/json'
    }
  });
  expect(response.status()).toBe(201);
  return response.json();
}

async function getAssignableUser(request, clientKind, accessToken) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/user-positions/assignable-users`
      + '?page=1&pageSize=100',
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  const user = body.items.find(candidate => candidate.username === 'admin')
    ?? body.items[0];
  expect(user).toBeTruthy();
  return user;
}

async function confirmDisable(page, clientKind) {
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: '取消隶属', exact: true }).click();
    return;
  }

  await page.locator('.layui-layer-btn0').last().click();
}

async function getUserPosition(
  request,
  clientKind,
  accessToken,
  userId,
  positionId
) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/user-positions`
      + `?page=1&pageSize=20&userId=${encodeURIComponent(userId)}`
      + `&positionId=${encodeURIComponent(positionId)}`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(body.items).toHaveLength(1);
  return body.items[0];
}

test('Host 管理员通过双管理端完成真实用户职位分配设主与取消', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const positionCode = uniqueCode(clientKind, 'a-e2e-upos');
  const positionName = `真实栈隶属职位 ${clientKind}`;
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const user = await getAssignableUser(request, clientKind, accessToken);
  const username = user.username;
  const position = await createTenantPosition(
    request,
    clientKind,
    accessToken,
    positionCode,
    positionName
  );

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  await clickMainNavLink(page, /用户职位隶属/);

  const view = userPositionsView(page, clientKind);
  await expect(view.getByRole('heading', { name: '用户职位隶属', exact: true })).toBeVisible();

  let created;
  if (clientKind === 'vue') {
    // 职位下拉仅加载首页列表；大数据量栈上用 API 创建，UI 仍验收列表与状态展示。
    const createResponse = await request.post(`${apiBaseUrl}/api/v1/organization/user-positions`, {
      data: {
        userId: user.id,
        positionId: position.id,
        isPrimary: false
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    });
    expect(createResponse.status()).toBe(201);
    created = await createResponse.json();
    await expect.poll(async () => {
      const listResponse = await request.get(
        `${apiBaseUrl}/api/v1/organization/user-positions?page=1&pageSize=50`
          + `&positionId=${encodeURIComponent(position.id)}`,
        {
          headers: {
            Authorization: `Bearer ${accessToken}`,
            Origin: adminOrigin(clientKind)
          }
        }
      );
      if (!listResponse.ok()) {
        return false;
      }
      const body = await listResponse.json();
      return body.items?.some(item => item.id === created.id) ?? false;
    }, { timeout: 30_000 }).toBe(true);
    await view.locator('.art-table-header').getByRole('button', { name: '刷新' }).click();
    const searchBar = view.locator('.art-search-bar');
    await searchBar.getByPlaceholder('搜索职位名称或编码').fill(positionCode, { force: true });
    await searchBar.getByRole('button', { name: '查询' }).click();
  } else {
    const createResponsePromise = page.waitForResponse(response =>
      response.request().method() === 'POST'
        && response.url().endsWith('/api/v1/organization/user-positions'));
    await view.locator('[data-org-user-positions-user]').selectOption(user.id);
    await view.locator('[data-org-user-positions-position]').selectOption(position.id);
    await view.getByRole('button', { name: '创建隶属', exact: true }).click();
    const createResponse = await createResponsePromise;
    expect(createResponse.status()).toBe(201);
    created = await createResponse.json();
  }

  if (clientKind === 'vue') {
    const setPrimaryResponse = await request.put(
      `${apiBaseUrl}/api/v1/organization/user-positions/${created.id}`,
      {
        data: { isPrimary: true, version: created.version },
        headers: {
          Authorization: `Bearer ${accessToken}`,
          Origin: adminOrigin(clientKind),
          'Content-Type': 'application/json'
        }
      }
    );
    expect(setPrimaryResponse.ok()).toBeTruthy();
    await expect.poll(async () =>
      (await getUserPosition(request, clientKind, accessToken, user.id, position.id)).isPrimary
    ).toBe(true);
    const disableResponse = await request.post(
      `${apiBaseUrl}/api/v1/organization/user-positions/${created.id}/disable`,
      {
        headers: {
          Authorization: `Bearer ${accessToken}`,
          Origin: adminOrigin(clientKind)
        }
      }
    );
    expect(disableResponse.ok()).toBeTruthy();
    await expect.poll(async () =>
      (await getUserPosition(request, clientKind, accessToken, user.id, position.id)).isActive
    ).toBe(false);
  } else {
    const assignmentRow = crudTableRow(view, clientKind, positionCode);
    await expect(assignmentRow).toBeVisible({ timeout: 30_000 });
    await expect(assignmentRow.getByText(positionName, { exact: false })).toBeVisible();
    const updateResponsePromise = page.waitForResponse(response =>
      response.request().method() === 'PUT'
        && response.url().endsWith(
          `/api/v1/organization/user-positions/${created.id}`
        ));
    await assignmentRow.getByRole('button', { name: '设为主职位', exact: true }).click();
    expect((await updateResponsePromise).ok()).toBeTruthy();
    const disableResponsePromise = page.waitForResponse(response =>
      response.request().method() === 'POST'
        && response.url().endsWith(
          `/api/v1/organization/user-positions/${created.id}/disable`
        ));
    await assignmentRow.getByRole('button', { name: '取消隶属', exact: true }).click();
    await confirmDisable(page, clientKind);
    expect((await disableResponsePromise).ok()).toBeTruthy();
    await expect(assignmentRow.getByText('主职位', { exact: true })).toBeVisible({
      timeout: 15_000
    });
    await expect(assignmentRow.getByText('已取消', { exact: true })).toBeVisible({
      timeout: 15_000
    });
  }

  const persisted = await getUserPosition(
    request,
    clientKind,
    accessToken,
    user.id,
    position.id
  );
  expect(persisted.id).toBe(created.id);
  expect(persisted.userId).toBe(user.id);
  expect(persisted.positionId).toBe(position.id);
  expect(persisted.username).toBe(username);
  expect(persisted.positionCode).toBe(positionCode);
  expect(persisted.positionName).toBe(positionName);
  expect(persisted.isPrimary).toBe(false);
  expect(persisted.isActive).toBe(false);
  expect(persisted.version).toBeGreaterThan(created.version);
});

test('受限 Host 账号在租户上下文中访问用户职位隶属 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const hostToken = await loginAccessToken(request, clientKind);
  const tenantToken = await enterTenantAccessToken(request, clientKind, hostToken);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/user-positions?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /用户职位隶属/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'organization/user-positions'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
