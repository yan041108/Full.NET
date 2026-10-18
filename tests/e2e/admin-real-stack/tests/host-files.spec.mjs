import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  trackUiAccessToken,
  statusPath,
  uploadHostFileViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueFileName(clientKind, prefix) {
  // 双端并行时用时间戳与客户端后缀保证 Host 全局唯一文件名。
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `${prefix}-${stamp}-${suffix}.txt`;
}

test('Host 管理员可从真实 API 加载文件列表', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const fileName = uniqueFileName(clientKind, 'e2e-file');
  await uploadHostFileViaApi(request, clientKind, {
    fileName,
    content: `real-stack ${clientKind}`,
    contentType: 'text/plain'
  });

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /文件管理/);

  const hostFilesView = clientKind === 'layui'
    ? page.locator('[data-route-view="host-files"]')
    : page.locator('.host-files-view');

  await expect(hostFilesView.getByRole('heading', { name: '文件管理', exact: true })).toBeVisible();
  await expect(hostFilesView.getByText(fileName, { exact: true })).toBeVisible();
});

test('受限 Host 账号访问文件 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /文件管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'files/host-files'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

test('Host 管理员可管理虚拟目录、更新元数据并查询引用（清单 31）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const authHeaders = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };
  const jsonHeaders = { ...authHeaders, 'Content-Type': 'application/json' };
  const stamp = Date.now().toString(36);

  const folderResponse = await request.post(`${apiBaseUrl}/api/v1/files/host-folders`, {
    headers: jsonHeaders,
    data: {
      parentId: null,
      name: `e2e-folder-${stamp}`,
      displayOrder: 0
    }
  });
  expect(folderResponse.ok()).toBeTruthy();
  const folder = await folderResponse.json();
  expect(folder.id).toBeTruthy();

  const treeResponse = await request.get(`${apiBaseUrl}/api/v1/files/host-folders/tree`, {
    headers: authHeaders
  });
  expect(treeResponse.ok()).toBeTruthy();
  const tree = await treeResponse.json();
  expect(Array.isArray(tree)).toBe(true);

  const fileName = uniqueFileName(clientKind, 'e2e-meta');
  const uploaded = await uploadHostFileViaApi(request, clientKind, {
    fileName,
    content: `metadata ${stamp}`,
    accessToken
  });

  const detailResponse = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files/${uploaded.id}`,
    { headers: authHeaders }
  );
  expect(detailResponse.ok()).toBeTruthy();
  const file = await detailResponse.json();

  const renamed = `renamed-${stamp}.txt`;
  const updateResponse = await request.post(
    `${apiBaseUrl}/api/v1/files/host-files/${uploaded.id}/update`,
    {
      headers: jsonHeaders,
      data: {
        expectedRevision: file.revision,
        originalFileName: renamed,
        folderId: folder.id
      }
    }
  );
  expect(updateResponse.ok()).toBeTruthy();
  const updated = await updateResponse.json();
  expect(updated.originalFileName).toBe(renamed);
  expect(updated.folderId).toBe(folder.id);
  expect(updated.revision).toBeGreaterThan(file.revision);

  const conflictResponse = await request.post(
    `${apiBaseUrl}/api/v1/files/host-files/${uploaded.id}/update`,
    {
      headers: jsonHeaders,
      data: {
        expectedRevision: file.revision,
        originalFileName: 'stale.txt',
        folderId: null
      }
    }
  );
  expect(conflictResponse.status()).toBe(409);

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files?page=1&pageSize=50&folderId=${folder.id}`,
    { headers: authHeaders }
  );
  expect(listResponse.ok()).toBeTruthy();
  const listBody = await listResponse.json();
  expect(listBody.items.some(item => item.id === uploaded.id)).toBe(true);

  const referencesResponse = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files/${uploaded.id}/references?page=1&pageSize=20`,
    { headers: authHeaders }
  );
  expect(referencesResponse.ok()).toBeTruthy();
  const references = await referencesResponse.json();
  expect(Array.isArray(references.items)).toBe(true);
});

test('Vue 文件管理可编辑元数据并查看引用（清单 31）', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '文件元数据 UI 仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const fileName = uniqueFileName(clientKind, 'e2e-ui-meta');
  await uploadHostFileViaApi(request, clientKind, {
    fileName,
    content: 'ui metadata'
  });

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /文件管理/);

  const hostFilesView = page.locator('.host-files-view');
  await expect(hostFilesView.getByText(fileName, { exact: true })).toBeVisible({ timeout: 15_000 });

  const fileRow = hostFilesView.getByRole('row').filter({ hasText: fileName }).first();
  await fileRow.getByRole('button', { name: '编辑元数据', exact: true }).click();
  await expect(page.getByRole('dialog', { name: '编辑元数据' })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: '返回工作台' }).click();

  await fileRow.getByRole('button', { name: '引用', exact: true }).click();
  await expect(page.locator('.el-drawer__header')).toContainText('引用', { timeout: 10_000 });
});

test('Host 管理员可批量上传、预览并批量删除（清单 32）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const authHeaders = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  const firstName = `batch-a-${stamp}-${suffix}.txt`;
  const secondName = `batch-b-${stamp}-${suffix}.txt`;
  const firstBody = `batch-one-${stamp}`;
  const secondBody = `batch-two-${stamp}`;

  const multipart = new FormData();
  multipart.append('files', new File([firstBody], firstName, { type: 'text/plain' }));
  multipart.append('files', new File([secondBody], secondName, { type: 'text/plain' }));
  const uploadResponse = await request.post(`${apiBaseUrl}/api/v1/files/host-files/batch-upload`, {
    headers: authHeaders,
    multipart
  });
  expect(uploadResponse.ok()).toBeTruthy();
  const uploaded = await uploadResponse.json();
  expect(uploaded.succeededCount).toBe(2);
  expect(uploaded.results).toHaveLength(2);
  expect(uploaded.results.every(item => item.succeeded)).toBe(true);
  const fileIds = uploaded.results.map(item => item.file.id);

  const previewResponse = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files/${fileIds[0]}/preview`,
    { headers: authHeaders }
  );
  expect(previewResponse.ok()).toBeTruthy();
  expect(previewResponse.headers()['content-type']).toContain('text/plain');
  expect((await previewResponse.body()).toString('utf8')).toBe(firstBody);

  const deleteResponse = await request.post(`${apiBaseUrl}/api/v1/files/host-files/batch-delete`, {
    headers: { ...authHeaders, 'Content-Type': 'application/json' },
    data: { fileIds }
  });
  expect(deleteResponse.ok()).toBeTruthy();
  const deleted = await deleteResponse.json();
  expect(deleted.succeededCount).toBe(2);
  expect(deleted.results.every(item => item.succeeded)).toBe(true);

  for (const fileId of fileIds) {
    const detail = await request.get(`${apiBaseUrl}/api/v1/files/host-files/${fileId}`, {
      headers: authHeaders
    });
    expect(detail.status()).toBe(404);
  }
});

test('Vue 文件管理可批量上传并批量删除（清单 32）', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '批量文件 UI 仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const stamp = Date.now().toString(36);
  const firstName = `batch-ui-a-${stamp}.txt`;
  const secondName = `batch-ui-b-${stamp}.txt`;

  const currentAccessToken = trackUiAccessToken(page);
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /文件管理/);

  const hostFilesView = page.locator('.host-files-view');
  const selectableFileRow = fileName => hostFilesView.getByRole('row')
    .filter({ hasText: fileName, has: page.getByRole('checkbox') });
  await hostFilesView.locator('[data-testid="host-files-file-input"]').setInputFiles([
    {
      name: firstName,
      mimeType: 'text/plain',
      buffer: Buffer.from('ui-batch-a')
    },
    {
      name: secondName,
      mimeType: 'text/plain',
      buffer: Buffer.from('ui-batch-b')
    }
  ]);
  const uploadedResponse = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/v1/files/host-files/batch-upload');
  await hostFilesView.getByTestId('host-files-upload').click();
  const uploaded = await uploadedResponse;
  expect(uploaded.ok()).toBe(true);
  const uploadResult = await uploaded.json();
  expect(uploadResult.succeededCount).toBe(2);
  expect(uploadResult.results.every(item => item.succeeded && item.file)).toBe(true);
  const fileIds = uploadResult.results.map(item => item.file.id);
  expect(fileIds).toHaveLength(2);
  await expect(selectableFileRow(firstName))
    .toBeVisible({ timeout: 15_000 });
  await expect(selectableFileRow(secondName))
    .toBeVisible();

  for (const fileName of [firstName, secondName]) {
    // 上传回执也展示同名文件；仅选择带复选框的数据行，避免误中回执行。
    const row = selectableFileRow(fileName);
    await row.locator('.el-checkbox').click();
    await expect(row.getByRole('checkbox')).toBeChecked();
  }
  await hostFilesView.getByTestId('host-files-batch-delete').click();
  const deletedResponse = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/v1/files/host-files/batch-delete');
  await page.getByTestId('host-files-confirm-accept').click();
  const deleted = await deletedResponse;
  expect(deleted.ok()).toBe(true);
  const deleteResult = await deleted.json();
  expect(deleteResult.succeededCount).toBe(2);
  expect(deleteResult.results.every(item => item.succeeded)).toBe(true);
  expect(deleteResult.results.map(item => item.fileId).sort()).toEqual([...fileIds].sort());

  // 上传及删除回执应保留文件名；验证业务列表移除，并直接读取服务器确认已删除。
  for (const fileName of [firstName, secondName]) {
    await expect(selectableFileRow(fileName))
      .toHaveCount(0, { timeout: 15_000 });
  }
  for (const fileId of fileIds) {
    const detail = await request.get(`${apiBaseUrl}/api/v1/files/host-files/${fileId}`, {
      headers: { Authorization: `Bearer ${currentAccessToken()}`, Origin: adminOrigin(clientKind) }
    });
    expect(detail.status()).toBe(404);
  }
});
