import { describe, expect, it } from 'vitest';
import type { WorkflowDefinitionDraft } from '@fullnet/client-contracts';
import {
  fromWorkflowVue3Tree,
  toWorkflowVue3Tree
} from './workflow-vue3-adapter';

describe('Workflow-Vue3 定义适配器', () => {
  it('开始节点的撤回边界无损往返，不接受客户端授权或补偿豁免', () => {
    const tree = { id: 'start', type: 0, withdrawalPolicy: { modeKey: 'at_active_nodes', nodeKeys: ['review'] },
      childNode: { id: 'review', type: 1, childNode: null } };
    expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
    expect(() => fromWorkflowVue3Tree({ ...tree, withdrawalPolicy: { modeKey: 'before_any_human_action', skipCompensation: true } })).toThrow();
    expect(() => fromWorkflowVue3Tree({ ...tree, withdrawalPolicy: { modeKey: 'at_active_nodes', nodeKeys: ['review', 'review'] } })).toThrow();
  });
  it('领取往返固定候选和释放策略，不能混入审批或个人投票', () => {
    const tree = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'claim', type: 17, childNode: null,
      claimPolicy: { allowRelease: true }, assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] },
      operationPermissions: { complete: true, release: true, transfer: false } } };
    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'claim')?.nodeTypeKey).toBe('human.claim');
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, claimPolicy: { allowRelease: true, actorId: 'foreign' } } })).toThrow();
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, approvalPolicy: { modeKey: 'single' } } })).toThrow();
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, operationPermissions: { vote: true } } })).toThrow();
  });
  it('办理往返保留独立提交规则，不接受审批策略混入', () => {
    const tree = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'task', type: 18, childNode: null,
      taskPolicy: { completionModeKey: 'sequential' }, assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] },
      operationPermissions: { complete: true, transfer: false }, commentRequired: true } };
    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.workflowKindKey).toBe('automation'); expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'task')?.nodeTypeKey).toBe('human.task');
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, approvalPolicy: { modeKey: 'all', approverUserIds: [] } } })).toThrow();
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, operationPermissions: { approve: true } } })).toThrow();
  });

  it('分组往返保留组内顺序与组间比例，并拒绝跨组重复成员', () => {
    const users = ['019bc2b1-2a40-7cc3-8992-a80de51bfd21', '019bc2b1-2a40-7cc3-8992-a80de51bfd22', '019bc2b1-2a40-7cc3-8992-a80de51bfd23', '019bc2b1-2a40-7cc3-8992-a80de51bfd24'];
    const group = (key: string, ids: string[]) => ({ groupKey: key, approvalPolicy: { modeKey: 'sequential', approverUserIds: ids } });
    const tree = { id: 'start', type: 0, childNode: { id: 'review', type: 21, childNode: null,
      groups: [group('first', users.slice(0, 2)), group('second', users.slice(2))],
      groupApprovalPolicy: { modeKey: 'percent', requiredPercentage: 67 }, commentRequired: true } };
    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'review')?.nodeTypeKey).toBe('human.group_approval');
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
    const overlapping = structuredClone(tree);
    overlapping.childNode.groups[1]!.approvalPolicy.approverUserIds[0] = users[0]!;
    expect(() => fromWorkflowVue3Tree(overlapping)).toThrow('client.invalid_workflow_approval_policy');
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'empty', type: 21, childNode: null } })).toThrow();
  });

  it('依次审批往返保留指定成员顺序，不能排序或降级为会签', () => {
    const users = ['019bc2b1-2a40-7cc3-8992-a80de51bfd23', '019bc2b1-2a40-7cc3-8992-a80de51bfd21'];
    const tree = { id: 'start', type: 0, nodeName: '发起人', childNode: {
      id: 'review', type: 1, nodeName: '依次审批', childNode: null,
      approvalPolicy: { modeKey: 'sequential', approverUserIds: users }
    } };
    expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
  });

  it('按比例审批完整往返并拒绝越界或隐式转换百分比', () => {
    const users = ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'];
    const tree = (percentage: unknown) => ({ id: 'start', type: 0, nodeName: '发起人', childNode: {
      id: 'review', type: 1, nodeName: '比例审批', childNode: null,
      approvalPolicy: { modeKey: 'percent', approverUserIds: users, requiredPercentage: percentage }
    } });
    const draft = fromWorkflowVue3Tree(tree(67));
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree(67));
    for (const value of [0, 101, 50.5, '50']) {
      expect(() => fromWorkflowVue3Tree(tree(value))).toThrow('client.invalid_workflow_approval_policy');
    }
  });

  it('无人工节点的自动化类别在保存与回显后保留', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, nodeName: '发起人', workflowKindKey: 'automation', childNode: null });
    expect(draft.workflowKindKey).toBe('automation');
    expect(draft.nodes.map(node => node.nodeTypeKey)).toEqual(['start', 'end']);
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toEqual(draft);
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, workflowKindKey: 'unknown' })).toThrow();
  });

  const financeUserId = '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001';

  function gateway(id: string, type = 10, branchCount = 1, childNode: unknown = null): Record<string, unknown> {
    return {
      id, type, childNode,
      conditionNodes: [
        ...Array.from({ length: branchCount }, (_, index) => ({
          id: `${id}-condition-${index}`, type: 3, branchKey: `branch-${index}`,
          fieldKey: 'amount', operator: 'greaterThan', value: index,
          childNode: { id: `${id}-approval-${index}`, type: 1, childNode: null }
        })),
        { id: `${id}-default`, type: 3, isDefault: true,
          childNode: { id: `${id}-fallback`, type: 1, childNode: null } }
      ]
    };
  }

  it.each([
    ['公共审批', { id: 'common', type: 1, childNode: null }],
    ['公共排他网关', gateway('common')],
    ['公共并行网关', gateway('common', 12)],
    ['公共包容网关', gateway('common', 13)]
  ])('排他分支末端连接%s且双向转换保留公共节点', (_label, common) => {
    const tree = { id: 'start', type: 0, childNode: gateway('route', 10, 1, common) };
    const draft = fromWorkflowVue3Tree(tree);
    for (const key of ['route-approval-0', 'route-fallback']) {
      expect(draft.nodes.find(node => node.nodeKey === key)?.config).toMatchObject({ nextNodeKeys: ['common'] });
    }
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toMatchObject(draft);
  });

  it('嵌套排他网关分别接入本层公共节点及外层公共节点', () => {
    const inner = gateway('inner', 10, 1, { id: 'inner-common', type: 1, childNode: null });
    const outer = gateway('outer', 10, 1, { id: 'outer-common', type: 1, childNode: null });
    (outer.conditionNodes as Array<Record<string, unknown>>)[0]!.childNode = inner;
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: outer });
    expect(draft.nodes.find(node => node.nodeKey === 'inner-approval-0')?.config).toMatchObject({ nextNodeKeys: ['inner-common'] });
    expect(draft.nodes.find(node => node.nodeKey === 'inner-common')?.config).toMatchObject({ nextNodeKeys: ['outer-common'] });
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toMatchObject(draft);
  });

  it('包容网关允许八条条件分支和一个独立默认分支往返', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: gateway('inclusive', 13, 8) });
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toMatchObject(draft);
  });

  it('正向及反向转换均拒绝九条包容条件分支', () => {
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: gateway('inclusive', 13, 9) }))
      .toThrow('client.invalid_workflow_gateway');
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: gateway('inclusive', 13, 8) });
    const fork = draft.nodes.find(node => node.nodeKey === 'inclusive')!;
    const config = fork.config as { branches: Array<Record<string, unknown>> };
    config.branches.push({ branchKey: 'overflow', nextNodeKey: 'overflow', condition: { fieldKey: 'amount', operator: 'equals', value: 1 } });
    expect(() => toWorkflowVue3Tree(draft)).toThrow('client.invalid_workflow_gateway');
  });

  it.each([12, 13])('类型 %i 的内部汇合键不能与用户节点重复或超长', type => {
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0,
      childNode: gateway('fork', type, 1, { id: 'fork-join', type: 1, childNode: null })
    })).toThrow('client.invalid_workflow_definition_draft');
    const longKeyGateway = { ...gateway('fork', type), id: 'f'.repeat(125) };
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0,
      childNode: longKeyGateway
    })).toThrow('client.invalid_workflow_definition_draft');
  });

  it('反向转换拒绝定义或节点的不支持版本而不静默降级', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0,
      childNode: { id: 'approve', type: 1, childNode: null } });
    expect(() => toWorkflowVue3Tree({ ...draft, schemaVersion: 2 })).toThrow('client.invalid_workflow_definition_draft');
    expect(() => toWorkflowVue3Tree({ ...draft, nodes: draft.nodes.map(item => ({ ...item, nodeSchemaVersion: 2 })) }))
      .toThrow('client.invalid_workflow_definition_draft');
  });

  it('反向转换拒绝重复节点键和悬空分支并返回稳定错误', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: gateway('route') });
    expect(() => toWorkflowVue3Tree({ ...draft, nodes: [...draft.nodes, draft.nodes[0]!] }))
      .toThrow('client.invalid_workflow_definition_draft');
    expect(() => toWorkflowVue3Tree({ ...draft, nodes: draft.nodes.filter(item => item.nodeKey !== 'route-approval-0') }))
      .toThrow('client.invalid_workflow_definition_draft');
  });

  it('审批节点超时策略在设计树与服务端草稿之间保持闭合往返', () => {
    const tree = {
      id: 'start', type: 0, nodeName: '发起人', childNode: {
        id: 'approve', type: 1, nodeName: '审批人',
        timeoutPolicy: {
          dueAfterMinutes: 60,
          reminderIntervalMinutes: 15,
          maxReminderCount: 2,
          escalationAfterMinutes: 120,
          escalationRecipientUserId: financeUserId
        },
        childNode: null
      }
    };

    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'approve')?.config).toMatchObject({
      timeoutPolicy: { dueAfterMinutes: 60, maxReminderCount: 2 }
    });
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
  });

  it('拒绝审批节点开放式或非法超时配置', () => {
    expect(() => fromWorkflowVue3Tree({
      id: 'start', type: 0, childNode: {
        id: 'approve', type: 1,
        timeoutPolicy: { dueAfterMinutes: 0, script: 'alert(1)' },
        childNode: null
      }
    })).toThrow();
  });

  it('多人审批策略在设计树与服务端草稿之间保持闭合往返', () => {
    const legalUserId = '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002';
    const tree = {
      id: 'start', type: 0, nodeName: '发起人', childNode: {
        id: 'approve', type: 1, nodeName: '审批人',
        approvalPolicy: {
          modeKey: 'nOfM',
          approverUserIds: [financeUserId, legalUserId],
          requiredApprovals: 1
        },
        childNode: null
      }
    };

    expect(() => fromWorkflowVue3Tree(tree)).toThrow('client.invalid_workflow_approval_policy');
    tree.childNode.approvalPolicy.approverUserIds.push(
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003');
    tree.childNode.approvalPolicy.requiredApprovals = 2;

    const draft = fromWorkflowVue3Tree(tree);
    expect((draft.nodes[1]?.config as Record<string, unknown>).approvalPolicy)
      .toEqual(tree.childNode.approvalPolicy);
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
  });

  it('把线性审批树转换成服务端权威节点并补齐显式结束节点', () => {
    const draft = fromWorkflowVue3Tree({
      id: 'start',
      type: 0,
      nodeName: '发起人',
      childNode: {
        id: 'manager-approval',
        type: 1,
        nodeName: '主管审批',
        settype: 1,
        childNode: {
          id: 'finance-cc',
          type: 2,
          nodeName: '抄送财务',
          recipientUserIds: [financeUserId],
          childNode: null
        }
      }
    });

    expect(draft.schemaVersion).toBe(1);
    expect(draft.nodes.map(node => [node.nodeKey, node.nodeTypeKey])).toEqual([
      ['start', 'start'],
      ['manager-approval', 'human.approval'],
      ['finance-cc', 'notify.cc'],
      ['end', 'end']
    ]);
    expect(draft.nodes[0]?.config).toMatchObject({ nextNodeKeys: ['manager-approval'] });
    expect(draft.nodes[1]?.config).toEqual({
      nodeName: '主管审批',
      nextNodeKeys: ['finance-cc']
    });
    expect(draft.nodes[2]?.config).toEqual({
      nodeName: '抄送财务',
      recipientUserIds: [financeUserId],
      nextNodeKeys: ['end']
    });
  });

  it('从权威线性节点恢复 Workflow-Vue3 编辑树', () => {
    const draft = {
      schemaVersion: 1,
      nodes: [
        node('start', 'start', ['approve']),
        node('approve', 'human.approval', ['cc'], { nodeName: '审批' }),
        node('cc', 'notify.cc', ['end'], {
          nodeName: '抄送',
          recipientUserIds: [financeUserId]
        }),
        node('end', 'end', [])
      ]
    } satisfies WorkflowDefinitionDraft;

    const tree = toWorkflowVue3Tree(draft);

    expect(tree).toMatchObject({
      id: 'start',
      type: 0,
      childNode: {
        id: 'approve',
        type: 1,
        nodeName: '审批',
        childNode: {
          id: 'cc',
          type: 2,
          nodeName: '抄送',
          recipientUserIds: [financeUserId]
        }
      }
    });
  });

  it('在 Workflow-Vue3 并行分支树与并行网关 Draft 之间双向转换', () => {
    const tree = {
      id: 'start',
      type: 0,
      childNode: {
        id: 'parallel-fork',
        type: 12,
        nodeName: '并行审批',
        conditionNodes: [
          {
            id: 'branch-a',
            type: 3,
            branchKey: 'a',
            childNode: { id: 'approve-a', type: 1, childNode: null }
          },
          {
            id: 'branch-b',
            type: 3,
            branchKey: 'b',
            childNode: { id: 'approve-b', type: 1, childNode: null }
          }
        ],
        childNode: { id: 'after-join', type: 1, childNode: null }
      }
    };

    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'parallel-fork')).toMatchObject({
      nodeKey: 'parallel-fork',
      nodeTypeKey: 'gateway.parallel',
      config: {
        nodeName: '并行审批',
        gatewayRoleKey: 'fork',
        joinNodeKey: 'parallel-fork-join',
        branches: [
          { branchKey: 'a', nextNodeKey: 'approve-a' },
          { branchKey: 'b', nextNodeKey: 'approve-b' }
        ]
      }
    });
    expect(draft.nodes.find(node => node.nodeKey === 'parallel-fork-join')).toMatchObject({
      nodeKey: 'parallel-fork-join',
      nodeTypeKey: 'gateway.parallel',
      config: {
        gatewayRoleKey: 'join',
        forkNodeKey: 'parallel-fork',
        nextNodeKeys: ['after-join']
      }
    });
    expect(toWorkflowVue3Tree(draft)).toMatchObject({
      id: 'start',
      type: 0,
      childNode: {
        id: 'parallel-fork',
        type: 12,
        nodeName: '并行审批',
        conditionNodes: [
          {
            branchKey: 'a',
            childNode: { id: 'approve-a', type: 1 }
          },
          {
            branchKey: 'b',
            childNode: { id: 'approve-b', type: 1 }
          }
        ],
        childNode: { id: 'after-join', type: 1 }
      }
    });
  });

  it('在 Workflow-Vue3 分支树与排他网关 Draft 之间双向转换', () => {
    const tree = {
      id: 'start',
      type: 0,
      childNode: {
        id: 'route',
        type: 10,
        nodeName: '金额分流',
        conditionNodes: [
          {
            id: 'large-branch',
            type: 3,
            nodeName: '大额',
            branchKey: 'large',
            fieldKey: 'amount',
            operator: 'greaterThanOrEqual',
            value: '1000.00',
            childNode: { id: 'finance', type: 1, childNode: null }
          },
          {
            id: 'default-branch',
            type: 3,
            nodeName: '其他条件',
            isDefault: true,
            childNode: { id: 'manager', type: 1, childNode: null }
          }
        ],
        childNode: null
      }
    };

    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'route')).toEqual({
      nodeKey: 'route',
      nodeTypeKey: 'gateway.exclusive',
      nodeSchemaVersion: 1,
      config: {
        nodeName: '金额分流',
        nextNodeKeys: ['finance', 'manager'],
        branches: [{
          branchKey: 'large',
          nextNodeKey: 'finance',
          condition: {
            fieldKey: 'amount',
            operator: 'greaterThanOrEqual',
            value: '1000.00'
          }
        }],
        defaultNextNodeKey: 'manager'
      }
    });

    expect(toWorkflowVue3Tree(draft)).toMatchObject({
      id: 'start',
      type: 0,
      childNode: {
        id: 'route',
        type: 10,
        conditionNodes: [
          {
            branchKey: 'large',
            fieldKey: 'amount',
            operator: 'greaterThanOrEqual',
            value: '1000.00',
            childNode: { id: 'finance', type: 1 }
          },
          {
            isDefault: true,
            childNode: { id: 'manager', type: 1 }
          }
        ]
      }
    });
  });

  it('拒绝空、重复或非法抄送人标识', () => {
    const tree = (recipientUserIds: string[]) => ({
      id: 'start',
      type: 0,
      childNode: {
        id: 'copy',
        type: 2,
        recipientUserIds,
        childNode: null
      }
    });

    expect(() => fromWorkflowVue3Tree(tree([])))
      .toThrow('client.invalid_workflow_cc_recipients');
    expect(() => fromWorkflowVue3Tree(tree([financeUserId, financeUserId])))
      .toThrow('client.invalid_workflow_cc_recipients');
    expect(() => fromWorkflowVue3Tree(tree(['not-a-guid'])))
      .toThrow('client.invalid_workflow_cc_recipients');
  });

  it('拒绝动态脚本、远程请求节点和非目录节点', () => {
    expect(() => fromWorkflowVue3Tree({
      id: 'start',
      type: 0,
      childNode: { id: 'trigger', type: 6, remoteUrl: 'https://example.test', childNode: null }
    })).toThrow('client.invalid_workflow_connector');

    expect(() => fromWorkflowVue3Tree({
      id: 'start',
      type: 0,
      childNode: { id: 'approve', type: 1, voteScript: 'return true', childNode: null }
    })).toThrow('client.unsafe_workflow_configuration');
  });

  it('拒绝缺失稳定节点键与重复键', () => {
    expect(() => fromWorkflowVue3Tree({
      type: 0,
      childNode: null
    })).toThrow('client.invalid_workflow_definition_draft');

    expect(() => fromWorkflowVue3Tree({
      id: 'same',
      type: 0,
      childNode: { id: 'same', type: 1, childNode: null }
    })).toThrow('client.invalid_workflow_definition_draft');
  });

  it('丢弃已知设计态字段并拒绝未知或结构覆盖配置', () => {
    const draft = fromWorkflowVue3Tree({
      id: 'start',
      type: 0,
      nodeName: '发起人',
      childNode: {
        id: 'approval',
        type: 1,
        nodeName: '审批人',
        error: false,
        settype: 1,
        examineMode: 1,
        nodeUserList: [],
        placeHolder: '当前登录人审批',
        childNode: null
      }
    });
    expect(draft.nodes[1]?.config).toEqual({ nodeName: '审批人', nextNodeKeys: ['end'] });

    expect(() => fromWorkflowVue3Tree({
      id: 'start',
      type: 0,
      unknownAction: 'pass-through',
      childNode: null
    })).toThrow('client.unsupported_workflow_node_configuration');

    expect(() => toWorkflowVue3Tree({
      schemaVersion: 1,
      nodes: [
        node('start', 'start', ['end'], { id: 'override' }),
        node('end', 'end', [])
      ]
    })).toThrow('client.unsupported_workflow_node_configuration');
  });

  it('审批节点办理人策略在设计树与服务端草稿之间保持闭合', () => {
    const roleId = '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002';
    const tree = {
      id: 'start', type: 0, childNode: {
        id: 'approve', type: 1,
        assigneePolicy: {
          sources: [{ resolverKindKey: 'role_members', roleIds: [roleId] }]
        },
        childNode: null
      }
    };

    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'approve')?.config).toMatchObject({
      assigneePolicy: { sources: [{ resolverKindKey: 'role_members', roleIds: [roleId] }] }
    });
  });

  it.each([
    { resolverKindKey: 'position_members', positionIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'] },
    { resolverKindKey: 'organization_unit_members', unitId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002' },
    { resolverKindKey: 'initiator_leader_chain', maximumLevels: 3 }
  ])('动态来源独立提供审批成员，配置往返不注入其他用户：%j', source => {
    const approvalPolicy = { modeKey: 'all', approverUserIds: [] };
    const assigneePolicy = { sources: [source] };
    const draft = { schemaVersion: 1, nodes: [node('start', 'start', ['review']),
      node('review', 'human.approval', ['end'], { assigneePolicy, approvalPolicy }), node('end', 'end', [])] };
    const saved = fromWorkflowVue3Tree(toWorkflowVue3Tree(draft));
    expect(saved.nodes.find(item => item.nodeKey === 'review')?.config).toMatchObject({ approvalPolicy, assigneePolicy });
    expect(() => toWorkflowVue3Tree({ ...draft, nodes: [node('start', 'start', ['review']),
      node('review', 'human.approval', ['end'], { approvalPolicy }), node('end', 'end', [])] }))
      .toThrow('client.invalid_workflow_approval_policy');
  });

  it.each([0, 21, 1.5, '3'])('连续主管级数必须为有界整数：%j', maximumLevels => {
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'review', type: 1, childNode: null,
      assigneePolicy: { sources: [{ resolverKindKey: 'initiator_leader_chain', maximumLevels }] } } }))
      .toThrow('client.invalid_workflow_assignee_policy');
  });

  it.each([
    { sources: [{ resolverKindKey: 'initiator' }], fallback: 'skip' },
    { sources: [{ resolverKindKey: 'initiator', unexpected: true }] }
  ])('办理人来源的未知配置必须拒绝而非静默删除：%j', assigneePolicy => {
    const draft = { schemaVersion: 1, nodes: [node('start', 'start', ['review']),
      node('review', 'human.approval', ['end'], { assigneePolicy }), node('end', 'end', [])] };
    expect(() => toWorkflowVue3Tree(draft)).toThrow('client.invalid_workflow_assignee_policy');
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0,
      childNode: { id: 'review', type: 1, assigneePolicy, childNode: null } }))
      .toThrow('client.invalid_workflow_assignee_policy');
  });
});

function node(
  nodeKey: string,
  nodeTypeKey: string,
  nextNodeKeys: string[],
  rest: Record<string, unknown> = {}
) {
  return {
    nodeKey,
    nodeTypeKey,
    nodeSchemaVersion: 1,
    config: { nextNodeKeys, ...rest }
  };
}
