import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getSettingsEnumCatalog, listSettingsEnumCatalogs } from './enum-catalogs';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('Vue Settings 枚举目录 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验目录列表与详情', async () => {
    requestMock.mockResolvedValueOnce([{
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: null,
      memberCount: 1
    }]);
    await expect(listSettingsEnumCatalogs()).resolves.toHaveLength(1);
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/settings/enum-catalogs',
      { method: 'GET' },
      undefined
    );

    requestMock.mockResolvedValueOnce({
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: null,
      members: [{ code: 'string', label: 'string', displayOrder: 0 }]
    });
    await expect(getSettingsEnumCatalog('settings.config_value_kind'))
      .resolves.toMatchObject({ key: 'settings.config_value_kind' });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/settings/enum-catalogs/settings.config_value_kind',
      { method: 'GET' },
      undefined
    );
  });
});
