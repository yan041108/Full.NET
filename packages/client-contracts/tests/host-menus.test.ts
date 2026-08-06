import { describe, expect, it } from 'vitest';
import {
  HOST_MENU_COMPONENT_OPTIONS,
  isHostMenu,
  isHostMenuPermissionOptionArray,
  isHostMenuPage,
  isUpdateHostMenuRequest
} from '../src/host-menus';

describe('Host 菜单客户端契约', () => {
  it('校验分页列表、单条菜单与写请求', () => {
    const menu = {
      id: 'menu-id',
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
      version: 1
    };
    expect(isHostMenu(menu)).toBe(true);
    expect(isHostMenuPage({
      items: [menu],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostMenu({ id: 'menu-id' })).toBe(false);
    expect(isUpdateHostMenuRequest({
      parentId: null,
      path: '/',
      componentKey: 'overview',
      title: '更新标题',
      caption: 'Caption',
      icon: 'grid',
      displayOrder: 12,
      requiredPermission: 'platform.dashboard.read',
      version: 2
    })).toBe(true);
    expect(HOST_MENU_COMPONENT_OPTIONS.some(entry => entry.componentKey === 'menus'))
      .toBe(true);
    expect(isHostMenuPermissionOptionArray([
      {
        code: 'identity.menus.read',
        moduleKey: 'identity',
        moduleTitle: '身份与权限',
        pageId: 'menus',
        pageTitle: '菜单管理',
        kind: 'page',
        displayName: '菜单管理',
        displayNameKey: 'authorization.pages.menus'
      },
      {
        code: 'jobs.schedules.preview',
        moduleKey: 'jobs',
        moduleTitle: '任务调度',
        pageId: 'host-job-schedules',
        pageTitle: '计划任务',
        kind: 'action',
        displayName: '预览计划',
        displayNameKey: 'authorization.actions.jobs.schedules.preview',
        actionId: 'jobs.schedules.preview',
        actionKey: 'preview'
      }
    ])).toBe(true);
  });
});
