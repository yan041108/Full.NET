import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostUserOrganizationPosition,
  createHostUserOrganizationUnit,
  disableHostUserOrganizationPosition,
  disableHostUserOrganizationUnit,
  getHostUserOrganizationReference,
  updateHostUserOrganizationPosition,
  updateHostUserOrganizationUnit
} from './host-user-organization-reference';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf331';
const unitId = '019bc2b1-2a40-7cc3-8992-a80de51bf332';
const positionId = '019bc2b1-2a40-7cc3-8992-a80de51bf333';
const unitAssignmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf334';
const positionAssignmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf335';

const sampleUnitAssignment = {
  id: unitAssignmentId,
  userId,
  username: 'admin',
  displayName: '系统管理员',
  unitId,
  unitCode: 'hq',
  unitName: '总部',
  isPrimary: true,
  isActive: true,
  createdAtUtc: '2026-08-06T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

const samplePositionAssignment = {
  id: positionAssignmentId,
  userId,
  username: 'admin',
  displayName: '系统管理员',
  positionId,
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
        id: unitId,
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
        id: positionId,
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

    await expect(getHostUserOrganizationReference(tenantId)).resolves.toMatchObject({
      userUnits: [sampleUnitAssignment],
      userPositions: [samplePositionAssignment]
    });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/organization/host-user-management/reference?tenantId=${tenantId}`,
      { method: 'GET' },
      undefined
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

    await createHostUserOrganizationUnit(tenantId, userId, unitId, true);
    await updateHostUserOrganizationUnit(tenantId, unitAssignmentId, false, 1);
    await disableHostUserOrganizationUnit(tenantId, unitAssignmentId);
    await createHostUserOrganizationPosition(tenantId, userId, positionId, true);
    await updateHostUserOrganizationPosition(tenantId, positionAssignmentId, true, 1);
    await disableHostUserOrganizationPosition(tenantId, positionAssignmentId);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/organization/host-user-management/user-units?tenantId=${tenantId}`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ userId, unitId, isPrimary: true })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/organization/host-user-management/user-units/${unitAssignmentId}?tenantId=${tenantId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ isPrimary: false, version: 1 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/organization/host-user-management/user-units/${unitAssignmentId}/disable?tenantId=${tenantId}`,
      { method: 'POST' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      4,
      `/api/v1/organization/host-user-management/user-positions?tenantId=${tenantId}`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ userId, positionId, isPrimary: true })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      5,
      `/api/v1/organization/host-user-management/user-positions/${positionAssignmentId}?tenantId=${tenantId}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ isPrimary: true, version: 1 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      6,
      `/api/v1/organization/host-user-management/user-positions/${positionAssignmentId}/disable?tenantId=${tenantId}`,
      { method: 'POST' },
      undefined
    );
  });
});
