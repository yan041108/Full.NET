import { describe, expect, it } from 'vitest';
import {
  isCreateHostApiKeyResult,
  isHostApiKey,
  isHostApiKeyPage
} from '../src/host-api-keys';

describe('Host API Key 客户端契约', () => {
  it('校验创建结果、单条与分页列表', () => {
    const key = {
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
      userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
      username: 'admin',
      displayName: '集成测试密钥',
      keyPrefix: 'fnk_abcd1234',
      permissions: ['identity.users.read'],
      expiresAtUtc: null,
      isActive: true,
      lastUsedAtUtc: null,
      createdAtUtc: '2026-07-26T00:00:00Z'
    };
    expect(isHostApiKey(key)).toBe(true);
    expect(isCreateHostApiKeyResult({ key, secret: 'fnk_secret_value' })).toBe(true);
    expect(isHostApiKeyPage({
      items: [key],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostApiKey({ id: 'invalid' })).toBe(false);
  });
});
