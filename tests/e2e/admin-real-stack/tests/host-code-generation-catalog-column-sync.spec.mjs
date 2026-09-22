import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import { skipCodegenWhenAttachModeWithoutWorkspace } from './support/codegeneration-attach-skip.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(() => {
  skipCodegenWhenAttachModeWithoutWorkspace();
});

test('Host 管理员可调用 catalog column-sync API（清单 36）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/code-generation/catalog/column-sync`,
    {
      data: {
        ownerKey: 'acme',
        moduleKey: 'catalog',
        entityKey: 'product',
        databaseTableName: 'acme_catalog_product'
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: adminOrigin(clientKind),
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.status()).toBeLessThan(500);
  if (response.ok()) {
    const body = await response.json();
    expect(body).toBeTruthy();
  } else {
    const problem = await response.json();
    expect(problem).toHaveProperty('type');
  }
});
