export interface NavigationNode {
  id: string;
  parentId: string | null;
  routeName: string;
  path: string;
  componentKey: string;
  title: string;
  caption: string;
  icon: string;
  order: number;
  requiredPermission: string;
  /** 是否固定在标签栏；工作台等首页通常为 true。 */
  isAffix?: boolean;
  children: NavigationNode[];
}

const stableKeyPattern = /^[a-z][a-z0-9-]{0,63}$/;
const routePathPattern = /^\/(?:[a-z0-9-]+(?:\/[a-z0-9-]+)*)?$/;
const permissionPattern = /^[a-z][a-z0-9_]*(?:[.-][a-z][a-z0-9_]*)+$/;

/**
 * 校验服务端导航投影的完整树结构，不修改或修补任何不可信输入。
 */
export function isNavigationTree(value: unknown): value is NavigationNode[] {
  if (!Array.isArray(value)) {
    return false;
  }

  const ids = new Set<string>();
  return value.every(node => isNavigationNode(node, null, ids));
}

/** 校验组件键是否满足公共语法约束；具体客户端仍须执行本地白名单检查。 */
export function isNavigationComponentKey(value: unknown): value is string {
  return typeof value === 'string' && stableKeyPattern.test(value);
}

function isNavigationNode(
  value: unknown,
  expectedParentId: string | null,
  ids: Set<string>
): value is NavigationNode {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || !stableKeyPattern.test(value.id)
    || ids.has(value.id)
    || value.parentId !== expectedParentId
    || typeof value.routeName !== 'string'
    || !stableKeyPattern.test(value.routeName)
    || typeof value.path !== 'string'
    || !routePathPattern.test(value.path)
    || !isNavigationComponentKey(value.componentKey)
    || !isDisplayText(value.title)
    || typeof value.caption !== 'string'
    || !isDisplayText(value.icon)
    || typeof value.order !== 'number'
    || !Number.isInteger(value.order)
    || typeof value.requiredPermission !== 'string'
    || !permissionPattern.test(value.requiredPermission)
    || (value.isAffix !== undefined && typeof value.isAffix !== 'boolean')
    || !Array.isArray(value.children)) {
    return false;
  }

  const nodeId = value.id;
  ids.add(nodeId);
  return value.children.every(child => isNavigationNode(child, nodeId, ids));
}

function isDisplayText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
