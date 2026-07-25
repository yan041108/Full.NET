import { describe, expect, it } from 'vitest';
import {
  isCreateHostTenantPackageRequest,
  isHostTenantPackage,
  isHostTenantPackagePage,
  isUpdateHostTenantPackageRequest
} from '../src/host-tenant-packages';

const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';

describe('Host 租户套餐契约', () => {
  const tenantPackage = {
    id: packageId,
    code: 'standard',
    name: '标准版',
    description: '默认套餐',
    isActive: true,
    version: 1,
    assignedTenantCount: 0
  };

  it('校验套餐详情、分页与写请求', () => {
    expect(isHostTenantPackage(tenantPackage)).toBe(true);
    expect(isHostTenantPackagePage({
      items: [tenantPackage],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostTenantPackage({ ...tenantPackage, id: 'bad' })).toBe(false);
    expect(isCreateHostTenantPackageRequest({
      code: 'pro',
      name: '专业版',
      description: null
    })).toBe(true);
    expect(isUpdateHostTenantPackageRequest({
      name: '新名称',
      description: '说明',
      version: 2
    })).toBe(true);
    expect(isUpdateHostTenantPackageRequest({ name: '', version: 2 })).toBe(false);
  });
});
