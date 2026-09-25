import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  createHostDocumentPreviewTaskViaApi,
  listHostDocumentPreviewTasksViaApi,
  uploadHostDocumentOfficeVersionViaApi,
  uploadHostDocumentVersionViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 预览任务页仅在 Vue 管理端交付。');
}

test('API：Office 预览任务入队与非 Office 拒绝（清单 64）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const stamp = Date.now().toString(36);
  const item = await createHostDocumentItemViaApi(request, clientKind, {
    title: `e2e-preview-task-${stamp}`
  });

  await uploadHostDocumentVersionViaApi(request, clientKind, item.id, `plain-${stamp}`, 'note.txt');
  const nonOffice = await createHostDocumentPreviewTaskViaApi(request, clientKind, item.id);
  expect(nonOffice.status()).toBe(422);
  expect((await nonOffice.json()).code).toBe('document.office_preview.unsupported_source');

  const officeUpload = await uploadHostDocumentOfficeVersionViaApi(
    request,
    clientKind,
    item.id,
    `docx-${stamp}`
  );
  expect(officeUpload.ok()).toBeTruthy();

  const createTask = await createHostDocumentPreviewTaskViaApi(request, clientKind, item.id);
  expect(createTask.status()).toBe(201);
  const task = await createTask.json();
  expect(task.documentItemId).toBe(item.id);
  expect(task.statusKey).toBe('pending');
  expect(['disabled', 'external_http', 'external_process']).toContain(task.providerKey);

  const listResponse = await listHostDocumentPreviewTasksViaApi(request, clientKind, item.id);
  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(pageResult.items.some((entry) => entry.id === task.id)).toBe(true);
});

test('UI：预览任务列表与提交入口（清单 64）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /预览任务/, '文档');

  await expect(page.getByRole('heading', { name: '预览任务', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('document-preview-task-create')).toBeVisible();
  await expect(page.getByTestId('document-preview-task-filter')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
