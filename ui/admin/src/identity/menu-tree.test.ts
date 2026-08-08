import { describe, expect, it } from 'vitest';
import {
  HOST_MENU_TYPES,
  type HostMenu,
  type HostMenuPermissionOption
} from '@fullnet/client-contracts';
import {
  buildHostMenuTree,
  filterMenusForTree,
  mergeCatalogButtonRows
} from './menu-tree';

function sampleMenu(overrides: Partial<HostMenu> = {}): HostMenu {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    parentId: null,
    routeName: 'users',
    path: '/identity/users',
    componentKey: 'users',
    title: 'Users',
    caption: 'Users',
    icon: 'user',
    displayOrder: 10,
    requiredPermission: 'identity.users.read',
    isSystem: true,
    isActive: true,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    version: 1,
    menuType: HOST_MENU_TYPES.menu,
    redirect: null,
    linkUrl: null,
    isHidden: false,
    isKeepAlive: false,
    isAffix: false,
    isEmbedded: false,
    remark: null,
    ...overrides
  };
}

describe('menu-tree', () => {
  it('merges catalog action rows under matching page routeName', () => {
    const menus = [sampleMenu()];
    const options: HostMenuPermissionOption[] = [{
      code: 'identity.users.create',
      moduleKey: 'identity',
      moduleTitle: 'Identity',
      pageId: 'users',
      pageTitle: 'Users',
      kind: 'action',
      displayName: 'Create',
      displayNameKey: 'authorization.actions.identity.users.create',
      actionId: 'identity.users.create',
      actionKey: 'create'
    }];

    const merged = mergeCatalogButtonRows(menus, options);
    expect(merged).toHaveLength(2);
    const button = merged.find(row => row.menuType === HOST_MENU_TYPES.button);
    expect(button?.parentId).toBe(menus[0].id);
    expect(button?.requiredPermission).toBe('identity.users.create');
  });

  it('builds parent-child tree from flat rows', () => {
    const parent = sampleMenu({
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      routeName: 'platform',
      title: 'Platform',
      menuType: HOST_MENU_TYPES.directory,
      componentKey: 'layout',
      path: '/platform'
    });
    const child = sampleMenu({
      id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      parentId: parent.id,
      routeName: 'custom-page',
      title: 'Custom'
    });
    const tree = buildHostMenuTree([parent, child]);
    expect(tree).toHaveLength(1);
    expect(tree[0].children).toHaveLength(1);
    expect(tree[0].children?.[0].routeName).toBe('custom-page');
  });

  it('keeps ancestor chain when filtering', () => {
    const parent = sampleMenu({
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      routeName: 'platform',
      title: 'Platform'
    });
    const child = sampleMenu({
      id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      parentId: parent.id,
      routeName: 'custom-page',
      title: 'Target'
    });
    const filtered = filterMenusForTree(
      [parent, child],
      row => row.title.includes('Target')
    );
    expect(filtered.map(row => row.id)).toEqual([parent.id, child.id]);
  });
});
