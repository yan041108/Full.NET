import type {
  AuthorizationTreeAction,
  AuthorizationTreeModule,
  AuthorizationTreePage
} from '@fullnet/client-contracts';

/** Element Plus 树节点，仅承载目录展示与权限码映射。 */
export interface PermissionTreeNode {
  id: string;
  label: string;
  permissionCode: string;
  kind: 'module' | 'page' | 'action';
  pagePermissionCode?: string;
  children?: PermissionTreeNode[];
}

/** 将服务端授权树投影为本地树节点。 */
export function buildPermissionTreeNodes(
  modules: readonly AuthorizationTreeModule[]
): PermissionTreeNode[] {
  return modules.map(module => ({
    id: `module:${module.id}`,
    label: module.title,
    permissionCode: '',
    kind: 'module',
    children: module.pages.map(page => projectPageNode(page))
  }));
}

/** 收集授权树中的全部已知权限码。 */
export function collectCatalogPermissionCodes(
  modules: readonly AuthorizationTreeModule[]
): Set<string> {
  const codes = new Set<string>();
  for (const module of modules) {
    for (const page of module.pages) {
      collectPagePermissionCodes(page, codes);
    }
  }

  return codes;
}

/** 返回角色已存但当前目录未知的权限码。 */
export function findUnknownPermissionCodes(
  permissionCodes: readonly string[],
  catalog: ReadonlySet<string>
): string[] {
  return permissionCodes.filter(code => !catalog.has(code));
}

/** 按模块/页面/操作父子规则更新精确权限集合。 */
export function applyPermissionNodeCheck(
  selected: ReadonlySet<string>,
  node: PermissionTreeNode,
  checked: boolean
): Set<string> {
  const next = new Set(selected);
  if (node.kind === 'module') {
    const codes = collectSubtreePermissionCodes(node);
    if (checked) {
      for (const code of codes) {
        next.add(code);
      }
    } else {
      for (const code of codes) {
        next.delete(code);
      }
    }

    return next;
  }

  if (node.kind === 'action') {
    if (checked) {
      next.add(node.permissionCode);
      if (node.pagePermissionCode) {
        next.add(node.pagePermissionCode);
      }
    } else {
      next.delete(node.permissionCode);
    }

    return next;
  }

  if (checked) {
    next.add(node.permissionCode);
    return next;
  }

  next.delete(node.permissionCode);
  for (const code of collectSubtreePermissionCodes(node)) {
    next.delete(code);
  }

  return next;
}

/** 将精确权限集合映射为树节点勾选键。 */
export function permissionCodesToCheckedNodeIds(
  selected: ReadonlySet<string>,
  nodes: readonly PermissionTreeNode[]
): string[] {
  const checked: string[] = [];
  for (const node of flattenPermissionTreeNodes(nodes)) {
    if (node.kind === 'module') {
      if (isNodeFullySelected(node, selected)) {
        checked.push(node.id);
      }
      continue;
    }

    if (selected.has(node.permissionCode)) {
      checked.push(node.id);
    }
  }

  return checked;
}

/** 将树形节点拍平成深度优先列表，便于统一做勾选状态计算。 */
export function flattenPermissionTreeNodes(
  nodes: readonly PermissionTreeNode[]
): PermissionTreeNode[] {
  const flattened: PermissionTreeNode[] = [];
  for (const node of nodes) {
    flattened.push(node);
    if (node.children?.length) {
      flattened.push(...flattenPermissionTreeNodes(node.children));
    }
  }

  return flattened;
}

/** 将页面节点投影为本地树节点，并把操作与子页面递归挂到同一层 children。 */
function projectPageNode(page: AuthorizationTreePage): PermissionTreeNode {
  const children: PermissionTreeNode[] = [
    ...page.actions.map(action => projectActionNode(page.permissionCode, action)),
    ...page.children.map(child => projectPageNode(child))
  ];

  return {
    id: `page:${page.id}`,
    label: page.title,
    permissionCode: page.permissionCode,
    kind: 'page',
    children: children.length > 0 ? children : undefined
  };
}

/** 为操作节点补齐所属页面权限码，保证勾选子操作时父页面能被自动补齐。 */
function projectActionNode(
  pagePermissionCode: string,
  action: AuthorizationTreeAction
): PermissionTreeNode {
  return {
    id: `action:${action.id}`,
    label: action.name,
    permissionCode: action.permissionCode,
    kind: 'action',
    pagePermissionCode
  };
}

/** 递归收集页面及其全部后代页面、操作的权限码目录。 */
function collectPagePermissionCodes(
  page: AuthorizationTreePage,
  codes: Set<string>
): void {
  codes.add(page.permissionCode);
  for (const action of page.actions) {
    codes.add(action.permissionCode);
  }

  for (const child of page.children) {
    collectPagePermissionCodes(child, codes);
  }
}

/** 收集某个树节点后代的全部权限码；模块节点本身没有独立权限码，因此只看子树。 */
function collectSubtreePermissionCodes(node: PermissionTreeNode): string[] {
  const codes: string[] = [];
  for (const child of node.children ?? []) {
    if (child.permissionCode) {
      codes.push(child.permissionCode);
    }
    codes.push(...collectSubtreePermissionCodes(child));
  }

  return codes;
}

/** 仅当节点子树中的全部权限码都已命中时，才把该节点视为完全选中。 */
function isNodeFullySelected(
  node: PermissionTreeNode,
  selected: ReadonlySet<string>
): boolean {
  const codes = collectSubtreePermissionCodes(node);
  return codes.length > 0 && codes.every(code => selected.has(code));
}
