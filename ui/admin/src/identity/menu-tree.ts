import {
  HOST_MENU_TYPES,
  type HostMenu,
  type HostMenuPermissionOption,
  type HostMenuType
} from '@fullnet/client-contracts';

export interface MenuTreeRow extends HostMenu {
  isVirtual?: boolean;
  children?: MenuTreeRow[];
}

export interface MenuTreeOption {
  value: string;
  label: string;
  children?: MenuTreeOption[];
}

const VIRTUAL_BUTTON_ID_PREFIX = 'catalog:action:';

export function mergeCatalogButtonRows(
  menus: readonly HostMenu[],
  permissionOptions: readonly HostMenuPermissionOption[]
): MenuTreeRow[] {
  const routeNameToMenuId = new Map(
    menus.map(menu => [menu.routeName, menu.id] as const)
  );
  const virtualButtons: MenuTreeRow[] = [];

  for (const option of permissionOptions) {
    if (option.kind !== 'action' || !option.actionId) {
      continue;
    }

    const parentId = routeNameToMenuId.get(option.pageId) ?? null;
    virtualButtons.push({
      id: `${VIRTUAL_BUTTON_ID_PREFIX}${option.actionId}`,
      parentId,
      routeName: option.actionKey ?? option.actionId,
      path: '',
      componentKey: '',
      title: option.displayName,
      caption: option.pageTitle,
      icon: 'key',
      displayOrder: 0,
      requiredPermission: option.code,
      isSystem: true,
      isActive: true,
      createdAtUtc: '',
      updatedAtUtc: null,
      version: 0,
      menuType: HOST_MENU_TYPES.button,
      redirect: null,
      linkUrl: null,
      isHidden: false,
      isKeepAlive: false,
      isAffix: false,
      isEmbedded: false,
      remark: null,
      isVirtual: true
    });
  }

  return [...menus, ...virtualButtons];
}

export function buildHostMenuTree(rows: readonly MenuTreeRow[]): MenuTreeRow[] {
  const byId = new Map(rows.map(row => [row.id, row]));
  const childrenByParent = new Map<string | null, MenuTreeRow[]>();

  for (const row of rows) {
    const parentKey =
      row.parentId && byId.has(row.parentId) ? row.parentId : null;
    const siblings = childrenByParent.get(parentKey) ?? [];
    siblings.push(row);
    childrenByParent.set(parentKey, siblings);
  }

  const attached = new Set<string>();

  const toNodes = (
    parentId: string | null,
    ancestors: ReadonlySet<string>
  ): MenuTreeRow[] =>
    sortMenuRows(childrenByParent.get(parentId) ?? []).flatMap(row => {
      if (ancestors.has(row.id) || attached.has(row.id)) {
        return [];
      }

      attached.add(row.id);
      const nextAncestors = new Set(ancestors);
      nextAncestors.add(row.id);
      return [{
        ...row,
        children: toNodes(row.id, nextAncestors)
      }];
    });

  const roots = toNodes(null, new Set());
  for (const row of sortMenuRows(rows)) {
    if (attached.has(row.id)) {
      continue;
    }
    attached.add(row.id);
    roots.push({
      ...row,
      children: toNodes(row.id, new Set([row.id]))
    });
  }

  return roots;
}

export function filterMenusForTree(
  rows: readonly MenuTreeRow[],
  matches: (row: MenuTreeRow) => boolean
): MenuTreeRow[] {
  const byId = new Map(rows.map(row => [row.id, row]));
  const keep = new Set<string>();

  for (const row of rows) {
    if (!matches(row)) {
      continue;
    }
    let current: MenuTreeRow | undefined = row;
    while (current) {
      if (keep.has(current.id)) {
        break;
      }
      keep.add(current.id);
      current = current.parentId ? byId.get(current.parentId) : undefined;
    }
  }

  return rows.filter(row => keep.has(row.id));
}

export function buildMenuParentTreeOptions(
  menus: readonly HostMenu[],
  excludeMenuId?: string
): MenuTreeOption[] {
  const excludedIds = new Set<string>();
  if (excludeMenuId) {
    excludedIds.add(excludeMenuId);
    const childrenByParent = new Map<string, string[]>();
    for (const menu of menus) {
      if (!menu.parentId) {
        continue;
      }
      const siblings = childrenByParent.get(menu.parentId) ?? [];
      siblings.push(menu.id);
      childrenByParent.set(menu.parentId, siblings);
    }
    const stack = [...(childrenByParent.get(excludeMenuId) ?? [])];
    while (stack.length > 0) {
      const current = stack.pop()!;
      if (excludedIds.has(current)) {
        continue;
      }
      excludedIds.add(current);
      const children = childrenByParent.get(current);
      if (children) {
        stack.push(...children);
      }
    }
  }

  const candidates = menus.filter(menu =>
    !excludedIds.has(menu.id)
    && menu.menuType !== HOST_MENU_TYPES.button);

  const childrenByParent = new Map<string | null, HostMenu[]>();
  for (const menu of candidates) {
    const bucket = childrenByParent.get(menu.parentId) ?? [];
    bucket.push(menu);
    childrenByParent.set(menu.parentId, bucket);
  }

  const walk = (parentId: string | null): MenuTreeOption[] =>
    sortMenuRows(childrenByParent.get(parentId) ?? []).map(menu => {
      const children = walk(menu.id);
      return children.length > 0
        ? { value: menu.id, label: `${menu.title} (${menu.routeName})`, children }
        : { value: menu.id, label: `${menu.title} (${menu.routeName})` };
    });

  return walk(null);
}

export function isVirtualMenuRow(row: MenuTreeRow): boolean {
  return row.isVirtual === true || row.id.startsWith(VIRTUAL_BUTTON_ID_PREFIX);
}

export function menuTypeLabelKey(menuType: HostMenuType): string {
  switch (menuType) {
    case HOST_MENU_TYPES.directory:
      return 'menus.typeDirectory';
    case HOST_MENU_TYPES.button:
      return 'menus.typeButton';
    default:
      return 'menus.typeMenu';
  }
}

function sortMenuRows<T extends { displayOrder: number; title: string }>(
  rows: readonly T[]
): T[] {
  return [...rows].sort((left, right) =>
    left.displayOrder - right.displayOrder
    || left.title.localeCompare(right.title, 'zh-CN'));
}
