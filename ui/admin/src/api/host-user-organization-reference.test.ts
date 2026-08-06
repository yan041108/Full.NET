import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostUserOrganizationPosition,
  createHostUserOrganizationUnit,
  disableHostUserOrganizationPosition,
  disableHostUserOrganizationUnit,
  getHostUserOrganizationReference,
  updateHostUserOrganizationPosition,
  updateHostUserOrganizationUnit
} from './host-user-organization-reference';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleUnitAssignment = {
  id: 'unit-assignment-id',
  userId: 'user-id',
  username: 'admin',
  displayName: '系统管理员',
  unitId: 'unit-id',
  unitCode: 'hq',
  unitName: '总部',
  isPrimary: true,
  isActive: true,
  createdAtUtc: '2026-08-06T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

const samplePositionAssignment = {
  id: 'position-assignment-id',
  userId: 'user-id',
  username: 'admin',
  displayName: '系统管理员',
  positionId: 'position-id',
  positionCode: 'engineer',
  positionName: '工程师',
  isPrimary: true,
  isActive: true,
  createdAtUtc: '2026-08-06T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Host 用户组织参考 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验聚合参考响应结构', async () => {
    requestMock.mockResolvedValueOnce({
      units: [{
        id: 'unit-id',
        parentId: null,
        code: 'hq',
        name: '总部',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-08-06T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      positions: [{
        id: 'position-id',
        code: 'engineer',
        name: '工程师',
        unitId: null,
        unitCode: null,
        unitName: null,
        positionLevelId: null,
        positionLevelCode: null,
        positionLevelName: null,
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-08-06T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      userUnits: [sampleUnitAssignment],
      userPositions: [samplePositionAssignment]
    });

    await expect(getHostUserOrganizationReference('tenant-id')).resolves.toMatchObject({
      userUnits: [sampleUnitAssignment],
      userPositions: [samplePositionAssignment]
    });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/organization/host-user-management/reference?tenantId=tenant-id'
    );
  });

  it('通过 JSON 正文创建、更新并禁用 Host 用户机构与职位隶属', async () => {
    requestMock
      .mockResolvedValueOnce(sampleUnitAssignment)
      .mockResolvedValueOnce({ ...sampleUnitAssignment, isPrimary: false, version: 2 })
      .mockResolvedValueOnce({ ...sampleUnitAssignment, isActive: false, version: 3 })
      .mockResolvedValueOnce(samplePositionAssignment)
      .mockResolvedValueOnce({ ...samplePositionAssignment, version: 2 })
      .mockResolvedValueOnce({ ...samplePositionAssignment, isActive: false, version: 3 });

    await createHostUserOrganizationUnit('tenant-id', 'user-id', 'unit-id', true);
    await updateHostUserOrganizationUnit('tenant-id', 'unit-assignment-id', false, 1);
    await disableHostUserOrganizationUnit('tenant-id', 'unit-assignment-id');
    await createHostUserOrganizationPosition('tenant-id', 'user-id', 'position-id', true);
    await updateHostUserOrganizationPosition('tenant-id', 'position-assignment-id', true, 1);
    await disableHostUserOrganizationPosition('tenant-id', 'position-assignment-id');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/host-user-management/user-units?tenantId=tenant-id',
      expect.objectContaining({ method: 'POST' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/host-user-management/user-units/unit-assignment-id?tenantId=tenant-id',
      expect.objectContaining({ method: 'PUT' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/organization/host-user-management/user-units/unit-assignment-id/disable?tenantId=tenant-id',
      expect.objectContaining({ method: 'POST' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      4,
      '/api/v1/organization/host-user-management/user-positions?tenantId=tenant-id',
      expect.objectContaining({ method: 'POST' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      5,
      '/api/v1/organization/host-user-management/user-positions/position-assignment-id?tenantId=tenant-id',
      expect.objectContaining({ method: 'PUT' })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      6,
      '/api/v1/organization/host-user-management/user-positions/position-assignment-id/disable?tenantId=tenant-id',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
