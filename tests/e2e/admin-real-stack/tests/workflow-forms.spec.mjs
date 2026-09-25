import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi,
  trackUiAccessToken
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
  const currentAccessToken = trackUiAccessToken(page);
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /工作流表单/, '工作流');

  const view = page.locator('.workflow-forms');
  await expect(view.getByRole('heading', { name: '工作流表单', level: 1 })).toBeVisible();
  await view.getByTestId('workflow-form-create').click();
  await view.getByTestId('workflow-form-key').fill(formKey);
  await view.getByTestId('workflow-form-create-submit').click();

  const row = view.getByRole('row').filter({ hasText: formKey });
  await expect(row).toBeVisible({ timeout: 15_000 });

  const headers = {
    Authorization: `Bearer ${currentAccessToken()}`,
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
              constraints: { scale: 2 }
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
  await expect(refreshedRow.locator('td').nth(3)).not.toHaveText('—', { timeout: 15_000 });

  headers.Authorization = `Bearer ${currentAccessToken()}`;
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

test('管理员可保存并发布含子表列配置的表单草稿', async ({ request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '子表字段契约仅在 Vue 交付线验收');
  test.setTimeout(60_000);

  const stamp = Date.now().toString(36);
  const formKey = `e2e.subtable.${stamp}`;
  const subtableKey = `lines_${stamp}`;
  const columnKey = `item_${stamp}`;
  const accessToken = await loginHostAdminAccessToken(request, testInfo.project.metadata.clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(testInfo.project.metadata.clientKind),
    'Content-Type': 'application/json'
  };

  const createResponse = await request.post(`${apiBaseUrl}/api/v1/workflow/forms/`, {
    data: {
      formKey,
      draft: {
        schemaVersion: 1,
        adapterVersion: 1,
        sections: [{
          sectionKey: 'main',
          fields: [{
            fieldKey: subtableKey,
            fieldTypeKey: 'subtable',
            required: false,
            constraints: {
              maxRows: 5,
              columns: [{
                columnKey,
                fieldTypeKey: 'text',
                required: true,
                constraints: { minLength: 1, maxLength: 128 }
              }]
            }
          }]
        }]
      }
    },
    headers
  });
  expect(createResponse.status()).toBe(201);
  const form = await createResponse.json();

  const publishResponse = await request.post(
    `${apiBaseUrl}/api/v1/workflow/forms/${form.id}/publish`,
    { data: { expectedRevision: form.draftRevision }, headers }
  );
  expect(publishResponse.status()).toBe(200);
  const published = await publishResponse.json();
  expect(published.id).toBeTruthy();

  const frozenResponse = await request.get(
    `${apiBaseUrl}/api/v1/workflow/form-versions/${published.id}`,
    { headers }
  );
  expect(frozenResponse.status()).toBe(200);
  const frozen = await frozenResponse.json();
  const subtableField = JSON.parse(frozen.formSchemaJson).sections
    .flatMap(section => section.fields)
    .find(field => field.fieldKey === subtableKey);
  expect(subtableField?.fieldTypeKey).toBe('subtable');
  expect(subtableField?.constraints?.columns?.[0]?.columnKey).toBe(columnKey);
});

test('管理员可在工作流待办上传附件并经实例上下文读取', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作流表单附件仅在 Vue 交付线验收');
  test.setTimeout(90_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
  const stamp = Date.now().toString(36);
  const formKey = `e2e.attachment.${stamp}`;
  const definitionKey = `e2e.attachment.${stamp}`;
  const businessId = crypto.randomUUID();
  const fieldKey = 'evidence';

  const createFormResponse = await request.post(`${apiBaseUrl}/api/v1/workflow/forms`, {
    headers,
    data: {
      formKey,
      draft: {
        schemaVersion: 1,
        adapterVersion: 1,
        sections: [{
          sectionKey: 'main',
          fields: [{
            fieldKey,
            fieldTypeKey: 'attachment',
            required: false,
            constraints: {
              maxCount: 2,
              maxSizeBytes: 4096,
              allowedExtensions: ['txt']
            }
          }]
        }]
      }
    }
  });
  expect(createFormResponse.status(), await createFormResponse.text()).toBe(201);
  const form = await createFormResponse.json();
  const publishFormResponse = await request.post(
    `${apiBaseUrl}/api/v1/workflow/forms/${form.id}/publish`,
    { headers, data: { expectedRevision: form.draftRevision } }
  );
  expect(publishFormResponse.status(), await publishFormResponse.text()).toBe(200);
  const formVersion = await publishFormResponse.json();

  const createDefinitionResponse = await request.post(`${apiBaseUrl}/api/v1/workflow/definitions`, {
    headers,
    data: {
      definitionKey,
      draft: {
        schemaVersion: 1,
        nodes: [
          { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['approve'] } },
          {
            nodeKey: 'approve',
            nodeTypeKey: 'human.approval',
            nodeSchemaVersion: 1,
            config: { nextNodeKeys: ['end'], fieldPolicies: { [fieldKey]: 'editable' } }
          },
          { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } }
        ]
      }
    }
  });
  expect(createDefinitionResponse.status(), await createDefinitionResponse.text()).toBe(201);
  const definition = await createDefinitionResponse.json();
  const publishDefinitionResponse = await request.post(
    `${apiBaseUrl}/api/v1/workflow/definitions/${definition.id}/publish`,
    { headers, data: { expectedRevision: definition.draftRevision, formVersionId: formVersion.id } }
  );
  expect(publishDefinitionResponse.status(), await publishDefinitionResponse.text()).toBe(200);
  const definitionVersion = await publishDefinitionResponse.json();

  const startResponse = await request.post(`${apiBaseUrl}/api/v1/workflow/instances`, {
    headers,
    data: {
      definitionVersionId: definitionVersion.id,
      businessType: 'e2e.workflow.attachment',
      businessId,
      initialValues: {},
      idempotencyKey: `start-${crypto.randomUUID()}`
    }
  });
  expect(startResponse.status(), await startResponse.text()).toBe(201);
  const instance = await startResponse.json();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我的待办/, '工作流');
  const todos = page.locator('.workflow-todos');
  await expect(todos.getByRole('heading', { name: '我的工作流待办', exact: true })).toBeVisible();
  await todos.getByTestId('workflow-todo-business-type-filter').fill('e2e.workflow.attachment');
  await todos.getByTestId('workflow-todo-filter-apply').click();
  const todoRow = todos.getByRole('row').filter({ hasText: definitionKey });
  await expect(todoRow).toBeVisible({ timeout: 15_000 });
  await todoRow.getByTestId('workflow-todo-open').click();

  const attachmentField = page.getByTestId(`workflow-form-attachment-${fieldKey}`);
  await expect(attachmentField).toBeVisible();
  await attachmentField.locator('input[type="file"]').setInputFiles({
    name: `evidence-${stamp}.txt`,
    mimeType: 'text/plain',
    buffer: Buffer.from(`workflow-attachment-${stamp}`)
  });
  const attachmentLink = attachmentField.locator('.workflow-form__attachment-link');
  await expect(attachmentLink).toBeVisible({ timeout: 15_000 });
  const fileId = (await attachmentLink.innerText()).trim();
  expect(fileId).toMatch(/^[0-9a-f-]{36}$/iu);

  const attachmentUrl = `${apiBaseUrl}/api/v1/workflow/instances/${instance.id}/form-attachments/${fileId}/content`;
  const beforeApproval = await request.get(attachmentUrl, { headers });
  expect(beforeApproval.status()).toBe(403);

  await todos.getByTestId('workflow-todo-approve').click();
  await expect(todoRow).toHaveCount(0, { timeout: 15_000 });
  const downloadResponse = await request.get(attachmentUrl, { headers });
  expect(downloadResponse.status(), await downloadResponse.text()).toBe(200);
  expect(downloadResponse.headers()['content-type']).toContain('text/plain');
  expect((await downloadResponse.body()).toString('utf8')).toBe(`workflow-attachment-${stamp}`);
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

  const currentAccessToken = trackUiAccessToken(page);
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

  headers.Authorization = `Bearer ${currentAccessToken()}`;
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
