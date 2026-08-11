import { Comment, Fragment, type VNode, type VNodeArrayChildren, type VNodeChild } from 'vue';

/** 将默认插槽 vnode 展平为可操作的动作节点列表（忽略注释与空节点）。 */
export function flattenSlotVNodes(nodes: VNodeChild[]): VNode[] {
  const result: VNode[] = [];

  for (const node of nodes) {
    if (
      node === null
      || node === undefined
      || typeof node === 'boolean'
      || typeof node === 'string'
      || typeof node === 'number'
    ) {
      continue;
    }

    if (Array.isArray(node)) {
      result.push(...flattenSlotVNodes(node as VNodeArrayChildren));
      continue;
    }

    const vnode = node as VNode;
    if (vnode.type === Comment) {
      continue;
    }

    if (vnode.type === Fragment) {
      const children = vnode.children;
      if (Array.isArray(children)) {
        result.push(...flattenSlotVNodes(children as VNodeChild[]));
      }
      continue;
    }

    result.push(vnode);
  }

  return result;
}
