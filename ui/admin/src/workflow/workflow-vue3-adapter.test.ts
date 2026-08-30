import { describe, expect, it } from 'vitest';
import type { WorkflowDefinitionDraft } from '@fullnet/client-contracts';
import {
  fromWorkflowVue3Tree,
  toWorkflowVue3Tree
} from './workflow-vue3-adapter';

describe('Workflow-Vue3 定义适配器', () => {
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
      nextNodeKeys: ['end']
    });
  });

  it('从权威线性节点恢复 Workflow-Vue3 编辑树', () => {
    const draft = {
      schemaVersion: 1,
      nodes: [
        node('start', 'start', ['approve']),
        node('approve', 'human.approval', ['cc'], { nodeName: '审批' }),
        node('cc', 'notify.cc', ['end'], { nodeName: '抄送' }),
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
          nodeName: '抄送'
        }
      }
    });
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
