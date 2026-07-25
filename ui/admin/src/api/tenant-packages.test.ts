import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostTenantPackage,
  disableHostTenantPackage,
  listHostTenantPackages,
  updateHostTenantPackage
} from './tenant-packages';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const samplePackage = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  code: 'standard',
  name: '标准版',
  description: '默认套餐',
  isActive: true,
  version: 1,
  assignedTenantCount: 0
};

describe('Vue Host 租户套餐 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [samplePackage],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostTenantPackages()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/tenancy/tenant-packages?page=1&pageSize=20'
    );
  });

  it('通过 JSON 正文创建并禁用套餐', async () => {
    requestMock
      .mockResolvedValueOnce(samplePackage)
      .mockResolvedValueOnce({ ...samplePackage, isActive: false, version: 2 });

    await expect(
      createHostTenantPackage('standard', '标准版', '默认套餐')
    ).resolves.toMatchObject({ code: 'standard' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/tenancy/tenant-packages',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          code: 'standard',
          name: '标准版',
          description: '默认套餐'
        })
      })
    );

    await expect(disableHostTenantPackage(samplePackage.id))
      .resolves.toMatchObject({ isActive: false });
  });

  it('通过 JSON 正文更新套餐', async () => {
    requestMock.mockResolvedValueOnce({ ...samplePackage, name: '专业版', version: 2 });

    await expect(
      updateHostTenantPackage(samplePackage.id, '专业版', '说明', 1)
    ).resolves.toMatchObject({ name: '专业版' });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/tenancy/tenant-packages/${samplePackage.id}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          name: '专业版',
          description: '说明',
          version: 1
        })
      })
    );
  });
});
