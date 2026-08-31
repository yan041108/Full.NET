import { expect } from '@playwright/test';
import { adminOrigin, clickMainNavLink } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

export const workflowReadOnlyTodoPermissions = [
  'identity.navigation.read',
  'platform.dashboard.read',
  'workflow.forms.read',
  'workflow.forms.create',
  'workflow.forms.publish',
  'workflow.definitions.read',
  'workflow.definitions.create',
  'workflow.definitions.publish',
  'workflow.instances.read',
  'workflow.instances.start',
  'workflow.todos.read'
];

export const tenantWorkflowReadOnlyTodoPermissions = [
  ...workflowReadOnlyTodoPermissions,
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
];

export async function startFromDefinitionsPage(page, assets, reason) {
  await clickMainNavLink(page, /工作流定义/, '工作流');
  const view = page.locator('.workflow-definitions');
  const row = view.getByRole('row').filter({ hasText: assets.definitionKey });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('workflow-definition-versions').click();
  await view.getByTestId('workflow-definition-start').click();
  await view.getByTestId('workflow-business-type').fill('admin.approval');
  await view.getByTestId('workflow-business-id').fill(crypto.randomUUID());
  const reasonField = view.locator('.workflow-form__field').filter({ hasText: 'reason' });
  await reasonField.locator('input').fill(reason);
  const startedResponse = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/workflow/instances')
    && response.request().method() === 'POST'
  );
  await view.getByTestId('workflow-start-submit').click();
  const response = await startedResponse;
  expect(response.status(), await response.text()).toBe(201);
  return response.json();
}

export async function openTodoAndAct(page, instanceId, decision, action) {
  await openTodo(page, instanceId);
  const reasonField = page.locator('.workflow-form__field').filter({ hasText: 'reason' });
  await expect(reasonField.locator('input')).toHaveAttribute('readonly', '');
  await expect(page.locator('.workflow-form__field').filter({ hasText: 'secret' }))
    .toHaveCount(0);
  await fillDecision(page, decision);
  const actionButton = page.getByTestId(`workflow-todo-${action}`);
  await actionButton.click();
  await expect(page.getByRole('row').filter({ hasText: instanceId })).toHaveCount(0);
}

export async function openTodo(page, instanceId) {
  const row = page.getByRole('row').filter({ hasText: instanceId });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('workflow-todo-open').click();
  await expect(page.getByTestId('workflow-form-renderer')).toBeVisible();
}

export async function fillDecision(page, value) {
  const decisionField = page.locator('.workflow-form__field').filter({ hasText: 'decision' });
  await decisionField.locator('input').fill(value);
}

export async function publishApprovalAssets(request, clientKind, accessToken) {
  const stamp = `${Date.now().toString(36)}-${crypto.randomUUID()}`;
  const form = await post(request, clientKind, accessToken, '/api/v1/workflow/forms', {
    formKey: `admin.approval.${stamp}`,
    draft: {
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [{
        sectionKey: 'main',
        fields: [
          { fieldKey: 'reason', fieldTypeKey: 'text', required: true, constraints: {} },
          { fieldKey: 'secret', fieldTypeKey: 'text', required: false, constraints: {} },
          { fieldKey: 'decision', fieldTypeKey: 'text', required: false, constraints: {} }
        ]
      }]
    }
  }, 201);
  const formVersion = await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/forms/${form.id}/publish`,
    { expectedRevision: form.draftRevision }
  );
  const definitionKey = `admin.approval.${stamp}`;
  const definition = await post(
    request,
    clientKind,
    accessToken,
    '/api/v1/workflow/definitions',
    {
      definitionKey,
      draft: {
        schemaVersion: 1,
        nodes: [
          {
            nodeKey: 'start',
            nodeTypeKey: 'start',
            nodeSchemaVersion: 1,
            config: { nextNodeKeys: ['approve'] }
          },
          {
            nodeKey: 'approve',
            nodeTypeKey: 'human.approval',
            nodeSchemaVersion: 1,
            config: {
              nextNodeKeys: ['end'],
              fieldPolicies: { reason: 'readOnly', secret: 'hidden', decision: 'required' }
            }
          },
          {
            nodeKey: 'end',
            nodeTypeKey: 'end',
            nodeSchemaVersion: 1,
            config: { nextNodeKeys: [] }
          }
        ]
      }
    },
    201
  );
  const version = await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/definitions/${definition.id}/publish`,
    { expectedRevision: definition.draftRevision, formVersionId: formVersion.id }
  );
  return { definitionKey, versionId: version.id };
}

export async function startInstance(request, clientKind, accessToken, definitionVersionId, reason) {
  return post(request, clientKind, accessToken, '/api/v1/workflow/instances', {
    definitionVersionId,
    businessType: 'admin.approval',
    businessId: crypto.randomUUID(),
    initialValues: { reason, secret: 'must remain hidden' },
    idempotencyKey: `start-${crypto.randomUUID()}`
  }, 201);
}

export async function getInstance(request, clientKind, accessToken, instanceId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/workflow/instances/${instanceId}`, {
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  return response.json();
}

export async function getMyTodo(request, clientKind, accessToken, instanceId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/workflow/todos/mine`, {
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  const todo = (await response.json()).find(item => item.instanceId === instanceId);
  expect(todo, `实例 ${instanceId} 应产生当前用户待办`).toBeDefined();
  return todo;
}

export async function assertDangerousPatchesReturn422(request, clientKind, accessToken, todoId) {
  const patches = [
    { fieldPatch: { reason: 42 }, comment: 'invalid type' },
    { fieldPatch: { reason: 'changed' }, comment: 'read only' },
    { fieldPatch: { secret: 'exposed' }, comment: 'hidden' },
    { fieldPatch: {}, comment: 'missing required' },
    { fieldPatch: { injected: 'forbidden' }, comment: 'unknown field' }
  ];
  for (const patch of patches) {
    const response = await request.post(
      `${apiBaseUrl}/api/v1/workflow/todos/${todoId}/approve`,
      {
        data: {
          expectedRevision: 1,
          fieldPatch: patch.fieldPatch,
          comment: patch.comment,
          idempotencyKey: crypto.randomUUID()
        },
        headers: apiHeaders(clientKind, accessToken)
      }
    );
    expect(response.status(), `${patch.comment}: ${await response.text()}`).toBe(422);
    expect((await response.json()).code).toBe('workflow.schema.invalid');
  }
}

export async function post(request, clientKind, accessToken, path, data, expectedStatus = 200) {
  const response = await request.post(`${apiBaseUrl}${path}`, {
    data,
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(expectedStatus);
  return response.json();
}

export function apiHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}
