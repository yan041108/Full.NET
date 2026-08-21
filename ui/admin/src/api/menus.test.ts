import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostMenu,
  disableHostMenu,
  listHostMenus,
  updateHostMenu
} from './menus';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const menuId = '01912345-6789-7abc-8def-0123456789ab';

const sampleMenu = {
  id: menuId,
  parentId: null,
  routeName: 'custom-overview',
  path: '/',
  componentKey: 'overview',
  title: '自定义工作台',
  caption: 'Custom overview',
  icon: 'grid',
  displayOrder: 12,
  requiredPermission: 'platform.dashboard.read',
  isSystem: false,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1,
  menuType: 'menu',
  redirect: null,
  linkUrl: null,
  isHidden: false,
  isKeepAlive: true,
  isAffix: false,
  isEmbedded: false,
  remark: null
};

describe('Vue Host 菜单 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleMenu],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostMenus()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/menus?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('通过 JSON 正文创建、更新并禁用菜单', async () => {
    requestMock
      .mockResolvedValueOnce(sampleMenu)
      .mockResolvedValueOnce({ ...sampleMenu, title: '新标题', version: 2 })
      .mockResolvedValueOnce({ ...sampleMenu, isActive: false, version: 3 });

    await createHostMenu({
      parentId: null,
      routeName: 'custom-overview',
      path: '/',
      componentKey: 'overview',
      title: '自定义工作台',
      caption: 'Custom overview',
      icon: 'grid',
      displayOrder: 12,
      requiredPermission: 'platform.dashboard.read'
    });
    await updateHostMenu(menuId, {
      parentId: null,
      path: '/',
      componentKey: 'overview',
      title: '新标题',
      caption: 'Custom overview',
      icon: 'grid',
      displayOrder: 12,
      requiredPermission: 'platform.dashboard.read',
      version: 1
    });
    await disableHostMenu(menuId);

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/menus',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/identity/menus/${menuId}`,
      expect.objectContaining({ method: 'PUT' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/identity/menus/${menuId}/disable`,
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });
});
