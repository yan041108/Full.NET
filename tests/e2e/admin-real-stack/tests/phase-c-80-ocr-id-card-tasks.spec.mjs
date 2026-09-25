import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  confirmOcrIdCardTaskViaApi,
  createOcrIdCardTaskViaApi,
  expectOcrProviderConfigMasked,
  getOcrIdCardTaskViaApi,
  getOcrProviderConfigViaApi,
  listOcrIdCardTasksViaApi,
  ocrPaddleIdCardProviderKey
} from './support/ocr-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'OCR 仅 Vue 交付线');
}

test('API：PaddleOCR Provider 与身份证任务受控创建（清单 80）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: providerResponse } = await getOcrProviderConfigViaApi(
    request,
    clientKind,
    ocrPaddleIdCardProviderKey
  );
  expect(providerResponse.ok()).toBeTruthy();
  expectOcrProviderConfigMasked(await providerResponse.json());

  const { response: providerMissing } = await getOcrProviderConfigViaApi(
    request,
    clientKind,
    'unknown_ocr_provider'
  );
  expect(providerMissing.status()).toBe(404);

  const { response: tasksList } = await listOcrIdCardTasksViaApi(request, clientKind);
  expect(tasksList.ok()).toBeTruthy();
  expect(Array.isArray((await tasksList.json()).items)).toBeTruthy();

  const { response: createInvalidFile } = await createOcrIdCardTaskViaApi(request, clientKind, {
    sourceFileId: randomUUID()
  });
  expect(createInvalidFile.status()).toBe(422);

  const missingTaskId = randomUUID();
  const { response: taskMissing } = await getOcrIdCardTaskViaApi(
    request,
    clientKind,
    missingTaskId
  );
  expect(taskMissing.status()).toBe(404);

  const { response: confirmMissing } = await confirmOcrIdCardTaskViaApi(
    request,
    clientKind,
    missingTaskId,
    {
      name: '测试',
      idNumber: '110101199001011234',
      gender: null,
      nation: null,
      address: null,
      birthDate: null,
      version: 1
    }
  );
  expect(confirmMissing.status()).toBe(404);
});

test('UI：OCR Provider 与身份证识别任务页（清单 80）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /OCR Provider/);
  await expect(page.getByText('OCR Provider 配置', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('ocr-provider-save')).toBeVisible();

  await clickMainNavLink(page, /身份证 OCR/);
  await expect(page.getByText('身份证 OCR 任务', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('ocr-id-card-upload')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
