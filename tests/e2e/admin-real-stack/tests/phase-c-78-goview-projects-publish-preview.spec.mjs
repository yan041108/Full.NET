import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  createGoViewProjectViaApi,
  expectGoViewPreviewReadOnlyShape,
  getGoViewProjectViaApi,
  listGoViewProjectVersionsViaApi,
  listGoViewProjectsViaApi,
  previewGoViewProjectViaApi,
  publishGoViewProjectViaApi,
  updateGoViewProjectViaApi
} from './support/goview-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'GoView 仅 Vue 交付线');
}

const canvasWithMarker = JSON.stringify({
  width: 1920,
  height: 1080,
  backgroundColor: '#0a1628',
  components: [{ id: 'e2e-78', type: 'placeholder' }]
});

test('API：大屏草稿保存、发布快照与只读预览（清单 78）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: listResponse } = await listGoViewProjectsViaApi(request, clientKind);
  expect(listResponse.ok()).toBeTruthy();
  expect(Array.isArray(await listResponse.json())).toBeTruthy();

  const suffix = randomUUID().slice(0, 8);
  const projectKey = `e2e_goview_${suffix}`;

  const { response: invalidCreate } = await createGoViewProjectViaApi(request, clientKind, {
    projectKey,
    name: `E2E GoView ${suffix}`,
    canvasJson: '{not-json',
    isEnabled: true
  });
  expect(invalidCreate.status()).toBe(422);

  const { response: createResponse } = await createGoViewProjectViaApi(request, clientKind, {
    projectKey,
    name: `E2E GoView ${suffix}`,
    canvasJson: null,
    isEnabled: true
  });
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.projectKey).toBe(projectKey);
  expect(created.latestPublishedVersionNumber).toBe(0);

  const { response: updateResponse } = await updateGoViewProjectViaApi(
    request,
    clientKind,
    created.id,
    {
      name: created.name,
      canvasJson: canvasWithMarker,
      isEnabled: true,
      version: created.version
    }
  );
  expect(updateResponse.ok()).toBeTruthy();
  const updated = await updateResponse.json();
  expect(updated.version).toBeGreaterThan(created.version);

  const { response: previewUnpublished } = await previewGoViewProjectViaApi(
    request,
    clientKind,
    created.id
  );
  expect(previewUnpublished.status()).toBe(422);

  const { response: publishResponse } = await publishGoViewProjectViaApi(
    request,
    clientKind,
    created.id,
    { changeNote: 'e2e-78', version: updated.version }
  );
  expect(publishResponse.ok()).toBeTruthy();
  const published = await publishResponse.json();
  expect(published.versionNumber).toBe(1);

  const { response: versionsResponse } = await listGoViewProjectVersionsViaApi(
    request,
    clientKind,
    created.id
  );
  expect(versionsResponse.ok()).toBeTruthy();
  const versions = await versionsResponse.json();
  expect(versions.length).toBeGreaterThanOrEqual(1);

  const { response: previewResponse } = await previewGoViewProjectViaApi(
    request,
    clientKind,
    created.id,
    {}
  );
  expect(previewResponse.ok()).toBeTruthy();
  const preview = await previewResponse.json();
  expectGoViewPreviewReadOnlyShape(preview);
  expect(preview.canvasJson).toContain('e2e-78');

  const { response: missing } = await getGoViewProjectViaApi(request, clientKind, randomUUID());
  expect(missing.status()).toBe(404);
});

test('UI：大屏项目列表与编辑入口（清单 78）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /大屏项目/);
  await expect(page.locator('.goview-projects-view')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('大屏项目', { exact: true }).first()).toBeVisible();
  await expect(page.getByTestId('goview-project-create')).toBeVisible();
  await page.getByTestId('goview-project-create').click();
  await expect(page.getByTestId('goview-project-key-input')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
