import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createSettingsConfigEntry,
  disableSettingsConfigEntry,
  listSettingsConfigEntries,
  updateSettingsConfigEntry
} from './config-entries';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleEntry = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
  configKey: 'system.title',
  displayName: '系统标题',
  description: '管理端标题',
  valueKind: 'string' as const,
  value: 'Full.NET',
  displayOrder: 10,
  isActive: true,
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
      '/api/v1/settings/config-entries?page=1&pageSize=20'
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
          valueKind: 'string',
          value: 'Full.NET',
          displayOrder: 10
        })
      })
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
          value: 'Admin',
          displayOrder: 10,
          version: 1
        })
      })
    );
  });
});
