import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  assignHostTenantPackage,
  createHostTenant,
  disableHostTenant,
  listHostTenants,
  updateHostTenant
} from './tenants';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const sampleTenant = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf294',
  identifier: 'acme',
  name: 'Acme Corporation',
  domain: 'acme.localhost',
  isActive: true,
  version: 1,
  defaultLocale: 'zh-CN'
};

describe('Vue Host 租户 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleTenant],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostTenants()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/tenancy/tenants?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文开通并禁用租户', async () => {
    requestMock
      .mockResolvedValueOnce(sampleTenant)
      .mockResolvedValueOnce({ ...sampleTenant, isActive: false });

    await createHostTenant('parity', '对等租户', 'parity.localhost');
    await disableHostTenant('019bc2b1-2a40-7cc3-8992-a80de51bf294');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/tenancy/tenants',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          identifier: 'parity',
          name: '对等租户',
          domain: 'parity.localhost'
        })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/tenancy/tenants/019bc2b1-2a40-7cc3-8992-a80de51bf294/disable',
      { method: 'POST' },
      undefined
    );
  });

  it('通过 JSON 正文更新显示名称与乐观版本', async () => {
    requestMock.mockResolvedValue({
      ...sampleTenant,
      name: '新名称',
      version: 2
    });

    await updateHostTenant('019bc2b1-2a40-7cc3-8992-a80de51bf294', '新名称', 1);

    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/tenancy/tenants/019bc2b1-2a40-7cc3-8992-a80de51bf294',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ name: '新名称', version: 1 })
      }),
      undefined
    );
  });

  it('通过 JSON 正文分配租户套餐', async () => {
    const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
    requestMock.mockResolvedValue({
      ...sampleTenant,
      tenantPackageId: packageId,
      version: 2
    });

    await assignHostTenantPackage(sampleTenant.id, packageId, 1);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/tenancy/tenants/${sampleTenant.id}/package`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ tenantPackageId: packageId, version: 1 })
      }),
      undefined
    );
  });
});
