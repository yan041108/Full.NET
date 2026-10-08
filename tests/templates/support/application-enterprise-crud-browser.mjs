import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { runPrintingBrowserResponseAction } from './application-printing-browser-lifecycle.mjs';

const requireE2e = createRequire(new URL('../../e2e/admin-real-stack/package.json', import.meta.url));
const { expect } = requireE2e('@playwright/test');

// 使用独立应用的实际会话和组织夹具，所有业务写入由 Vue 表单完成。
export async function verifyEnterpriseRequestCrudBrowser(page, origin, fixture) {
  const path = '/api/v1/enterprise_request/enterprise-requests';
  const number = fixture.importValues[0] + '-CRUD';
  const view = page.locator('.generated-crud-view');
  await view.getByRole('button', { name: '创建', exact: true }).click();
  const createDialog = page.getByRole('dialog', { name: '创建', exact: true });
  await createDialog.getByLabel('OrganizationUnitId', { exact: true }).fill(fixture.importValues[4]);
  await createDialog.getByLabel('RequestNumber', { exact: true }).fill(number);
  await createDialog.getByLabel('Title', { exact: true }).fill('Enterprise Browser CRUD');
  await createDialog.getByLabel('TotalAmount', { exact: true }).fill('67.89');
  await createDialog.getByLabel('ApplicantUserId', { exact: true }).fill(fixture.importValues[3]);
  const createdHttp = await runPrintingBrowserResponseAction(page,
    response => new URL(response.url()).pathname === path && response.request().method() === 'POST',
    () => createDialog.getByRole('button', { name: '保存', exact: true }).click());
  assert.equal(createdHttp.status(), 201);
  const created = await createdHttp.json();
  assert.equal(created.tenantId, fixture.tenantId);
  assert.equal(created.organizationUnitId, fixture.importValues[4]);
  assert.equal(created.applicantUserId, fixture.importValues[3]);
  assert.equal(created.requestNumber, number);
  await expect(createDialog).toBeHidden();
  const row = view.locator('.el-table__row').filter({ hasText: number });
  await expect(row).toContainText('Enterprise Browser CRUD');

  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const editDialog = page.getByRole('dialog', { name: '编辑', exact: true });
  await editDialog.getByLabel('Title', { exact: true }).fill('Enterprise Browser CRUD updated');
  const updatedHttp = await runPrintingBrowserResponseAction(page,
    response => new URL(response.url()).pathname === path + '/' + created.id && response.request().method() === 'PUT',
    () => editDialog.getByRole('button', { name: '保存', exact: true }).click());
  assert.equal(updatedHttp.status(), 200);
  assert.equal(updatedHttp.request().postDataJSON().version, Number(created.version));
  await expect(editDialog).toBeHidden();
  await expect(row).toContainText('Enterprise Browser CRUD updated');

  // 离开缓存页面时同步关闭并清理编辑输入；重新进入只读取当前权威列表。
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  await editDialog.getByLabel('Title', { exact: true }).fill('discarded page input');
  await page.goto(origin + '/#/tenant-context');
  await expect(editDialog).toBeHidden();
  await page.goto(origin + '/#/enterprise-requests');
  await expect(row).toContainText('Enterprise Browser CRUD updated');
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  await expect(editDialog.getByLabel('Title', { exact: true })).toHaveValue('Enterprise Browser CRUD updated');
  await editDialog.getByRole('button', { name: '取消', exact: true }).click();

  let deleteRequests = 0;
  const countDelete = request => {
    if (new URL(request.url()).pathname === path + '/' + created.id + '/delete' && request.method() === 'POST') deleteRequests++;
  };
  page.on('request', countDelete);
  try {
    await row.getByRole('button', { name: '删除', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: '确认删除', exact: true });
    await expect(dialog).toBeVisible(); assert.equal(deleteRequests, 0);
    await dialog.getByRole('button', { name: '取消', exact: true }).click();
    await expect(dialog).toBeHidden(); assert.equal(deleteRequests, 0); await expect(row).toBeVisible();
    await row.getByRole('button', { name: '删除', exact: true }).click();
    const deletedHttp = await runPrintingBrowserResponseAction(page,
      response => new URL(response.url()).pathname === path + '/' + created.id + '/delete' && response.request().method() === 'POST',
      () => dialog.getByRole('button', { name: '确认删除', exact: true }).click());
    assert.equal(deletedHttp.status(), 200); assert.equal(deleteRequests, 1);
    await expect(dialog).toBeHidden(); await expect(row).toHaveCount(0);
    await expect(view.getByRole('alert')).toHaveCount(0);
    return { completed: true, created: true, updated: true, pageInputDiscarded: true, deleteCancelled: true, deleteRequests };
  } finally { page.off('request', countDelete); }
}
