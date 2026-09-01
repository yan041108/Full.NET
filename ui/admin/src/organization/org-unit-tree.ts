import type { OrganizationUnit } from '@fullnet/client-contracts';

/** 机构树节点：在扁平机构上附加子节点，供表格树与上级选择器复用。 */
export interface OrganizationUnitTreeNode extends OrganizationUnit {
  children: OrganizationUnitTreeNode[];
}

/** 上级选择器选项；disabled 表示会形成环或指向自身，禁止选中。 */
export interface OrganizationUnitParentOption {
  value: string;
  label: string;
  disabled: boolean;
}

/** ElTreeSelect 树节点；disabled 用于附属机构中排除已选主机构等场景。 */
export interface OrganizationUnitTreeSelectOption {
  value: string;
  label: string;
  disabled?: boolean;
  children?: OrganizationUnitTreeSelectOption[];
}

/**
 * 将扁平机构列表组装为森林。
 * 缺失或越界的 ParentId 会提升为根，避免悬挂节点与脏数据导致递归死循环。
 */
export function buildOrganizationUnitTree(
  units: readonly OrganizationUnit[]
): OrganizationUnitTreeNode[] {
  const byId = new Map(units.map(unit => [unit.id, unit]));
  const childrenByParent = new Map<string | null, OrganizationUnit[]>();

  for (const unit of units) {
    const parentKey =
      unit.parentId && byId.has(unit.parentId) ? unit.parentId : null;
    const siblings = childrenByParent.get(parentKey) ?? [];
    siblings.push(unit);
    childrenByParent.set(parentKey, siblings);
  }

  const attached = new Set<string>();

  const toNodes = (
    parentId: string | null,
    ancestors: ReadonlySet<string>
  ): OrganizationUnitTreeNode[] =>
    sortUnits(childrenByParent.get(parentId) ?? []).flatMap(unit => {
      // 已见过的节点截断，避免脏环数据无限递归。
      if (ancestors.has(unit.id) || attached.has(unit.id)) {
        return [];
      }

      attached.add(unit.id);
      const nextAncestors = new Set(ancestors);
      nextAncestors.add(unit.id);
      return [{
        ...unit,
        children: toNodes(unit.id, nextAncestors)
      }];
    });

  const roots = toNodes(null, new Set());
  // 环上的节点不会挂在 null 根下；提升为额外根以保证渲染可终止。
  for (const unit of sortUnits(units)) {
    if (attached.has(unit.id)) {
      continue;
    }
    attached.add(unit.id);
    roots.push({
      ...unit,
      children: toNodes(unit.id, new Set([unit.id]))
    });
  }

  return roots;
}

/** 将机构森林映射为 ElTreeSelect 数据。 */
export function mapOrganizationUnitTreeToSelectOptions(
  nodes: readonly OrganizationUnitTreeNode[]
): OrganizationUnitTreeSelectOption[] {
  return nodes.map(node => ({
    value: node.id,
    label: `${node.name} (${node.code})`,
    children: node.children.length > 0
      ? mapOrganizationUnitTreeToSelectOptions(node.children)
      : undefined
  }));
}

/** 在已有树选项上叠加 disabledIds，供附属机构排除主机构等场景复用。 */
export function applyDisabledToOrganizationUnitTreeSelectOptions(
  nodes: readonly OrganizationUnitTreeSelectOption[],
  disabledIds: ReadonlySet<string>
): OrganizationUnitTreeSelectOption[] {
  return nodes.map(node => ({
    ...node,
    disabled: node.disabled || disabledIds.has(node.value),
    children: node.children
      ? applyDisabledToOrganizationUnitTreeSelectOptions(node.children, disabledIds)
      : undefined
  }));
}

/** 收集某机构全部后代 Id（不含自身），用于编辑时禁用不可选上级。 */
export function collectDescendantIds(
  units: readonly OrganizationUnit[],
  rootId: string
): Set<string> {
  const childrenByParent = new Map<string, string[]>();
  for (const unit of units) {
    if (!unit.parentId) {
      continue;
    }
    const siblings = childrenByParent.get(unit.parentId) ?? [];
    siblings.push(unit.id);
    childrenByParent.set(unit.parentId, siblings);
  }

  const descendants = new Set<string>();
  const stack = [...(childrenByParent.get(rootId) ?? [])];
  while (stack.length > 0) {
    const current = stack.pop()!;
    if (descendants.has(current)) {
      continue;
    }
    descendants.add(current);
    const children = childrenByParent.get(current);
    if (children) {
      stack.push(...children);
    }
  }

  return descendants;
}

/**
 * 判断把 unitId 的上级改为 parentId 是否会形成环（含自指）。
 * parentId 为 null 表示挂到根，永不构成环。
 */
export function wouldCreateOrganizationUnitCycle(
  units: readonly OrganizationUnit[],
  unitId: string,
  parentId: string | null
): boolean {
  if (!parentId) {
    return false;
  }
  if (parentId === unitId) {
    return true;
  }

  const parentById = new Map(units.map(unit => [unit.id, unit.parentId]));
  let current: string | null = parentId;
  const seen = new Set<string>();
  while (current) {
    if (current === unitId) {
      return true;
    }
    if (seen.has(current)) {
      return true;
    }
    seen.add(current);
    current = parentById.get(current) ?? null;
  }

  return false;
}

/** 生成带缩进的上级选项；编辑时禁用自身与全部后代。 */
export function buildOrganizationUnitParentOptions(
  units: readonly OrganizationUnit[],
  editingUnitId: string | null
): OrganizationUnitParentOption[] {
  const blocked = editingUnitId
    ? new Set([
        editingUnitId,
        ...collectDescendantIds(units, editingUnitId)
      ])
    : new Set<string>();

  const options: OrganizationUnitParentOption[] = [];
  const walk = (nodes: OrganizationUnitTreeNode[], depth: number): void => {
    for (const node of nodes) {
      const dash = String.fromCharCode(0x2014);
      const indent = depth > 0 ? dash.repeat(depth) + ' ' : '';
      options.push({
        value: node.id,
        label: indent + node.name + ' (' + node.code + ')',
        disabled: blocked.has(node.id) || !node.isActive
      });
      walk(node.children, depth + 1);
    }
  };

  walk(buildOrganizationUnitTree(units), 0);
  return options;
}

/** 过滤时保留命中节点及其祖先，使树路径仍可展示。 */
export function filterOrganizationUnitsForTree(
  units: readonly OrganizationUnit[],
  matches: (unit: OrganizationUnit) => boolean
): OrganizationUnit[] {
  const byId = new Map(units.map(unit => [unit.id, unit]));
  const keep = new Set<string>();

  for (const unit of units) {
    if (!matches(unit)) {
      continue;
    }
    let current: OrganizationUnit | undefined = unit;
    while (current) {
      if (keep.has(current.id)) {
        break;
      }
      keep.add(current.id);
      current = current.parentId ? byId.get(current.parentId) : undefined;
    }
  }

  return units.filter(unit => keep.has(unit.id));
}

/** 按显示顺序再按机构编码稳定排序，保证树与下拉的遍历结果可预测。 */
function sortUnits(units: readonly OrganizationUnit[]): OrganizationUnit[] {
  return [...units].sort((left, right) => {
    if (left.displayOrder !== right.displayOrder) {
      return left.displayOrder - right.displayOrder;
    }
    return left.code.localeCompare(right.code);
  });
}
