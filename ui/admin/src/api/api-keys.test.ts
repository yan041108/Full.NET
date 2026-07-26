import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostApiKey,
  disableHostApiKey,
  listHostApiKeys
} from './api-keys';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleApiKey = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  username: 'automation',
  displayName: '部署流水线',
  keyPrefix: 'fn_live_abcd',
  permissions: ['platform.dashboard.read'],
  expiresAtUtc: null,
  isActive: true,
  lastUsedAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z'
};

describe('Vue Host API Key API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验并返回分页列表', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleApiKey],
      page: 2,
      pageSize: 10,
      total: 11
    });

    await expect(listHostApiKeys(2, 10, sampleApiKey.userId, '部署'))
      .resolves.toMatchObject({ total: 11 });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/identity/api-keys?page=2&pageSize=10&userId=${sampleApiKey.userId}&displayNameContains=%E9%83%A8%E7%BD%B2`
    );
  });

  it('创建时发送结构化权限并校验一次性明文', async () => {
    requestMock.mockResolvedValueOnce({
      key: sampleApiKey,
      secret: 'fn_live_secret'
    });

    await expect(createHostApiKey({
      userId: sampleApiKey.userId,
      displayName: sampleApiKey.displayName,
      permissions: sampleApiKey.permissions,
      expiresAtUtc: null
    })).resolves.toMatchObject({ secret: 'fn_live_secret' });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/api-keys',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          userId: sampleApiKey.userId,
          displayName: sampleApiKey.displayName,
          permissions: sampleApiKey.permissions,
          expiresAtUtc: null
        })
      })
    );
  });

  it('通过 POST 禁用 API Key', async () => {
    requestMock.mockResolvedValueOnce({ ...sampleApiKey, isActive: false });

    await disableHostApiKey(sampleApiKey.id);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/identity/api-keys/${sampleApiKey.id}/disable`,
      { method: 'POST' }
    );
  });

  it('拒绝缺少一次性明文的创建响应', async () => {
    requestMock.mockResolvedValueOnce({ key: sampleApiKey, secret: '' });

    await expect(createHostApiKey({
      userId: sampleApiKey.userId,
      displayName: sampleApiKey.displayName,
      permissions: sampleApiKey.permissions,
      expiresAtUtc: null
    })).rejects.toThrow('client.invalid_create_host_api_key_result');
  });
});
