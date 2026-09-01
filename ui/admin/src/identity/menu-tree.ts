import {
  HOST_MENU_TYPES,
  type HostMenu,
  type HostMenuPermissionOption,
  type HostMenuType
} from '@fullnet/client-contracts';

/** Host 菜单树节点，兼容数据库持久化记录与前端补齐的虚拟按钮行。 */
export interface MenuTreeRow extends HostMenu {
  isVirtual?: boolean;
  children?: MenuTreeRow[];
}

/** 父级菜单选择树节点，只保留级联选择所需的最小字段。 */
export interface MenuTreeOption {
  value: string;
  label: string;
  children?: MenuTreeOption[];
}

/** 用固定前缀标识仅存在于授权目录中的虚拟按钮行，避免与数据库主键混淆。 */
const VIRTUAL_BUTTON_ID_PREFIX = 'catalog:action:';

const MENU_TYPE_SORT_ORDER: Record<HostMenuType, number> = {
  [HOST_MENU_TYPES.directory]: 0,
  [HOST_MENU_TYPES.menu]: 1,
  [HOST_MENU_TYPES.button]: 2
};

/**
 * 在 Host 菜单树后挂接授权目录中的按钮行；目录与页面以数据库记录为准，可编辑。
 */
export function mergeCatalogButtonRows(
  menus: readonly HostMenu[],
  permissionOptions: readonly HostMenuPermissionOption[]
): MenuTreeRow[] {
  const pageKeyToRowId = new Map<string, string>();
  const permissionsInDb = new Set<string>();
  for (const menu of menus) {
    pageKeyToRowId.set(menu.routeName, menu.id);
    if (menu.componentKey) {
      pageKeyToRowId.set(menu.componentKey, menu.id);
    }
    if (menu.requiredPermission) {
      permissionsInDb.add(menu.requiredPermission);
    }
  }

  const menuRows: MenuTreeRow[] = menus.map(menu => ({ ...menu }));
  const virtualRows: MenuTreeRow[] = [];
  let actionOrder = 0;

  for (const option of permissionOptions) {
    if (option.kind !== 'action' || !option.actionId) {
      continue;
    }
    if (permissionsInDb.has(option.code)) {
      continue;
    }

    actionOrder += 10;
    const parentId = pageKeyToRowId.get(option.pageId) ?? null;
    virtualRows.push({
      id: `${VIRTUAL_BUTTON_ID_PREFIX}${option.actionId}`,
      parentId,
      routeName: option.actionKey ?? option.actionId,
      path: '',
      componentKey: '',
      title: option.displayName,
      caption: option.pageTitle,
      icon: 'key',
      displayOrder: actionOrder,
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

  return [...menuRows, ...virtualRows];
}

/** 构建可安全渲染的菜单树；父节点缺失或存在环时自动降级为根节点，避免界面死循环。 */
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

  return sortMenuRows(roots);
}

/** 按匹配条件筛出菜单，同时保留命中节点的全部祖先链，便于树表保持可展开路径。 */
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

/** 构建父菜单下拉树，并排除当前菜单及其全部后代，防止把节点挂到自身子树下。 */
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

/** 判断行是否为授权目录投影出来的虚拟按钮，而不是数据库中的真实菜单记录。 */
export function isVirtualCatalogButtonRow(row: MenuTreeRow): boolean {
  return row.isVirtual === true || row.id.startsWith(VIRTUAL_BUTTON_ID_PREFIX);
}

/** 判断行是否来自持久化菜单表，供编辑、删除等只允许真实记录的操作复用。 */
export function isPersistedMenuRow(row: MenuTreeRow): boolean {
  return !isVirtualCatalogButtonRow(row);
}

/** 返回菜单类型对应的国际化键，保持表格、表单与筛选器展示语义一致。 */
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

/** Element Plus 标签类型，对齐 Admin.NET 目录/菜单/按钮配色。 */
export function menuTypeTagType(
  menuType: HostMenuType
): 'warning' | 'primary' | 'info' | undefined {
  switch (menuType) {
    case HOST_MENU_TYPES.directory:
      return 'warning';
    case HOST_MENU_TYPES.button:
      return 'info';
    default:
      return 'primary';
  }
}

/** 按类型、显示顺序和中文标题稳定排序，确保树表与父级选择器展示一致。 */
function sortMenuRows<T extends { displayOrder: number; title: string; menuType?: HostMenuType }>(
  rows: readonly T[]
): T[] {
  return [...rows].sort((left, right) => {
    const leftType = left.menuType === undefined
      ? MENU_TYPE_SORT_ORDER[HOST_MENU_TYPES.menu]
      : MENU_TYPE_SORT_ORDER[left.menuType];
    const rightType = right.menuType === undefined
      ? MENU_TYPE_SORT_ORDER[HOST_MENU_TYPES.menu]
      : MENU_TYPE_SORT_ORDER[right.menuType];
    return leftType - rightType
      || left.displayOrder - right.displayOrder
      || left.title.localeCompare(right.title, 'zh-CN');
  });
}
