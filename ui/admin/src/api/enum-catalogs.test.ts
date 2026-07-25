import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { getSettingsEnumCatalog, listSettingsEnumCatalogs } from './enum-catalogs';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

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

    requestMock.mockResolvedValueOnce({
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: null,
      members: [{ code: 'string', label: 'string', displayOrder: 0 }]
    });
    await expect(getSettingsEnumCatalog('settings.config_value_kind'))
      .resolves.toMatchObject({ key: 'settings.config_value_kind' });
  });
});
