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
  [2, 'notify.cc'],
  [10, 'gateway.exclusive']
]);
/** 服务端节点类型回投影到旧设计器枚举，保持历史草稿可回显。 */
const targetToSourceType = new Map<string, number>([
  ['start', 0],
  ['human.approval', 1],
  ['notify.cc', 2],
  ['gateway.exclusive', 10]
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

  const compile = (current: WorkflowVue3Node, continuationKey: string): void => {
    const nodeKey = readStableKey(current.id);
    if (keys.has(nodeKey)) throw new Error('client.invalid_workflow_definition_draft');
    keys.add(nodeKey);
    const nodeTypeKey = sourceToTargetType.get(Number(current.type));
    if (nodeTypeKey === undefined) throw new Error('client.unsupported_workflow_node');
    assertSafeConfiguration(current);

    if (nodeTypeKey === 'gateway.exclusive') {
      const conditionNodes = current.conditionNodes;
      if (!Array.isArray(conditionNodes) || conditionNodes.length < 2 || conditionNodes.length > 16) {
        throw new Error('client.invalid_workflow_gateway');
      }
      const defaults = conditionNodes.filter(condition => condition.isDefault === true);
      if (defaults.length !== 1 || conditionNodes.at(-1) !== defaults[0]) {
        throw new Error('client.invalid_workflow_gateway');
      }
      const targets = conditionNodes.map(condition => {
        if (!isRecord(condition.childNode)) throw new Error('client.invalid_workflow_gateway');
        return readStableKey(condition.childNode.id);
      });
      if (new Set(targets).size !== targets.length) throw new Error('client.invalid_workflow_gateway');
      const branches = conditionNodes.slice(0, -1).map((condition, index) => ({
        branchKey: readStableKey(condition.branchKey ?? `branch-${index + 1}`),
        nextNodeKey: targets[index]!,
        condition: readDesignerGatewayCondition(condition)
      }));
      nodes.push({
        nodeKey,
        nodeTypeKey,
        nodeSchemaVersion: 1,
        config: {
          ...(typeof current.nodeName === 'string' ? { nodeName: current.nodeName } : {}),
          nextNodeKeys: targets,
          branches,
          defaultNextNodeKey: targets.at(-1)!
        }
      });
      conditionNodes.forEach(condition => compile(condition.childNode as WorkflowVue3Node, continuationKey));
      if (isRecord(current.childNode)) compile(current.childNode as WorkflowVue3Node, continuationKey);
      return;
    }

    const next: WorkflowVue3Node | null = isRecord(current.childNode)
      ? current.childNode as WorkflowVue3Node
      : null;
    const nextNodeKey = next === null ? continuationKey : readStableKey(next.id);
    nodes.push({
      nodeKey,
      nodeTypeKey,
      nodeSchemaVersion: 1,
      config: {
        ...readClosedNodeConfig(current, nodeTypeKey),
        nextNodeKeys: [nextNodeKey]
      }
    });
    if (next !== null) compile(next, continuationKey);
  };

  compile(root as WorkflowVue3Node, 'end');

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

  const build = (node: WorkflowNodeDraft, stopKey = 'end'): WorkflowVue3Node | null => {
    if (node.nodeKey === stopKey || node.nodeTypeKey === 'end') return null;
    if (visited.has(node.nodeKey)) throw new Error('client.invalid_workflow_definition_draft');
    visited.add(node.nodeKey);
    const type = targetToSourceType.get(node.nodeTypeKey);
    if (type === undefined) throw new Error('client.unsupported_workflow_node');
    const config = isRecord(node.config) ? node.config : {};
    assertSafeConfiguration(config);
    if (node.nodeTypeKey === 'gateway.exclusive') {
      const gateway = readTargetGatewayConfig(config);
      const mergeKey = findGatewayMergeKey(gateway.nextNodeKeys, byKey);
      const conditionNodes: WorkflowVue3Node[] = gateway.branches.map((branch, index) => ({
        id: `${node.nodeKey}-${branch.branchKey}`,
        type: 3,
        nodeName: branch.branchKey,
        branchKey: branch.branchKey,
        fieldKey: branch.condition.fieldKey,
        operator: branch.condition.operator,
        ...(branch.condition.value === undefined ? {} : { value: cloneJson(branch.condition.value) }),
        priorityLevel: index + 1,
        childNode: build(byKey.get(branch.nextNodeKey)!, mergeKey)
      }));
      conditionNodes.push({
        id: `${node.nodeKey}-default`,
        type: 3,
        nodeName: '其他条件',
        isDefault: true,
        priorityLevel: conditionNodes.length + 1,
        childNode: build(byKey.get(gateway.defaultNextNodeKey)!, mergeKey)
      });
      const merge = byKey.get(mergeKey);
      if (merge === undefined) throw new Error('client.invalid_workflow_definition_draft');
      return {
        id: node.nodeKey,
        type,
        nodeName: typeof config.nodeName === 'string' ? config.nodeName : '条件分支',
        conditionNodes,
        childNode: build(merge, stopKey)
      };
    }
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
      childNode: build(next, stopKey)
    };
  };

  return build(start) ?? (() => { throw new Error('client.invalid_workflow_definition_draft'); })();
}

interface GatewayConditionConfig {
  readonly fieldKey: string;
  readonly operator: string;
  readonly value?: unknown;
}

interface GatewayBranchConfig {
  readonly branchKey: string;
  readonly nextNodeKey: string;
  readonly condition: GatewayConditionConfig;
}

interface GatewayTargetConfig {
  readonly nextNodeKeys: string[];
  readonly branches: GatewayBranchConfig[];
  readonly defaultNextNodeKey: string;
}

/** 从条件分支卡片读取闭合字段比较，不接受组合表达式或扩展执行参数。 */
function readDesignerGatewayCondition(node: WorkflowVue3Node): GatewayConditionConfig {
  const fieldKey = readStableKey(node.fieldKey);
  const operator = typeof node.operator === 'string' ? node.operator : '';
  const empty = ['isEmpty', 'isNotEmpty'].includes(operator);
  if (!['equals', 'notEquals', 'greaterThan', 'greaterThanOrEqual', 'lessThan',
    'lessThanOrEqual', 'isEmpty', 'isNotEmpty'].includes(operator)
    || (empty ? node.value !== undefined : node.value === undefined)) {
    throw new Error('client.invalid_workflow_gateway');
  }
  return { fieldKey, operator, ...(empty ? {} : { value: cloneJson(node.value) }) };
}

/** 从服务端 Draft 读取排他网关闭合配置，并核对有序出口集合。 */
function readTargetGatewayConfig(config: Record<string, unknown>): GatewayTargetConfig {
  const nextNodeKeys = Array.isArray(config.nextNodeKeys)
    ? config.nextNodeKeys.map(readStableKey)
    : [];
  if (!Array.isArray(config.branches) || config.branches.length < 1 || config.branches.length > 15) {
    throw new Error('client.invalid_workflow_gateway');
  }
  const branches = config.branches.map(value => {
    if (!isRecord(value) || !isRecord(value.condition)) {
      throw new Error('client.invalid_workflow_gateway');
    }
    return {
      branchKey: readStableKey(value.branchKey),
      nextNodeKey: readStableKey(value.nextNodeKey),
      condition: readDesignerGatewayCondition(value.condition as WorkflowVue3Node)
    };
  });
  const defaultNextNodeKey = readStableKey(config.defaultNextNodeKey);
  const expected = [...branches.map(branch => branch.nextNodeKey), defaultNextNodeKey];
  if (new Set(expected).size !== expected.length
    || JSON.stringify(nextNodeKeys) !== JSON.stringify(expected)) {
    throw new Error('client.invalid_workflow_gateway');
  }
  const allowed = new Set(['nodeName', 'nextNodeKeys', 'branches', 'defaultNextNodeKey']);
  if (Object.keys(config).some(key => !allowed.has(key))) {
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return { nextNodeKeys, branches, defaultNextNodeKey };
}

/** 选择所有分支首次汇合的最近节点；显式 end 也是合法汇合点。 */
function findGatewayMergeKey(
  targetKeys: string[],
  byKey: ReadonlyMap<string, WorkflowNodeDraft>
): string {
  const distances = targetKeys.map(target => collectDistances(target, byKey));
  const common = [...distances[0]!.keys()].filter(key => distances.every(items => items.has(key)));
  common.sort((left, right) =>
    Math.max(...distances.map(items => items.get(left)!))
    - Math.max(...distances.map(items => items.get(right)!)));
  if (common[0] === undefined) throw new Error('client.invalid_workflow_definition_draft');
  return common[0];
}

/** 收集节点到所有后继的最短距离，供分支树恢复共同汇合点。 */
function collectDistances(
  startKey: string,
  byKey: ReadonlyMap<string, WorkflowNodeDraft>
): Map<string, number> {
  const result = new Map<string, number>();
  const pending: Array<[string, number]> = [[startKey, 0]];
  while (pending.length > 0) {
    const [key, distance] = pending.shift()!;
    if (result.has(key)) continue;
    result.set(key, distance);
    const node = byKey.get(key);
    if (node === undefined || !isRecord(node.config)) continue;
    const next = Array.isArray(node.config.nextNodeKeys)
      ? node.config.nextNodeKeys.filter((value): value is string => typeof value === 'string')
      : [];
    next.forEach(value => pending.push([value, distance + 1]));
  }
  return result;
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
    if (key === 'timeoutPolicy' && nodeTypeKey === 'human.approval') {
      assertTimeoutPolicy(value);
      result.timeoutPolicy = cloneJson(value);
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
    if (key === 'timeoutPolicy' && nodeTypeKey === 'human.approval') {
      assertTimeoutPolicy(value);
      result.timeoutPolicy = cloneJson(value);
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

/** 校验审批节点的闭合超时策略，客户端仅做结构前置检查，服务端仍是权威边界。 */
function assertTimeoutPolicy(value: unknown): asserts value is Record<string, unknown> {
  if (!isRecord(value)) throw new Error('client.invalid_workflow_timeout_policy');
  const allowed = new Set([
    'dueAfterMinutes', 'reminderIntervalMinutes', 'maxReminderCount',
    'escalationAfterMinutes', 'escalationRecipientUserId'
  ]);
  const due = value.dueAfterMinutes;
  const interval = value.reminderIntervalMinutes;
  const count = value.maxReminderCount;
  const escalation = value.escalationAfterMinutes;
  const recipient = value.escalationRecipientUserId;
  const hasEscalation = escalation !== undefined || recipient !== undefined;
  if (Object.keys(value).some(key => !allowed.has(key))
    || !Number.isInteger(due) || Number(due) < 1 || Number(due) > 525_600
    || !Number.isInteger(interval) || Number(interval) < 1 || Number(interval) > 43_200
    || !Number.isInteger(count) || Number(count) < 0 || Number(count) > 100
    || (Number(count) === 0 && !hasEscalation)
    || (hasEscalation && (!Number.isInteger(escalation)
      || Number(escalation) < Number(due) || Number(escalation) > 525_600
      || typeof recipient !== 'string' || !userIdPattern.test(recipient)))) {
    throw new Error('client.invalid_workflow_timeout_policy');
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
  if (nodeTypeKey === 'gateway.exclusive') return '条件分支';
  return '抄送人';
}

/** 统一识别普通对象，避免把数组或 null 当成可递归节点。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
