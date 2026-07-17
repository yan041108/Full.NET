import { describe, expect, it } from 'vitest';
import {
  isTenantContextSummaryArray,
  isTenantContextTokenResponse
} from '../src/tenancy';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

describe('租户上下文契约', () => {
  it('接受完整的活动租户摘要列表', () => {
    expect(isTenantContextSummaryArray([{
      id: tenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      domain: 'acme.localhost'
    }])).toBe(true);
  });

  it('拒绝残缺、重复和非法租户摘要', () => {
    const tenant = {
      id: tenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      domain: 'acme.localhost'
    };
    expect(isTenantContextSummaryArray([{ id: tenantId }])).toBe(false);
    expect(isTenantContextSummaryArray([tenant, { ...tenant }])).toBe(false);
    expect(isTenantContextSummaryArray([{ ...tenant, id: 'not-a-guid' }])).toBe(false);
  });

  it('接受租户与 Host 上下文令牌响应', () => {
    expect(isTenantContextTokenResponse({
      accessToken: 'tenant-token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: `tenant:${tenantId.replaceAll('-', '')}`
      }
    })).toBe(true);
    expect(isTenantContextTokenResponse({
      accessToken: 'host-token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId: null,
        identifier: 'host',
        name: 'Full.NET Host',
        scope: 'host'
      }
    })).toBe(true);
  });

  it('拒绝上下文与作用域不一致的令牌响应', () => {
    expect(isTenantContextTokenResponse({
      accessToken: 'token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: 'host'
      }
    })).toBe(false);
    expect(isTenantContextTokenResponse({
      accessToken: 'token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId: null,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: 'host'
      }
    })).toBe(false);
  });
});
