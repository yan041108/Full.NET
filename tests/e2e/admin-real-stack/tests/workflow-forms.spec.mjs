import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('管理员可在严格 CSP 下通过 VForm3 完成表单草稿回读、保存、发布与冻结版本读取', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'VForm3 表单设计器仅在 Vue 交付线验收');
  test.setTimeout(90_000);

  const runtimeErrors = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    const text = message.text();
    if (/content security policy|unsafe-eval|failed to resolve component|unknown custom element/iu.test(text)) {
      runtimeErrors.push(text);
    }
  });

  const stamp = Date.now().toString(36);
  const formKey = `e2e.form.${stamp}`;
  const fieldKey = `amount_${stamp}`;
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /工作流表单/, '工作流');

  const view = page.locator('.workflow-forms');
  await expect(view.getByRole('heading', { name: '工作流表单', level: 1 })).toBeVisible();
  await view.getByTestId('workflow-form-create').click();
  await view.getByTestId('workflow-form-key').fill(formKey);
  await view.getByTestId('workflow-form-create-submit').click();

  const row = view.getByRole('row').filter({ hasText: formKey });
  await expect(row).toBeVisible({ timeout: 15_000 });

  const accessToken = await loginHostAdminAccessToken(request, testInfo.project.metadata.clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(testInfo.project.metadata.clientKind),
    'Content-Type': 'application/json'
  };
  const formsResponse = await request.get(`${apiBaseUrl}/api/v1/workflow/forms`, { headers });
  expect(formsResponse.status()).toBe(200);
  const created = (await formsResponse.json()).find(item => item.formKey === formKey);
  expect(created?.id).toBeTruthy();
  const updateResponse = await request.put(
    `${apiBaseUrl}/api/v1/workflow/forms/${created.id}/draft`,
    {
      data: {
        expectedRevision: created.draftRevision,
        draft: {
          schemaVersion: 1,
          adapterVersion: 1,
          sections: [{
            sectionKey: 'main',
            fields: [{
              fieldKey,
              fieldTypeKey: 'money',
              required: true,
              constraints: {}
            }]
          }]
        }
      },
      headers
    }
  );
  expect(updateResponse.status()).toBe(200);
  await page.reload();

  const refreshedView = page.locator('.workflow-forms');
  const refreshedRow = refreshedView.getByRole('row').filter({ hasText: formKey });
  await expect(refreshedRow).toBeVisible({ timeout: 15_000 });
  await refreshedRow.getByTestId('workflow-form-edit').click();
  const designer = refreshedView.getByTestId('vform3-workflow-designer');
  await expect(designer).toBeVisible();
  // VForm3 会把必填标记与字段标签合成渲染，不应将第三方 DOM 文本结构当成稳定契约。
  await expect(designer.getByText(fieldKey, { exact: false }).first()).toBeVisible({ timeout: 15_000 });
  await refreshedView.getByTestId('workflow-form-save').click();
  await expect(refreshedView.getByRole('dialog').getByText('Revision 3', { exact: true }))
    .toBeVisible({ timeout: 15_000 });

  await refreshedView.getByTestId('workflow-form-close-editor').click();
  await refreshedRow.getByTestId('workflow-form-publish').click();
  await expect(refreshedRow.locator('td').nth(2)).not.toHaveText('—', { timeout: 15_000 });

  const authoritativeResponse = await request.get(`${apiBaseUrl}/api/v1/workflow/forms`, { headers });
  expect(authoritativeResponse.status()).toBe(200);
  const authoritative = (await authoritativeResponse.json()).find(item => item.formKey === formKey);
  expect(authoritative?.latestPublishedVersionId).toBeTruthy();

  const frozenResponse = await request.get(
    `${apiBaseUrl}/api/v1/workflow/form-versions/${authoritative.latestPublishedVersionId}`,
    { headers }
  );
  expect(frozenResponse.status()).toBe(200);
  const frozen = await frozenResponse.json();
  expect(JSON.parse(frozen.formSchemaJson).sections
    .flatMap(section => section.fields)
    .some(field => field.fieldKey === fieldKey && field.fieldTypeKey === 'money')).toBeTruthy();
  expect(runtimeErrors).toEqual([]);
});

test('管理员可通过 Workflow-Vue3 创建、保存并绑定已发布表单发布流程定义', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'Workflow-Vue3 设计器仅在 Vue 交付线验收');
  test.setTimeout(90_000);

  const runtimeErrors = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    const text = message.text();
    if (/content security policy|unsafe-eval|failed to resolve component|unknown custom element/iu.test(text)) {
      runtimeErrors.push(text);
    }
  });

  const clientKind = testInfo.project.metadata.clientKind;
  const stamp = Date.now().toString(36);
  const formKey = `e2e.definition.form.${stamp}`;
  const definitionKey = `e2e.definition.${stamp}`;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
  const createFormResponse = await request.post(`${apiBaseUrl}/api/v1/workflow/forms/`, {
    data: {
      formKey,
      draft: {
        schemaVersion: 1,
        adapterVersion: 1,
        sections: [{
          sectionKey: 'main',
          fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }]
        }]
      }
    },
    headers
  });
  expect(createFormResponse.status()).toBe(201);
  const form = await createFormResponse.json();
  const publishFormResponse = await request.post(
    `${apiBaseUrl}/api/v1/workflow/forms/${form.id}/publish`,
    { data: { expectedRevision: form.draftRevision }, headers }
  );
  expect(publishFormResponse.status()).toBe(200);
  const formVersion = await publishFormResponse.json();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /工作流定义/, '工作流');
  const view = page.locator('.workflow-definitions');
  await expect(view.getByRole('heading', { name: '工作流定义', level: 1 })).toBeVisible();
  await view.getByTestId('workflow-definition-create').click();
  await view.getByTestId('workflow-definition-key').fill(definitionKey);
  await view.getByTestId('workflow-definition-create-submit').click();

  const designer = view.getByTestId('workflow-vue3-designer');
  await expect(designer).toBeVisible({ timeout: 15_000 });
  await view.getByTestId('workflow-definition-save').click();
  await expect(view.getByRole('dialog').getByText('Revision 2', { exact: true }))
    .toBeVisible({ timeout: 15_000 });
  await view.getByTestId('workflow-definition-form-version').selectOption(formVersion.id);
  await view.getByTestId('workflow-definition-publish').click();

  const definitionsResponse = await request.get(`${apiBaseUrl}/api/v1/workflow/definitions`, { headers });
  expect(definitionsResponse.status()).toBe(200);
  await expect.poll(async () => {
    const response = await request.get(`${apiBaseUrl}/api/v1/workflow/definitions`, { headers });
    const authoritative = (await response.json()).find(item => item.definitionKey === definitionKey);
    return authoritative?.latestPublishedVersionId ?? null;
  }).not.toBeNull();
  expect(runtimeErrors).toEqual([]);
});

test('仅有表单读取权限时导航可达但写按钮与写 API 均失败关闭', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '精确动作权限仅在 Vue 交付线验收');
  test.setTimeout(90_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'workflow.forms.read'
    ]
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const forbidden = await request.post(`${apiBaseUrl}/api/v1/workflow/forms`, {
    data: {
      formKey: `e2e.forbidden.${Date.now().toString(36)}`,
      draft: {
        schemaVersion: 1,
        adapterVersion: 1,
        sections: [{
          sectionKey: 'main',
          fields: [{
            fieldKey: 'summary',
            fieldTypeKey: 'text',
            required: true,
            constraints: {}
          }]
        }]
      }
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind),
      'Content-Type': 'application/json'
    }
  });
  expect(forbidden.status()).toBe(403);
  expect((await forbidden.json()).code).toBe('authorization.permission_denied');

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /工作流表单/, '工作流');
  const view = page.locator('.workflow-forms');
  await expect(view.getByRole('heading', { name: '工作流表单', level: 1 })).toBeVisible();
  await expect(view.getByTestId('workflow-form-create')).toHaveCount(0);
  await expect(view.getByTestId('workflow-form-edit')).toHaveCount(0);
  await expect(view.getByTestId('workflow-form-publish')).toHaveCount(0);
});
