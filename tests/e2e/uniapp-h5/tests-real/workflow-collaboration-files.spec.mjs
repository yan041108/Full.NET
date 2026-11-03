import { expect, test } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

test('真实 H5 接收方读取、复用及上传附件、编辑明细，撤权后销毁草稿', async ({ page }) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(20_000);
  const directory = process.env.FULLNET_E2E_FILES_DIRECTORY;
  const todo = process.env.FULLNET_E2E_FILES_TODO;
  const capability = process.env.FULLNET_E2E_FILES_CAPABILITY;
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const readWrites = []; page.on('request', request => {
    if (request.method() !== 'GET' && /my-inbox-messages\/.+\/read$/.test(request.url())) readWrites.push(request.url());
  });
  const message = process.env.FULLNET_E2E_MESSAGE_TODO;
  await page.goto(`/#/pages/notifications/inbox-detail?id=${message}`);
  // uni H5 将 pages.json 的首屏登录页规范化为根路径别名，查询仍必须完整保留。
  await expect(page).toHaveURL(/#\/(?:pages\/identity\/login)?\?resourceTypeKey=inbox_message&resourceId=/);
  await page.locator('input').nth(0).fill(process.env.FULLNET_E2E_FILES_USERNAME);
  await page.locator('input').nth(1).fill(process.env.FULLNET_E2E_PASSWORD);
  await page.getByTestId('login-submit').click();
  await expect(page).toHaveURL(new RegExp(`pages/notifications/inbox-detail\\?id=${message}`));
  await expect(page.getByText('notification-only body', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '查看关联流程', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`pages/workflow/todo-detail\\?id=${todo}`));
  // 撤销的消息不展示正文或可操作深链；只读权限不会偷偷标记已读。
  await page.goto(`/#/pages/notifications/inbox-detail?id=${process.env.FULLNET_E2E_MESSAGE_REVOKED}`);
  await expect(page.getByText('此消息已撤销，关联入口已关闭。', { exact: true })).toBeVisible();
  await expect(page.getByText('查看关联流程', { exact: true })).toHaveCount(0);
  await expect(page.getByText('notification-only body', { exact: true })).toHaveCount(0);
  await page.goto(`/#/pages/notifications/inbox-detail?id=${process.env.FULLNET_E2E_MESSAGE_INSTANCE}`);
  await page.getByRole('button', { name: '查看关联流程', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`pages/workflow/instance-detail\\?id=${process.env.FULLNET_E2E_FILES_INSTANCE}`));
  await expect(page.getByText('当前状态: 进行中', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '查看当前待办', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`pages/workflow/todo-detail\\?id=${todo}`));
  const input = page.locator('[data-field-key="inputFiles"]');
  const sourceFile = input.getByRole('button', { name: 'source-owned.png', exact: true });
  await expect(sourceFile).toBeVisible();
  const downloading = page.waitForEvent('download'); await sourceFile.focus(); await page.keyboard.press('Enter');
  const download = await downloading;
  expect(download.suggestedFilename()).toBe('source-owned.png');
  expect(await readFile(await download.path())).toEqual(await readFile(path.join(directory, 'input.png')));
  if (process.env.FULLNET_E2E_FILES_MODE === 'revoke') {
    await page.getByLabel('secret', { exact: true }).fill('discard-on-revoke');
    await writeFile(path.join(directory, 'ready-to-revoke.json'), '{}');
    await expect.poll(async () => { try { await readFile(path.join(directory, 'revoked.json')); return true; }
      catch (error) { if (error.code === 'ENOENT') return false; throw error; } }, { timeout: 30_000 }).toBe(true);
    const denied = page.waitForResponse(response => response.url().includes(`/form-attachments/${capability}/content`));
    await sourceFile.click(); expect((await denied).status()).toBe(403);
    await expect(input).toHaveCount(0); await expect(page.locator('[data-field-key="secret"]')).toHaveCount(0);
    await expect(page.getByRole('button', { name: '同意', exact: true })).toHaveCount(0);
    await page.goto(`/#/pages/workflow/todo-detail?id=${todo}`);
    await expect(page.getByText('资源已失效、不可见或当前账号无权访问，请从列表重新确认。', { exact: true })).toBeVisible();
    await expect(page.locator('[data-field-key="secret"]')).toHaveCount(0);
    expect(errors).toEqual([]); expect(readWrites).toEqual([]);
    await writeFile(path.join(directory, 'result.json'), JSON.stringify({ revoked: true })); return;
  }
  await page.setViewportSize({ width: 390, height: 844 });
  const returned = page.locator('[data-field-key="returnedFiles"]');
  const reuse = returned.locator(`[data-reuse-file-id="${capability}"]`);
  await expect(reuse).toBeVisible();
  const bounds = await reuse.boundingBox(); expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.x + bounds.width).toBeLessThanOrEqual(390);
  await reuse.click(); await expect(returned.getByRole('button', { name: 'source-owned.png', exact: true })).toBeVisible();
  const chooser = page.waitForEvent('filechooser'); await returned.getByRole('button', { name: '上传附件', exact: true }).click();
  const uploading = page.waitForResponse(response => response.url().includes(`/todos/${todo}/attachment-uploads`) && response.request().method() === 'POST');
  await (await chooser).setFiles(path.join(directory, 'target-owned.png'));
  const response = await uploading; expect(response.status(), await response.text()).toBe(200); const uploaded = await response.json();
  await expect(returned.getByRole('button', { name: 'target-owned.png', exact: true })).toBeVisible();
  await page.getByLabel('secret', { exact: true }).fill('target-private');
  const row = page.locator('[data-field-key="rows"] [data-row-index="0"]');
  await row.getByLabel('label', { exact: true }).fill('target result');
  await row.getByLabel('amount', { exact: true }).fill('8');
  await page.locator('textarea').fill('真实 H5 能力回传');
  const approve = page.getByRole('button', { name: '同意', exact: true });
  await expect(approve).toBeVisible();
  const approving = page.waitForResponse(response => response.url().endsWith(`/todos/${todo}/approve`));
  await approve.click();
  // 成功后页面立即导航，浏览器可能已释放响应体；状态码足以确认动作成功，结果由后端用例继续核验。
  const approved = await approving; expect(approved.status()).toBe(200);
  await expect(page).toHaveURL(/pages\/workflow\/todos/);
  await page.goto(`/#/pages/workflow/instance-detail?id=${process.env.FULLNET_E2E_FILES_INSTANCE}`);
  await expect(page.getByText('以下是当前权威状态，旧通知不会重新开启已关闭的活动。', { exact: true })).toBeVisible();
  await expect(page.getByText('查看当前待办', { exact: true })).toHaveCount(0);
  expect(errors).toEqual([]); expect(readWrites).toEqual([]);
  await writeFile(path.join(directory, 'result.json'), JSON.stringify({ fileId: uploaded.fileId }));
});
