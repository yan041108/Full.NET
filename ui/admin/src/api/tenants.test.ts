import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostTenant,
  disableHostTenant,
  listHostTenants,
  updateHostTenant
} from './tenants';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

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
      '/api/v1/tenancy/tenants?page=1&pageSize=20'
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
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/tenancy/tenants/019bc2b1-2a40-7cc3-8992-a80de51bf294/disable',
      expect.objectContaining({ method: 'POST' })
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
      })
    );
  });
});
