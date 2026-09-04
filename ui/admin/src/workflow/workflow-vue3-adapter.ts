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

/** 旧设计器节点类型到服务端稳定节点类型的闭合映射。 */
const sourceToTargetType = new Map<number, string>([
  [0, 'start'],
  [1, 'human.approval'],
  [2, 'notify.cc']
]);
/** 服务端节点类型回投影到旧设计器枚举，保持历史草稿可回显。 */
const targetToSourceType = new Map<string, number>([
  ['start', 0],
  ['human.approval', 1],
  ['notify.cc', 2]
]);
/** 节点键进入服务端后会成为稳定引用标识，因此禁止临时随机串或中文标签。 */
const stableKeyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
/** 抄送人使用服务端 Guid D 格式，禁止空标识和展示名称进入权威配置。 */
const userIdPattern = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
/** 旧设计器里涉及脚本、远程调用或副作用的配置一律视为不可信。 */
const unsafePropertyPattern = /(?:script|function|javascript|remote|url|header|body|webhook|sql|deleteData|modifyData)/iu;
/** 这些字段只参与树结构，不属于节点业务配置。 */
const structuralKeys = new Set(['id', 'type', 'childNode', 'conditionNodes']);
/** 这些字段是旧设计器的瞬时 UI 状态，编译时必须丢弃，避免污染权威 Draft。 */
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
    const closedConfig = readClosedTargetConfig(config, node.nodeTypeKey);
    const recipientUserIds = node.nodeTypeKey === 'notify.cc'
      ? closedConfig.recipientUserIds as string[]
      : undefined;
    return {
      id: node.nodeKey,
      type,
      nodeName: typeof config.nodeName === 'string'
        ? config.nodeName
        : defaultNodeName(node.nodeTypeKey),
      ...closedConfig,
      ...(recipientUserIds === undefined ? {} : {
        nodeUserList: recipientUserIds.map(userId => ({
          id: userId,
          name: userId,
          type: 'user'
        }))
      }),
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

/** 只允许旧设计器节点携带适配层显式支持的闭合配置，其余字段一律拒绝。 */
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
    if (key === 'recipientUserIds' && nodeTypeKey === 'notify.cc') {
      result.recipientUserIds = readRecipientUserIds(value);
      continue;
    }
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return result;
}

/** 服务端回显到旧设计器时，同样只恢复被批准的闭合配置。 */
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
    if (key === 'recipientUserIds' && nodeTypeKey === 'notify.cc') {
      result.recipientUserIds = readRecipientUserIds(value);
      continue;
    }
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return result;
}

/** 验证抄送人闭合集合，并通过复制切断设计器可变数组引用。 */
function readRecipientUserIds(value: unknown): string[] {
  if (!Array.isArray(value)
    || value.length < 1
    || value.length > 20
    || value.some(userId => typeof userId !== 'string' || !userIdPattern.test(userId))) {
    throw new Error('client.invalid_workflow_cc_recipients');
  }
  const normalized = value.map(userId => String(userId).toLowerCase());
  if (new Set(normalized).size !== normalized.length) {
    throw new Error('client.invalid_workflow_cc_recipients');
  }
  return normalized;
}

/** 字段权限策略必须是稳定字段键到有限策略枚举的映射，避免任意值透传。 */
function assertFieldPolicies(value: unknown): asserts value is Record<string, string> {
  if (!isRecord(value) || Object.entries(value).some(([fieldKey, policy]) =>
    !stableKeyPattern.test(fieldKey)
    || !['hidden', 'readOnly', 'editable', 'required'].includes(String(policy)))) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
}

/** 通过 JSON 克隆切断设计器对象引用，防止回显后再编辑时反向污染草稿源对象。 */
function cloneJson<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

/** 旧设计器配置按不可信输入处理，命中危险键或脚本内容时立即失败关闭。 */
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

/** 仅当危险字段真正配置了内容时才拒绝，避免把空壳结构误判为非法。 */
function hasConfiguredValue(value: unknown): boolean {
  if (value === undefined || value === null || value === false || value === '') return false;
  if (Array.isArray(value)) return value.length > 0;
  if (isRecord(value)) return Object.keys(value).length > 0;
  return true;
}

/** 为旧设计器缺失名称的节点补稳定默认文案，保证回显后仍可辨识。 */
function defaultNodeName(nodeTypeKey: string): string {
  if (nodeTypeKey === 'start') return '发起人';
  if (nodeTypeKey === 'human.approval') return '审批人';
  return '抄送人';
}

/** 统一识别普通对象，避免把数组或 null 当成可递归节点。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
