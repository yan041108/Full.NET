import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import {
  adminOrigin,
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

function templateNameInput(view, clientKind) {
  return clientKind === 'layui'
    ? view.locator('[name="templateName"]')
    : view.getByTestId('codegen-template-name');
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
  await page
    .getByRole('navigation', { name: '主导航' })
    .getByRole('link', { name: /代码生成/ })
    .click();

  let view = codeGenerationView(page, clientKind);
  await expect(
    view.getByRole('heading', { name: 'CRUD 产物预览', exact: true })
  ).toBeVisible();
  const explicitSchema = JSON.parse(await schemaInput(view).inputValue());
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
  await schemaInput(view).fill(JSON.stringify(explicitSchema, null, 2));
  await templateNameInput(view, clientKind).fill(templateName);
  await templateSaveButton(view, clientKind).click();
  await expect(
    view.getByRole('button', { name: new RegExp(`^${templateName}`) })
  ).toBeVisible();

  const created = await templateByName(
    request,
    clientKind,
    accessToken,
    templateName
  );
  expect(created).toBeTruthy();
  expect(created.version).toBe(1);

  // 刷新页面后重新从数据库加载，避免只验证前端内存状态。
  await page.reload();
  view = codeGenerationView(page, clientKind);
  const persistedButton = view.getByRole(
    'button',
    { name: new RegExp(`^${templateName}`) }
  );
  await expect(persistedButton).toBeVisible();
  await persistedButton.click();
  const persistedSchema = JSON.parse(await schemaInput(view).inputValue());
  expect(persistedSchema.hasVersion).toBeUndefined();
  expect(persistedSchema.dataScope).toBe('tenant.required');
  expect(persistedSchema.entityCapabilities.deleteMode)
    .toBe('hard.delete');
  await view.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(view.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible();
  const clientArtifactPath = 'clients/vue/products.generated.ts';
  await view
    .getByRole('navigation', { name: '生成产物' })
    .getByRole('button', {
      name: new RegExp(clientArtifactPath.replaceAll('.', '\\.'))
    })
    .click();
  await expect(generatedContent(view, clientKind))
    .toContainText('/api/v1/catalog/products');

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
  expect(applied.previewRunId).toBeTruthy();
  expect(applied.artifactCount).toBeGreaterThan(0);
  await expect(runHistory(view, clientKind)).toContainText(applied.runId);
  assertAppliedWorkspaceArtifact(clientArtifactPath);

  await templateNameInput(view, clientKind).fill(updatedName);
  await templateUpdateButton(view, clientKind).click();
  await expect(
    view.getByRole('button', { name: new RegExp(`^${updatedName}`) })
  ).toBeVisible();

  const schemaForSecondApply = JSON.parse(await schemaInput(view).inputValue());
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
  await schemaInput(view).fill(JSON.stringify(schemaForSecondApply, null, 2));
  await view.getByRole('button', { name: '生成预览', exact: true }).click();
  await expect(view.getByText('acme_catalog_product', { exact: true }))
    .toBeVisible();

  const secondApplyResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/apply')
  );
  await view.getByRole('button', {
    name: '应用已审查预览',
    exact: true
  }).click();
  await confirmApply(page, clientKind);
  const secondAppliedResponse = await secondApplyResponse;
  expect(secondAppliedResponse.ok()).toBeTruthy();
  const secondApplied = await secondAppliedResponse.json();
  await expect(runHistory(view, clientKind)).toContainText(secondApplied.runId);

  const rollbackChainResponse = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().endsWith('/api/v1/code-generation/runs/rollback-chain')
  );
  await runHistory(view, clientKind)
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

  // 使用创建时的旧版本直连真实 API，验证乐观并发冲突是稳定契约。
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

  await templateDeleteButton(view, clientKind).click();
  await expect(
    view.getByRole('button', { name: new RegExp(`^${updatedName}`) })
  ).toHaveCount(0);

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
  await page
    .getByRole('navigation', { name: '主导航' })
    .getByRole('link', { name: /代码生成/ })
    .click();

  const view = codeGenerationView(page, clientKind);
  const explicitSchema = toOrganizationOwnedExplicitSchema(
    JSON.parse(await schemaInput(view).inputValue())
  );
  await schemaInput(view).fill(JSON.stringify(explicitSchema, null, 2));
  await templateNameInput(view, clientKind).fill(templateName);
  await templateSaveButton(view, clientKind).click();
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
    page
      .getByRole('navigation', { name: '主导航' })
      .getByRole('link', { name: /代码生成/ })
  ).toHaveCount(0);
});
