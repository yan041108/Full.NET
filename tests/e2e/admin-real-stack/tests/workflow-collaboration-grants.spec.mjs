import { expect, test } from '@playwright/test';
import { loginAsHostUser, loginAsHostAdmin, enterDevelopmentTenant, loginAccessTokenWithPassword, loginHostAdminAccessToken, provisionLimitedHostUserViaApi, adminOrigin } from './support/real-stack-auth.mjs';
import { post } from './support/workflow-approval-fixtures.mjs';
const api = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const permissions = ['identity.navigation.read', 'platform.dashboard.read', 'workflow.collaboration_grants.read', 'workflow.collaboration_grants.propose', 'workflow.collaboration_grants.revoke',
  'workflow.forms.read', 'workflow.forms.create', 'workflow.forms.publish', 'workflow.definitions.read', 'workflow.definitions.create', 'workflow.definitions.publish'];
async function asset(request, token) {
  const stamp = crypto.randomUUID();
  const form = await post(request, 'vue', token, '/api/v1/workflow/forms', { formKey: `collab.${stamp}`, draft: { schemaVersion: 1, adapterVersion: 1,
    sections: [{ sectionKey: 'main', fields: [{ fieldKey: 'title', fieldTypeKey: 'text', required: false, constraints: {} }] }] } }, 201);
  const formVersion = await post(request, 'vue', token, `/api/v1/workflow/forms/${form.id}/publish`, { expectedRevision: form.draftRevision });
  const definition = await post(request, 'vue', token, '/api/v1/workflow/definitions', { definitionKey: `collab.${stamp}`, draft: { schemaVersion: 1, workflowKindKey: 'automation', nodes: [
    { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['end'] } },
    { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: {} }] } }, 201);
  const version = await post(request, 'vue', token, `/api/v1/workflow/definitions/${definition.id}/publish`, { expectedRevision: definition.draftRevision, formVersionId: formVersion.id });
  return { definition: definition.id, formVersion: formVersion.id, version: version.id, key: definition.definitionKey };
}
test('真实双方会话通过UI提出、接受、刷新和撤销固定授权', async ({ page, browser, request }) => {
  test.setTimeout(180_000);
  const user = await provisionLimitedHostUserViaApi(request, 'vue', { username: `collab-${crypto.randomUUID()}`, permissionCodes: permissions });
  const sourceToken = await loginAccessTokenWithPassword(request, 'vue', user.username, user.password);
  const sourceAsset = await asset(request, sourceToken);
  const administrator = await loginHostAdminAccessToken(request, 'vue');
  const availableResponse = await request.get(`${api}/api/v1/tenancy/available`, { headers: { Authorization: `Bearer ${administrator}`, Origin: adminOrigin('vue') } });
  expect(availableResponse.status()).toBe(200); const available = await availableResponse.json();
  const destination = available.find(value => value.identifier === 'acme') ?? available.find(value => value.identifier === 'local'); expect(destination).toBeTruthy();
  const switchResponse = await request.put(`${api}/api/v1/tenancy/context`, { data: { tenantId: destination.id }, headers: { Authorization: `Bearer ${administrator}`, Origin: adminOrigin('vue') } });
  expect(switchResponse.status()).toBe(200); const targetToken = (await switchResponse.json()).accessToken;
  const targetAsset = await asset(request, targetToken);
  const headers = { Authorization: `Bearer ${targetToken}`, Origin: adminOrigin('vue') };
  const tenantResponse = await request.get(`${api}/api/v1/tenancy/current`, { headers }); expect(tenantResponse.status()).toBe(200); const tenant = await tenantResponse.json();
  const memberId = process.env.FULLNET_E2E_COLLABORATION_MEMBER_ID;
  expect(memberId).toMatch(/^[0-9a-f-]{36}$/iu);
  // 源与目标使用独立用户，API准备在浏览器登录之前完成，不能复用已被旋转的旧会话。
  await loginAsHostUser(page, user.username, user.password); await page.goto('/#/workflow/collaboration-grants');
  await page.getByTestId('grant-target-tenant').fill(tenant.id);
  await page.getByTestId('grant-sourceDefinitionId').fill(sourceAsset.definition);
  await page.getByTestId('grant-sourceFormVersionId').fill(sourceAsset.formVersion);
  await page.getByTestId('grant-targetWorkflowKey').fill(targetAsset.key);
  await page.getByTestId('grant-input-fields').fill('title'); await page.getByTestId('grant-output-fields').fill('title');
  await page.getByLabel('到期时间', { exact: true }).fill('2030-01-01 00:00:00'); await page.keyboard.press('Enter');
  const proposedResponse = page.waitForResponse(r => /\/workflow\/collaboration-grants\/?$/u.test(r.url()) && r.request().method() === 'POST');
  await page.getByTestId('grant-propose').click(); const proposal = await proposedResponse; expect(proposal.status(), await proposal.text()).toBe(200); const grant = await proposal.json();
  await expect(page.getByTestId('grant-detail')).toContainText('待接受');
  const context = await browser.newContext(); const targetPage = await context.newPage();
  try {
    await loginAsHostAdmin(targetPage, `${adminOrigin('vue')}/`); await enterDevelopmentTenant(targetPage, tenant.name);
    await targetPage.goto(`${adminOrigin('vue')}/#/workflow/collaboration-grants`);
    await targetPage.getByTestId('grant-id').fill(grant.id); await targetPage.getByTestId('grant-load').click();
    await targetPage.getByTestId('grant-version').fill(targetAsset.version); await targetPage.getByTestId('grant-starter').fill(memberId); await targetPage.getByTestId('grant-members').fill(memberId);
    const acceptedResponse = targetPage.waitForResponse(r => r.url().endsWith(`/collaboration-grants/${grant.id}/accept`));
    await targetPage.getByTestId('grant-accept').click(); const accepted = await acceptedResponse; expect(accepted.status(), await accepted.text()).toBe(200);
    await expect(targetPage.getByTestId('grant-detail')).toContainText('已生效');
    await page.getByTestId('grant-load').click(); await expect(page.getByTestId('grant-detail')).toContainText('已生效');
    await expect(page.getByTestId('grant-accept')).toHaveCount(0);
    await page.getByTestId('grant-reason').fill('真实双方授权验收完成'); await page.getByTestId('grant-revoke').click();
    await expect(page.getByTestId('grant-detail')).toContainText('已撤销');
    await targetPage.getByTestId('grant-load').click(); await expect(targetPage.getByTestId('grant-detail')).toContainText('已撤销');
    await expect(targetPage.getByTestId('grant-accept')).toHaveCount(0);
  } finally { await context.close(); }
});
