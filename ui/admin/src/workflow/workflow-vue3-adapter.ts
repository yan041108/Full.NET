import type {
  WorkflowDefinitionDraft,
  WorkflowNodeDraft
} from '@fullnet/client-contracts';

export interface WorkflowVue3Node {
  readonly id?: string;
  readonly type?: number;
  readonly nodeName?: string;
  childNode?: WorkflowVue3Node | null;
  readonly conditionNodes?: WorkflowVue3Node[];
  readonly [key: string]: unknown;
}

const sourceToTargetType = new Map<number, string>([
  [0, 'start'],
  [1, 'human.approval'],
  [2, 'notify.cc']
]);
const targetToSourceType = new Map<string, number>([
  ['start', 0],
  ['human.approval', 1],
  ['notify.cc', 2]
]);
const stableKeyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
const unsafePropertyPattern = /(?:script|function|javascript|remote|url|header|body|webhook|sql|deleteData|modifyData)/iu;
const structuralKeys = new Set(['id', 'type', 'childNode', 'conditionNodes']);
const transientDesignerKeys = new Set([
  'error',
  'errorTip',
  'settype',
  'examineMode',
  'nodeUserList',
  'placeHolder'
]);

/** 把 Workflow-Vue3 的编辑树转换为服务端可编译的稳定节点列表。 */
export function fromWorkflowVue3Tree(root: unknown): WorkflowDefinitionDraft {
  if (!isRecord(root)) throw new Error('client.invalid_workflow_definition_draft');
  const nodes: WorkflowNodeDraft[] = [];
  const keys = new Set<string>();
  let current: WorkflowVue3Node | null = root as WorkflowVue3Node;
  while (current !== null) {
    const nodeKey = readStableKey(current.id);
    if (keys.has(nodeKey)) throw new Error('client.invalid_workflow_definition_draft');
    keys.add(nodeKey);
    const nodeTypeKey = sourceToTargetType.get(Number(current.type));
    if (nodeTypeKey === undefined) throw new Error('client.unsupported_workflow_node');
    assertSafeConfiguration(current);
    if (Array.isArray(current.conditionNodes) && current.conditionNodes.length > 0) {
      throw new Error('client.unsupported_workflow_node');
    }

    const next: WorkflowVue3Node | null = isRecord(current.childNode)
      ? current.childNode as WorkflowVue3Node
      : null;
    const nextNodeKey = next === null ? 'end' : readStableKey(next.id);
    nodes.push({
      nodeKey,
      nodeTypeKey,
      nodeSchemaVersion: 1,
      config: {
        ...readClosedNodeConfig(current, nodeTypeKey),
        nextNodeKeys: [nextNodeKey]
      }
    });
    current = next;
  }

  if (nodes[0]?.nodeTypeKey !== 'start') {
    throw new Error('client.invalid_workflow_definition_draft');
  }
  if (keys.has('end')) throw new Error('client.invalid_workflow_definition_draft');
  nodes.push({ nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } });
  return { schemaVersion: 1, nodes };
}

/** 把当前服务端线性 Draft 恢复为 Workflow-Vue3 可编辑树。 */
export function toWorkflowVue3Tree(draft: WorkflowDefinitionDraft): WorkflowVue3Node {
  const byKey = new Map(draft.nodes.map(node => [node.nodeKey, node]));
  const start = draft.nodes.find(node => node.nodeTypeKey === 'start');
  if (start === undefined) throw new Error('client.invalid_workflow_definition_draft');
  const visited = new Set<string>();

  const build = (node: WorkflowNodeDraft): WorkflowVue3Node | null => {
    if (node.nodeTypeKey === 'end') return null;
    if (visited.has(node.nodeKey)) throw new Error('client.invalid_workflow_definition_draft');
    visited.add(node.nodeKey);
    const type = targetToSourceType.get(node.nodeTypeKey);
    if (type === undefined) throw new Error('client.unsupported_workflow_node');
    const config = isRecord(node.config) ? node.config : {};
    assertSafeConfiguration(config);
    const nextKeys = Array.isArray(config.nextNodeKeys)
      ? config.nextNodeKeys.filter((value): value is string => typeof value === 'string')
      : [];
    if (nextKeys.length !== 1) throw new Error('client.unsupported_workflow_node');
    const next = byKey.get(nextKeys[0]!);
    if (next === undefined) throw new Error('client.invalid_workflow_definition_draft');
    return {
      id: node.nodeKey,
      type,
      nodeName: typeof config.nodeName === 'string'
        ? config.nodeName
        : defaultNodeName(node.nodeTypeKey),
      ...readClosedTargetConfig(config, node.nodeTypeKey),
      childNode: build(next)
    };
  };

  return build(start) ?? (() => { throw new Error('client.invalid_workflow_definition_draft'); })();
}

function readStableKey(value: unknown): string {
  const key = typeof value === 'string' ? value.trim() : '';
  if (!stableKeyPattern.test(key)) throw new Error('client.invalid_workflow_definition_draft');
  return key;
}

function readClosedNodeConfig(
  node: WorkflowVue3Node,
  nodeTypeKey: string
): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(node)) {
    if (structuralKeys.has(key) || transientDesignerKeys.has(key)) continue;
    if (key === 'nodeName') {
      if (typeof value !== 'string') throw new Error('client.invalid_workflow_definition_draft');
      result.nodeName = value;
      continue;
    }
    if (key === 'fieldPolicies' && nodeTypeKey === 'human.approval') {
      assertFieldPolicies(value);
      result.fieldPolicies = cloneJson(value);
      continue;
    }
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return result;
}

function readClosedTargetConfig(
  config: Record<string, unknown>,
  nodeTypeKey: string
): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(config)) {
    if (key === 'nextNodeKeys' || key === 'nodeName') continue;
    if (key === 'fieldPolicies' && nodeTypeKey === 'human.approval') {
      assertFieldPolicies(value);
      result.fieldPolicies = cloneJson(value);
      continue;
    }
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return result;
}

function assertFieldPolicies(value: unknown): asserts value is Record<string, string> {
  if (!isRecord(value) || Object.entries(value).some(([fieldKey, policy]) =>
    !stableKeyPattern.test(fieldKey)
    || !['hidden', 'readOnly', 'editable', 'required'].includes(String(policy)))) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
}

function cloneJson<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function assertSafeConfiguration(value: unknown): void {
  if (Array.isArray(value)) {
    value.forEach(assertSafeConfiguration);
    return;
  }
  if (!isRecord(value)) return;
  for (const [key, child] of Object.entries(value)) {
    if (unsafePropertyPattern.test(key) && hasConfiguredValue(child)) {
      throw new Error('client.unsafe_workflow_configuration');
    }
    if (typeof child === 'string' && /(?:javascript:|https?:\/\/|<script|<iframe)/iu.test(child)) {
      throw new Error('client.unsafe_workflow_configuration');
    }
    if (key !== 'childNode') assertSafeConfiguration(child);
  }
}

function hasConfiguredValue(value: unknown): boolean {
  if (value === undefined || value === null || value === false || value === '') return false;
  if (Array.isArray(value)) return value.length > 0;
  if (isRecord(value)) return Object.keys(value).length > 0;
  return true;
}

function defaultNodeName(nodeTypeKey: string): string {
  if (nodeTypeKey === 'start') return '发起人';
  if (nodeTypeKey === 'human.approval') return '审批人';
  return '抄送人';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
