import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import { readAppliedWorkspaceArtifact } from './support/codegeneration-workspace.mjs';
import { toOrganizationOwnedExplicitSchema } from './support/organization-owned-codegen-schema.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

function assertAppliedWorkspaceArtifact(relativePath) {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1') {
    return;
  }

  const statePath = new URL('../.stack-state.json', import.meta.url);
  expect(existsSync(statePath)).toBeTruthy();
  const state = JSON.parse(readFileSync(statePath, 'utf8'));
  expect(state.codeGenerationWorkspaceRoot).toBeTruthy();

  const manifestPath = path.join(
    state.codeGenerationWorkspaceRoot,
    '.fullnet',
    'codegeneration-manifest.json'
  );
  expect(existsSync(manifestPath)).toBeTruthy();
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  const entry = manifest.artifacts.find(
    artifact => artifact.relativePath === relativePath
  );
  expect(entry).toBeTruthy();

  const artifactPath = path.join(
    state.codeGenerationWorkspaceRoot,
    ...relativePath.split('/')
  );
  expect(existsSync(artifactPath)).toBeTruthy();
  const hash = createHash('sha256')
    .update(readFileSync(artifactPath))
    .digest('hex');
  expect(hash).toBe(entry.sha256);
}

function codeGenerationView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="code-generation-previews"]')
    : page.locator('.codegen-workbench');
}

function codeGenerationTemplatesView(page, clientKind) {
  return clientKind === 'layui'
    ? page.locator('[data-route-view="code-generation-previews"]')
    : page.locator('.code-generation-templates-view');
}

async function openTemplateWorkspace(page, clientKind) {
  if (clientKind === 'layui') {
    const navigation = page.getByRole('navigation', { name: '主导航' });
    await navigation.getByRole('link', { name: /代码生成/ }).click();
    return;
  }
  await clickMainNavLink(page, '代码生成模板', '代码生成');
}

async function openPreviewWorkspace(page, clientKind) {
  if (clientKind === 'layui') {
    const navigation = page.getByRole('navigation', { name: '主导航' });
    await navigation.getByRole('link', { name: /代码生成/ }).click();
    return;
  }
  await clickMainNavLink(page, '代码生成预览', '代码生成');
}

function templateNameInput(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[name="templateName"]')
    : view.getByTestId('codegen-template-name');
}

function templateSchemaInput(view, clientKind) {
  return clientKind === 'layui'
    ? view.getByRole('textbox', { name: 'Schema 输入', exact: true })
    : view.getByTestId('codegen-template-schema');
}

async function focusTemplateSchema(view, clientKind) {
  if (clientKind === 'layui') {
    return templateSchemaInput(view, clientKind);
  }
  await view.getByRole('tab', { name: '高级 JSON' }).click();
  const input = templateSchemaInput(view, clientKind);
  await expect(input).toBeVisible();
  return input;
}

async function fillTemplateName(view, clientKind, templateName) {
  if (clientKind === 'layui') {
    await templateNameInput(view, clientKind).fill(templateName);
    return;
  }
  await view.getByRole('tab', { name: '基础' }).click();
  await templateNameInput(view, clientKind).fill(templateName);
}

async function expectTemplateListed(templateView, clientKind, templateName) {
  if (clientKind === 'layui') {
    await expect(
      templateView.getByRole('button', { name: new RegExp(`^${templateName}`) })
    ).toBeVisible();
    return;
  }
  await expect(templateView.locator('.art-crud-data-table')).toContainText(templateName);
}

function templateSaveButton(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-template-form] button[type="submit"]')
    : view.getByTestId('codegen-template-save');
}

function templateUpdateButton(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-template-update]')
    : view.getByTestId('codegen-template-update');
}

function templateDeleteButton(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-template-delete]')
    : view.getByTestId('codegen-template-delete');
}

function schemaInput(view) {
  return view.getByRole('textbox', { name: 'Schema 输入', exact: true });
}

function generatedContent(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-content] code')
    : view.getByTestId('codegen-content').locator('code');
}

function runHistory(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[data-codegen-run-history]')
    : view.getByTestId('codegen-run-history');
}

async function confirmApply(page, clientKind) {
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', {
      name: '应用已审查预览',
      exact: true
    }).click();
    return;
  }

  await page.locator('.layui-layer-btn0').last().click();
}

async function confirmRollback(page, clientKind) {
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', {
      name: '回滚此 Apply',
      exact: true
    }).click();
    return;
  }

  await page.locator('.layui-layer-btn0').last().click();
}

function assertEmptyWorkspaceManifest() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1') {
    return;
  }

  const statePath = new URL('../.stack-state.json', import.meta.url);
  expect(existsSync(statePath)).toBeTruthy();
  const state = JSON.parse(readFileSync(statePath, 'utf8'));
  const manifestPath = path.join(
    state.codeGenerationWorkspaceRoot,
    '.fullnet',
    'codegeneration-manifest.json'
  );
  expect(existsSync(manifestPath)).toBeTruthy();
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  expect(manifest.artifacts).toEqual([]);
}

async function templateByName(request, clientKind, accessToken, name) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/templates?page=1&pageSize=100`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const page = await response.json();
  return page.items.find(template => template.name === name);
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可通过双管理端持久化、更新并软删除生成模板', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const suffix = `${clientKind}-${Date.now()}`;
  const templateName = `e2e-template-${suffix}`;
  const updatedName = `${templateName}-updated`;

  await loginAsHostAdmin(page);
  await openTemplateWorkspace(page, clientKind);

  let templateView = codeGenerationTemplatesView(page, clientKind);
  await expect(
    templateView.getByRole('heading', {
      name: clientKind === 'layui' ? 'CRUD 产物预览' : '代码生成模板',
      exact: true
    })
  ).toBeVisible();
  const explicitSchema = clientKind === 'layui'
    ? JSON.parse(await schemaInput(codeGenerationView(page, clientKind)).inputValue())
    : JSON.parse(await (await focusTemplateSchema(templateView, clientKind)).inputValue());
  delete explicitSchema.hasVersion;
  explicitSchema.dataScope = 'tenant.required';
  explicitSchema.entityCapabilities = {
    deleteMode: 'hard.delete',
    hasCreatedAudit: false,
    hasUpdatedAudit: false,
    hasDeletedAudit: false,
    hasVersion: true,
    ownershipMode: 'none'
  };
  explicitSchema.scene = 'single';
  explicitSchema.relationships = [];
  const schemaTarget = clientKind === 'layui'
    ? schemaInput(codeGenerationView(page, clientKind))
    : await focusTemplateSchema(templateView, clientKind);
  await schemaTarget.fill(JSON.stringify(explicitSchema, null, 2));
  await fillTemplateName(templateView, clientKind, templateName);
  await templateSaveButton(templateView, clientKind).click();
  if (clientKind === 'layui') {
    await expectTemplateListed(templateView, clientKind, templateName);
  } else {
    await expectTemplateListed(templateView, clientKind, templateName);
  }

  const created = await templateByName(
    request,
    clientKind,
    accessToken,
    templateName
  );
  expect(created).toBeTruthy();
  expect(created.version).toBe(1);

  await page.reload();
  if (clientKind !== 'layui') {
    await openTemplateWorkspace(page, clientKind);
  }
  templateView = codeGenerationTemplatesView(page, clientKind);
  if (clientKind === 'layui') {
    const persistedButton = templateView.getByRole(
      'button',
      { name: new RegExp(`^${templateName}`) }
    );
    await expect(persistedButton).toBeVisible();
    await persistedButton.click();
  } else {
    await expectTemplateListed(templateView, clientKind, templateName);
    await templateView
      .getByTestId('codegen-template-load')
      .first()
      .click();
  }

  let previewView = codeGenerationView(page, clientKind);
  if (clientKind !== 'layui') {
    await openPreviewWorkspace(page, clientKind);
    previewView = codeGenerationView(page, clientKind);
    await previewView
      .getByTestId('codegen-template-load')
      .filter({ hasText: templateName })
      .click();
  }
  const persistedSchema = JSON.parse(await schemaInput(previewView).inputValue());
  expect(persistedSchema.hasVersion).toBeUndefined();
  expect(persistedSchema.dataScope).toBe('tenant.required');
  expect(persistedSchema.entityCapabilities.deleteMode)
    .toBe('hard.delete');
  await previewView.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(previewView.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible();
  const clientArtifactPath = 'clients/vue/products.generated.ts';
  await previewView
    .getByRole('navigation', { name: '生成产物' })
    .getByRole('button', {
      name: new RegExp(clientArtifactPath.replaceAll('.', '\\.'))
    })
    .click();
  await expect(generatedContent(previewView, clientKind))
    .toContainText('/api/v1/catalog/products');

  const applyResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/apply')
  );
  await previewView.getByRole('button', {
    name: '应用已审查预览',
    exact: true
  }).click();
  await confirmApply(page, clientKind);
  const appliedResponse = await applyResponse;
  expect(appliedResponse.ok()).toBeTruthy();
  const applied = await appliedResponse.json();
  expect(applied.previewRunId).toBeTruthy();
  expect(applied.artifactCount).toBeGreaterThan(0);
  await expect(runHistory(previewView, clientKind)).toContainText(applied.runId);
  assertAppliedWorkspaceArtifact(clientArtifactPath);

  if (clientKind !== 'layui') {
    await openTemplateWorkspace(page, clientKind);
    templateView = codeGenerationTemplatesView(page, clientKind);
    await templateView
      .locator('tr', { hasText: templateName })
      .getByTestId('codegen-template-load')
      .click();
  }
  await fillTemplateName(templateView, clientKind, updatedName);
  await templateUpdateButton(templateView, clientKind).click();
  if (clientKind === 'layui') {
    await expect(
      templateView.getByRole('button', { name: new RegExp(`^${updatedName}`) })
    ).toBeVisible();
  } else {
    await expectTemplateListed(templateView, clientKind, updatedName);
  }

  if (clientKind !== 'layui') {
    await openPreviewWorkspace(page, clientKind);
    previewView = codeGenerationView(page, clientKind);
    await previewView
      .getByTestId('codegen-template-load')
      .filter({ hasText: updatedName })
      .click();
  }
  const schemaForSecondApply = JSON.parse(await schemaInput(previewView).inputValue());
  schemaForSecondApply.columns.push({
    databaseName: 'Remark',
    clrPropertyName: 'Remark',
    jsonPropertyName: 'remark',
    scalarType: 'String',
    isNullable: true,
    maxLength: 500,
    numericPrecision: null,
    numericScale: null
  });
  await schemaInput(previewView).fill(JSON.stringify(schemaForSecondApply, null, 2));
  await previewView.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(previewView.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible();

  const secondApplyResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/apply')
  );
  await previewView.getByRole('button', {
    name: '应用已审查预览',
    exact: true
  }).click();
  await confirmApply(page, clientKind);
  const secondAppliedResponse = await secondApplyResponse;
  expect(secondAppliedResponse.ok()).toBeTruthy();
  const secondApplied = await secondAppliedResponse.json();
  await expect(runHistory(previewView, clientKind)).toContainText(secondApplied.runId);

  const rollbackChainResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/rollback-chain')
  );
  await runHistory(previewView, clientKind)
    .locator('article', { hasText: applied.runId })
    .getByRole('button', { name: '回滚此 Apply', exact: true })
    .click();
  await confirmRollback(page, clientKind);
  const rolledBackChainResponse = await rollbackChainResponse;
  expect(rolledBackChainResponse.ok()).toBeTruthy();
  const rolledBackChain = await rolledBackChainResponse.json();
  expect(rolledBackChain.rollbacks).toHaveLength(2);
  expect(rolledBackChain.rollbacks[0].applyRunId).toBe(secondApplied.runId);
  expect(rolledBackChain.rollbacks[1].applyRunId).toBe(applied.runId);
  assertEmptyWorkspaceManifest();

  const staleResponse = await request.put(
    `${apiBaseUrl}/api/v1/code-generation/templates/${created.id}`,
    {
      data: {
        name: `${updatedName}-stale`,
        description: null,
        schema: created.schema,
        version: created.version
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(staleResponse.status()).toBe(409);
  expect((await staleResponse.json()).code)
    .toBe('codegen.template.version_conflict');

  if (clientKind !== 'layui') {
    await openTemplateWorkspace(page, clientKind);
    templateView = codeGenerationTemplatesView(page, clientKind);
    await templateView
      .locator('tr', { hasText: updatedName })
      .getByTestId('codegen-template-load')
      .click();
  }
  await templateDeleteButton(templateView, clientKind).click();
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog').last();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: '删除所选模板', exact: true }).click();
  }
  if (clientKind === 'layui') {
    await expect(
      templateView.getByRole('button', { name: new RegExp(`^${updatedName}`) })
    ).toHaveCount(0);
  } else {
    await expect(templateView.locator('.art-crud-data-table')).not.toContainText(updatedName);
  }

  const deletedResponse = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/templates/${created.id}`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(deletedResponse.status()).toBe(404);
  expect((await deletedResponse.json()).code)
    .toBe('codegen.template.not_found');
});

test('Host 管理员可 Apply 组织归属模板并落盘写入授权 Feature', async ({
  page
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const suffix = `${clientKind}-${Date.now()}`;
  const templateName = `e2e-org-owned-${suffix}`;
  const featureArtifactPath = 'backend/ProductFeature.g.cs';

  await loginAsHostAdmin(page);
  await openTemplateWorkspace(page, clientKind);

  const templateView = codeGenerationTemplatesView(page, clientKind);
  const explicitSchema = toOrganizationOwnedExplicitSchema(
    JSON.parse(
      clientKind === 'layui'
        ? await schemaInput(codeGenerationView(page, clientKind)).inputValue()
        : await (await focusTemplateSchema(templateView, clientKind)).inputValue()
    )
  );
  const schemaTarget = clientKind === 'layui'
    ? schemaInput(codeGenerationView(page, clientKind))
    : await focusTemplateSchema(templateView, clientKind);
  await schemaTarget.fill(JSON.stringify(explicitSchema, null, 2));
  await fillTemplateName(templateView, clientKind, templateName);
  await templateSaveButton(templateView, clientKind).click();

  if (clientKind !== 'layui') {
    await openPreviewWorkspace(page, clientKind);
  }
  const view = codeGenerationView(page, clientKind);
  if (clientKind !== 'layui') {
    await view
      .getByTestId('codegen-template-load')
      .filter({ hasText: templateName })
      .click();
  }
  await view.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(view.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible({ timeout: 15_000 });
  await view
    .getByRole('navigation', { name: '生成产物' })
    .getByRole('button', { name: /backend\/ProductFeature\.g\.cs/ })
    .click();
  await expect(generatedContent(view, clientKind))
    .toContainText('IOrganizationOwnedEntityWriteAuthorizer');

  const applyResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/apply')
  );
  await view.getByRole('button', {
    name: '应用已审查预览',
    exact: true
  }).click();
  await confirmApply(page, clientKind);
  const appliedResponse = await applyResponse;
  expect(appliedResponse.ok()).toBeTruthy();
  const applied = await appliedResponse.json();
  expect(applied.artifactCount).toBeGreaterThan(0);

  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP !== '1') {
    const featureSource = readAppliedWorkspaceArtifact(featureArtifactPath);
    expect(featureSource).toContain('IOrganizationOwnedEntityWriteAuthorizer');
    expect(featureSource).toContain('BuildOrganizationUnitFilter');
    expect(featureSource).toContain(
      'OrganizationRequestHeaders.OrganizationUnitId'
    );
  }
});

test('受限 Host 账号不能读取模板 API 且双端导航保持裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginAccessToken(request, clientKind);
  const response = await request.get(
    `${apiBaseUrl}/api/v1/code-generation/templates?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind)
      }
    }
  );
  expect(response.status()).toBe(403);
  expect((await response.json()).code)
    .toBe('authorization.permission_denied');

  const applyResponse = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/runs/apply`,
    {
      data: {
        previewRunId: '018f0f0e-7c36-7b25-8d3a-b2bd5a34d001'
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(applyResponse.status()).toBe(403);
  expect((await applyResponse.json()).code)
    .toBe('authorization.permission_denied');

  const rollbackDenied = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/runs/rollback`,
    {
      data: {
        applyRunId: '018f0f0e-7c36-7b25-8d3a-b2bd5a34d001'
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(rollbackDenied.status()).toBe(403);
  expect((await rollbackDenied.json()).code)
    .toBe('authorization.permission_denied');

  const rollbackChainDenied = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/runs/rollback-chain`,
    {
      data: {
        applyRunIds: [
          '018f0f0e-7c36-7b25-8d3a-b2bd5a34d001',
          '018f0f0e-7c36-7b25-8d3a-b2bd5a34d002'
        ]
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(rollbackChainDenied.status()).toBe(403);
  expect((await rollbackChainDenied.json()).code)
    .toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  await expect(
    page.getByRole('navigation', { name: '主导航' }).getByRole('link', { name: '代码生成模板' })
  ).toHaveCount(0);
  await expect(
    page.getByRole('navigation', { name: '主导航' }).getByRole('link', { name: '代码生成预览' })
  ).toHaveCount(0);
});

test('Vue 工作台支持筛选、复制、列元数据与预览深链', async ({
  page,
  request
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind !== 'vue',
    '工作台 UX 对齐只验收 Vue 管理端'
  );

  const accessToken = await loginHostAdminAccessToken(request, 'vue');
  const suffix = `ux-${Date.now()}`;
  const templateName = `e2e-ux-${suffix}`;

  await loginAsHostAdmin(page);
  await openTemplateWorkspace(page, 'vue');
  const templateView = codeGenerationTemplatesView(page, 'vue');

  const schema = JSON.parse(
    await (await focusTemplateSchema(templateView, 'vue')).inputValue()
  );
  delete schema.hasVersion;
  schema.entityCapabilities = {
    deleteMode: 'soft.delete',
    hasCreatedAudit: true,
    hasUpdatedAudit: true,
    hasDeletedAudit: true,
    hasVersion: true,
    ownershipMode: 'none'
  };
  schema.scene = 'single';
  schema.relationships = [];
  if (!schema.columns.some(column => column.ui)) {
    schema.columns = schema.columns.map(column => ({
      ...column,
      ui: {
        controlKind: column.scalarType === 'Boolean' ? 'switch' : 'text',
        showInList: true,
        includeInCreate: true,
        includeInUpdate: true,
        required: !column.isNullable,
        sortable: true,
        queryable: true,
        queryKind: 'equals',
        unique: false,
        includeInImportExport: true
      }
    }));
  }
  await (await focusTemplateSchema(templateView, 'vue'))
    .fill(JSON.stringify(schema, null, 2));
  await fillTemplateName(templateView, 'vue', templateName);
  await templateSaveButton(templateView, 'vue').click();
  await expectTemplateListed(templateView, 'vue', templateName);

  await templateView.getByTestId('codegen-template-filter-name').fill(templateName);
  await templateView.getByTestId('codegen-template-filter-search').click();
  await expectTemplateListed(templateView, 'vue', templateName);

  const row = templateView.locator('tr', { hasText: templateName });
  await row.getByTestId('codegen-template-copy').click();
  await expect(templateView.locator('.art-crud-data-table')).toContainText(`${templateName} (copy)`);

  await row.getByTestId('codegen-template-load').click();
  await templateView.getByRole('tab', { name: '列配置' }).click();
  await expect(templateView.getByTestId('codegen-column-sortable').first())
    .toBeVisible();
  await expect(templateView.getByTestId('codegen-column-query-kind').first())
    .toBeVisible();
  await expect(templateView.getByTestId('codegen-column-import-export').first())
    .toBeVisible();

  await row.getByTestId('codegen-template-preview-link').click();
  const previewView = codeGenerationView(page, 'vue');
  await expect(previewView).toBeVisible();
  await expect(page).toHaveURL(/templateId=/);
  await expect(previewView.getByTestId('codegen-schema')).toContainText(
    schema.databaseTableName
  );
  await expect(previewView.getByTestId('codegen-integration-target')).toBeVisible();

  const listed = await templateByName(request, 'vue', accessToken, templateName);
  expect(listed).toBeTruthy();
});
