import { expect, test } from '@playwright/test';
import { adminOrigin, enterDevelopmentTenant, loginAsHostAdmin, trackUiAccessToken } from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const requestPath = '/api/v1/enterprise_request/enterprise-requests';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('租户管理员通过 Vue 创建、编辑和删除企业申请单', async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const accessToken = trackUiAccessToken(page);
  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  const headers = { Authorization: `Bearer ${accessToken()}`, Origin: adminOrigin(testInfo.project.metadata.clientKind) };
  const meResponse = await page.request.get(`${apiBaseUrl}/api/v1/me`, { headers });
  expect(meResponse.status()).toBe(200);
  const me = await meResponse.json();
  // 通过所有者 API 建立组织夹具；申请单的实际写入由浏览器表单完成。
  const unitResponse = await page.request.post(`${apiBaseUrl}/api/v1/organization/units`, {
    headers, data: { parentId: null, code: `er-e2e-${Date.now()}`, name: 'ER E2E Unit', displayOrder: 10 }
  });
  expect(unitResponse.status()).toBe(201);
  const unit = await unitResponse.json();
  const assignment = await page.request.post(`${apiBaseUrl}/api/v1/organization/user-units`, {
    headers, data: { userId: me.id, unitId: unit.id, isPrimary: false }
  });
  expect(assignment.status()).toBe(201);

  const listResponse = page.waitForResponse(response => response.url().includes(requestPath)
    && response.request().method() === 'GET');
  await page.goto('/#/enterprise-requests');
  expect((await listResponse).status()).toBe(200);
  const view = page.locator('.generated-crud-view');
  await view.getByRole('button', { name: '创建', exact: true }).click();
  const createDialog = page.getByRole('dialog', { name: '创建', exact: true });
  const requestNumber = `REQ-${Date.now()}`;
  const title = `E2E request ${requestNumber}`;
  await createDialog.getByLabel('所属机构标识', { exact: true }).fill(unit.id);
  await createDialog.getByLabel('申请编号', { exact: true }).fill(requestNumber);
  await createDialog.getByLabel('标题', { exact: true }).fill(title);
  await createDialog.getByLabel('申请金额', { exact: true }).fill('12.5');
  await createDialog.getByLabel('申请人标识', { exact: true }).fill(me.id);
  const createdResponse = page.waitForResponse(response => new URL(response.url()).pathname === requestPath
    && response.request().method() === 'POST');
  await createDialog.getByRole('button', { name: '保存', exact: true }).click();
  const createdHttp = await createdResponse;
  expect(createdHttp.status()).toBe(201);
  const created = await createdHttp.json();
  expect(created.organizationUnitId).toBe(unit.id);
  expect(created.createdById).toBe(me.id);
  await expect(createDialog).toBeHidden();
  const row = view.locator('.el-table__row').filter({ hasText: requestNumber });
  await expect(row).toContainText(title);

  const detailResponse = page.waitForResponse(response => new URL(response.url()).pathname === `${requestPath}/${created.id}`
    && response.request().method() === 'GET');
  await row.getByRole('button', { name: '详情', exact: true }).click();
  expect((await detailResponse).status()).toBe(200);
  const detailDialog = page.getByRole('dialog', { name: '申请详情', exact: true });
  await expect(detailDialog).toContainText(title);
  await expect(detailDialog).toContainText('草稿');
  await detailDialog.getByRole('button', { name: '取消', exact: true }).click();
  await expect(detailDialog).toBeHidden();

  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const editDialog = page.getByRole('dialog', { name: '编辑', exact: true });
  await editDialog.getByLabel('标题', { exact: true }).fill(`${title} updated`);
  const updatedResponse = page.waitForResponse(response => new URL(response.url()).pathname === `${requestPath}/${created.id}`
    && response.request().method() === 'PUT');
  await editDialog.getByRole('button', { name: '保存', exact: true }).click();
  expect((await updatedResponse).status()).toBe(200);
  await expect(editDialog).toBeHidden();
  await expect(row).toContainText(`${title} updated`);

  const linesResponse = page.waitForResponse(response => new URL(response.url()).pathname === `${requestPath}/${created.id}/lines`
    && response.request().method() === 'GET');
  await row.getByRole('button', { name: '明细', exact: true }).click();
  expect((await linesResponse).status()).toBe(200);
  const linesDialog = page.getByRole('dialog', { name: '申请明细', exact: true });
  await linesDialog.getByRole('button', { name: '编辑明细', exact: true }).click();
  await linesDialog.getByRole('button', { name: '添加明细', exact: true }).click();
  await linesDialog.getByLabel('项目说明', { exact: true }).fill('E2E precision item');
  await linesDialog.getByLabel('数量', { exact: true }).fill('1.0001');
  await linesDialog.getByLabel('单价', { exact: true }).fill('50');
  const savedLines = page.waitForResponse(response => new URL(response.url()).pathname === `${requestPath}/${created.id}/lines`
    && response.request().method() === 'PUT');
  await linesDialog.getByRole('button', { name: '保存明细', exact: true }).click();
  const savedLinesHttp = await savedLines;
  expect(savedLinesHttp.status()).toBe(200);
  const savedLinesBody = await savedLinesHttp.json();
  expect(savedLinesBody.totalAmount).toBe('50.01');
  expect(savedLinesBody.items[0].lineAmount).toBe('50.01');
  await expect(linesDialog.getByRole('table')).toContainText('E2E precision item');
  await expect(row).toContainText('50.01');
  await linesDialog.getByRole('button', { name: '取消', exact: true }).click();
  await expect(linesDialog).toBeHidden();

  await row.getByRole('button', { name: '删除', exact: true }).click();
  const deleteDialog = page.getByRole('dialog', { name: '确认删除', exact: true });
  await expect(deleteDialog).toBeVisible();
  await deleteDialog.getByRole('button', { name: '取消', exact: true }).click();
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: '删除', exact: true }).click();
  const deletedResponse = page.waitForResponse(response => new URL(response.url()).pathname === `${requestPath}/${created.id}/delete`
    && response.request().method() === 'POST');
  await deleteDialog.getByRole('button', { name: '确认删除', exact: true }).click();
  expect((await deletedResponse).status()).toBe(200);
  await expect(row).toHaveCount(0);
  await expect(view.getByRole('alert')).toHaveCount(0);
});

test('Host 管理员可访问 enterprise-requests CRUD 页', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);
  await page.goto('/#/enterprise-requests');
  if (clientKind === 'vue') {
    await expect(page).toHaveURL(/\/enterprise-requests/);
    await expect(page.locator('.generated-crud-view, .enterprise-requests-view').first()).toBeVisible({ timeout: 15000 });
  } else {
    await expect(page).toHaveURL(/\/enterprise-requests/);
  }
});
