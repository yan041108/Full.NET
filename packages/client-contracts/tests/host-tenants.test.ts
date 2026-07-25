import { describe, expect, it } from 'vitest';
import {
  isCreateHostTenantRequest,
  isHostTenant,
  isHostTenantPage,
  isUpdateHostTenantRequest,
  isAssignHostTenantPackageRequest
} from '../src/host-tenants';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

describe('Host 租户管理契约', () => {
  const tenant = {
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost',
    isActive: true,
    version: 1,
    defaultLocale: 'zh-CN'
  };

  it('校验租户详情、分页与写请求', () => {
    expect(isHostTenant(tenant)).toBe(true);
    expect(isHostTenantPage({
      items: [tenant],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostTenant({ ...tenant, id: 'bad' })).toBe(false);
    expect(isCreateHostTenantRequest({
      identifier: 'parity',
      name: '对等租户',
      domain: 'parity.localhost'
    })).toBe(true);
    expect(isCreateHostTenantRequest({
      identifier: 'parity',
      name: '对等租户',
      domain: 'parity.localhost',
      tenantPackageId: tenantId
    })).toBe(true);
    expect(isUpdateHostTenantRequest({ name: '新名称', version: 2 })).toBe(true);
    expect(isUpdateHostTenantRequest({ name: '', version: 2 })).toBe(false);
    expect(isAssignHostTenantPackageRequest({
      tenantPackageId: tenantId,
      version: 2
    })).toBe(true);
    expect(isAssignHostTenantPackageRequest({
      tenantPackageId: null,
      version: 2
    })).toBe(true);
  });
});
