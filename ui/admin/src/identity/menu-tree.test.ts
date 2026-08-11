import { describe, expect, it } from 'vitest';
import {
  HOST_MENU_TYPES,
  type HostMenu,
  type HostMenuPermissionOption
} from '@fullnet/client-contracts';
import {
  buildHostMenuTree,
  filterMenusForTree,
  isPersistedMenuRow,
  isVirtualCatalogButtonRow,
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
      code: 'identity.users.read',
      moduleKey: 'identity',
      moduleTitle: 'Identity',
      pageId: 'users',
      pageTitle: 'Users',
      kind: 'page',
      displayName: 'Users',
      displayNameKey: 'authorization.pages.users'
    }, {
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
    const button = merged.find(row => row.menuType === HOST_MENU_TYPES.button);
    expect(button?.parentId).toBe(menus[0].id);
    expect(button?.requiredPermission).toBe('identity.users.create');
    expect(isVirtualCatalogButtonRow(button!)).toBe(true);
  });

  it('skips virtual buttons when the permission already exists in host menus', () => {
    const menus = [sampleMenu({
      menuType: HOST_MENU_TYPES.button,
      requiredPermission: 'identity.users.create',
      routeName: 'create'
    })];
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
    expect(merged).toHaveLength(1);
    expect(isPersistedMenuRow(merged[0])).toBe(true);
  });

  it('keeps persisted menu parentId unchanged', () => {
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
    const merged = mergeCatalogButtonRows([parent, child], []);
    expect(merged.find(row => row.id === child.id)?.parentId).toBe(parent.id);
  });

  it('orders directory children before pages under the same parent', () => {
    const directory = sampleMenu({
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      routeName: 'platform',
      title: 'Platform',
      menuType: HOST_MENU_TYPES.directory,
      componentKey: 'layout',
      path: '/platform',
      displayOrder: 30
    });
    const page = sampleMenu({
      id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      parentId: directory.id,
      routeName: 'custom-page',
      title: 'Custom',
      displayOrder: 10
    });
    const nestedDirectory = sampleMenu({
      id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
      parentId: directory.id,
      routeName: 'nested',
      title: 'Nested',
      menuType: HOST_MENU_TYPES.directory,
      componentKey: 'layout',
      displayOrder: 20
    });
    const tree = buildHostMenuTree([directory, page, nestedDirectory]);
    expect(tree[0].children?.map(row => row.menuType)).toEqual([
      HOST_MENU_TYPES.directory,
      HOST_MENU_TYPES.menu
    ]);
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
