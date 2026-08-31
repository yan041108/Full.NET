import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  buildNonSecretConfig,
  createNotificationProviderProfile,
  createNotificationTemplate,
  listNotificationProviderTypes,
  listNotificationTemplates,
  parseNonSecretConfigJson,
  retryNotificationDelivery
} from './notification-platform';
import type { NotificationProviderTypeDescriptor } from './notification-platform';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const descriptor: NotificationProviderTypeDescriptor = {
  providerTypeKey: 'test.notification',
  adapterVersion: '1.0.0',
  supportedChannelKeys: ['test'],
  nonSecretFields: [
    { name: 'endpointBaseUrl', typeKey: 'string', required: true },
    { name: 'fromDisplayName', typeKey: 'string', required: false }
  ],
  secretFieldKeys: ['apiToken'],
  supportsNativeAot: true,
  receiptModeKey: 'signed'
};

const template = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411201',
  templateKey: 'order.shipped',
  channelKey: 'inbox',
  contentCategoryKey: 'transactional',
  draftSubject: '已发货',
  draftBodyJson: '{"text":"订单已发货"}',
  draftParameterSchemaJson: '{"schemaVersion":1,"parameters":[]}',
  draftRevision: 1,
  latestPublishedVersionId: null,
  latestPublishedVersionNumber: null,
  latestContentHash: null,
  latestContentClassificationKey: null,
  createdAtUtc: '2026-08-31T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('notification-platform api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists templates and creates an inbox draft', async () => {
    requestMock
      .mockResolvedValueOnce({ items: [template], page: 1, pageSize: 20, total: 1 })
      .mockResolvedValueOnce(template);

    await expect(listNotificationTemplates(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/templates?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );

    await createNotificationTemplate({
      templateKey: 'order.shipped',
      channelKey: 'inbox',
      contentCategoryKey: 'transactional',
      draftSubject: '已发货',
      draftBody: { text: '订单已发货' },
      parameterSchema: { schemaVersion: 1, parameters: [] }
    });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/notifications/templates',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });

  it('lists provider types and creates a profile without secret plaintext', async () => {
    requestMock
      .mockResolvedValueOnce([descriptor])
      .mockResolvedValueOnce({
        id: '0198f36e-f7a7-7c52-9cbb-774e67411202',
        profileKey: 'primary',
        providerTypeKey: 'test.notification',
        nonSecretConfigJson: '{"endpointBaseUrl":"https://example.test"}',
        secretStatus: 'configured',
        isEnabled: false,
        draftRevision: 1,
        latestPublishedVersionId: null,
        latestPublishedVersionNumber: null,
        latestAdapterVersion: null,
        createdAtUtc: '2026-08-31T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      });

    await expect(listNotificationProviderTypes()).resolves.toEqual([descriptor]);
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/provider-types',
      { method: 'GET' },
      undefined
    );

    const body = JSON.stringify({
      endpointBaseUrl: 'https://example.test'
    });
    await createNotificationProviderProfile({
      profileKey: 'primary',
      providerTypeKey: 'test.notification',
      nonSecretConfig: { endpointBaseUrl: 'https://example.test' },
      secretReference: 'vault://test/notifications-api-token'
    });
    const createCall = requestMock.mock.calls[1];
    expect(createCall?.[0]).toBe('/api/v1/notifications/provider-profiles');
    expect(String(createCall?.[1]?.body)).not.toContain('apiToken');
    expect(String(createCall?.[1]?.body)).toContain(body);
  });

  it('retries a delivery with a short reason', async () => {
    requestMock.mockResolvedValueOnce({
      id: '0198f36e-f7a7-7c52-9cbb-774e67411203',
      intentId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
      recipientId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      channelKey: 'test',
      providerProfileVersionId: null,
      bindingVersionId: null,
      statusKey: 'accepted',
      revision: 3,
      nextAttemptAtUtc: '2026-08-31T00:00:00Z',
      createdAtUtc: '2026-08-31T00:00:00Z',
      updatedAtUtc: null,
      attempts: []
    });

    await retryNotificationDelivery('0198f36e-f7a7-7c52-9cbb-774e67411203', {
      revision: 2,
      reason: 'ops-retry'
    });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/notifications/deliveries/0198f36e-f7a7-7c52-9cbb-774e67411203/retry',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ revision: 2, reason: 'ops-retry' })
      }),
      undefined
    );
  });

  it('rejects secret fields, unknown keys and unknown type keys', () => {
    expect(() => buildNonSecretConfig(descriptor, { apiToken: 'secret' }))
      .toThrow('client.unknown_provider_config_field');
    expect(() => buildNonSecretConfig(descriptor, { webhookHeaders: {} }))
      .toThrow('client.unknown_provider_config_field');
    expect(() => parseNonSecretConfigJson(
      '{"endpointBaseUrl":"https://example.test","extra":"x"}',
      descriptor
    )).toThrow('client.unknown_provider_config_field');
    expect(buildNonSecretConfig(descriptor, {
      endpointBaseUrl: 'https://example.test'
    })).toEqual({ endpointBaseUrl: 'https://example.test' });
  });

  it('accepts integer strings from schema-driven inputs', () => {
    const numeric: NotificationProviderTypeDescriptor = {
      ...descriptor,
      nonSecretFields: [{ name: 'timeoutSeconds', typeKey: 'integer', required: false }]
    };
    expect(buildNonSecretConfig(numeric, { timeoutSeconds: '15' })).toEqual({ timeoutSeconds: 15 });
    expect(() => buildNonSecretConfig(numeric, { timeoutSeconds: '15s' }))
      .toThrow('client.unknown_provider_config_field');
  });
});
