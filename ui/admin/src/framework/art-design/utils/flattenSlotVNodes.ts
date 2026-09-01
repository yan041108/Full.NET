import { Comment, Fragment, type VNode, type VNodeArrayChildren, type VNodeChild } from 'vue';

/** 将默认插槽 vnode 展平为可操作的动作节点列表（忽略注释与空节点）。 */
export function flattenSlotVNodes(nodes: VNodeChild[]): VNode[] {
  const result: VNode[] = [];

  for (const node of nodes) {
    /** 文本、布尔和空值不可能成为可点击动作节点，直接跳过。 */
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

    /** Fragment 只承载结构，不是最终动作节点，需要把内部 children 继续拍平。 */
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
