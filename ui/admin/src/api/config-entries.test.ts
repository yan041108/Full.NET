import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  batchDeleteSettingsConfigEntries,
  createSettingsConfigEntry,
  deleteSettingsConfigEntry,
  disableSettingsConfigEntry,
  listSettingsConfigEntries,
  updateSettingsConfigEntry
} from './config-entries';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const sampleEntry = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
  configKey: 'system.title',
  displayName: '系统标题',
  description: '管理端标题',
  groupName: null,
  valueKind: 'string' as const,
  value: 'Full.NET',
  hasValue: true,
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00+00:00',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Settings 系统配置 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleEntry],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listSettingsConfigEntries()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/settings/config-entries?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文创建并禁用配置项', async () => {
    requestMock
      .mockResolvedValueOnce(sampleEntry)
      .mockResolvedValueOnce({ ...sampleEntry, isActive: false, version: 2 });

    await expect(
      createSettingsConfigEntry(
        'system.title',
        '系统标题',
        '管理端标题',
        'string',
        'Full.NET',
        10
      )
    ).resolves.toMatchObject({ configKey: 'system.title' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/settings/config-entries',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          configKey: 'system.title',
          displayName: '系统标题',
          description: '管理端标题',
          groupName: null,
          valueKind: 'string',
          value: 'Full.NET',
          displayOrder: 10
        })
      }),
      undefined
    );

    await expect(disableSettingsConfigEntry(sampleEntry.id))
      .resolves.toMatchObject({ isActive: false });
  });

  it('通过 JSON 正文更新配置项', async () => {
    requestMock.mockResolvedValueOnce({
      ...sampleEntry,
      displayName: '新标题',
      value: 'Admin',
      version: 2
    });

    await expect(
      updateSettingsConfigEntry(sampleEntry.id, '新标题', '说明', 'Admin', 10, 1)
    ).resolves.toMatchObject({ displayName: '新标题' });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/settings/config-entries/${sampleEntry.id}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          displayName: '新标题',
          description: '说明',
          groupName: null,
          value: 'Admin',
          displayOrder: 10,
          version: 1
        })
      }),
      undefined
    );
  });

  it('拒绝未知枚举并让 204 Operation 返回 void', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [{ ...sampleEntry, valueKind: 'xml' }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce(undefined);

    await expect(listSettingsConfigEntries())
      .rejects.toThrow('client.invalid_paged_result_of_config_entry_response');
    await expect(deleteSettingsConfigEntry(sampleEntry.id, 1)).resolves.toBeUndefined();
    await expect(batchDeleteSettingsConfigEntries([sampleEntry.id]))
      .resolves.toBeUndefined();
  });
});
