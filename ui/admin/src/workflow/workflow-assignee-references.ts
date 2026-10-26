import type { WorkflowDefinitionDraft, WorkflowFormField } from '@fullnet/client-contracts';
const human = new Set(['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim']);
const uuid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);

/** 人员和部门引用只能选择已发布形状中的稳定标识选项，不把普通文本提升为身份。 */
export function workflowAssigneeReferenceFields(fields: readonly WorkflowFormField[]): readonly WorkflowFormField[] {
  return fields.filter(field => ['select', 'checkbox'].includes(field.fieldTypeKey) && object(field.constraints) && Array.isArray(field.constraints.options) &&
    field.constraints.options.length > 0 && field.constraints.options.every(value => typeof value === 'string' && uuid.test(value)) &&
    new Set(field.constraints.options.map(value => String(value).toLowerCase())).size === field.constraints.options.length);
}

/** 前序候选须支配目标；条件分支未选中也能到达目标时不能引用该分支的处理人。 */
export function workflowPreviousActorNodes(draft: WorkflowDefinitionDraft, target: string): readonly { key: string; label: string }[] {
  const start = draft.nodes.find(node => node.nodeTypeKey === 'start')?.nodeKey;
  if (!start) return [];
  const edges = new Map(draft.nodes.map(node => {
    const config = object(node.config) ? node.config : {};
    const next = Array.isArray(config.nextNodeKeys) ? config.nextNodeKeys.filter((key): key is string => typeof key === 'string') : [];
    if (Array.isArray(config.branches)) for (const branch of config.branches) if (object(branch) && typeof branch.nextNodeKey === 'string') next.push(branch.nextNodeKey);
    if (typeof config.defaultNextNodeKey === 'string') next.push(config.defaultNextNodeKey);
    return [node.nodeKey, next] as const;
  }));
  function reaches(excluded?: string): boolean {
    const seen = new Set<string>(); const pending = [start!];
    while (pending.length) {
      const key = pending.pop()!;
      if (key === excluded || seen.has(key)) continue;
      if (key === target) return true;
      seen.add(key); pending.push(...edges.get(key) ?? []);
    }
    return false;
  }
  if (!reaches()) return [];
  return draft.nodes.filter(node => human.has(node.nodeTypeKey) && node.nodeKey !== target && !reaches(node.nodeKey))
    .map(node => ({ key: node.nodeKey, label: object(node.config) && typeof node.config.nodeName === 'string' ? node.config.nodeName : node.nodeKey }));
}
