import { expect, test } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { adminOrigin, enterDevelopmentTenant, loginAsHostUser, trackUiAccessToken } from './support/real-stack-auth.mjs';

test('真实接收成员读取安全名称、办理或在已打开页面撤销后清空授权详情', async ({ page }) => {
  test.setTimeout(180_000);
  const directory = process.env.FULLNET_E2E_FILES_DIRECTORY;
  const todo = process.env.FULLNET_E2E_FILES_TODO;
  const capability = process.env.FULLNET_E2E_FILES_CAPABILITY;
  expect(directory).toBeTruthy(); expect(todo).toMatch(/^[0-9a-f-]{36}$/iu);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const accessToken = trackUiAccessToken(page);
  await loginAsHostUser(page, process.env.FULLNET_E2E_FILES_USERNAME, process.env.FULLNET_E2E_PASSWORD);
  await enterDevelopmentTenant(page, process.env.FULLNET_E2E_FILES_TENANT);
  await page.goto(`/#/workflow/todos?resourceId=${todo}`);
  const input = page.getByTestId('workflow-form-attachment-inputFiles');
  await expect(input.getByRole('button', { name: 'source-owned.png', exact: true })).toBeVisible();
  const downloading = page.waitForEvent('download');
  await input.getByRole('button', { name: 'source-owned.png', exact: true }).click();
  const download = await downloading; const downloaded = await download.path();
  expect(download.suggestedFilename()).toBe('source-owned.png');
  expect(await readFile(downloaded)).toEqual(await readFile(path.join(directory, 'input.png')));
  const naked = await page.request.get(`${process.env.FULLNET_E2E_API_URL}/api/v1/workflow/todos/${todo}/form-attachments/${process.env.FULLNET_E2E_FILES_ORIGINAL}/content`,
    { headers: { Authorization: `Bearer ${accessToken()}`, Origin: adminOrigin('vue') } });
  expect(naked.status()).toBe(403);
  if (process.env.FULLNET_E2E_FILES_MODE === 'revoke') {
    await page.locator('[data-field-key="secret"] input').fill('discard-on-revoke');
    await writeFile(path.join(directory, 'ready-to-revoke.json'), '{}');
    await expect.poll(async () => {
      try { await readFile(path.join(directory, 'revoked.json')); return true; }
      catch (error) { if (error.code === 'ENOENT') return false; throw error; }
    }, { timeout: 30_000 }).toBe(true);
    const denied = page.waitForResponse(response => response.url().includes(`/form-attachments/${capability}/content`));
    await input.getByRole('button', { name: 'source-owned.png', exact: true }).click();
    expect((await denied).status()).toBe(403);
    await expect(page.getByTestId('workflow-todo-approve')).toHaveCount(0);
    await expect(page.getByTestId('workflow-form-attachment-inputFiles')).toHaveCount(0);
    await expect(page.locator('[data-field-key="secret"] input')).toHaveCount(0);
    expect(errors).toEqual([]);
    await writeFile(path.join(directory, 'result.json'), JSON.stringify({ revoked: true }));
    return;
  }
  const returned = page.getByTestId('workflow-form-attachment-returnedFiles');
  await page.setViewportSize({ width: 390, height: 844 });
  const reuse = returned.locator(`[data-reuse-file-id="${capability}"]`);
  await expect(reuse).toBeVisible();
  // 抽屉有宽度过渡，等待真实布局收敛后仍按完整视口边界断言，不能读取过渡首帧。
  await expect.poll(async () => {
    const bounds = await reuse.boundingBox();
    return bounds !== null && bounds.x >= 0 && bounds.x + bounds.width <= 390;
  }).toBe(true);
  await page.setViewportSize({ width: 1280, height: 900 });
  await returned.locator(`[data-reuse-file-id="${capability}"]`).click();
  await expect(returned.getByRole('button', { name: 'source-owned.png', exact: true })).toBeVisible();
  const uploading = page.waitForResponse(response => response.url().includes(`/todos/${todo}/attachment-uploads`) && response.request().method() === 'POST');
  await returned.locator('input[type=file]').setInputFiles(path.join(directory, 'target-owned.png'));
  const response = await uploading; expect(response.status(), await response.text()).toBe(200);
  const uploaded = await response.json(); await expect(returned.getByRole('button', { name: 'target-owned.png', exact: true })).toBeVisible();
  await page.locator('[data-field-key="secret"] input').fill('target-private');
  const rows = page.getByTestId('workflow-form-subtable-rows');
  await rows.locator('[data-row-index="0"] [data-column-key="label"] input').fill('target result');
  await rows.locator('[data-row-index="0"] [data-column-key="amount"] input').fill('8');
  await page.getByTestId('workflow-todo-comment').fill('真实浏览器能力回传');
  const approving = page.waitForResponse(result => result.url().endsWith(`/todos/${todo}/approve`));
  await page.getByTestId('workflow-todo-approve').click(); const approved = await approving;
  expect(approved.status(), await approved.text()).toBe(200);
  expect(errors).toEqual([]);
  await writeFile(path.join(directory, 'result.json'), JSON.stringify({ fileId: uploaded.fileId }));
});
