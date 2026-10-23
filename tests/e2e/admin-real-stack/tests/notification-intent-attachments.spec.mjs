import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginHostAdminAccessToken,
  uploadHostFileViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

async function getCurrentUserId(request, token, origin) {
  const response = await request.get(`${apiBaseUrl}/api/v1/me`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(response.ok()).toBeTruthy();
  const user = await response.json();
  return user.id;
}

async function requireRegisteredEmailChannel(request, token, origin) {
  const response = await request.get(`${apiBaseUrl}/api/v1/notifications/provider-types`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(response.ok()).toBeTruthy();
  const providerTypes = await response.json();
  test.skip(
    !providerTypes.some(provider => provider.supportedChannelKeys?.includes('email')),
    'Email attachment intent requires a registered email provider; Inbox coverage remains active.'
  );
}

// 受控 Producer/Scene 目录是分类与合法渠道的唯一权威，缺失声明时受理失败关闭；
// 因此用例必须先按当前环境实际注册的渠道声明目录项，重复运行保持幂等。
async function ensureAttachmentsProducerScene(request, token, origin) {
  const headers = authHeaders(token, origin);
  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/producer-scenes?page=1&pageSize=100`,
    { headers }
  );
  expect(listResponse.ok()).toBeTruthy();
  const page = await listResponse.json();
  if (page.items?.some(
    item => item.producerKey === 'e2e.notifications' && item.sceneKey === 'e2e.attachments'
  )) {
    return;
  }

  const providerTypesResponse = await request.get(`${apiBaseUrl}/api/v1/notifications/provider-types`, {
    headers
  });
  expect(providerTypesResponse.ok()).toBeTruthy();
  const providerTypes = await providerTypesResponse.json();
  const allowedChannelKeys = [
    'inbox',
    ...(providerTypes.some(provider => provider.supportedChannelKeys?.includes('email'))
      ? ['email']
      : [])
  ];
  const response = await request.post(`${apiBaseUrl}/api/v1/notifications/producer-scenes`, {
    headers,
    data: {
      producerKey: 'e2e.notifications',
      sceneKey: 'e2e.attachments',
      policyCategoryKey: 'transactional',
      allowedChannelKeys,
      emergencyOverrideAllowed: false
    }
  });
  expect(response.status()).toBe(201);
}

async function createTemplate(request, headers, templateKey, channelKey) {
  const response = await request.post(`${apiBaseUrl}/api/v1/notifications/templates`, {
    headers,
    data: {
      templateKey,
      channelKey,
      contentCategoryKey: 'transactional',
      draftSubject: 'E2E 附件',
      draftBody: { text: '正文 {{name}}' },
      parameterSchema: {
        schemaVersion: 1,
        parameters: [{ name: 'name', typeKey: 'string', required: true, maxLength: 64 }]
      }
    }
  });
  expect(response.status()).toBe(201);
  return response.json();
}

async function publishTemplate(request, headers, template) {
  const response = await request.post(
    `${apiBaseUrl}/api/v1/notifications/templates/${template.id}/publish`,
    { headers, data: { version: template.version, contentClassificationKey: 'c0' } }
  );
  expect(response.ok()).toBeTruthy();
  return response.json();
}

function intentBody(templateKey, userId, idempotencyKey, attachmentFileIds) {
  return {
    producerKey: 'e2e.notifications',
    sceneKey: 'e2e.attachments',
    templateKey,
    recipients: [{ recipientTypeKey: 'user', recipientKey: userId.replace(/-/g, '') }],
    parameters: { name: 'E2E' },
    idempotencyKey,
    attachmentFileIds
  };
}

test('Inbox Intent 拒绝附件引用（清单 43）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  await ensureAttachmentsProducerScene(request, token, origin);
  const templateKey = `e2e.inbox.attach.${stamp}`.slice(0, 32);
  const template = await createTemplate(request, headers, templateKey, 'inbox');
  await publishTemplate(request, headers, template);
  const file = await uploadHostFileViaApi(request, clientKind, {
    fileName: `e2e-${stamp}.pdf`,
    content: '%PDF-1.4 e2e',
    contentType: 'application/pdf',
    accessToken: token
  });
  const userId = await getCurrentUserId(request, token, origin);

  const response = await request.post(`${apiBaseUrl}/api/v1/notifications/intents`, {
    headers,
    data: intentBody(templateKey, userId, `idem-inbox-${stamp}`, [file.id])
  });
  expect(response.status()).toBe(400);
  expect((await response.json()).code).toBe('notifications.intent_attachment_invalid');
});

test('Email Intent 可绑定 Files 引用附件（清单 43 API）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  await requireRegisteredEmailChannel(request, token, origin);
  await ensureAttachmentsProducerScene(request, token, origin);
  const templateKey = `e2e.email.attach.${stamp}`.slice(0, 32);
  const template = await createTemplate(request, headers, templateKey, 'email');
  await publishTemplate(request, headers, template);
  const file = await uploadHostFileViaApi(request, clientKind, {
    fileName: `report-${stamp}.pdf`,
    content: '%PDF-1.4 e2e attachment',
    contentType: 'application/pdf',
    accessToken: token
  });
  const userId = await getCurrentUserId(request, token, origin);

  const response = await request.post(`${apiBaseUrl}/api/v1/notifications/intents`, {
    headers,
    data: intentBody(templateKey, userId, `idem-email-${stamp}`, [file.id])
  });
  expect(response.status()).toBe(201);
  const intent = await response.json();
  expect(intent.attachments).toHaveLength(1);
  expect(intent.attachments[0].fileId).toBe(file.id);

  const detailResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/intents/${intent.id}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(detailResponse.ok()).toBeTruthy();
  const detail = await detailResponse.json();
  expect(detail.attachments).toHaveLength(1);
});

test('Email Intent 拒绝不允许的附件扩展名（清单 43）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  await requireRegisteredEmailChannel(request, token, origin);
  await ensureAttachmentsProducerScene(request, token, origin);
  const templateKey = `e2e.email.bad.${stamp}`.slice(0, 32);
  const template = await createTemplate(request, headers, templateKey, 'email');
  await publishTemplate(request, headers, template);
  const file = await uploadHostFileViaApi(request, clientKind, {
    fileName: `bad-${stamp}.exe`,
    content: 'MZ',
    contentType: 'application/octet-stream',
    accessToken: token
  });
  const userId = await getCurrentUserId(request, token, origin);

  const response = await request.post(`${apiBaseUrl}/api/v1/notifications/intents`, {
    headers,
    data: intentBody(templateKey, userId, `idem-bad-${stamp}`, [file.id])
  });
  expect(response.status()).toBe(400);
  expect((await response.json()).code).toBe('notifications.intent_attachment_invalid');
});
