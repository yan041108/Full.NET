import type {
  WorkflowDefinitionDraft,
  WorkflowNodeDraft
} from '@fullnet/client-contracts';
import { readWorkflowOperationPermissions } from './workflow-human-actions';
import { readVotePolicy } from './workflow-vote-policy';
import { readWorkflowDelayPolicy } from './workflow-delay-policy';
import { readWorkflowFieldMutation } from './workflow-field-mutation';
import { readWorkflowConnector } from './workflow-connector';
import { readWorkflowBusinessCommand } from './workflow-business-command';
import { readWorkflowSubprocess } from './workflow-subprocess';
import { readWorkflowSlaPolicy } from './workflow-sla-policy';
import { readWorkflowResultCallback } from './workflow-result-callback';
import { readWorkflowActivityListeners } from './workflow-activity-listeners';
import { workflowGatewayLimits } from './workflow-gateway-limits';
import { readWorkflowCondition, projectWorkflowCondition, type WorkflowConditionConfig } from './workflow-conditions';

export interface WorkflowVue3Node {
  readonly id?: string;
  readonly type?: number;
  readonly nodeName?: string;
  childNode?: WorkflowVue3Node | null;
  readonly conditionNodes?: WorkflowVue3Node[];
  readonly [key: string]: unknown;
}

/** Workflow-Vue3 并行分支节点类型。 */
const PARALLEL_ROUTE_TYPE = 12;
/** Workflow-Vue3 包容分支节点类型。 */
const INCLUSIVE_ROUTE_TYPE = 13;
/** 旧设计器节点类型到服务端稳定节点类型的闭合映射。 */
const sourceToTargetType = new Map<number, string>([
  [0, 'start'],
  [1, 'human.approval'],
  [21, 'human.group_approval'],
  [18, 'human.task'],
  [19, 'human.vote'],
  [17, 'human.claim'],
  [2, 'notify.cc'],
  [22, 'control.empty'],
  [23, 'business.command'],
  [9, 'process.subprocess'],
  [7, 'timer.delay'],
  [15, 'data.modify'],
  [16, 'data.clear'],
  [6, 'integration.trigger'],
  [11, 'integration.async_trigger'],
  [10, 'gateway.exclusive']
]);
/** 服务端节点类型回投影到旧设计器枚举，保持历史草稿可回显。 */
const targetToSourceType = new Map<string, number>([
  ['start', 0],
  ['human.approval', 1],
  ['human.group_approval', 21],
  ['human.task', 18],
  ['human.vote', 19],
  ['human.claim', 17],
  ['notify.cc', 2],
  ['control.empty', 22],
  ['business.command', 23],
  ['process.subprocess', 9],
  ['timer.delay', 7],
  ['data.modify', 15],
  ['data.clear', 16],
  ['integration.trigger', 6],
  ['integration.async_trigger', 11],
  ['gateway.exclusive', 10]
]);
/** 节点键进入服务端后会成为稳定引用标识，因此禁止临时随机串或中文标签。 */
const stableKeyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
/** 抄送人使用服务端 Guid D 格式，禁止空标识和展示名称进入权威配置。 */
const userIdPattern = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
/** 旧设计器里涉及脚本、远程调用或副作用的配置一律视为不可信。 */
const unsafePropertyPattern = /(?:script|function|javascript|remote|url|header|body|webhook|sql|deleteData|modifyData)/iu;
/** 这些字段只参与树结构，不属于节点业务配置。 */
const structuralKeys = new Set(['id', 'type', 'childNode', 'conditionNodes', 'awaitActivityNodeKeys', 'boundaryAwaitActivityNodeKeys']);

/** 依赖引用属于图结构；禁止将无效引用静默丢弃或混入业务配置。 */
export function readWorkflowAwaitDependencies(value: unknown): string[] | undefined {
  if (value === undefined || value === null) return undefined;
  if (!Array.isArray(value) || value.length > 16 || value.some(key => typeof key !== 'string' || !stableKeyPattern.test(key)) || new Set(value).size !== value.length) {
    throw new Error('client.invalid_workflow_async_dependency');
  }
  return value.length === 0 ? undefined : [...value] as string[];
}
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
  const dependencies = new Map<string, string[]>();

  const reserveKey = (value: unknown): string => {
    const key = readStableKey(value);
    if (keys.has(key)) throw new Error('client.invalid_workflow_definition_draft');
    keys.add(key);
    return key;
  };

  const compile = (current: WorkflowVue3Node, continuationKey: string): void => {
    const nodeKey = reserveKey(current.id);
    if (current !== root && current.boundaryAwaitActivityNodeKeys !== undefined) throw new Error('client.invalid_workflow_async_dependency');
    const awaited = readWorkflowAwaitDependencies(current.awaitActivityNodeKeys);
    if (awaited) dependencies.set(nodeKey, awaited);
    for (const condition of current.conditionNodes ?? []) {
      if (readWorkflowAwaitDependencies(condition.awaitActivityNodeKeys)) throw new Error('client.invalid_workflow_async_dependency');
    }

    if (Number(current.type) === PARALLEL_ROUTE_TYPE) {
      assertSafeConfiguration(current);
      const conditionNodes = current.conditionNodes;
      const limits = workflowGatewayLimits['gateway.parallel'];
      if (!Array.isArray(conditionNodes) || conditionNodes.length < limits.minimumBranches || conditionNodes.length > limits.maximumBranches) {
        throw new Error('client.invalid_workflow_gateway');
      }
      const branches = conditionNodes.map((condition, index) => {
        if (!isRecord(condition.childNode)) throw new Error('client.invalid_workflow_gateway');
        return {
          branchKey: readStableKey(condition.branchKey ?? `branch-${index + 1}`),
          nextNodeKey: readStableKey(condition.childNode.id)
        };
      });
      if (new Set(branches.map(branch => branch.nextNodeKey)).size !== branches.length) {
        throw new Error('client.invalid_workflow_gateway');
      }
      // 内部汇合与可见节点共享同一个键空间，不能覆盖导入稿中的同名节点。
      const joinNodeKey = reserveKey(`${nodeKey}-join`);
      nodes.push({
        nodeKey,
        nodeTypeKey: 'gateway.parallel',
        nodeSchemaVersion: 1,
        config: {
          ...(typeof current.nodeName === 'string' ? { nodeName: current.nodeName } : {}),
          gatewayRoleKey: 'fork',
          joinNodeKey,
          branches
        }
      });
      conditionNodes.forEach(condition => compile(condition.childNode as WorkflowVue3Node, joinNodeKey));
      const merge = isRecord(current.childNode) ? current.childNode as WorkflowVue3Node : null;
      const nextAfterJoin = merge === null ? continuationKey : readStableKey(merge.id);
      nodes.push({
        nodeKey: joinNodeKey,
        nodeTypeKey: 'gateway.parallel',
        nodeSchemaVersion: 1,
        config: {
          gatewayRoleKey: 'join',
          forkNodeKey: nodeKey,
          nextNodeKeys: [nextAfterJoin]
        }
      });
      if (merge !== null) compile(merge, continuationKey);
      return;
    }

    if (Number(current.type) === INCLUSIVE_ROUTE_TYPE) {
      assertSafeConfiguration(current);
      const conditionNodes = current.conditionNodes;
      const limits = workflowGatewayLimits['gateway.inclusive'];
      if (!Array.isArray(conditionNodes) || conditionNodes.length < limits.minimumBranches + 1 || conditionNodes.length > limits.maximumBranches + 1) {
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
      const joinNodeKey = reserveKey(`${nodeKey}-join`);
      nodes.push({
        nodeKey,
        nodeTypeKey: 'gateway.inclusive',
        nodeSchemaVersion: 1,
        config: {
          ...(typeof current.nodeName === 'string' ? { nodeName: current.nodeName } : {}),
          gatewayRoleKey: 'fork',
          joinNodeKey,
          branches,
          defaultNextNodeKey: targets.at(-1)!
        }
      });
      conditionNodes.forEach(condition => compile(condition.childNode as WorkflowVue3Node, joinNodeKey));
      const merge = isRecord(current.childNode) ? current.childNode as WorkflowVue3Node : null;
      const nextAfterJoin = merge === null ? continuationKey : readStableKey(merge.id);
      nodes.push({
        nodeKey: joinNodeKey,
        nodeTypeKey: 'gateway.inclusive',
        nodeSchemaVersion: 1,
        config: {
          gatewayRoleKey: 'join',
          forkNodeKey: nodeKey,
          nextNodeKeys: [nextAfterJoin]
        }
      });
      if (merge !== null) compile(merge, continuationKey);
      return;
    }

    const nodeTypeKey = sourceToTargetType.get(Number(current.type));
    if (nodeTypeKey === undefined) throw new Error('client.unsupported_workflow_node');
    assertSafeConfiguration(current);

    if (nodeTypeKey === 'gateway.exclusive') {
      const conditionNodes = current.conditionNodes;
      const limits = workflowGatewayLimits['gateway.exclusive'];
      if (!Array.isArray(conditionNodes) || conditionNodes.length < limits.minimumBranches + 1 || conditionNodes.length > limits.maximumBranches + 1) {
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
      // 各分支先接入本层公共后继；只有公共后继末端才能继续外层路径。
      const merge = isRecord(current.childNode) ? current.childNode as WorkflowVue3Node : null;
      const branchContinuation = merge === null ? continuationKey : readStableKey(merge.id);
      conditionNodes.forEach(condition => compile(condition.childNode as WorkflowVue3Node, branchContinuation));
      if (merge !== null) compile(merge, continuationKey);
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
  if (root.boundaryAwaitActivityNodeKeys !== undefined) {
    if (!isRecord(root.boundaryAwaitActivityNodeKeys)) throw new Error('client.invalid_workflow_async_dependency');
    for (const [key, value] of Object.entries(root.boundaryAwaitActivityNodeKeys)) {
      const boundary = nodes.find(node => node.nodeKey === key);
      if (!boundary || !(boundary.nodeTypeKey === 'end' || (['gateway.parallel', 'gateway.inclusive'].includes(boundary.nodeTypeKey) && isRecord(boundary.config) && boundary.config.gatewayRoleKey === 'join'))) {
        throw new Error('client.invalid_workflow_async_dependency');
      }
      const awaited = readWorkflowAwaitDependencies(value);
      if (awaited) dependencies.set(key, awaited);
    }
  }
  const kind = root.workflowKindKey ?? 'approval';
  if (kind !== 'approval' && kind !== 'automation') throw new Error('client.invalid_workflow_definition_draft');
  return { schemaVersion: 1, nodes: nodes.map(node => dependencies.has(node.nodeKey)
    ? { ...node, awaitActivityNodeKeys: dependencies.get(node.nodeKey) } : node), ...(kind === 'automation' ? { workflowKindKey: kind } : {}) };
}

/** 把当前服务端结构化 Draft 恢复为 Workflow-Vue3 可编辑树，不降级未知版本。 */
export function toWorkflowVue3Tree(draft: WorkflowDefinitionDraft): WorkflowVue3Node {
  if (draft.schemaVersion !== 1 || draft.nodes.some(node => node.nodeSchemaVersion !== 1)) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
  const byKey = new Map(draft.nodes.map(node => [node.nodeKey, node]));
  if (byKey.size !== draft.nodes.length || draft.nodes.filter(node => node.nodeTypeKey === 'start').length !== 1) {
    throw new Error('client.invalid_workflow_definition_draft');
  }
  const start = draft.nodes.find(node => node.nodeTypeKey === 'start');
  if (start === undefined) throw new Error('client.invalid_workflow_definition_draft');
  const visited = new Set<string>();

  const build = (node: WorkflowNodeDraft | undefined, stopKey = 'end'): WorkflowVue3Node | null => {
    if (node === undefined) throw new Error('client.invalid_workflow_definition_draft');
    if (node.nodeKey === stopKey || node.nodeTypeKey === 'end') return null;
    if (visited.has(node.nodeKey)) throw new Error('client.invalid_workflow_definition_draft');
    visited.add(node.nodeKey);
    const config = isRecord(node.config) ? node.config : {};
    assertSafeConfiguration(config);
    if (node.nodeTypeKey === 'gateway.parallel' && config.gatewayRoleKey === 'fork') {
      const parallel = readTargetParallelForkConfig(config);
      const join = byKey.get(parallel.joinNodeKey);
      if (join === undefined || join.nodeTypeKey !== 'gateway.parallel') {
        throw new Error('client.invalid_workflow_definition_draft');
      }
      const joinConfig = isRecord(join.config) ? join.config : {};
      const nextAfterJoin = Array.isArray(joinConfig.nextNodeKeys)
        ? joinConfig.nextNodeKeys.find((value): value is string => typeof value === 'string')
        : undefined;
      const conditionNodes: WorkflowVue3Node[] = parallel.branches.map((branch, index) => ({
        id: `${node.nodeKey}-${branch.branchKey}`,
        type: 3,
        nodeName: branch.branchKey,
        branchKey: branch.branchKey,
        priorityLevel: index + 1,
        childNode: build(byKey.get(branch.nextNodeKey)!, parallel.joinNodeKey)
      }));
      const merge = nextAfterJoin === undefined ? undefined : byKey.get(nextAfterJoin);
      return {
        id: node.nodeKey,
        type: PARALLEL_ROUTE_TYPE,
        nodeName: typeof config.nodeName === 'string' ? config.nodeName : '并行分支',
        conditionNodes,
        childNode: merge === undefined ? null : build(merge, stopKey)
      };
    }
    if (node.nodeTypeKey === 'gateway.parallel') {
      throw new Error('client.unsupported_workflow_node');
    }
    if (node.nodeTypeKey === 'gateway.inclusive' && config.gatewayRoleKey === 'fork') {
      const inclusive = readTargetInclusiveForkConfig(config);
      const join = byKey.get(inclusive.joinNodeKey);
      if (join === undefined || join.nodeTypeKey !== 'gateway.inclusive') {
        throw new Error('client.invalid_workflow_definition_draft');
      }
      const joinConfig = isRecord(join.config) ? join.config : {};
      const nextAfterJoin = Array.isArray(joinConfig.nextNodeKeys)
        ? joinConfig.nextNodeKeys.find((value): value is string => typeof value === 'string')
        : undefined;
      const conditionNodes: WorkflowVue3Node[] = inclusive.branches.map((branch, index) => ({
        id: `${node.nodeKey}-${branch.branchKey}`,
        type: 3,
        nodeName: branch.branchKey,
        branchKey: branch.branchKey,
        ...projectWorkflowCondition(branch.condition),
        priorityLevel: index + 1,
        childNode: build(byKey.get(branch.nextNodeKey)!, inclusive.joinNodeKey)
      }));
      conditionNodes.push({
        id: `${node.nodeKey}-default`,
        type: 3,
        nodeName: '其他条件',
        isDefault: true,
        priorityLevel: conditionNodes.length + 1,
        childNode: build(byKey.get(inclusive.defaultNextNodeKey)!, inclusive.joinNodeKey)
      });
      const merge = nextAfterJoin === undefined ? undefined : byKey.get(nextAfterJoin);
      return {
        id: node.nodeKey,
        type: INCLUSIVE_ROUTE_TYPE,
        nodeName: typeof config.nodeName === 'string' ? config.nodeName : '包容分支',
        conditionNodes,
        childNode: merge === undefined ? null : build(merge, stopKey)
      };
    }
    if (node.nodeTypeKey === 'gateway.inclusive') {
      throw new Error('client.unsupported_workflow_node');
    }
    const type = targetToSourceType.get(node.nodeTypeKey);
    if (type === undefined) throw new Error('client.unsupported_workflow_node');
    if (node.nodeTypeKey === 'gateway.exclusive') {
      const gateway = readTargetGatewayConfig(config);
      const mergeKey = findGatewayMergeKey(gateway.nextNodeKeys, byKey);
      const conditionNodes: WorkflowVue3Node[] = gateway.branches.map((branch, index) => ({
        id: `${node.nodeKey}-${branch.branchKey}`,
        type: 3,
        nodeName: branch.branchKey,
        branchKey: branch.branchKey,
        ...projectWorkflowCondition(branch.condition),
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
    const nextConfig = isRecord(next.config) ? next.config : {};
    const stopsAtJoinGateway = (next.nodeTypeKey === 'gateway.parallel' || next.nodeTypeKey === 'gateway.inclusive')
      && nextConfig.gatewayRoleKey === 'join';
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
      childNode: stopsAtJoinGateway ? null : build(next, stopKey)
    };
  };

  const tree = build(start) ?? (() => { throw new Error('client.invalid_workflow_definition_draft'); })();
  const projectDependencies = (current: WorkflowVue3Node): void => {
    const source = byKey.get(current.id ?? '') as (WorkflowNodeDraft & { awaitActivityNodeKeys?: unknown }) | undefined;
    const awaited = readWorkflowAwaitDependencies(source?.awaitActivityNodeKeys);
    if (awaited) (current as Record<string, unknown>).awaitActivityNodeKeys = awaited;
    if (current.childNode) projectDependencies(current.childNode);
    for (const branch of current.conditionNodes ?? []) if (branch.childNode) projectDependencies(branch.childNode);
  };
  projectDependencies(tree);
  // 内部汇合和结束不显示为可编辑卡片，但导入版本的显式引用仍须无损保存。
  const boundaryDependencies: Record<string, string[]> = {};
  for (const node of draft.nodes) {
    if (visited.has(node.nodeKey)) continue;
    const awaited = readWorkflowAwaitDependencies((node as WorkflowNodeDraft & { awaitActivityNodeKeys?: unknown }).awaitActivityNodeKeys);
    if (awaited) boundaryDependencies[node.nodeKey] = awaited;
  }
  if (Object.keys(boundaryDependencies).length) (tree as Record<string, unknown>).boundaryAwaitActivityNodeKeys = boundaryDependencies;
  const kind = draft.workflowKindKey ?? 'approval';
  if (kind !== 'approval' && kind !== 'automation') throw new Error('client.invalid_workflow_definition_draft');
  return kind === 'automation' ? { ...tree, workflowKindKey: kind } : tree;
}

type GatewayConditionConfig = WorkflowConditionConfig;

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

interface ParallelForkBranchConfig {
  readonly branchKey: string;
  readonly nextNodeKey: string;
}

interface ParallelForkTargetConfig {
  readonly joinNodeKey: string;
  readonly branches: ParallelForkBranchConfig[];
}

/** 从服务端 Draft 读取并行分叉闭合配置。 */
function readTargetParallelForkConfig(config: Record<string, unknown>): ParallelForkTargetConfig {
  if (config.gatewayRoleKey !== 'fork' || typeof config.joinNodeKey !== 'string') {
    throw new Error('client.invalid_workflow_gateway');
  }
  const joinNodeKey = readStableKey(config.joinNodeKey);
  const limits = workflowGatewayLimits['gateway.parallel'];
  if (!Array.isArray(config.branches) || config.branches.length < limits.minimumBranches || config.branches.length > limits.maximumBranches) {
    throw new Error('client.invalid_workflow_gateway');
  }
  const branches = config.branches.map(value => {
    if (!isRecord(value)) throw new Error('client.invalid_workflow_gateway');
    return {
      branchKey: readStableKey(value.branchKey),
      nextNodeKey: readStableKey(value.nextNodeKey)
    };
  });
  const allowed = new Set(['nodeName', 'gatewayRoleKey', 'joinNodeKey', 'branches']);
  if (Object.keys(config).some(key => !allowed.has(key))) {
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return { joinNodeKey, branches };
}

interface InclusiveForkTargetConfig {
  readonly joinNodeKey: string;
  readonly branches: GatewayBranchConfig[];
  readonly defaultNextNodeKey: string;
}

/** 从服务端 Draft 读取包容分叉闭合配置。 */
function readTargetInclusiveForkConfig(config: Record<string, unknown>): InclusiveForkTargetConfig {
  if (config.gatewayRoleKey !== 'fork' || typeof config.joinNodeKey !== 'string') {
    throw new Error('client.invalid_workflow_gateway');
  }
  const joinNodeKey = readStableKey(config.joinNodeKey);
  const limits = workflowGatewayLimits['gateway.inclusive'];
  if (!Array.isArray(config.branches) || config.branches.length < limits.minimumBranches || config.branches.length > limits.maximumBranches) {
    throw new Error('client.invalid_workflow_gateway');
  }
  const branches = config.branches.map(value => {
    if (!isRecord(value)) throw new Error('client.invalid_workflow_gateway');
    const condition = value.condition;
    if (!isRecord(condition)) throw new Error('client.invalid_workflow_gateway');
    return {
      branchKey: readStableKey(value.branchKey),
      nextNodeKey: readStableKey(value.nextNodeKey),
      condition: readWorkflowCondition(condition)
    };
  });
  const defaultNextNodeKey = readStableKey(config.defaultNextNodeKey);
  const allowed = new Set(['nodeName', 'gatewayRoleKey', 'joinNodeKey', 'branches', 'defaultNextNodeKey']);
  if (Object.keys(config).some(key => !allowed.has(key))) {
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return { joinNodeKey, branches, defaultNextNodeKey };
}

/** 条件卡片同时支持叶子比较与保留结构的有界组合表达式。 */
function readDesignerGatewayCondition(node: WorkflowVue3Node): GatewayConditionConfig {
  if (node.condition !== undefined) return readWorkflowCondition(node.condition);
  return readWorkflowCondition({ fieldKey: node.fieldKey, operator: node.operator,
    ...(node.value === undefined ? {} : { value: node.value }) });
}

/** 从服务端 Draft 读取排他网关闭合配置，并核对有序出口集合。 */
function readTargetGatewayConfig(config: Record<string, unknown>): GatewayTargetConfig {
  const nextNodeKeys = Array.isArray(config.nextNodeKeys)
    ? config.nextNodeKeys.map(readStableKey)
    : [];
  const limits = workflowGatewayLimits['gateway.exclusive'];
  if (!Array.isArray(config.branches) || config.branches.length < limits.minimumBranches || config.branches.length > limits.maximumBranches) {
    throw new Error('client.invalid_workflow_gateway');
  }
  const branches = config.branches.map(value => {
    if (!isRecord(value) || !isRecord(value.condition)) {
      throw new Error('client.invalid_workflow_gateway');
    }
    return {
      branchKey: readStableKey(value.branchKey),
      nextNodeKey: readStableKey(value.nextNodeKey),
      condition: readWorkflowCondition(value.condition)
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
    // 分叉节点的出口存于 branches/defaultNextNodeKey；忽略它们会丢失跨网关公共后继。
    if ((node.nodeTypeKey === 'gateway.parallel' || node.nodeTypeKey === 'gateway.inclusive')
      && node.config.gatewayRoleKey === 'fork' && Array.isArray(node.config.branches)) {
      for (const branch of node.config.branches) {
        if (isRecord(branch) && typeof branch.nextNodeKey === 'string') next.push(branch.nextNodeKey);
      }
      if (typeof node.config.defaultNextNodeKey === 'string') next.push(node.config.defaultNextNodeKey);
    }
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
  if (nodeTypeKey === 'human.group_approval') Object.assign(result, readGroupApprovalConfiguration(node));
  if (nodeTypeKey === 'human.task') result.taskPolicy = readTaskPolicy(node.taskPolicy);
  if (nodeTypeKey === 'start' && node.withdrawalPolicy !== undefined) result.withdrawalPolicy = readWithdrawalPolicy(node.withdrawalPolicy);
  if (nodeTypeKey === 'start' && node.administratorUserIds !== undefined) result.administratorUserIds = readFlowAdministratorUserIds(node.administratorUserIds);
  if (nodeTypeKey === 'human.vote') result.votePolicy = readVotePolicy(node.votePolicy);
  if (nodeTypeKey === 'human.claim') result.claimPolicy = readClaimPolicy(node.claimPolicy);
  if (nodeTypeKey === 'timer.delay') result.delayPolicy = readWorkflowDelayPolicy(node.delayPolicy);
  if (nodeTypeKey === 'data.modify' || nodeTypeKey === 'data.clear') Object.assign(result, readWorkflowFieldMutation(nodeTypeKey, node));
  if (['integration.trigger', 'integration.async_trigger'].includes(nodeTypeKey)) Object.assign(result, readWorkflowConnector(node));
  if (nodeTypeKey === 'business.command') Object.assign(result, readWorkflowBusinessCommand(node));
  if (nodeTypeKey === 'process.subprocess') Object.assign(result, readWorkflowSubprocess(node));
  for (const [key, value] of Object.entries(node)) {
    if (nodeTypeKey === 'process.subprocess' && ['subprocessPolicy', 'fieldPolicies', 'boundSubprocess', 'multiInstancePolicy'].includes(key)) continue;
    if (nodeTypeKey === 'business.command' && ['businessCommandPolicy', 'boundBusinessActionHash'].includes(key)) continue;
    if ((nodeTypeKey === 'data.modify' || nodeTypeKey === 'data.clear') && (key === 'mutationPolicy' || key === 'fieldPolicies')) continue;
    if (['integration.trigger', 'integration.async_trigger'].includes(nodeTypeKey) && ['connectorPolicy', 'fieldPolicies', 'boundConnectorHash'].includes(key)) continue;
    if (nodeTypeKey === 'human.group_approval' && (key === 'groups' || key === 'groupApprovalPolicy')) continue;
    if (nodeTypeKey === 'human.task' && key === 'taskPolicy') continue;
    if (nodeTypeKey === 'human.vote' && key === 'votePolicy') continue;
    if (nodeTypeKey === 'human.claim' && key === 'claimPolicy') continue;
    if (nodeTypeKey === 'timer.delay' && key === 'delayPolicy') continue;
    if (structuralKeys.has(key) || transientDesignerKeys.has(key)) continue;
    if (key === 'workflowKindKey' && nodeTypeKey === 'start') continue;
    if (key === 'withdrawalPolicy' && nodeTypeKey === 'start') continue;
    if (key === 'administratorUserIds' && nodeTypeKey === 'start') continue;
    if (key === 'nodeName') {
      if (typeof value !== 'string') throw new Error('client.invalid_workflow_definition_draft');
      result.nodeName = value;
      continue;
    }
    if ((key === 'signRequired' || key === 'commentRequired') && isHumanFormType(nodeTypeKey)) {
      if (typeof value !== 'boolean') throw new Error('client.invalid_workflow_approval_policy');
      result[key] = value;
      continue;
    }
    if (key === 'operationPermissions' && isHumanFormType(nodeTypeKey)) {
      result.operationPermissions = readWorkflowOperationPermissions(value, nodeTypeKey);
      continue;
    }
    if (key === 'fieldPolicies' && isHumanFormType(nodeTypeKey)) {
      assertFieldPolicies(value);
      result.fieldPolicies = cloneJson(value);
      continue;
    }
    if (key === 'timeoutPolicy' && isHumanFormType(nodeTypeKey)) {
      assertTimeoutPolicy(value);
      result.timeoutPolicy = cloneJson(value);
      continue;
    }
    if (key === 'slaPolicy' && isHumanFormType(nodeTypeKey)) {
      result.slaPolicy = readWorkflowSlaPolicy(value, nodeTypeKey); continue;
    }
    if (key === 'resultCallbackFieldKey' && isHumanFormType(nodeTypeKey)) {
      result.resultCallbackFieldKey = readWorkflowResultCallback({ resultCallbackFieldKey: value, fieldPolicies: node.fieldPolicies }, nodeTypeKey); continue;
    }
    if (key === 'listeners' && isHumanFormType(nodeTypeKey)) { result.listeners = readWorkflowActivityListeners(value); continue; }
    if (key === 'approvalPolicy' && nodeTypeKey === 'human.approval') {
      result.approvalPolicy = readApprovalPolicy(value, node.assigneePolicy);
      continue;
    }
    if (key === 'assigneePolicy' && (nodeTypeKey === 'human.approval' || nodeTypeKey === 'human.task' || nodeTypeKey === 'human.vote' || nodeTypeKey === 'human.claim')) {
      result.assigneePolicy = readAssigneePolicy(value);
      continue;
    }
    if (key === 'recipientUserIds' && nodeTypeKey === 'notify.cc') {
      if (node.recipientPolicy !== undefined) throw new Error('client.invalid_workflow_cc_recipients');
      result.recipientUserIds = readRecipientUserIds(value);
      continue;
    }
    if (key === 'recipientPolicy' && nodeTypeKey === 'notify.cc') {
      if (node.recipientUserIds !== undefined) throw new Error('client.invalid_workflow_cc_recipients');
      result.recipientPolicy = readCcRecipientPolicy(value); continue;
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
  if (nodeTypeKey === 'human.group_approval') Object.assign(result, readGroupApprovalConfiguration(config));
  if (nodeTypeKey === 'human.task') result.taskPolicy = readTaskPolicy(config.taskPolicy);
  if (nodeTypeKey === 'start' && config.withdrawalPolicy !== undefined) result.withdrawalPolicy = readWithdrawalPolicy(config.withdrawalPolicy);
  if (nodeTypeKey === 'start' && config.administratorUserIds !== undefined) result.administratorUserIds = readFlowAdministratorUserIds(config.administratorUserIds);
  if (nodeTypeKey === 'human.vote') result.votePolicy = readVotePolicy(config.votePolicy);
  if (nodeTypeKey === 'human.claim') result.claimPolicy = readClaimPolicy(config.claimPolicy);
  if (nodeTypeKey === 'timer.delay') result.delayPolicy = readWorkflowDelayPolicy(config.delayPolicy);
  if (nodeTypeKey === 'data.modify' || nodeTypeKey === 'data.clear') Object.assign(result, readWorkflowFieldMutation(nodeTypeKey, config));
  if (['integration.trigger', 'integration.async_trigger'].includes(nodeTypeKey)) Object.assign(result, readWorkflowConnector(config));
  if (nodeTypeKey === 'business.command') Object.assign(result, readWorkflowBusinessCommand(config));
  if (nodeTypeKey === 'process.subprocess') Object.assign(result, readWorkflowSubprocess(config));
  for (const [key, value] of Object.entries(config)) {
    if (nodeTypeKey === 'process.subprocess' && ['subprocessPolicy', 'fieldPolicies', 'boundSubprocess', 'multiInstancePolicy'].includes(key)) continue;
    if (nodeTypeKey === 'business.command' && ['businessCommandPolicy', 'boundBusinessActionHash'].includes(key)) continue;
    if ((nodeTypeKey === 'data.modify' || nodeTypeKey === 'data.clear') && (key === 'mutationPolicy' || key === 'fieldPolicies')) continue;
    if (['integration.trigger', 'integration.async_trigger'].includes(nodeTypeKey) && ['connectorPolicy', 'fieldPolicies', 'boundConnectorHash'].includes(key)) continue;
    if (nodeTypeKey === 'human.group_approval' && (key === 'groups' || key === 'groupApprovalPolicy')) continue;
    if (nodeTypeKey === 'human.task' && key === 'taskPolicy') continue;
    if (nodeTypeKey === 'human.vote' && key === 'votePolicy') continue;
    if (nodeTypeKey === 'human.claim' && key === 'claimPolicy') continue;
    if (nodeTypeKey === 'timer.delay' && key === 'delayPolicy') continue;
    if (key === 'nextNodeKeys' || key === 'nodeName') continue;
    if (key === 'withdrawalPolicy' && nodeTypeKey === 'start') continue;
    if (key === 'administratorUserIds' && nodeTypeKey === 'start') continue;
    if ((key === 'signRequired' || key === 'commentRequired') && isHumanFormType(nodeTypeKey)) {
      if (typeof value !== 'boolean') throw new Error('client.invalid_workflow_approval_policy');
      result[key] = value;
      continue;
    }
    if (key === 'operationPermissions' && isHumanFormType(nodeTypeKey)) {
      result.operationPermissions = readWorkflowOperationPermissions(value, nodeTypeKey);
      continue;
    }
    if (key === 'fieldPolicies' && isHumanFormType(nodeTypeKey)) {
      assertFieldPolicies(value);
      result.fieldPolicies = cloneJson(value);
      continue;
    }
    if (key === 'timeoutPolicy' && isHumanFormType(nodeTypeKey)) {
      assertTimeoutPolicy(value);
      result.timeoutPolicy = cloneJson(value);
      continue;
    }
    if (key === 'slaPolicy' && isHumanFormType(nodeTypeKey)) {
      result.slaPolicy = readWorkflowSlaPolicy(value, nodeTypeKey); continue;
    }
    if (key === 'resultCallbackFieldKey' && isHumanFormType(nodeTypeKey)) {
      result.resultCallbackFieldKey = readWorkflowResultCallback({ resultCallbackFieldKey: value, fieldPolicies: config.fieldPolicies }, nodeTypeKey); continue;
    }
    if (key === 'listeners' && isHumanFormType(nodeTypeKey)) { result.listeners = readWorkflowActivityListeners(value); continue; }
    if (key === 'approvalPolicy' && nodeTypeKey === 'human.approval') {
      result.approvalPolicy = readApprovalPolicy(value, config.assigneePolicy);
      continue;
    }
    if (key === 'assigneePolicy' && (nodeTypeKey === 'human.approval' || nodeTypeKey === 'human.task' || nodeTypeKey === 'human.vote' || nodeTypeKey === 'human.claim')) {
      result.assigneePolicy = readAssigneePolicy(value);
      continue;
    }
    if (key === 'recipientUserIds' && nodeTypeKey === 'notify.cc') {
      if (config.recipientPolicy !== undefined) throw new Error('client.invalid_workflow_cc_recipients');
      result.recipientUserIds = readRecipientUserIds(value);
      continue;
    }
    if (key === 'recipientPolicy' && nodeTypeKey === 'notify.cc') {
      if (config.recipientUserIds !== undefined) throw new Error('client.invalid_workflow_cc_recipients');
      result.recipientPolicy = readCcRecipientPolicy(value); continue;
    }
    throw new Error('client.unsupported_workflow_node_configuration');
  }
  return result;
}

/** 验证抄送人闭合集合，并通过复制切断设计器可变数组引用。 */
function readRecipientUserIds(
  value: unknown,
  errorCode = 'client.invalid_workflow_cc_recipients'
): string[] {
  if (!Array.isArray(value)
    || value.length < 1
    || value.length > 20
    || value.some(userId => typeof userId !== 'string' || !userIdPattern.test(userId))) {
    throw new Error(errorCode);
  }
  const normalized = value.map(userId => String(userId).toLowerCase());
  if (new Set(normalized).size !== normalized.length) {
    throw new Error(errorCode);
  }
  return normalized;
}

/** 验证办理人解析闭合配置，默认发起人语义允许省略整个策略对象。 */
export function readAssigneePolicy(value: unknown): Record<string, unknown> {
  if (!isRecord(value) || Object.keys(value).some(key => !['sources', 'handling'].includes(key)) || !Array.isArray(value.sources) || value.sources.length < 1 || value.sources.length > 8) {
    throw new Error('client.invalid_workflow_assignee_policy');
  }
  const sources = value.sources.map(readAssigneeSource);
  return { sources, ...(value.handling === undefined ? {} : { handling: readAssigneeHandling(value.handling) }) };
}

/** 抄送允许正常无人，但不复用审批的通过、拒绝或替补处置。 */
export function readCcRecipientPolicy(value: unknown): { sources: Record<string, unknown>[] } {
  if (!isRecord(value) || Object.keys(value).length !== 1 || !Object.hasOwn(value, 'sources')) throw new Error('client.invalid_workflow_cc_recipients');
  const policy = readAssigneePolicy(value); const sources = policy.sources as Record<string, unknown>[];
  const known = sources.filter(source => source.resolverKindKey === 'specified_users').flatMap(source => source.userIds as string[]);
  if (new Set(known).size > 20) throw new Error('client.invalid_workflow_cc_recipients');
  return { sources };
}

export interface WorkflowAssigneeHandlingAction { actionKey: string; fallbackPolicies?: Array<{ sources: Record<string, unknown>[] }> }
export interface WorkflowAssigneeHandling { whenEmpty?: WorkflowAssigneeHandlingAction; whenInitiator?: WorkflowAssigneeHandlingAction }

/** 替补是有限的固定目录链，不能递归配置或读取另一份启动、表单和未完成节点输入。 */
export function readAssigneeHandling(value: unknown): WorkflowAssigneeHandling {
  const invalid = (): never => { throw new Error('client.invalid_workflow_assignee_policy'); };
  if (!isRecord(value) || Object.keys(value).length < 1 || Object.keys(value).some(key => !['whenEmpty', 'whenInitiator'].includes(key))) return invalid();
  const result: WorkflowAssigneeHandling = {};
  for (const trigger of ['whenEmpty', 'whenInitiator'] as const) {
    const action = value[trigger]; if (action === undefined) continue;
    const actions = trigger === 'whenEmpty' ? ['error', 'suspend', 'auto_pass', 'auto_reject', 'assign'] : ['keep', 'skip', 'assign'];
    if (!isRecord(action) || typeof action.actionKey !== 'string' || !actions.includes(action.actionKey) ||
      Object.keys(action).sort().join(',') !== (action.actionKey === 'assign' ? 'actionKey,fallbackPolicies' : 'actionKey')) return invalid();
    if (action.actionKey !== 'assign') { result[trigger] = { actionKey: action.actionKey }; continue; }
    if (!Array.isArray(action.fallbackPolicies) || action.fallbackPolicies.length < 1 || action.fallbackPolicies.length > 3) return invalid();
    const fallbackPolicies = action.fallbackPolicies.map((policy: unknown) => {
      if (!isRecord(policy) || Object.keys(policy).join(',') !== 'sources') return invalid();
      const parsed = readAssigneePolicy(policy) as { sources: Record<string, unknown>[] };
      if (parsed.sources.some(source => ['form_users', 'form_units', 'previous_node_actors', 'starter_selected_users'].includes(String(source.resolverKindKey)))) return invalid();
      return parsed;
    });
    result[trigger] = { actionKey: 'assign', fallbackPolicies };
  }
  return result;
}

/** 验证单条办理人来源配置。 */
function readAssigneeSource(value: unknown): Record<string, unknown> {
  if (!isRecord(value) || typeof value.resolverKindKey !== 'string') {
    throw new Error('client.invalid_workflow_assignee_policy');
  }
  switch (value.resolverKindKey) {
    case 'starter_selected_users':
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'selectionPolicy']);
      return { resolverKindKey: value.resolverKindKey, selectionPolicy: readStarterSelectionPolicy(value.selectionPolicy) };
    case 'form_users':
    case 'form_units':
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'fieldKey']);
      if (typeof value.fieldKey !== 'string' || !stableKeyPattern.test(value.fieldKey)) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, fieldKey: value.fieldKey };
    case 'previous_node_actors':
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'nodeKey']);
      if (typeof value.nodeKey !== 'string' || !stableKeyPattern.test(value.nodeKey)) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, nodeKey: value.nodeKey };
    case 'specified_users':
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'userIds']);
      return {
        resolverKindKey: value.resolverKindKey,
        userIds: readRecipientUserIds(value.userIds, 'client.invalid_workflow_assignee_policy')
      };
    case 'role_members': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'roleIds']);
      const roleIds = readRecipientUserIds(value.roleIds, 'client.invalid_workflow_assignee_policy');
      if (roleIds.length < 1 || roleIds.length > 5) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, roleIds };
    }
    case 'organization_unit_leader': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'unitId']);
      const unitId = typeof value.unitId === 'string' && userIdPattern.test(value.unitId)
        ? value.unitId.toLowerCase() : null;
      if (unitId === null) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, unitId };
    }
    case 'organization_unit_members': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'unitId']);
      const unitId = typeof value.unitId === 'string' && userIdPattern.test(value.unitId)
        ? value.unitId.toLowerCase()
        : null;
      if (unitId === null) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, unitId };
    }
    case 'initiator':
    case 'initiator_primary_unit_leader':
    case 'initiator_direct_manager':
    case 'flow_administrators':
      assertAssigneeSourceKeys(value, ['resolverKindKey']);
      return { resolverKindKey: value.resolverKindKey };
    case 'initiator_ancestor_unit_leader': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'ancestorLevel']);
      const ancestorLevel = typeof value.ancestorLevel === 'number'
        ? value.ancestorLevel
        : typeof value.ancestorLevel === 'string' ? Number(value.ancestorLevel) : Number.NaN;
      if (!Number.isInteger(ancestorLevel) || ancestorLevel < 1 || ancestorLevel > 20) {
        throw new Error('client.invalid_workflow_assignee_policy');
      }
      return { resolverKindKey: value.resolverKindKey, ancestorLevel };
    }
    case 'position_members': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'positionIds']);
      const positionIds = readRecipientUserIds(value.positionIds, 'client.invalid_workflow_assignee_policy');
      if (positionIds.length > 5) throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, positionIds };
    }
    case 'initiator_leader_chain': {
      assertAssigneeSourceKeys(value, ['resolverKindKey', 'maximumLevels']);
      if (!Number.isInteger(value.maximumLevels) || Number(value.maximumLevels) < 1 || Number(value.maximumLevels) > 20)
        throw new Error('client.invalid_workflow_assignee_policy');
      return { resolverKindKey: value.resolverKindKey, maximumLevels: value.maximumLevels };
    }
    default:
      throw new Error('client.invalid_workflow_assignee_policy');
  }
}

/** 开始节点名单保序、去重并有界，空名单不产生隐含管理员。 */
export function readFlowAdministratorUserIds(value: unknown): string[] {
  if (!Array.isArray(value) || value.length > 20) throw new Error('client.invalid_workflow_administrators');
  if (value.length === 0) return [];
  const result = readRecipientUserIds(value, 'client.invalid_workflow_administrators');
  if (result.some(id => /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id))) throw new Error('client.invalid_workflow_administrators');
  return result;
}

/** 未识别的来源参数必须明确拒绝，不能在保存时静默删除。 */
function assertAssigneeSourceKeys(value: Record<string, unknown>, expected: string[]): void {
  const keys = Object.keys(value);
  if (keys.length !== expected.length || keys.some(key => !expected.includes(key))) {
    throw new Error('client.invalid_workflow_assignee_policy');
  }
}

export interface WorkflowStarterSelectionPolicy {
  readonly selectionKey: string;
  readonly modeKey: 'single' | 'multiple';
  readonly maximumUsers: number;
  readonly range: { readonly kindKey: 'all' | 'specified_users' | 'role_members'; readonly userIds?: readonly string[]; readonly roleIds?: readonly string[] };
}

/** 发起人选择的模式和范围由发布版本固定，不能夹带租户、身份或任意目录过滤器。 */
export function readStarterSelectionPolicy(value: unknown): WorkflowStarterSelectionPolicy {
  if (!isRecord(value)) throw new Error('client.invalid_workflow_assignee_policy');
  assertAssigneeSourceKeys(value, ['selectionKey', 'modeKey', 'maximumUsers', 'range']);
  if (typeof value.selectionKey !== 'string' || !stableKeyPattern.test(value.selectionKey) || !['single', 'multiple'].includes(String(value.modeKey)) ||
    !Number.isInteger(value.maximumUsers) || Number(value.maximumUsers) < 1 || Number(value.maximumUsers) > 20 || value.modeKey === 'single' && value.maximumUsers !== 1 || !isRecord(value.range))
    throw new Error('client.invalid_workflow_assignee_policy');
  const range = value.range;
  let parsed: WorkflowStarterSelectionPolicy['range'];
  if (range.kindKey === 'all') { assertAssigneeSourceKeys(range, ['kindKey']); parsed = { kindKey: 'all' }; }
  else if (range.kindKey === 'specified_users') {
    assertAssigneeSourceKeys(range, ['kindKey', 'userIds']); parsed = { kindKey: 'specified_users', userIds: readRecipientUserIds(range.userIds, 'client.invalid_workflow_assignee_policy') };
  } else if (range.kindKey === 'role_members') {
    assertAssigneeSourceKeys(range, ['kindKey', 'roleIds']);
    const roles = readRecipientUserIds(range.roleIds, 'client.invalid_workflow_assignee_policy');
    if (roles.length > 5) throw new Error('client.invalid_workflow_assignee_policy');
    parsed = { kindKey: 'role_members', roleIds: roles };
  } else throw new Error('client.invalid_workflow_assignee_policy');
  return { selectionKey: value.selectionKey, modeKey: value.modeKey as 'single' | 'multiple', maximumUsers: Number(value.maximumUsers), range: parsed };
}

/** 验证多人审批闭合配置，防止设计器把展示字段或未知模式写入发布草稿。 */
export function readApprovalPolicy(value: unknown, assigneePolicy?: unknown): Record<string, unknown> {
  if (!isRecord(value) || !['all', 'any', 'nOfM', 'percent', 'sequential'].includes(String(value.modeKey))) {
    throw new Error('client.invalid_workflow_approval_policy');
  }
  const keys = Object.keys(value);
  const expectedKeys = value.modeKey === 'nOfM'
    ? ['approverUserIds', 'modeKey', 'requiredApprovals']
    : value.modeKey === 'percent'
      ? ['approverUserIds', 'modeKey', 'requiredPercentage']
      : ['approverUserIds', 'modeKey'];
  if (keys.length !== expectedKeys.length || expectedKeys.some(key => !keys.includes(key))) {
    throw new Error('client.invalid_workflow_approval_policy');
  }
  const approverUserIds = Array.isArray(value.approverUserIds) && value.approverUserIds.length === 0 ? [] : readRecipientUserIds(
    value.approverUserIds,
    'client.invalid_workflow_approval_policy'
  );
  if (approverUserIds.length < 2 && assigneePolicy === undefined) {
    throw new Error('client.invalid_workflow_approval_policy');
  }
  if (assigneePolicy !== undefined) readAssigneePolicy(assigneePolicy);
  if (value.modeKey === 'nOfM' &&
    (!Number.isInteger(value.requiredApprovals) || Number(value.requiredApprovals) <= 1 ||
      Number(value.requiredApprovals) >= (assigneePolicy === undefined ? approverUserIds.length : 20))) {
    throw new Error('client.invalid_workflow_approval_policy');
  }
  if (value.modeKey === 'percent' &&
    (!Number.isInteger(value.requiredPercentage) || Number(value.requiredPercentage) < 1 ||
      Number(value.requiredPercentage) > 100)) {
    throw new Error('client.invalid_workflow_approval_policy');
  }
  return {
    modeKey: value.modeKey,
    approverUserIds,
    ...(value.modeKey === 'nOfM' ? { requiredApprovals: value.requiredApprovals } : {}),
    ...(value.modeKey === 'percent' ? { requiredPercentage: value.requiredPercentage } : {})
  };
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
  const fieldNode = value.type === 15 || value.type === 16;
  const connectorNode = value.type === 6 || value.type === 11;
  const businessNode = value.type === 23;
  const subprocessNode = value.type === 9;
  if (subprocessNode) readWorkflowSubprocess(value);
  if (businessNode) readWorkflowBusinessCommand(value);
  if (connectorNode) readWorkflowConnector(value);
  if (fieldNode) readWorkflowFieldMutation(value.type === 16 ? 'data.clear' : 'data.modify', value);
  for (const [key, child] of Object.entries(value)) {
    // 字段常量是被闭合规则读取的数据，URL或普通属性名不能被误当作连接器配置执行。
    if (fieldNode && (key === 'mutationPolicy' || key === 'fieldPolicies')) continue;
    if (connectorNode && ['connectorPolicy', 'fieldPolicies', 'boundConnectorHash'].includes(key)) continue;
    if (businessNode && ['businessCommandPolicy', 'boundBusinessActionHash'].includes(key)) continue;
    if (subprocessNode && ['subprocessPolicy', 'fieldPolicies', 'boundSubprocess', 'multiInstancePolicy'].includes(key)) continue;
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
  if (nodeTypeKey === 'data.modify') return '修改字段';
  if (nodeTypeKey === 'data.clear') return '清空字段';
  if (nodeTypeKey === 'integration.async_trigger') return '异步连接器';
  if (nodeTypeKey === 'integration.trigger') return '连接器';
  if (nodeTypeKey === 'business.command') return '业务操作';
  if (nodeTypeKey === 'process.subprocess') return '子流程';
  if (nodeTypeKey === 'timer.delay') return '延时';
  if (nodeTypeKey === 'start') return '发起人';
  if (nodeTypeKey === 'human.approval') return '审批人';
  if (nodeTypeKey === 'gateway.exclusive') return '条件分支';
  if (nodeTypeKey === 'gateway.parallel') return '并行分支';
  if (nodeTypeKey === 'gateway.inclusive') return '包容分支';
  return '抄送人';
}

/** 统一识别普通对象，避免把数组或 null 当成可递归节点。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** 分组配置保留明确组键、组内来源和组间门槛，禁止把个人名单投影成组票。 */
export function readGroupApprovalConfiguration(config: Record<string, unknown>): {
  groups: Record<string, unknown>[]; groupApprovalPolicy: Record<string, unknown> & { modeKey: 'all' | 'any' | 'nOfM' | 'percent' }
} {
  const invalid = (): never => { throw new Error('client.invalid_workflow_approval_policy'); };
  if (!Array.isArray(config.groups) || config.groups.length < 2 || config.groups.length > 8) return invalid();
  const keys = new Set<string>();
  const knownUsers = new Set<string>();
  const groups = config.groups.map((group: unknown) => {
    if (!isRecord(group) || typeof group.groupKey !== 'string' || !/^[a-z][a-z0-9_]{0,63}$/u.test(group.groupKey) ||
      keys.has(group.groupKey) || Object.keys(group).some(key => !['groupKey', 'groupName', 'assigneePolicy', 'approvalPolicy'].includes(key)) ||
      group.assigneePolicy === undefined && group.approvalPolicy === undefined) return invalid();
    keys.add(group.groupKey);
    if (group.groupName !== undefined && (typeof group.groupName !== 'string' || group.groupName.trim().length < 1 || group.groupName.trim().length > 128)) return invalid();
    const sources = group.assigneePolicy === undefined ? undefined : readAssigneePolicy(group.assigneePolicy);
    const policy = group.approvalPolicy === undefined ? undefined : readApprovalPolicy(group.approvalPolicy, group.assigneePolicy);
    const localUsers = new Set<string>((policy?.approverUserIds as string[] | undefined) ?? []);
    for (const source of (sources?.sources as Record<string, unknown>[] | undefined) ?? []) {
      if (source.resolverKindKey === 'specified_users') for (const id of source.userIds as string[]) localUsers.add(id);
    }
    for (const id of localUsers) { if (knownUsers.has(id)) return invalid(); knownUsers.add(id); }
    return { groupKey: group.groupKey, ...(group.groupName === undefined ? {} : { groupName: String(group.groupName).trim() }),
      ...(sources === undefined ? {} : { assigneePolicy: sources }), ...(policy === undefined ? {} : { approvalPolicy: policy }) };
  });
  if (knownUsers.size > 20 || !isRecord(config.groupApprovalPolicy)) return invalid();
  const gate = config.groupApprovalPolicy;
  if (!['all', 'any', 'nOfM', 'percent'].includes(String(gate.modeKey))) return invalid();
  const expected = gate.modeKey === 'nOfM' ? ['modeKey', 'requiredApprovals'] : gate.modeKey === 'percent' ? ['modeKey', 'requiredPercentage'] : ['modeKey'];
  if (Object.keys(gate).length !== expected.length || expected.some(key => !(key in gate))) return invalid();
  if (gate.modeKey === 'nOfM' && (!Number.isInteger(gate.requiredApprovals) || Number(gate.requiredApprovals) <= 1 || Number(gate.requiredApprovals) >= groups.length)) return invalid();
  if (gate.modeKey === 'percent' && (!Number.isInteger(gate.requiredPercentage) || Number(gate.requiredPercentage) < 1 || Number(gate.requiredPercentage) > 100)) return invalid();
  return { groups, groupApprovalPolicy: { ...gate, modeKey: gate.modeKey as 'all' | 'any' | 'nOfM' | 'percent' } };
}

/** 共享字段、动作及超时边界只适用于真实审批活动。 */
function isHumanFormType(kind: string): boolean { return kind === 'human.approval' || kind === 'human.group_approval' || kind === 'human.task' || kind === 'human.vote' || kind === 'human.claim'; }

/** 办理收敛规则与审批模式分开，禁止未知选项或脚本扩展。 */
export function readTaskPolicy(value: unknown): { completionModeKey: 'all' | 'any' | 'sequential' } {
  if (!isRecord(value) || Object.keys(value).length !== 1 || !['all', 'any', 'sequential'].includes(String(value.completionModeKey)))
    throw new Error('client.invalid_workflow_task_policy');
  return { completionModeKey: value.completionModeKey as 'all' | 'any' | 'sequential' };
}

/** 领取池只能声明是否允许释放，候选身份和循环预算不接受设计器扩展。 */
export function readClaimPolicy(value: unknown): { allowRelease: boolean } {
  if (!isRecord(value) || Object.keys(value).length !== 1 || typeof value.allowRelease !== 'boolean')
    throw new Error('client.invalid_workflow_claim_policy');
  return { allowRelease: value.allowRelease };
}

export type WorkflowWithdrawalPolicy = { modeKey: 'disabled' | 'before_any_human_action' } | { modeKey: 'at_active_nodes'; nodeKeys: string[] };
/** 撤回只声明发布边界，不能从设计器透传发起人、权限或补偿豁免。 */
export function readWithdrawalPolicy(value: unknown): WorkflowWithdrawalPolicy {
  const invalid = (): never => { throw new Error('client.invalid_workflow_withdrawal_policy'); };
  if (!isRecord(value) || !['disabled', 'before_any_human_action', 'at_active_nodes'].includes(String(value.modeKey))) return invalid();
  if (value.modeKey !== 'at_active_nodes') {
    if (Object.keys(value).length !== 1) return invalid();
    return { modeKey: value.modeKey as 'disabled' | 'before_any_human_action' };
  }
  if (Object.keys(value).length !== 2 || !Array.isArray(value.nodeKeys) || value.nodeKeys.length < 1 || value.nodeKeys.length > 16 ||
    value.nodeKeys.some(key => typeof key !== 'string' || !stableKeyPattern.test(key)) || new Set(value.nodeKeys).size !== value.nodeKeys.length) return invalid();
  return { modeKey: 'at_active_nodes', nodeKeys: [...value.nodeKeys] as string[] };
}
