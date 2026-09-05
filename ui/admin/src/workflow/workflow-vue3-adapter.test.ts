import { describe, expect, it } from 'vitest';
import type { WorkflowDefinitionDraft } from '@fullnet/client-contracts';
import {
  fromWorkflowVue3Tree,
  toWorkflowVue3Tree
} from './workflow-vue3-adapter';

describe('Workflow-Vue3 定义适配器', () => {
  const financeUserId = '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001';

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
      childNode: { id: 'trigger', type: 7, remoteUrl: 'https://example.test', childNode: null }
    })).toThrow('client.unsupported_workflow_node');

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
